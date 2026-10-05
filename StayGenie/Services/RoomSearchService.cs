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

    // SQL Server ke rooms ko Qdrant mein daalta hai
    public async Task IndexRoomsAsync()
    {
        var collections = await qdrant.ListCollectionsAsync();
        if (collections.Contains(Collection))
            await qdrant.DeleteCollectionAsync(Collection);

        await qdrant.CreateCollectionAsync(Collection,
            new VectorParams { Size = 384, Distance = Distance.Cosine });

        var rooms = await db.Rooms.ToListAsync();
        var points = rooms.Select(r =>
        {
            var text = $"{r.Type} room. {r.Description} Amenities: {r.Amenities}";
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
                    ["description"] = r.Description
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
            (decimal)h.Payload["price"].DoubleValue)).ToList();
    }
}

public record RoomResult(int RoomId, string RoomNumber, string Type,
    string Description, string Amenities, int MaxOccupancy, decimal PricePerNight);