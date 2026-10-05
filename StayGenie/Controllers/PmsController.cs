using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayGenie.Data;

[ApiController]
[Route("api/pms")]
public class PmsController(HotelDbContext db) : ControllerBase
{
    public record BookingRequest(int RoomId, DateOnly CheckIn, DateOnly CheckOut, int Guests, string GuestName);

    // Public: chatbot (MCP tool) availability check yahin se karta hai
    [HttpGet("availability")]
    public async Task<IActionResult> Availability(int roomId, DateOnly checkIn, DateOnly checkOut)
    {
        var taken = await db.Reservations.AnyAsync(r =>
            r.RoomId == roomId && r.CheckIn < checkOut && checkIn < r.CheckOut);
        return Ok(new { roomId, checkIn, checkOut, available = !taken });
    }

    // Sirf Admin: is mein sab guests ka data hota hai
    [HttpGet("reservations")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> List() =>
        Ok(await db.Reservations.Include(r => r.Room).OrderByDescending(r => r.Id).ToListAsync());

    // Public: chatbot (MCP tool) booking yahin se banata hai
    [HttpPost("reservations")]
    public async Task<IActionResult> Create(BookingRequest b)
    {
        if (b.CheckIn < DateOnly.FromDateTime(DateTime.Today)) return BadRequest("Check-in date is in the past.");
        if (b.CheckOut <= b.CheckIn) return BadRequest("Check-out must be after check-in.");
        if (string.IsNullOrWhiteSpace(b.GuestName)) return BadRequest("Guest name is required.");

        var room = await db.Rooms.FindAsync(b.RoomId);
        if (room is null) return NotFound("Room not found.");
        if (b.Guests < 1 || b.Guests > room.MaxOccupancy)
            return BadRequest($"Room {room.RoomNumber} allows max {room.MaxOccupancy} guests.");

        var taken = await db.Reservations.AnyAsync(r =>
            r.RoomId == b.RoomId && r.CheckIn < b.CheckOut && b.CheckIn < r.CheckOut);
        if (taken) return Conflict("Room is no longer available for these dates.");

        var nights = b.CheckOut.DayNumber - b.CheckIn.DayNumber;
        var res = new Reservation
        {
            RoomId = b.RoomId,
            CheckIn = b.CheckIn,
            CheckOut = b.CheckOut,
            Guests = b.Guests,
            GuestName = b.GuestName.Trim(),
            TotalPrice = nights * room.PricePerNight
        };
        db.Reservations.Add(res);
        await db.SaveChangesAsync();
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