using Microsoft.EntityFrameworkCore;

namespace StayGenie.Data;

public class HotelDbContext(DbContextOptions<HotelDbContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Room>().Property(r => r.PricePerNight).HasPrecision(10, 2);

        b.Entity<Reservation>(r =>
        {
            r.Property(x => x.TotalPrice).HasPrecision(10, 2);
            r.Property(x => x.GuestEmail).HasMaxLength(256);

            // Status database mein text ("Confirmed") ke tor par save hoga.
            // Default Confirmed isliye, ke purani bookings Confirmed ho jayein.
            r.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(ReservationStatus.Confirmed);

            r.HasIndex(x => x.UserId);
            r.HasOne<User>().WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<User>(u =>
        {
            u.Property(x => x.Name).HasMaxLength(100);
            u.Property(x => x.Email).HasMaxLength(256);
            u.HasIndex(x => x.Email).IsUnique();
        });
    }
}