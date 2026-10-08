using Microsoft.EntityFrameworkCore;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SmartComponents.LocalEmbeddings;
using StayGenie.Data;

namespace StayGenie.Services;

public class RoomSearchService(HotelDbContext db, QdrantClient qdrant)
{
    private const string Collection = "rooms";
    private static readonly LocalEmbedder Embedder = new();

    // SQL Server ke active rooms ko Qdrant mein daalta hai
    public async Task IndexRoomsAsync()
    {
        var collections = await qdrant.ListCollectionsAsync();
        if (collections.Contains(Collection))
            await qdrant.DeleteCollectionAsync(Collection);

        await qdrant.CreateCollectionAsync(Collection,
            new VectorParams { Size = 384, Distance = Distance.Cosine });

        // Sirf active rooms jo active hotels ke hain
        var rooms = await db.Rooms
            .Include(r => r.Hotel)
            .Where(r => r.IsActive && r.Hotel!.IsActive)
            .ToListAsync();

        var points = rooms.Select(r =>
        {
            var hotelName = r.Hotel!.Name;
            var city = r.Hotel.City;
            var text = $"{r.Type} room at {hotelName} in {city}. {r.Description} Amenities: {r.Amenities}";
            var vector = Embedder.Embed(text).Values.ToArray();
            return new PointStruct
            {
                Id = (ulong)r.Id,
                Vectors = vector,
                Payload =
                {
                    ["roomId"] = r.Id,
                    ["roomNumber"] = r.RoomNumber,
                    ["type"] = r.Type,
                    ["maxOccupancy"] = r.MaxOccupancy,
                    ["price"] = (double)r.PricePerNight,
                    ["amenities"] = r.Amenities,
                    ["description"] = r.Description,
                    ["hotelName"] = hotelName,
                    ["city"] = city
                }
            };
        }).ToList();

        if (points.Count > 0)
            await qdrant.UpsertAsync(Collection, points);
    }

    // Query ko vector bana kar milte julte rooms nikalta hai
    public async Task<List<RoomResult>> SearchAsync(string query, int guests, decimal? maxPrice)
    {
        var vector = Embedder.Embed(query).Values.ToArray();

        var filter = new Filter();
        filter.Must.Add(new Condition
        {
            Field = new FieldCondition
            {
                Key = "maxOccupancy",
                Range = new Qdrant.Client.Grpc.Range { Gte = guests }
            }
        });
        if (maxPrice.HasValue)
        {
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "price",
                    Range = new Qdrant.Client.Grpc.Range { Lte = (double)maxPrice.Value }
                }
            });
        }

        var hits = await qdrant.SearchAsync(Collection, vector, filter: filter, limit: 5);

        return hits.Select(h => new RoomResult(
            (int)h.Payload["roomId"].IntegerValue,
            h.Payload["roomNumber"].StringValue,
            h.Payload["type"].StringValue,
            h.Payload["description"].StringValue,
            h.Payload["amenities"].StringValue,
            (int)h.Payload["maxOccupancy"].IntegerValue,
            (decimal)h.Payload["price"].DoubleValue,
            h.Payload.TryGetValue("hotelName", out var hn) ? hn.StringValue : "",
            h.Payload.TryGetValue("city", out var ct) ? ct.StringValue : "")).ToList();
    }
}

// HotelName aur City naye hain, default "" isliye purana code toote ga nahi
public record RoomResult(int RoomId, string RoomNumber, string Type,
    string Description, string Amenities, int MaxOccupancy, decimal PricePerNight,
    string HotelName = "", string City = "");