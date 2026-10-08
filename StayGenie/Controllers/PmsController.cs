using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StayGenie.Data;
using StayGenie.Services;

[ApiController]
[Route("api/pms")]
public class PmsController(
    HotelDbContext db,
    IPaymentService payments,
    ILogger<PmsController> logger) : ControllerBase
{
    public record BookingRequest(
        int RoomId, DateOnly CheckIn, DateOnly CheckOut, int Guests,
        string GuestName, string? GuestEmail = null, int? UserId = null);

    // Kya is room ki koi active booking in dates se takrati hai?
    private Task<bool> IsRoomTakenAsync(int roomId, DateOnly checkIn, DateOnly checkOut) =>
        db.Reservations.AnyAsync(r =>
            r.RoomId == roomId
            && r.Status != ReservationStatus.Cancelled
            && r.Status != ReservationStatus.Expired
            && r.CheckIn < checkOut && checkIn < r.CheckOut);

    // Is room par lock: ek waqt mein sirf ek booking request check+insert kar sakti hai.
    // Lock transaction khatam hote hi khud khul jata hai.
    private async Task<bool> TryLockRoomAsync(int roomId)
    {
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', " +
            "@LockOwner = 'Transaction', @LockTimeout = 10000",
            result, new SqlParameter("@resource", $"room-{roomId}"));
        return result.Value is int code && code >= 0;
    }

    // Public: chatbot (MCP tool) availability check yahin se karta hai
    [HttpGet("availability")]
    public async Task<IActionResult> Availability(int roomId, DateOnly checkIn, DateOnly checkOut)
    {
        var dateError = BookingRules.ValidateDates(checkIn, checkOut, BookingRules.Today());
        if (dateError is not null) return BadRequest(dateError);

        // Deactivated room/hotel bookable nahi
        var bookable = await db.Rooms.AnyAsync(r => r.Id == roomId && r.IsActive && r.Hotel!.IsActive);
        if (!bookable) return Ok(new { roomId, checkIn, checkOut, available = false });

        var taken = await IsRoomTakenAsync(roomId, checkIn, checkOut);
        return Ok(new { roomId, checkIn, checkOut, available = !taken });
    }

    // Sirf Admin: is mein sab guests ka data hota hai
    [HttpGet("reservations")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> List() =>
        Ok(await db.Reservations.Include(r => r.Room).OrderByDescending(r => r.Id).ToListAsync());

    // Public: chatbot ek booking dekhta hai. Id AUR guest ka naam dono sahi hone chahiye.
    [HttpGet("reservations/{id:int}")]
    public async Task<IActionResult> GetOne(int id, [FromQuery] string guestName)
    {
        var res = await db.Reservations.Include(r => r.Room)
            .FirstOrDefaultAsync(r => r.Id == id);

        // Booking na mile ya naam na mile, dono surat mein same jawab
        if (res is null ||
            !string.Equals(res.GuestName.Trim(), guestName?.Trim(), StringComparison.OrdinalIgnoreCase))
            return NotFound();

        // Sirf zaroori cheezein wapas (email aur payment ids jaan bhujh kar shamil nahi)
        return Ok(new
        {
            res.Id,
            roomNumber = res.Room?.RoomNumber,
            res.GuestName,
            res.CheckIn,
            res.CheckOut,
            res.TotalPrice,
            status = res.Status.ToString()
        });
    }

    // Public: chatbot (MCP tool) booking yahin se banata hai
    [HttpPost("reservations")]
    public async Task<IActionResult> Create(BookingRequest b)
    {
        var dateError = BookingRules.ValidateDates(b.CheckIn, b.CheckOut, BookingRules.Today());
        if (dateError is not null) return BadRequest(dateError);

        if (string.IsNullOrWhiteSpace(b.GuestName)) return BadRequest("Guest name is required.");
        if (b.GuestName.Trim().Length > 100) return BadRequest("Guest name is too long.");

        // 0 ya khali userId ka matlab: bina login wali booking
        int? userId = b.UserId is > 0 ? b.UserId : null;
        if (userId is int uid && !await db.Users.AnyAsync(u => u.Id == uid))
            return BadRequest("Unknown user.");

        // Email: pehle request se, warna login user ki account email
        var guestEmail = b.GuestEmail?.Trim();
        if (string.IsNullOrEmpty(guestEmail) && userId is int accountId)
            guestEmail = await db.Users
                .Where(u => u.Id == accountId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync();
        if (!string.IsNullOrEmpty(guestEmail) && !new EmailAddressAttribute().IsValid(guestEmail))
            return BadRequest("Guest email is not valid.");

        // Sirf active room, active hotel ka
        var room = await db.Rooms
            .FirstOrDefaultAsync(r => r.Id == b.RoomId && r.IsActive && r.Hotel!.IsActive);
        if (room is null) return NotFound("Room not found or not available for booking.");

        var guestError = BookingRules.ValidateGuests(b.Guests, room.MaxOccupancy, room.RoomNumber);
        if (guestError is not null) return BadRequest(guestError);

        var nights = BookingRules.Nights(b.CheckIn, b.CheckOut);
        Reservation res;

        // Lock + check + insert ek transaction mein: double booking nahi ho sakti.
        // Neeche return ho jaye to transaction khud rollback ho jati hai.
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            if (!await TryLockRoomAsync(b.RoomId))
                return StatusCode(503, "The system is busy. Please try again in a moment.");

            if (await IsRoomTakenAsync(b.RoomId, b.CheckIn, b.CheckOut))
                return Conflict("Room is no longer available for these dates.");

            res = new Reservation
            {
                RoomId = b.RoomId,
                CheckIn = b.CheckIn,
                CheckOut = b.CheckOut,
                Guests = b.Guests,
                GuestName = b.GuestName.Trim(),
                GuestEmail = string.IsNullOrEmpty(guestEmail) ? null : guestEmail,
                TotalPrice = BookingRules.TotalPrice(room.PricePerNight, b.CheckIn, b.CheckOut),
                UserId = userId,
                // Payment hone tak room hold hai, confirm sirf Adyen webhook karega
                Status = ReservationStatus.PendingPayment
            };
            db.Reservations.Add(res);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        try
        {
            var link = await payments.CreatePaymentLinkAsync(res, room.RoomNumber, nights);
            res.PaymentLinkId = link.LinkId;
            await db.SaveChangesAsync();

            return Created($"/api/pms/reservations/{res.Id}", new
            {
                res.Id,
                roomNumber = room.RoomNumber,
                nights,
                res.TotalPrice,
                status = res.Status.ToString(),
                paymentUrl = link.Url,
                holdMinutes = link.HoldMinutes
            });
        }
        catch (Exception ex)
        {
            // Payment shuru na ho saki: room ko hold mein na rakho
            logger.LogError(ex, "Could not start payment for reservation {ReservationId}.", res.Id);
            res.Status = ReservationStatus.Cancelled;
            await db.SaveChangesAsync();
            return StatusCode(502, "Could not start the payment. Please try again.");
        }
    }

    // Sirf Admin: booking cancel karna (database se delete nahi, record bacha rehta hai)
    [HttpDelete("reservations/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Cancel(int id)
    {
        var res = await db.Reservations.FindAsync(id);
        if (res is null) return NotFound();

        if (res.Status is ReservationStatus.Cancelled or ReservationStatus.Expired)
            return NoContent();

        res.Status = ReservationStatus.Cancelled;
        await db.SaveChangesAsync();

        if (res.PaidAt is not null)
            logger.LogWarning("Paid reservation {ReservationId} was cancelled by admin. Refund needed.", res.Id);

        return NoContent();
    }
}