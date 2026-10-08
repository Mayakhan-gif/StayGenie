using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayGenie.Data;
using StayGenie.Services;

[ApiController]
[Route("api/rooms")]
public class RoomsController(HotelDbContext db, RoomSearchService search) : ControllerBase
{
    public record RoomDto(
        [Range(1, int.MaxValue)] int HotelId,
        [Required, StringLength(20)] string RoomNumber,
        [Required, StringLength(50)] string Type,
        string Description,
        [Range(1, 20)] int MaxOccupancy,
        [Range(1, 10000000)] decimal PricePerNight,
        string Amenities,
        bool IsActive = true);

    // Public: sirf active rooms (active hotels ke). Optional: ?hotelId=1
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? hotelId)
    {
        var query = db.Rooms.Where(r => r.IsActive && r.Hotel!.IsActive);
        if (hotelId.HasValue)
            query = query.Where(r => r.HotelId == hotelId.Value);

        var rooms = await query
            .OrderBy(r => r.Hotel!.Name).ThenBy(r => r.RoomNumber)
            .Select(r => new
            {
                r.Id,
                r.RoomNumber,
                r.Type,
                r.Description,
                r.MaxOccupancy,
                r.PricePerNight,
                r.Amenities,
                r.IsActive,
                r.HotelId,
                HotelName = r.Hotel!.Name,
                City = r.Hotel.City
            })
            .ToListAsync();

        return Ok(rooms);
    }

    // Public: ek room ki detail
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var room = await db.Rooms
            .Where(r => r.Id == id && r.IsActive && r.Hotel!.IsActive)
            .Select(r => new
            {
                r.Id,
                r.RoomNumber,
                r.Type,
                r.Description,
                r.MaxOccupancy,
                r.PricePerNight,
                r.Amenities,
                r.IsActive,
                r.HotelId,
                HotelName = r.Hotel!.Name,
                City = r.Hotel.City
            })
            .FirstOrDefaultAsync();

        return room is null ? NotFound() : Ok(room);
    }

    // Sirf Admin
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(RoomDto dto)
    {
        if (!await db.Hotels.AnyAsync(h => h.Id == dto.HotelId && h.IsActive))
            return BadRequest("Hotel not found or inactive.");

        if (await db.Rooms.AnyAsync(r => r.HotelId == dto.HotelId && r.RoomNumber == dto.RoomNumber))
            return Conflict("Room number already exists in this hotel.");

        var room = new Room
        {
            HotelId = dto.HotelId,
            RoomNumber = dto.RoomNumber.Trim(),
            Type = dto.Type,
            Description = dto.Description,
            MaxOccupancy = dto.MaxOccupancy,
            PricePerNight = dto.PricePerNight,
            Amenities = dto.Amenities,
            IsActive = dto.IsActive
        };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        await search.IndexRoomsAsync();
        return CreatedAtAction(nameof(GetById), new { id = room.Id },
            new
            {
                room.Id,
                room.HotelId,
                room.RoomNumber,
                room.Type,
                room.Description,
                room.MaxOccupancy,
                room.PricePerNight,
                room.Amenities,
                room.IsActive
            });
    }

    // Sirf Admin: price, availability (IsActive), details badalne ke liye
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, RoomDto dto)
    {
        var room = await db.Rooms.FindAsync(id);
        if (room is null) return NotFound();

        if (!await db.Hotels.AnyAsync(h => h.Id == dto.HotelId && h.IsActive))
            return BadRequest("Hotel not found or inactive.");

        if (await db.Rooms.AnyAsync(r => r.Id != id && r.HotelId == dto.HotelId && r.RoomNumber == dto.RoomNumber))
            return Conflict("Room number already exists in this hotel.");

        room.HotelId = dto.HotelId;
        room.RoomNumber = dto.RoomNumber.Trim();
        room.Type = dto.Type;
        room.Description = dto.Description;
        room.MaxOccupancy = dto.MaxOccupancy;
        room.PricePerNight = dto.PricePerNight;
        room.Amenities = dto.Amenities;
        room.IsActive = dto.IsActive;

        await db.SaveChangesAsync();
        await search.IndexRoomsAsync();
        return Ok(new
        {
            room.Id,
            room.HotelId,
            room.RoomNumber,
            room.Type,
            room.Description,
            room.MaxOccupancy,
            room.PricePerNight,
            room.Amenities,
            room.IsActive
        });
    }

    // Sirf Admin
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var room = await db.Rooms.FindAsync(id);
        if (room is null) return NotFound();
        if (await db.Reservations.AnyAsync(r => r.RoomId == id))
            return Conflict("Room has reservations and cannot be deleted. Deactivate it instead (PUT with IsActive=false).");
        db.Rooms.Remove(room);
        await db.SaveChangesAsync();
        await search.IndexRoomsAsync();
        return NoContent();
    }
}