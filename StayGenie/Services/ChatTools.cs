using System.ComponentModel;
using System.Text.Json;

namespace StayGenie.Services;

public class ChatTools(RoomSearchService search)
{
    [Description("Search hotel rooms by natural language. Use this whenever the user asks for rooms, views, amenities or prices. Returns up to 5 matching rooms.")]
    public async Task<string> SearchRooms(
        [Description("What the user wants, e.g. 'sea view room with breakfast'")] string query,
        [Description("Number of guests")] int guests,
        [Description("Maximum price per night, or 0 if the user gave no budget")] decimal maxPrice)
    {
        var rooms = await search.SearchAsync(query, guests, maxPrice > 0 ? maxPrice : null);
        return JsonSerializer.Serialize(rooms);
    }
}