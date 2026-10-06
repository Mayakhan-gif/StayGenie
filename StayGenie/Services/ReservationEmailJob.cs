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
}