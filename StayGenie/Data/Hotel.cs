namespace StayGenie.Data;

public class Hotel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string Address { get; set; } = "";
    public string Description { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Amenities { get; set; } = "";

    // Delete ki jagah deactivate: purani bookings ka record bacha rehta hai
    public bool IsActive { get; set; } = true;
}