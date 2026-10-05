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

    [McpServerTool, Description("Creates a reservation via the PMS Reservation API. Only call after the user explicitly confirmed. Dates format: yyyy-MM-dd.")]
    public async Task<string> CreateReservation(int roomId, string checkIn, string checkOut, int guests, string guestName)
    {
        var res = await Pms.PostAsJsonAsync("/api/pms/reservations", new
        {
            roomId,
            checkIn = DateOnly.Parse(checkIn),
            checkOut = DateOnly.Parse(checkOut),
            guests,
            guestName
        });

        if (res.IsSuccessStatusCode)
        {
            var ok = await res.Content.ReadFromJsonAsync<CreatedResponse>();
            return $"CONFIRMED: Reservation ID {ok!.id}, Room {ok.roomNumber}, {ok.nights} night(s), total PKR {ok.totalPrice:N0}.";
        }

        var error = await res.Content.ReadAsStringAsync();
        return $"FAILED: {error}";
    }
    [McpServerTool, Description("Looks up a reservation by its ID via the PMS Reservation API and returns its real status.")]
    public async Task<string> GetReservation(int reservationId)
    {
        var res = await Pms.GetAsync($"/api/pms/reservations");
        if (!res.IsSuccessStatusCode) return "Could not look up reservations right now.";
        var list = await res.Content.ReadFromJsonAsync<List<ReservationView>>();
        var found = list?.FirstOrDefault(r => r.id == reservationId);
        if (found is null) return $"No reservation found with ID {reservationId}.";
        return $"Reservation ID {found.id}: Room {found.room?.roomNumber}, {found.guestName}, " +
               $"{found.checkIn} to {found.checkOut}, total PKR {found.totalPrice:N0}.";
    }

    private record ReservationView(int id, string guestName, string checkIn, string checkOut, decimal totalPrice, RoomView? room);
    private record RoomView(string roomNumber);

    private record AvailabilityResponse(bool available);
    private record CreatedResponse(int id, string roomNumber, int nights, decimal totalPrice);
}