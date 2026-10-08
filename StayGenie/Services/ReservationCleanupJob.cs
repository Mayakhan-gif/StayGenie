using Microsoft.EntityFrameworkCore;
using StayGenie.Data;

namespace StayGenie.Services;

// Hangfire ye job har 5 minute mein chalata hai.
// Kaam: jo booking PendingPayment mein bohat der se atki hai, usay Expired kar do,
// taake wo room dobara available ho jaye.
public class ReservationCleanupJob(
    HotelDbContext db,
    IConfiguration config,
    ILogger<ReservationCleanupJob> logger)
{
    public async Task ExpirePendingAsync()
    {
        // Kitne minute baad pending booking expire ho. Config mein na ho to 15.
        var minutes = config.GetValue<int?>("Reservations:PendingMinutes") ?? 15;
        var cutoff = DateTime.UtcNow.AddMinutes(-minutes);

        // Ek hi SQL UPDATE, records memory mein load nahi hote.
        var count = await db.Reservations
            .Where(r => r.Status == ReservationStatus.PendingPayment && r.CreatedAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.Expired));

        if (count > 0)
            logger.LogInformation("Expired {Count} pending reservations older than {Minutes} minutes.", count, minutes);
    }
}