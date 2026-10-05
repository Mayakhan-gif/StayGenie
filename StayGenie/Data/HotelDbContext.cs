using Microsoft.EntityFrameworkCore;

namespace StayGenie.Data;

public class HotelDbContext(DbContextOptions<HotelDbContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Room>().Property(r => r.PricePerNight).HasPrecision(10, 2);
        b.Entity<Reservation>().Property(r => r.TotalPrice).HasPrecision(10, 2);
    }
}