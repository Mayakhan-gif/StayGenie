using System.ComponentModel;
using System.Net.Http.Json;
using ModelContextProtocol.Server;

namespace StayGenie.Services;

[McpServerToolType]
public class ReservationTools(IHttpClientFactory httpFactory)
{
    private HttpClient Pms => httpFactory.CreateClient("Pms");

    [McpServerTool, Description("Live check via the PMS Reservation API: is this room free for these dates? Dates format: yyyy-MM-dd.")]
    public async Task<string> CheckAvailability(int roomId, string checkIn, string checkOut)
    {
        var res = await Pms.GetAsync($"/api/pms/availability?roomId={roomId}&checkIn={checkIn}&checkOut={checkOut}");
        if (!res.IsSuccessStatusCode) return "Could not check availability right now.";
        var body = await res.Content.ReadFromJsonAsync<AvailabilityResponse>();
        return body!.available ? "AVAILABLE" : "NOT_AVAILABLE";
    }

    [McpServerTool, Description("Creates a reservation via the PMS Reservation API and returns a payment link. The reservation is NOT confirmed until the guest pays. Only call after the user explicitly confirmed. Dates format: yyyy-MM-dd. guestEmail is the email for the confirmation message; leave it empty if the user is logged in. Never set userId yourself; the system fills it in automatically.")]
    public async Task<string> CreateReservation(
        int roomId, string checkIn, string checkOut, int guests, string guestName,
        string guestEmail = "", int userId = 0)
    {
        var res = await Pms.PostAsJsonAsync("/api/pms/reservations", new
        {
            roomId,
            checkIn = DateOnly.Parse(checkIn),
            checkOut = DateOnly.Parse(checkOut),
            guests,
            guestName,
            guestEmail = string.IsNullOrWhiteSpace(guestEmail) ? null : guestEmail.Trim(),
            userId = userId > 0 ? userId : (int?)null
        });

        if (res.IsSuccessStatusCode)
        {
            var ok = await res.Content.ReadFromJsonAsync<CreatedResponse>();
            return $"PENDING_PAYMENT: Reservation ID {ok!.id}, Room {ok.roomNumber}, {ok.nights} night(s), " +
                   $"total PKR {ok.totalPrice:N0}. Not confirmed until paid. " +
                   $"Held for {ok.holdMinutes} minutes. Payment link: {ok.paymentUrl}";
        }

        var error = await res.Content.ReadAsStringAsync();
        return $"FAILED: {error}";
    }

    [McpServerTool, Description("Looks up one reservation via the PMS Reservation API and returns its real details, including status. Requires BOTH the reservation ID and the guest name used when booking. Ask the user for both if missing.")]
    public async Task<string> GetReservation(int reservationId, string guestName)
    {
        var res = await Pms.GetAsync(
            $"/api/pms/reservations/{reservationId}?guestName={Uri.EscapeDataString(guestName)}");

        if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            return "No reservation found matching that ID and guest name.";
        if (!res.IsSuccessStatusCode)
            return "Could not look up the reservation right now.";

        var found = await res.Content.ReadFromJsonAsync<ReservationView>();
        return $"Reservation ID {found!.id}: status {found.status}, Room {found.roomNumber}, {found.guestName}, " +
               $"{found.checkIn} to {found.checkOut}, total PKR {found.totalPrice:N0}.";
    }

    private record ReservationView(int id, string roomNumber, string guestName, string checkIn, string checkOut, decimal totalPrice, string status);

    private record AvailabilityResponse(bool available);
    private record CreatedResponse(int id, string roomNumber, int nights, decimal totalPrice, string status, string paymentUrl, int holdMinutes);
}