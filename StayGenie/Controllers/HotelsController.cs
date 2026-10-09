using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayGenie.Data;
using StayGenie.Services;

[ApiController]
[Route("api/hotels")]
public class HotelsController(HotelDbContext db, RoomSearchService search) : ControllerBase
{
    public record HotelDto(
        [Required, StringLength(150)] string Name,
        [Required, StringLength(100)] string City,
        [Required, StringLength(300)] string Address,
        string Description = "",
        [StringLength(30)] string Phone = "",
        string Amenities = "");

    // Public: sirf active hotels, optional city filter (?city=Islamabad)
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? city)
    {
        var query = db.Hotels.Where(h => h.IsActive);
        if (!string.IsNullOrWhiteSpace(city))
            query = query.Where(h => h.City == city);

        var hotels = await query
            .OrderBy(h => h.Name)
            .Select(h => new
            {
                h.Id,
                h.Name,
                h.City,
                h.Address,
                h.Description,
                h.Phone,
                h.Amenities,
                RoomCount = db.Rooms.Count(r => r.HotelId == h.Id && r.IsActive),
                AverageRating = db.Reviews
                    .Where(rv => db.Rooms.Any(r => r.Id == rv.RoomId && r.HotelId == h.Id))
                    .Average(rv => (double?)rv.Rating),
                ReviewCount = db.Reviews
                    .Count(rv => db.Rooms.Any(r => r.Id == rv.RoomId && r.HotelId == h.Id))
            })
            .ToListAsync();

        return Ok(hotels);
    }

    // Public: ek hotel aur uske active rooms (har room ki rating ke saath)
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var hotel = await db.Hotels
            .Where(h => h.Id == id && h.IsActive)
            .Select(h => new
            {
                h.Id,
                h.Name,
                h.City,
                h.Address,
                h.Description,
                h.Phone,
                h.Amenities,
                AverageRating = db.Reviews
                    .Where(rv => db.Rooms.Any(r => r.Id == rv.RoomId && r.HotelId == h.Id))
                    .Average(rv => (double?)rv.Rating),
                ReviewCount = db.Reviews
                    .Count(rv => db.Rooms.Any(r => r.Id == rv.RoomId && r.HotelId == h.Id)),
                Rooms = db.Rooms
                    .Where(r => r.HotelId == h.Id && r.IsActive)
                    .OrderBy(r => r.RoomNumber)
                    .Select(r => new
                    {
                        r.Id,
                        r.RoomNumber,
                        r.Type,
                        r.Description,
                        r.MaxOccupancy,
                        r.PricePerNight,
                        r.Amenities,
                        AverageRating = db.Reviews
                            .Where(rv => rv.RoomId == r.Id)
                            .Average(rv => (double?)rv.Rating)
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        return hotel is null ? NotFound() : Ok(hotel);
    }

    // Sirf Admin
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(HotelDto dto)
    {
        var hotel = new Hotel
        {
            Name = dto.Name.Trim(),
            City = dto.City.Trim(),
            Address = dto.Address.Trim(),
            Description = dto.Description,
            Phone = dto.Phone,
            Amenities = dto.Amenities
        };
        db.Hotels.Add(hotel);
        await db.SaveChangesAsync();
        await search.IndexRoomsAsync();
        return CreatedAtAction(nameof(GetById), new { id = hotel.Id }, hotel);
    }

    // Sirf Admin
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, HotelDto dto)
    {
        var hotel = await db.Hotels.FindAsync(id);
        if (hotel is null) return NotFound();

        hotel.Name = dto.Name.Trim();
        hotel.City = dto.City.Trim();
        hotel.Address = dto.Address.Trim();
        hotel.Description = dto.Description;
        hotel.Phone = dto.Phone;
        hotel.Amenities = dto.Amenities;

        await db.SaveChangesAsync();
        await search.IndexRoomsAsync();
        return Ok(hotel);
    }

    // Sirf Admin: delete nahi, deactivate (purani bookings ka record bacha rahe)
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var hotel = await db.Hotels.FindAsync(id);
        if (hotel is null) return NotFound();

        hotel.IsActive = false;
        await db.SaveChangesAsync();
        await search.IndexRoomsAsync();
        return NoContent();
    }
}