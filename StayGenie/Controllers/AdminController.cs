using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayGenie.Data;
using StayGenie.Services;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")] // Poore controller par: har endpoint sirf Admin ke liye
public class AdminController(HotelDbContext db) : ControllerBase
{
    // Dashboard ke numbers
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var today = BookingRules.Today();

        // "Aaj" Pakistan ke hisaab se: din UTC mein 5 ghante pehle shuru hota hai
        var dayStartUtc = today.ToDateTime(TimeOnly.MinValue).AddHours(-5);
        var dayEndUtc = dayStartUtc.AddDays(1);

        var totalRooms = await db.Rooms.CountAsync(r => r.IsActive && r.Hotel!.IsActive);

        // Aaj raat kitne rooms bhare hue hain
        var occupiedRooms = await db.Reservations
            .Where(r => r.Status == ReservationStatus.Confirmed
                        && r.CheckIn <= today && today < r.CheckOut)
            .Select(r => r.RoomId)
            .Distinct()
            .CountAsync();

        var byStatus = await db.Reservations
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();
        int CountOf(ReservationStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

        var revenue = await db.Reservations
            .Where(r => r.Status == ReservationStatus.Confirmed)
            .SumAsync(r => (decimal?)r.TotalPrice) ?? 0m;

        var todaysBookings = await db.Reservations
            .CountAsync(r => r.CreatedAt >= dayStartUtc && r.CreatedAt < dayEndUtc);

        return Ok(new
        {
            totalHotels = await db.Hotels.CountAsync(h => h.IsActive),
            totalRooms,
            totalCustomers = await db.Users.CountAsync(u => u.Role == "Customer"),
            totalBookings = byStatus.Sum(x => x.Count),
            todaysBookings,
            confirmed = CountOf(ReservationStatus.Confirmed),
            pending = CountOf(ReservationStatus.PendingPayment),
            cancelled = CountOf(ReservationStatus.Cancelled),
            expired = CountOf(ReservationStatus.Expired),
            revenue,
            occupiedRoomsTonight = occupiedRooms,
            occupancyPercent = totalRooms == 0 ? 0 : Math.Round(occupiedRooms * 100.0 / totalRooms, 1)
        });
    }

    // Saare users (PasswordHash kabhi nahi)
    [HttpGet("customers")]
    public async Task<IActionResult> Customers()
    {
        var users = await db.Users
            .OrderByDescending(u => u.Id)
            .Select(u => new
            {
                u.Id,
                u.Name,
                u.Email,
                u.Role,
                bookings = db.Reservations.Count(r => r.UserId == u.Id)
            })
            .ToListAsync();

        return Ok(users);
    }

    // Saari bookings (optional filter: ?status=Confirmed)
    [HttpGet("reservations")]
    public async Task<IActionResult> Reservations([FromQuery] ReservationStatus? status)
    {
        var query = db.Reservations.AsQueryable();
        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        var list = await query
            .OrderByDescending(r => r.Id)
            .Select(r => new
            {
                r.Id,
                hotelName = r.Room!.Hotel!.Name,
                roomNumber = r.Room.RoomNumber,
                r.GuestName,
                r.GuestEmail,
                r.CheckIn,
                r.CheckOut,
                r.Guests,
                r.TotalPrice,
                status = r.Status.ToString(),
                r.PaidAt,
                r.CreatedAt,
                customer = db.Users.Where(u => u.Id == r.UserId).Select(u => u.Name).FirstOrDefault()
            })
            .ToListAsync();

        return Ok(list);
    }

    // Saare reviews (hatane ke liye DELETE /api/reviews/{id} pehle se hai)
    [HttpGet("reviews")]
    public async Task<IActionResult> Reviews()
    {
        var reviews = await (
            from rv in db.Reviews
            join room in db.Rooms on rv.RoomId equals room.Id
            join h in db.Hotels on room.HotelId equals h.Id
            join u in db.Users on rv.UserId equals u.Id
            orderby rv.Id descending
            select new
            {
                rv.Id,
                rv.Rating,
                rv.Comment,
                rv.CreatedAt,
                hotelName = h.Name,
                roomNumber = room.RoomNumber,
                reviewer = u.Name
            }).ToListAsync();

        return Ok(reviews);
    }
}