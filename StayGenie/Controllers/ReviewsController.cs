using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayGenie.Data;

[ApiController]
[Route("api/reviews")]
public class ReviewsController(HotelDbContext db) : ControllerBase
{
    public record CreateReviewRequest(int ReservationId, int Rating, string? Comment);

    // Login wala customer, apni mukammal stay par review
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create(CreateReviewRequest req)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        if (req.Rating is < 1 or > 5)
            return BadRequest("Rating must be between 1 and 5.");

        var comment = req.Comment?.Trim();
        if (comment is { Length: > 1000 })
            return BadRequest("Comment is too long (max 1000 characters).");

        // Apni hi booking: dusre ki ho ya na mile to same jawab
        var res = await db.Reservations
            .FirstOrDefaultAsync(r => r.Id == req.ReservationId && r.UserId == userId);
        if (res is null) return NotFound("Reservation not found.");

        if (res.Status != ReservationStatus.Confirmed)
            return BadRequest("Only confirmed bookings can be reviewed.");

        // Stay mukammal ho chuki ho (check-out guzar chuka ho)
        if (res.CheckOut > DateOnly.FromDateTime(DateTime.Today))
            return BadRequest("You can review only after your stay is completed.");

        if (await db.Reviews.AnyAsync(r => r.ReservationId == res.Id))
            return Conflict("You have already reviewed this booking.");

        var review = new Review
        {
            ReservationId = res.Id,
            RoomId = res.RoomId,
            UserId = userId,
            Rating = req.Rating,
            Comment = string.IsNullOrEmpty(comment) ? null : comment
        };
        db.Reviews.Add(review);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Do request ek saath aayein to unique index doosri ko rok deta hai
            return Conflict("You have already reviewed this booking.");
        }

        return Created($"/api/reviews/room/{review.RoomId}",
            new { review.Id, review.RoomId, review.Rating, review.Comment, review.CreatedAt });
    }

    // Public: ek room ke reviews aur average
    [HttpGet("room/{roomId:int}")]
    public async Task<IActionResult> ForRoom(int roomId)
    {
        var reviews = await db.Reviews
            .Where(r => r.RoomId == roomId)
            .OrderByDescending(r => r.Id)
            .Join(db.Users, r => r.UserId, u => u.Id, (r, u) => new
            {
                r.Id,
                r.Rating,
                r.Comment,
                r.CreatedAt,
                reviewer = u.Name   // sirf naam, email ya hash kabhi nahi
            })
            .ToListAsync();

        var average = reviews.Count == 0 ? 0 : Math.Round(reviews.Average(r => r.Rating), 1);
        return Ok(new { roomId, count = reviews.Count, average, reviews });
    }

    // Sirf Admin: nazebah review hatana
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var review = await db.Reviews.FindAsync(id);
        if (review is null) return NotFound();
        db.Reviews.Remove(review);
        await db.SaveChangesAsync();
        return NoContent();
    }
}