namespace StayGenie.Data;

public class Review
{
    public int Id { get; set; }

    // Ek booking par ek hi review (unique index DbContext mein)
    public int ReservationId { get; set; }

    public int RoomId { get; set; }
    public int UserId { get; set; }

    // 1 se 5
    public int Rating { get; set; }

    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}