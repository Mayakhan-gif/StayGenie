namespace StayGenie.Services;

// "Adyen" section se bharta hai. Keys user-secrets se aati hain, yahan koi secret nahi.
public class AdyenOptions
{
    public string ApiKey { get; set; } = "";           // user-secrets
    public string ClientKey { get; set; } = "";        // user-secrets (Drop-in ke liye, baad mein)
    public string MerchantAccount { get; set; } = "";  // user-secrets
    public string HmacKey { get; set; } = "";          // user-secrets (webhook ke step mein)

    public string CheckoutUrl { get; set; } = "https://checkout-test.adyen.com";
    public string CheckoutVersion { get; set; } = "v71";
    public string Currency { get; set; } = "PKR";
    public int LinkMinutes { get; set; } = 30;
    public string ReturnUrl { get; set; } = "http://localhost:5173/?payment=done";
}