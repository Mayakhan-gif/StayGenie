using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using StayGenie.Data;

namespace StayGenie.Services;

public class AdyenPaymentService(
    HttpClient http,
    IOptions<AdyenOptions> options,
    ILogger<AdyenPaymentService> logger) : IPaymentService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<PaymentLinkResult> CreatePaymentLinkAsync(
        Reservation reservation, string roomNumber, int nights, CancellationToken ct = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.ApiKey) || string.IsNullOrWhiteSpace(o.MerchantAccount))
            throw new InvalidOperationException("Adyen:ApiKey or Adyen:MerchantAccount is not configured.");

        var minutes = Math.Max(o.LinkMinutes, 5);

        var body = new
        {
            // Webhook isi reference se booking pehchanta hai
            reference = $"StayGenie-{reservation.Id}",
            // Adyen sab se chhoti unit (paisa) mein amount leta hai
            amount = new { value = (long)Math.Round(reservation.TotalPrice * 100m, 0), currency = o.Currency },
            merchantAccount = o.MerchantAccount,
            description = $"Room {roomNumber}, {nights} night(s) - StayGenie",
            expiresAt = DateTime.UtcNow.AddMinutes(minutes).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            shopperEmail = string.IsNullOrEmpty(reservation.GuestEmail) ? null : reservation.GuestEmail,
            returnUrl = o.ReturnUrl
        };

        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"{o.CheckoutUrl.TrimEnd('/')}/{o.CheckoutVersion}/paymentLinks")
        {
            Content = JsonContent.Create(body, options: Json)
        };
        req.Headers.Add("X-API-Key", o.ApiKey);

        using var res = await http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            // Adyen ka error body log mein, API key kabhi nahi
            logger.LogError("Adyen payment link failed: {Status} {Body}", (int)res.StatusCode, text);
            throw new InvalidOperationException($"Adyen returned {(int)res.StatusCode}.");
        }

        using var doc = JsonDocument.Parse(text);
        var id = doc.RootElement.GetProperty("id").GetString()!;
        var url = doc.RootElement.GetProperty("url").GetString()!;
        return new PaymentLinkResult(id, url, minutes);
    }
}