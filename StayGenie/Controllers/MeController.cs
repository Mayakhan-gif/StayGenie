using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayGenie.Data;
using StayGenie.Services;

[ApiController]
[Route("api/me")]
[Authorize] // Sirf login wale. Admin ya Customer, dono apna hi data dekhte hain.
public class MeController(HotelDbContext db, ILogger<MeController> logger) : ControllerBase
{
    public record UpdateProfileRequest(
        [Required, StringLength(100, MinimumLength = 2)] string Name);

    public record ChangePasswordRequest(
        [Required] string CurrentPassword,
        [Required, StringLength(100, MinimumLength = 8)] string NewPassword);

    // Token se user ki id. Dusre ki id kabhi request se nahi li jati.
    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private static object ToView(Reservation r, bool reviewed, DateOnly today) => new
    {
        r.Id,
        r.RoomId,
        roomNumber = r.Room?.RoomNumber,
        hotelName = r.Room?.Hotel?.Name,
        city = r.Room?.Hotel?.City,
        r.CheckIn,
        r.CheckOut,
        r.Guests,
        r.TotalPrice,
        status = r.Status.ToString(),
        r.PaidAt,
        r.CreatedAt,
        canCancel = BookingRules.CanCancel(r.Status, r.CheckIn, today),
        canReview = BookingRules.CanReview(r.Status, r.CheckOut, today, reviewed),
        reviewed
    };

    // Mera profile
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        // PasswordHash kabhi wapas nahi jata
        var user = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Name, u.Email, u.Role })
            .FirstOrDefaultAsync();

        return user is null ? NotFound() : Ok(user);
    }

    // Naam badalna
    [HttpPut]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest req)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var user = await db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        user.Name = req.Name.Trim();
        await db.SaveChangesAsync();
        return Ok(new { user.Id, user.Name, user.Email, user.Role });
    }

    // Password badalna (purana password dena zaroori)
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var user = await db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        if (!PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash))
            return BadRequest("Current password is incorrect.");

        user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Dashboard ke upar wale numbers
    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var today = BookingRules.Today();

        var mine = db.Reservations.Where(r => r.UserId == userId);

        var upcoming = await mine.CountAsync(r =>
            r.Status == ReservationStatus.Confirmed && r.CheckOut > today);
        var past = await mine.CountAsync(r =>
            r.Status == ReservationStatus.Confirmed && r.CheckOut <= today);
        var pending = await mine.CountAsync(r => r.Status == ReservationStatus.PendingPayment);
        var cancelled = await mine.CountAsync(r =>
            r.Status == ReservationStatus.Cancelled || r.Status == ReservationStatus.Expired);
        var totalSpent = await mine
            .Where(r => r.Status == ReservationStatus.Confirmed)
            .SumAsync(r => (decimal?)r.TotalPrice) ?? 0m;
        var reviewsWritten = await db.Reviews.CountAsync(rv => rv.UserId == userId);

        return Ok(new { upcoming, past, pending, cancelled, totalSpent, reviewsWritten });
    }

    // Meri saari bookings, nayi pehle
    [HttpGet("reservations")]
    public async Task<IActionResult> MyReservations()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var today = BookingRules.Today();

        var list = await db.Reservations
            .Include(r => r.Room).ThenInclude(room => room!.Hotel)
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.Id)
            .ToListAsync();

        var reviewedIds = (await db.Reviews
            .Where(rv => rv.UserId == userId)
            .Select(rv => rv.ReservationId)
            .ToListAsync()).ToHashSet();

        return Ok(list.Select(r => ToView(r, reviewedIds.Contains(r.Id), today)));
    }

    // Meri ek booking
    [HttpGet("reservations/{id:int}")]
    public async Task<IActionResult> MyReservation(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var res = await db.Reservations
            .Include(r => r.Room).ThenInclude(room => room!.Hotel)
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

        // Dusre ki booking ho ya na mile, dono surat mein same jawab (NotFound)
        if (res is null) return NotFound();

        var reviewed = await db.Reviews.AnyAsync(rv => rv.ReservationId == res.Id);
        return Ok(ToView(res, reviewed, BookingRules.Today()));
    }

    // Meri booking cancel
    [HttpPost("reservations/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var today = BookingRules.Today();

        var res = await db.Reservations
            .Include(r => r.Room).ThenInclude(room => room!.Hotel)
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
        if (res is null) return NotFound();

        if (!BookingRules.CanCancel(res.Status, res.CheckIn, today))
            return Conflict("This booking can no longer be cancelled (cancellation closes the day before check-in).");

        res.Status = ReservationStatus.Cancelled;
        await db.SaveChangesAsync();

        if (res.PaidAt is not null)
            logger.LogWarning("Paid reservation {ReservationId} was cancelled by the guest. Refund needed.", res.Id);

        return Ok(ToView(res, false, today));
    }

    // Mere likhe hue reviews
    [HttpGet("reviews")]
    public async Task<IActionResult> MyReviews()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var reviews = await (
            from rv in db.Reviews
            where rv.UserId == userId
            join room in db.Rooms on rv.RoomId equals room.Id
            join h in db.Hotels on room.HotelId equals h.Id
            orderby rv.Id descending
            select new
            {
                rv.Id,
                rv.ReservationId,
                rv.Rating,
                rv.Comment,
                rv.CreatedAt,
                roomNumber = room.RoomNumber,
                hotelName = h.Name
            }).ToListAsync();

        return Ok(reviews);
    }
}