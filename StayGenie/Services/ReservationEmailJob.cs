using System.Net;
using Microsoft.EntityFrameworkCore;
using StayGenie.Data;

namespace StayGenie.Services;

// Hangfire ye jobs background mein chalata hai.
public class ReservationEmailJob(
    HotelDbContext db,
    IEmailService email,
    ILogger<ReservationEmailJob> logger)
{
    // Payment hone ke baad, ek dafa (fire-and-forget)
    public async Task SendConfirmationAsync(int reservationId)
    {
        var res = await db.Reservations.Include(r => r.Room)
            .FirstOrDefaultAsync(r => r.Id == reservationId);

        // Booking delete/cancel ho gayi ya email di hi nahi gayi: chup chap skip
        if (res is null || string.IsNullOrEmpty(res.GuestEmail) || res.Status != ReservationStatus.Confirmed)
            return;

        var nights = res.CheckOut.DayNumber - res.CheckIn.DayNumber;

        // Guest ka naam HTML mein jaa raha hai, isliye encode karna zaroori hai
        var name = WebUtility.HtmlEncode(res.GuestName);

        var body = $"""
            <h2>Your StayGenie booking is confirmed</h2>
            <p>Dear {name},</p>
            <p>Thank you for booking with StayGenie. Your details:</p>
            <ul>
              <li>Reservation ID: <b>{res.Id}</b></li>
              <li>Room: {WebUtility.HtmlEncode(res.Room?.RoomNumber ?? "-")}</li>
              <li>Check-in: {res.CheckIn:dddd, d MMMM yyyy}</li>
              <li>Check-out: {res.CheckOut:dddd, d MMMM yyyy}</li>
              <li>Nights: {nights}</li>
              <li>Guests: {res.Guests}</li>
              <li>Total: PKR {res.TotalPrice:N0}</li>
            </ul>
            <p>Please keep your reservation ID and name to look up your booking later.</p>
            """;

        await email.SendAsync(res.GuestEmail, $"Booking confirmed (ID {res.Id})", body);

        // Log mein email address nahi, sirf booking ID
        logger.LogInformation("Confirmation email sent for reservation {ReservationId}.", res.Id);
    }

    // Roz ek dafa (recurring): kal jin ka check-in hai unko reminder
    public async Task SendRemindersAsync()
    {
        var tomorrow = DateOnly.FromDateTime(DateTime.Today.AddDays(1));

        var due = await db.Reservations.Include(r => r.Room)
            .Where(r => r.Status == ReservationStatus.Confirmed
                        && r.CheckIn == tomorrow
                        && r.GuestEmail != null
                        && r.ReminderSentAt == null)
            .ToListAsync();

        var sent = 0;
        foreach (var res in due)
        {
            try
            {
                var body = $"""
                    <h2>Your stay starts tomorrow</h2>
                    <p>Dear {WebUtility.HtmlEncode(res.GuestName)},</p>
                    <p>This is a reminder that your check-in at StayGenie is <b>tomorrow, {res.CheckIn:dddd, d MMMM yyyy}</b>.</p>
                    <ul>
                      <li>Reservation ID: <b>{res.Id}</b></li>
                      <li>Room: {WebUtility.HtmlEncode(res.Room?.RoomNumber ?? "-")}</li>
                      <li>Check-out: {res.CheckOut:dddd, d MMMM yyyy}</li>
                    </ul>
                    <p>We look forward to welcoming you.</p>
                    """;

                await email.SendAsync(res.GuestEmail!, $"Reminder: check-in tomorrow (ID {res.Id})", body);

                // Email jaane ke BAAD hi mark karo, warna fail hone par reminder kabhi nahi jati
                res.ReminderSentAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                sent++;
            }
            catch (Exception ex)
            {
                // Ek guest ki email fail ho to baaki ko roke nahi. Agle run par dobara koshish hogi.
                logger.LogWarning(ex, "Reminder email failed for reservation {ReservationId}.", res.Id);
            }
        }

        if (due.Count > 0)
            logger.LogInformation("Reminders sent: {Sent} of {Total}.", sent, due.Count);
    }
}