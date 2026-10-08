using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StayGenie.Data;
using StayGenie.Services;

[ApiController]
[Route("api/payments")]
public class PaymentsController(
    HotelDbContext db,
    IOptions<AdyenOptions> options,
    IBackgroundJobClient jobs,
    ILogger<PaymentsController> logger) : ControllerBase
{
    // Adyen khud call karta hai, is liye login nahi, lekin HMAC signature check zaroori hai.
    [HttpPost("adyen-webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> AdyenWebhook()
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.HmacKey))
        {
            logger.LogError("Adyen:HmacKey is not configured.");
            return StatusCode(500);
        }

        JsonDocument doc;
        try { doc = await JsonDocument.ParseAsync(Request.Body); }
        catch { return BadRequest(); }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("notificationItems", out var items)
                || items.ValueKind != JsonValueKind.Array)
                return BadRequest();

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("NotificationRequestItem", out var n))
                    return BadRequest();

                // Pehle signature: galat ho to Adyen se nahi aaya, kuch bhi mat chhuo
                if (!IsValidSignature(n, o.HmacKey))
                {
                    logger.LogWarning("Adyen webhook signature check failed.");
                    return Unauthorized();
                }

                await HandleAsync(n, o);
            }
        }

        // Adyen ko exactly yehi jawab chahiye, warna wohi event dobara bhejta rahega
        return Content("[accepted]");
    }

    // Adyen ka HMAC: kuch fields ko ':' se jorke, HMAC-SHA256 (hex key se), Base64
    private static bool IsValidSignature(JsonElement n, string hmacKeyHex)
    {
        if (!n.TryGetProperty("additionalData", out var ad)
            || !ad.TryGetProperty("hmacSignature", out var sigEl)
            || !n.TryGetProperty("amount", out var amount)
            || !amount.TryGetProperty("value", out var valueEl)
            || !amount.TryGetProperty("currency", out var currencyEl))
            return false;

        var received = sigEl.GetString() ?? "";

        string S(string name) =>
            n.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";

        var fields = new[]
        {
            S("pspReference"), S("originalReference"), S("merchantAccountCode"), S("merchantReference"),
            valueEl.GetRawText(), currencyEl.GetString() ?? "", S("eventCode"), S("success")
        };

        var payload = string.Join(":", fields.Select(f => f.Replace("\\", "\\\\").Replace(":", "\\:")));

        byte[] key;
        try { key = Convert.FromHexString(hmacKeyHex.Trim()); }
        catch (FormatException) { return false; }

        var computed = Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(received));
    }

    private async Task HandleAsync(JsonElement n, AdyenOptions o)
    {
        var eventCode = n.TryGetProperty("eventCode", out var e) ? e.GetString() : null;
        var success = n.TryGetProperty("success", out var s) ? s.ToString() : "";
        var reference = n.TryGetProperty("merchantReference", out var r) ? r.GetString() ?? "" : "";
        var psp = n.TryGetProperty("pspReference", out var p) ? p.GetString() : null;

        // Hamare apne links ke events hi chahiye (dashboard ka "test webhook" bhi yahan aata hai)
        if (eventCode != "AUTHORISATION" || !reference.StartsWith("StayGenie-")
            || !int.TryParse(reference["StayGenie-".Length..], out var reservationId))
            return;

        var res = await db.Reservations.FirstOrDefaultAsync(x => x.Id == reservationId);
        if (res is null)
        {
            logger.LogWarning("Adyen event for unknown reservation {ReservationId}.", reservationId);
            return;
        }

        if (success != "true")
        {
            // Payment fail: booking Pending hi rahe, guest usi link par dobara koshish kar sakta hai
            logger.LogInformation("Payment failed for reservation {ReservationId}.", res.Id);
            return;
        }

        // Amount aur currency booking se match karni chahiye
        var amount = n.GetProperty("amount");
        var paid = amount.GetProperty("value").GetInt64();
        var currency = amount.GetProperty("currency").GetString();
        var expected = (long)Math.Round(res.TotalPrice * 100m, 0);
        if (paid != expected || !string.Equals(currency, o.Currency, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Amount mismatch for reservation {ReservationId}.", res.Id);
            return;
        }

        // Adyen ek event kai dafa bhej sakta hai: pehle se Confirmed ho to kuch nahi karna
        if (res.Status == ReservationStatus.Confirmed) return;

        if (res.Status == ReservationStatus.Expired)
        {
            // Payment cleanup ke baad aayi: room abhi bhi khali ho to booking bacha lo
            var taken = await db.Reservations.AnyAsync(x =>
                x.Id != res.Id && x.RoomId == res.RoomId
                && x.Status != ReservationStatus.Cancelled && x.Status != ReservationStatus.Expired
                && x.CheckIn < res.CheckOut && res.CheckIn < x.CheckOut);

            if (taken)
            {
                logger.LogWarning("Reservation {ReservationId} paid after expiry and room is taken. Refund needed.", res.Id);
                return;
            }
        }
        else if (res.Status != ReservationStatus.PendingPayment)
        {
            return;
        }

        res.Status = ReservationStatus.Confirmed;
        res.PaidAt = DateTime.UtcNow;
        res.PspReference = psp;
        await db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(res.GuestEmail))
            jobs.Enqueue<ReservationEmailJob>(j => j.SendConfirmationAsync(res.Id));

        logger.LogInformation("Reservation {ReservationId} paid and confirmed.", res.Id);
    }
}