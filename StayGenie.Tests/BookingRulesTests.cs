using StayGenie.Data;

namespace StayGenie.Services;

// Booking ke saare rules ek jagah: controllers aur tests dono isi ko use karenge
public static class BookingRules
{
    public const int MaxNights = 30;

    // Pakistan time (UTC+5, DST nahi), server ka time zone kuch bhi ho
    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5));

    public static int Nights(DateOnly checkIn, DateOnly checkOut) => checkOut.DayNumber - checkIn.DayNumber;

    public static decimal TotalPrice(decimal pricePerNight, DateOnly checkIn, DateOnly checkOut) =>
        Nights(checkIn, checkOut) * pricePerNight;

    // Error message wapas deta hai, ya null agar dates theek hain
    public static string? ValidateDates(DateOnly checkIn, DateOnly checkOut, DateOnly today)
    {
        if (checkIn < today) return "Check-in date is in the past.";
        if (checkOut <= checkIn) return "Check-out must be after check-in.";
        if (Nights(checkIn, checkOut) > MaxNights) return $"Maximum stay is {MaxNights} nights.";
        return null;
    }

    public static string? ValidateGuests(int guests, int maxOccupancy, string roomNumber)
    {
        if (guests < 1 || guests > maxOccupancy)
            return $"Room {roomNumber} allows max {maxOccupancy} guests.";
        return null;
    }

    // Cancel tab ho sakti hai jab booking active ho aur check-in aaj ke baad ho
    // (yani check-in se kam az kam ek din pehle)
    public static bool CanCancel(ReservationStatus status, DateOnly checkIn, DateOnly today) =>
        (status == ReservationStatus.Confirmed || status == ReservationStatus.PendingPayment)
        && checkIn > today;

    // Review tab likh sakte hain jab booking confirmed ho, stay khatam ho chuki ho
    // aur is booking par pehle review na diya ho
    public static bool CanReview(ReservationStatus status, DateOnly checkOut, DateOnly today, bool alreadyReviewed) =>
        status == ReservationStatus.Confirmed && checkOut <= today && !alreadyReviewed;
}