using System.Text.Json.Serialization;

namespace StayGenie.Data;

// Booking ki halat. Values 1 se shuru hain, 0 jaan bhujh kar khali hai.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReservationStatus
{
    Confirmed = 1,
    PendingPayment = 2,
    Cancelled = 3,
    Expired = 4
}

public class Reservation
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public Room? Room { get; set; }
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public int Guests { get; set; }
    public string GuestName { get; set; } = "";

    // Confirmation/reminder email yahan jayegi. Null = purani booking ya email nahi di gayi.
    public string? GuestEmail { get; set; }

    public decimal TotalPrice { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ReservationStatus Status { get; set; } = ReservationStatus.Confirmed;

    // Reminder email kab gayi. Null = abhi nahi gayi. Isi se duplicate reminder rukti hai.
    public DateTime? ReminderSentAt { get; set; }

    // Kis user ki booking hai. Null = purani ya bina login wali booking.
    // Jaan bhujh kar User navigation property nahi rakhi, warna API se PasswordHash bhi nikal sakta tha.
    public int? UserId { get; set; }
}