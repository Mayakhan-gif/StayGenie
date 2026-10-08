using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayGenie.Data;
using StayGenie.Services;

[ApiController]
[Route("api/me")]
[Authorize] // Sirf login wale. Admin ya Customer, dono apni hi bookings dekhte hain.
public class MeController(HotelDbContext db, ILogger<MeController> logger) : ControllerBase
{
    // Token se user ki id. Dusre ki id kabhi request se nahi li jati.
    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private static bool CanCancel(Reservation r) =>
        BookingRules.CanCancel(r.Status, r.CheckIn, BookingRules.Today());

    private static object ToView(Reservation r) => new
    {
        r.Id,
        roomNumber = r.Room?.RoomNumber,
        r.CheckIn,
        r.CheckOut,
        r.Guests,
        r.TotalPrice,
        status = r.Status.ToString(),
        r.PaidAt,
        r.CreatedAt,
        canCancel = CanCancel(r)
    };

    // Meri saari bookings, nayi pehle
    [HttpGet("reservations")]
    public async Task<IActionResult> MyReservations()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var list = await db.Reservations
            .Include(r => r.Room)
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.Id)
            .ToListAsync();

        return Ok(list.Select(ToView));
    }

    // Meri ek booking
    [HttpGet("reservations/{id:int}")]
    public async Task<IActionResult> MyReservation(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var res = await db.Reservations.Include(r => r.Room)
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

        // Dusre ki booking ho ya na mile, dono surat mein same jawab (NotFound)
        return res is null ? NotFound() : Ok(ToView(res));
    }

    // Meri booking cancel
    [HttpPost("reservations/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var res = await db.Reservations.Include(r => r.Room)
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
        if (res is null) return NotFound();

        if (!CanCancel(res))
            return Conflict("This booking can no longer be cancelled (cancellation closes the day before check-in).");

        res.Status = ReservationStatus.Cancelled;
        await db.SaveChangesAsync();

        if (res.PaidAt is not null)
            logger.LogWarning("Paid reservation {ReservationId} was cancelled by the guest. Refund needed.", res.Id);

        return Ok(ToView(res));
    }
}