using fix_my_device_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace fix_my_device_backend.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceDrive> DeviceDrives => Set<DeviceDrive>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.HasIndex(user => user.Email).IsUnique();
            entity.Property(user => user.Email).IsRequired();
            entity.Property(user => user.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasKey(device => device.Id);
            entity.Property(device => device.DeviceName).IsRequired();
            entity.Property(device => device.DeviceId).IsRequired();
            entity.Property(device => device.Status).IsRequired();
            entity.HasIndex(device => new { device.UserId, device.DeviceId }).IsUnique();

            entity.HasOne(device => device.User)
                .WithMany(user => user.Devices)
                .HasForeignKey(device => device.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(device => device.Drives)
                .WithOne(drive => drive.Device)
                .HasForeignKey(drive => drive.DeviceEntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceDrive>(entity =>
        {
            entity.HasKey(drive => drive.Id);
            entity.Property(drive => drive.DriveLetter).IsRequired();
        });
    }
}
