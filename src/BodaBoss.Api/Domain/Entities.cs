namespace BodaBoss.Api.Domain;

public sealed class Tenant
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class FleetUser
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public required string PasswordHash { get; set; }
    public string Role { get; set; } = Roles.OperationsManager;
    public Guid? RiderId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string OperationsManager = "OperationsManager";
    public const string FinanceOfficer = "FinanceOfficer";
    public const string Rider = "Rider";

    public const string FleetManagers = SuperAdmin + "," + OperationsManager;
    public const string FinanceAndAdmin = SuperAdmin + "," + FinanceOfficer;
}

public sealed class Motorcycle
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string RegistrationNumber { get; set; }
    public required string Make { get; set; }
    public required string Model { get; set; }
    public string? EngineNumber { get; set; }
    public string? ChassisNumber { get; set; }
    public long? TraccarDeviceId { get; set; }
    public int Year { get; set; }
    public double MileageKm { get; set; }
    public string Status { get; set; } = "Available";
    public Guid? AssignedDriverId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTimeOffset? LocationRecordedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Driver
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string FullName { get; set; }
    public required string PhoneNumber { get; set; }
    public string? NationalId { get; set; }
    public required string LicenseNumber { get; set; }
    public string Status { get; set; } = "Active";
    public long DailyRemittanceUgx { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Trip
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid MotorcycleId { get; set; }
    public Guid DriverId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public double? StartLatitude { get; set; }
    public double? StartLongitude { get; set; }
    public double? EndLatitude { get; set; }
    public double? EndLongitude { get; set; }
    public long FareUgx { get; set; }
    public string Status { get; set; } = "InProgress";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Payment
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid DriverId { get; set; }
    public Guid? MotorcycleId { get; set; }
    public long AmountUgx { get; set; }
    public required string Method { get; set; }
    public string? Reference { get; set; }
    public string Status { get; set; } = "Recorded";
    public string? Provider { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BikeAssignment
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid MotorcycleId { get; set; }
    public Guid RiderId { get; set; }
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReturnedAt { get; set; }
    public Guid AssignedByUserId { get; set; }
}

public sealed class FleetJob
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string PickupLocation { get; set; }
    public required string DropoffLocation { get; set; }
    public string? Instructions { get; set; }
    public long AmountUgx { get; set; }
    public DateTimeOffset Deadline { get; set; }
    public Guid? RiderId { get; set; }
    public string Status { get; set; } = "Unassigned";
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedByUserId { get; set; }
}

public sealed class RiderReport
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RiderId { get; set; }
    public Guid? MotorcycleId { get; set; }
    public required string IssueType { get; set; }
    public required string Description { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string Status { get; set; } = "Open";
    public Guid? AssignedTechnicianUserId { get; set; }
    public string? Resolution { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class ReportMedia
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ReportId { get; set; }
    public required string ObjectKey { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string Kind { get; set; } = "Photo";
    public int? DurationSeconds { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MaintenanceRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid MotorcycleId { get; set; }
    public required string ServiceType { get; set; }
    public double DueAtMileageKm { get; set; }
    public double? CompletedAtMileageKm { get; set; }
    public long CostUgx { get; set; }
    public string Status { get; set; } = "Scheduled";
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Notes { get; set; }
}

public sealed class RiderNotification
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RiderId { get; set; }
    public required string Type { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public string Status { get; set; } = "Unread";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

public sealed class GpsLog
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid MotorcycleId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? SpeedKph { get; set; }
    public double? MileageKm { get; set; }
    public double? HeadingDegrees { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AuditLog
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? DataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RiderDevice
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RiderId { get; set; }
    public required string FcmToken { get; set; }
    public required string Platform { get; set; }
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}
