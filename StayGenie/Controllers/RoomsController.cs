using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayGenie.Data;
using StayGenie.Services;

[ApiController]
[Route("api/rooms")]
public class RoomsController(HotelDbContext db, RoomSearchService search) : ControllerBase
{
    public record RoomDto(string RoomNumber, string Type, string Description,
        int MaxOccupancy, decimal PricePerNight, string Amenities);

    // Public: rooms dekhne ke liye login zaroori nahi
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await db.Rooms.ToListAsync());

    // Sirf Admin
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(RoomDto dto)
    {
        if (await db.Rooms.AnyAsync(r => r.RoomNumber == dto.RoomNumber))
            return Conflict("Room number already exists.");

        var room = new Room
        {
            RoomNumber = dto.RoomNumber,
            Type = dto.Type,
            Description = dto.Description,
            MaxOccupancy = dto.MaxOccupancy,
            PricePerNight = dto.PricePerNight,
            Amenities = dto.Amenities
        };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        await search.IndexRoomsAsync();
        return Ok(room);
    }

    // Sirf Admin
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var room = await db.Rooms.FindAsync(id);
        if (room is null) return NotFound();
        if (await db.Reservations.AnyAsync(r => r.RoomId == id))
            return Conflict("Room has reservations and cannot be deleted.");
        db.Rooms.Remove(room);
        await db.SaveChangesAsync();
        await search.IndexRoomsAsync();
        return NoContent();
    }
}