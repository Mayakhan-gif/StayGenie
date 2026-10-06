using System.ComponentModel.DataAnnotations;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayGenie.Data;
using StayGenie.Services;

[ApiController]
[Route("api/pms")]
public class PmsController(HotelDbContext db, IBackgroundJobClient jobs) : ControllerBase
{
    public record BookingRequest(
        int RoomId, DateOnly CheckIn, DateOnly CheckOut, int Guests,
        string GuestName, string? GuestEmail = null, int? UserId = null);

    // Public: chatbot (MCP tool) availability check yahin se karta hai
    [HttpGet("availability")]
    public async Task<IActionResult> Availability(int roomId, DateOnly checkIn, DateOnly checkOut)
    {
        var taken = await db.Reservations.AnyAsync(r =>
            r.RoomId == roomId
            && r.Status != ReservationStatus.Cancelled
            && r.Status != ReservationStatus.Expired
            && r.CheckIn < checkOut && checkIn < r.CheckOut);
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

        // Sirf zaroori cheezein wapas, poora object nahi (email jaan bhujh kar shamil nahi)
        return Ok(new
        {
            res.Id,
            roomNumber = res.Room?.RoomNumber,
            res.GuestName,
            res.CheckIn,
            res.CheckOut,
            res.TotalPrice
        });
    }

    // Public: chatbot (MCP tool) booking yahin se banata hai
    [HttpPost("reservations")]
    public async Task<IActionResult> Create(BookingRequest b)
    {
        if (b.CheckIn < DateOnly.FromDateTime(DateTime.Today)) return BadRequest("Check-in date is in the past.");
        if (b.CheckOut <= b.CheckIn) return BadRequest("Check-out must be after check-in.");
        if (string.IsNullOrWhiteSpace(b.GuestName)) return BadRequest("Guest name is required.");

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

        var room = await db.Rooms.FindAsync(b.RoomId);
        if (room is null) return NotFound("Room not found.");
        if (b.Guests < 1 || b.Guests > room.MaxOccupancy)
            return BadRequest($"Room {room.RoomNumber} allows max {room.MaxOccupancy} guests.");

        var taken = await db.Reservations.AnyAsync(r =>
            r.RoomId == b.RoomId
            && r.Status != ReservationStatus.Cancelled
            && r.Status != ReservationStatus.Expired
            && r.CheckIn < b.CheckOut && b.CheckIn < r.CheckOut);
        if (taken) return Conflict("Room is no longer available for these dates.");

        var nights = b.CheckOut.DayNumber - b.CheckIn.DayNumber;
        var res = new Reservation
        {
            RoomId = b.RoomId,
            CheckIn = b.CheckIn,
            CheckOut = b.CheckOut,
            Guests = b.Guests,
            GuestName = b.GuestName.Trim(),
            GuestEmail = string.IsNullOrEmpty(guestEmail) ? null : guestEmail,
            TotalPrice = nights * room.PricePerNight,
            UserId = userId,
            Status = ReservationStatus.Confirmed // payment ke step mein PendingPayment hoga
        };
        db.Reservations.Add(res);
        await db.SaveChangesAsync();

        // Email background mein: booking ka jawab email ka intezar nahi karta
        if (res.GuestEmail is not null)
            jobs.Enqueue<ReservationEmailJob>(j => j.SendConfirmationAsync(res.Id));

        return Created($"/api/pms/reservations/{res.Id}",
            new { res.Id, roomNumber = room.RoomNumber, nights, res.TotalPrice });
    }

    // Sirf Admin: booking delete karna
    [HttpDelete("reservations/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Cancel(int id)
    {
        var res = await db.Reservations.FindAsync(id);
        if (res is null) return NotFound();
        db.Reservations.Remove(res);
        await db.SaveChangesAsync();
        return NoContent();
    }
}