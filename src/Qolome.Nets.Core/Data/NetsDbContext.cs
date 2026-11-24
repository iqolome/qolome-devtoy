using Microsoft.EntityFrameworkCore;

namespace Qolome.Nets.Core.Data;

public sealed class NetsDbContext : DbContext
{
    public NetsDbContext(DbContextOptions<NetsDbContext> options) : base(options)
    {
    }

    public DbSet<NetworkEnvironment> Environments => Set<NetworkEnvironment>();
    public DbSet<DeviceGroup> Groups => Set<DeviceGroup>();
    public DbSet<DeviceType> DeviceTypes => Set<DeviceType>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceDomain> DeviceDomains => Set<DeviceDomain>();
    public DbSet<DeviceNic> DeviceNics => Set<DeviceNic>();
    public DbSet<PortPurpose> PortPurposes => Set<PortPurpose>();
    public DbSet<OuiEntry> OuiEntries => Set<OuiEntry>();
    public DbSet<OuiMeta> OuiMetas => Set<OuiMeta>();
    public DbSet<NetworkTopology> Topologies => Set<NetworkTopology>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NetworkEnvironment>(e =>
        {
            e.ToTable(NetsTableNames.NetworkEnvironment);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(64).IsRequired();
            e.Property(x => x.Description).HasMaxLength(256);
            e.Property(x => x.DefaultCidr).HasMaxLength(64);
        });

        modelBuilder.Entity<DeviceGroup>(e =>
        {
            e.ToTable(NetsTableNames.DeviceGroup);
            e.HasIndex(x => new { x.NetworkEnvironmentId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.NetworkEnvironment)
                .WithMany(x => x.Groups)
                .HasForeignKey(x => x.NetworkEnvironmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeviceType>(e =>
        {
            e.ToTable(NetsTableNames.DeviceType);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32).IsRequired();
            e.Property(x => x.Name).HasMaxLength(64).IsRequired();
            e.Property(x => x.Description).HasMaxLength(256);
            e.Property(x => x.IconFile).HasMaxLength(128);
            e.Property(x => x.ColorGroup).HasMaxLength(32).IsRequired(false);
        });

        modelBuilder.Entity<Device>(e =>
        {
            e.ToTable(NetsTableNames.Device);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => new { x.NetworkEnvironmentId, x.Ip });
            e.HasIndex(x => new { x.NetworkEnvironmentId, x.Mac }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.Property(x => x.Hostname).HasMaxLength(255);
            e.Property(x => x.Vendor).HasMaxLength(256);
            e.Property(x => x.Brand).HasMaxLength(128);
            e.Property(x => x.Model).HasMaxLength(128);
            e.Property(x => x.Description).HasMaxLength(512);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.Mac).HasMaxLength(32);
            e.Property(x => x.OsType).HasMaxLength(64);
            e.Property(x => x.Source).HasMaxLength(16);
            e.HasOne(x => x.NetworkEnvironment)
                .WithMany(x => x.Devices)
                .HasForeignKey(x => x.NetworkEnvironmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DeviceGroup)
                .WithMany(x => x.Devices)
                .HasForeignKey(x => x.DeviceGroupId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.DeviceType)
                .WithMany(x => x.Devices)
                .HasForeignKey(x => x.DeviceTypeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DeviceDomain>(e =>
        {
            e.ToTable(NetsTableNames.DeviceDomain);
            e.HasIndex(x => new { x.DeviceId, x.Domain }).IsUnique();
            e.Property(x => x.Domain).HasMaxLength(255).IsRequired();
            e.HasOne(x => x.Device)
                .WithMany(x => x.Domains)
                .HasForeignKey(x => x.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceNic>(e =>
        {
            e.ToTable(NetsTableNames.DeviceNic);
            e.HasIndex(x => x.DeviceId);
            e.Property(x => x.Name).HasMaxLength(64);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.Mac).HasMaxLength(32);
            e.HasOne(x => x.Device)
                .WithMany(x => x.Nics)
                .HasForeignKey(x => x.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PortPurpose>(e =>
        {
            e.ToTable(NetsTableNames.PortPurpose);
            e.HasIndex(x => new { x.Port, x.Protocol }).IsUnique();
            e.Property(x => x.Protocol).HasMaxLength(8).IsRequired();
            e.Property(x => x.Service).HasMaxLength(64);
            e.Property(x => x.Description).HasMaxLength(256);
        });

        modelBuilder.Entity<OuiEntry>(e =>
        {
            e.ToTable(NetsTableNames.OuiEntry);
            e.HasIndex(x => x.Prefix).IsUnique();
            e.Property(x => x.Prefix).HasMaxLength(16).IsRequired();
            e.Property(x => x.Registry).HasMaxLength(16).IsRequired();
            e.Property(x => x.Vendor).HasMaxLength(256).IsRequired();
            e.Property(x => x.BatchKey).HasMaxLength(64);
        });

        modelBuilder.Entity<OuiMeta>(e =>
        {
            e.ToTable(NetsTableNames.OuiMeta);
            e.Property(x => x.Source).HasMaxLength(32).IsRequired();
            e.Property(x => x.Note).HasMaxLength(512);
            e.Property(x => x.BatchKey).HasMaxLength(64);
            e.Property(x => x.PreviousBatchKey).HasMaxLength(64);
        });

        modelBuilder.Entity<NetworkTopology>(e =>
        {
            e.ToTable(NetsTableNames.NetworkTopology);
            e.HasKey(x => x.NetworkEnvironmentId);
            e.Property(x => x.Json).HasColumnType("longtext").IsRequired();
            e.HasOne(x => x.NetworkEnvironment)
                .WithMany()
                .HasForeignKey(x => x.NetworkEnvironmentId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}