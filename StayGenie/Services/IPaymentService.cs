using StayGenie.Data;

namespace StayGenie.Services;

public record PaymentLinkResult(string LinkId, string Url, int HoldMinutes);

// Baaki code ko Adyen ka pata nahi, sirf is interface ka. Provider badalna aasan, tests mein nakli service.
public interface IPaymentService
{
    Task<PaymentLinkResult> CreatePaymentLinkAsync(
        Reservation reservation, string roomNumber, int nights, CancellationToken ct = default);
}