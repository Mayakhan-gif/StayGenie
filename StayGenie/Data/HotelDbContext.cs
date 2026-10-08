using Microsoft.EntityFrameworkCore;

namespace StayGenie.Data;

public class HotelDbContext(DbContextOptions<HotelDbContext> options) : DbContext(options)
{
    public DbSet<Hotel> Hotels => Set<Hotel>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Review> Reviews => Set<Review>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Hotel>(h =>
        {
            h.Property(x => x.Name).HasMaxLength(150);
            h.Property(x => x.City).HasMaxLength(100);
            h.Property(x => x.Address).HasMaxLength(300);
            h.Property(x => x.Phone).HasMaxLength(30);
            h.Property(x => x.IsActive).HasDefaultValue(true);
            h.HasIndex(x => x.City);
        });

        b.Entity<Room>(r =>
        {
            r.Property(x => x.PricePerNight).HasPrecision(10, 2);
            // Default true zaroori hai, warna purane rooms inactive ho jayenge
            r.Property(x => x.IsActive).HasDefaultValue(true);

            // Hotel delete hone par uske rooms khud delete na hon
            r.HasOne(x => x.Hotel).WithMany()
                .HasForeignKey(x => x.HotelId)
                .OnDelete(DeleteBehavior.Restrict);

            // Ek hotel mein ek room number sirf ek baar
            r.HasIndex(x => new { x.HotelId, x.RoomNumber }).IsUnique();
        });

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

        b.Entity<Review>(r =>
        {
            r.Property(x => x.Comment).HasMaxLength(1000);

            // Ek booking par sirf ek review: duplicate ko database khud rokta hai
            r.HasIndex(x => x.ReservationId).IsUnique();
            r.HasIndex(x => x.RoomId);

            // Booking delete ho to uska review bhi
            r.HasOne<Reservation>().WithMany()
                .HasForeignKey(x => x.ReservationId)
                .OnDelete(DeleteBehavior.Cascade);

            // Room/User ke saath Restrict: SQL Server mein multiple cascade paths ka error na aaye
            r.HasOne<Room>().WithMany()
                .HasForeignKey(x => x.RoomId)
                .OnDelete(DeleteBehavior.Restrict);
            r.HasOne<User>().WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}