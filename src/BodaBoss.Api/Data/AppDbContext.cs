using System.Text.RegularExpressions;
using System.Security.Claims;
using BodaBoss.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BodaBoss.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor accessor)
    : DbContext(options)
{
    private Guid? CurrentTenantId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst("tenant_id")?.Value, out var tenantId)
            ? tenantId
            : null;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<FleetUser> Users => Set<FleetUser>();
    public DbSet<Motorcycle> Motorcycles => Set<Motorcycle>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<BikeAssignment> BikeAssignments => Set<BikeAssignment>();
    public DbSet<FleetJob> Jobs => Set<FleetJob>();
    public DbSet<RiderReport> Reports => Set<RiderReport>();
    public DbSet<ReportMedia> ReportMedia => Set<ReportMedia>();
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<RiderNotification> Notifications => Set<RiderNotification>();
    public DbSet<GpsLog> GpsLogs => Set<GpsLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RiderDevice> RiderDevices => Set<RiderDevice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
        });

        modelBuilder.Entity<FleetUser>(entity =>
        {
            entity.ToTable("fleet_users");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.PhoneNumber }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.RiderId }).IsUnique().HasFilter("\"rider_id\" IS NOT NULL");
            entity.Property(x => x.Email).HasMaxLength(254);
            entity.Property(x => x.PhoneNumber).HasMaxLength(24);
            entity.Property(x => x.Role).HasMaxLength(32).IsRequired();
            entity.HasAlternateKey(x => new { x.TenantId, x.Id });
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            entity.HasOne<Driver>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.RiderId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<Motorcycle>(entity =>
        {
            entity.ToTable("motorcycles");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.RegistrationNumber }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Status });
            entity.HasIndex(x => new { x.TenantId, x.LocationRecordedAt });
            entity.HasIndex(x => new { x.TenantId, x.TraccarDeviceId })
                .IsUnique()
                .HasFilter("\"traccar_device_id\" IS NOT NULL");
            entity.HasAlternateKey(x => new { x.TenantId, x.Id });
            entity.HasOne<Driver>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.AssignedDriverId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            entity.Property(x => x.RegistrationNumber).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Make).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Model).HasMaxLength(80).IsRequired();
            entity.Property(x => x.EngineNumber).HasMaxLength(80);
            entity.Property(x => x.ChassisNumber).HasMaxLength(80);
            entity.Property(x => x.Status).HasMaxLength(24).IsRequired();
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<Driver>(entity =>
        {
            entity.ToTable("riders", table =>
                table.HasCheckConstraint("ck_riders_daily_remittance", "daily_remittance_ugx >= 0"));
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.PhoneNumber }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.LicenseNumber }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Status });
            entity.HasAlternateKey(x => new { x.TenantId, x.Id });
            entity.Property(x => x.FullName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.PhoneNumber).HasMaxLength(24).IsRequired();
            entity.Property(x => x.NationalId).HasMaxLength(40);
            entity.Property(x => x.LicenseNumber).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(24).IsRequired();
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<Trip>(entity =>
        {
            entity.ToTable("trips");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.StartedAt });
            entity.HasIndex(x => new { x.TenantId, x.Status });
            entity.HasIndex(x => new { x.TenantId, x.MotorcycleId })
                .IsUnique()
                .HasFilter("\"status\" = 'InProgress'");
            entity.HasIndex(x => new { x.TenantId, x.DriverId })
                .IsUnique()
                .HasFilter("\"status\" = 'InProgress'");
            entity.HasOne<Motorcycle>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.MotorcycleId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasOne<Driver>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.DriverId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            entity.Property(x => x.Status).HasMaxLength(24).IsRequired();
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("payments");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.ReceivedAt });
            entity.HasIndex(x => new { x.TenantId, x.Reference }).IsUnique();
            entity.HasOne<Driver>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.DriverId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasOne<Motorcycle>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.MotorcycleId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            entity.Property(x => x.Method).HasMaxLength(24).IsRequired();
            entity.Property(x => x.Reference).HasMaxLength(120);
            entity.Property(x => x.Provider).HasMaxLength(24);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(120);
            entity.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            entity.Property(x => x.Status).HasMaxLength(24).IsRequired();
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.TokenHash }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.ExpiresAt });
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasOne<FleetUser>().WithMany().HasForeignKey(x => new { x.TenantId, x.UserId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<BikeAssignment>(entity =>
        {
            entity.ToTable("bike_assignments");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.MotorcycleId, x.ReturnedAt });
            entity.HasIndex(x => new { x.TenantId, x.RiderId, x.ReturnedAt });
            entity.HasOne<Motorcycle>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.MotorcycleId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasOne<Driver>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.RiderId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasOne<FleetUser>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.AssignedByUserId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<FleetJob>(entity =>
        {
            entity.ToTable("jobs");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.Status, x.Deadline });
            entity.HasIndex(x => new { x.TenantId, x.RiderId, x.Status });
            entity.Property(x => x.PickupLocation).HasMaxLength(240).IsRequired();
            entity.Property(x => x.DropoffLocation).HasMaxLength(240).IsRequired();
            entity.Property(x => x.Instructions).HasMaxLength(2000);
            entity.Property(x => x.Status).HasMaxLength(24).IsRequired();
            entity.HasOne<Driver>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.RiderId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasOne<FleetUser>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.CreatedByUserId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<RiderReport>(entity =>
        {
            entity.ToTable("reports");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
            entity.HasIndex(x => new { x.TenantId, x.RiderId, x.CreatedAt });
            entity.HasAlternateKey(x => new { x.TenantId, x.Id });
            entity.Property(x => x.IssueType).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(24).IsRequired();
            entity.Property(x => x.Resolution).HasMaxLength(4000);
            entity.HasOne<Driver>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.RiderId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasOne<FleetUser>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.AssignedTechnicianUserId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasOne<Motorcycle>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.MotorcycleId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<ReportMedia>(entity =>
        {
            entity.ToTable("report_media", table =>
            {
                table.HasCheckConstraint("ck_report_media_size", "size_bytes > 0");
                table.HasCheckConstraint("ck_report_voice_duration",
                    "kind <> 'Voice' OR (duration_seconds BETWEEN 1 AND 120)");
            });
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.ReportId });
            entity.Property(x => x.ObjectKey).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Kind).HasMaxLength(16).IsRequired();
            entity.HasOne<RiderReport>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.ReportId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<MaintenanceRecord>(entity =>
        {
            entity.ToTable("maintenance");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.Status, x.DueAt });
            entity.HasIndex(x => new { x.TenantId, x.MotorcycleId, x.DueAtMileageKm });
            entity.Property(x => x.ServiceType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(24).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasOne<Motorcycle>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.MotorcycleId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<RiderNotification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.RiderId, x.Status, x.CreatedAt });
            entity.Property(x => x.Type).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(16).IsRequired();
            entity.HasOne<Driver>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.RiderId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<GpsLog>(entity =>
        {
            entity.ToTable("gps_logs");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.MotorcycleId, x.RecordedAt });
            entity.HasIndex(x => new { x.TenantId, x.ReceivedAt });
            entity.HasOne<Motorcycle>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.MotorcycleId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<RiderDevice>(entity =>
        {
            entity.ToTable("rider_devices");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.FcmToken).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.RiderId });
            entity.Property(x => x.FcmToken).HasMaxLength(4096).IsRequired();
            entity.Property(x => x.Platform).HasMaxLength(16).IsRequired();
            entity.HasOne<Driver>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.RiderId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.CreatedAt });
            entity.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });
            entity.Property(x => x.Action).HasMaxLength(120).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(120).IsRequired();
            entity.Property(x => x.EntityId).HasMaxLength(120);
            entity.Property(x => x.DataJson).HasColumnType("jsonb");
            entity.HasOne<FleetUser>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.UserId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id });
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Motorcycle>().Where(x => x.State == EntityState.Modified))
        {
            entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (CurrentTenantId is Guid tenantId)
        {
            var userId = Guid.TryParse(
                accessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? accessor.HttpContext?.User.FindFirst("sub")?.Value,
                out var parsedUserId)
                ? parsedUserId
                : (Guid?)null;
            var auditableTypes = new HashSet<Type>
            {
                typeof(FleetUser), typeof(Motorcycle), typeof(Driver), typeof(FleetJob),
                typeof(RiderReport), typeof(MaintenanceRecord), typeof(Payment), typeof(BikeAssignment)
            };
            foreach (var entry in ChangeTracker.Entries()
                .Where(entry => auditableTypes.Contains(entry.Entity.GetType())
                    && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToArray())
            {
                var entityType = entry.Entity.GetType();
                var entityId = entry.Properties.FirstOrDefault(property => property.Metadata.Name == "Id")?.CurrentValue;
                var action = entry.State.ToString();
                AuditLogs.Add(new AuditLog
                {
                    TenantId = tenantId,
                    UserId = userId,
                    Action = $"{entityType.Name}.{action}",
                    EntityType = entityType.Name,
                    EntityId = entityId?.ToString()
                });
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
