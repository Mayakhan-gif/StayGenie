namespace StayGenie.Data;

public class Room
{
    public int Id { get; set; }
    public string RoomNumber { get; set; } = "";
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public int MaxOccupancy { get; set; }
    public decimal PricePerNight { get; set; }
    public string Amenities { get; set; } = "";
}