using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using BodaBoss.Api.Application;
using BodaBoss.Api.Application.Behaviors;
using BodaBoss.Api.Application.Features.Jobs;
using BodaBoss.Api;
using BodaBoss.Api.Data;
using BodaBoss.Api.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MediatR;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var signingKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 bytes.");
}

var issuer = builder.Configuration["Jwt:Issuer"] ?? "BodaBoss";
var audience = builder.Configuration["Jwt:Audience"] ?? "BodaBoss.Mobile";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (builder.Environment.IsDevelopment() && allowedOrigins.Length == 0)
{
    allowedOrigins = ["http://localhost:5000"];
}
var trustedProxyAddresses = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
var trustedProxies = trustedProxyAddresses.Select(address =>
    System.Net.IPAddress.TryParse(address, out var ipAddress)
        ? ipAddress
        : throw new InvalidOperationException($"ForwardedHeaders:KnownProxies contains an invalid IP address: {address}"))
    .ToArray();

builder.Services.AddHttpContextAccessor();
if (trustedProxies.Length > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();
        foreach (var proxy in trustedProxies)
        {
            options.KnownProxies.Add(proxy);
        }
    });
}
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.")));
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton<AuthTokenService>();
builder.Services.AddMediatR(options => options.RegisterServicesFromAssembly(typeof(Program).Assembly));
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("postgres", tags: ["ready"]);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("telemetry", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{httpContext.User.FindFirst("tenant_id")?.Value}:{httpContext.User.FindFirst("rider_id")?.Value}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PortalUsers", policy =>
        policy.RequireRole(Roles.SuperAdmin, Roles.OperationsManager, Roles.FinanceOfficer));
    options.AddPolicy("FleetManagers", policy =>
        policy.RequireRole(Roles.SuperAdmin, Roles.OperationsManager));
    options.AddPolicy("FinanceUsers", policy =>
        policy.RequireRole(Roles.SuperAdmin, Roles.FinanceOfficer));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseForwardedHeaders();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
var auth = app.MapGroup("/api/v1/auth");
auth.MapPost("/login", async (
    LoginRequest request,
    AppDbContext db,
    AuthTokenService tokens,
    CancellationToken cancellationToken) =>
{
    if (request.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(request.Identifier)
        || string.IsNullOrEmpty(request.Password))
    {
        return Results.BadRequest(new { message = "Workspace ID, phone number/email, and password are required." });
    }

    var identifier = request.Identifier.Trim();
    var users = db.Users.IgnoreQueryFilters().AsNoTracking()
        .Where(x => x.TenantId == request.TenantId && x.IsActive);
    FleetUser? user;
    if (identifier.Contains('@', StringComparison.Ordinal))
    {
        user = await users.SingleOrDefaultAsync(
            x => x.Email == identifier.ToLowerInvariant(), cancellationToken);
    }
    else if (UgandaPhoneNumber.TryNormalize(identifier, out var phoneNumber))
    {
        user = await users.SingleOrDefaultAsync(
            x => x.PhoneNumber == phoneNumber, cancellationToken);
    }
    else
    {
        return Results.Unauthorized();
    }

    if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
    {
        return Results.Unauthorized();
    }

    var access = tokens.CreateAccessToken(user);
    var refreshToken = AuthTokenService.CreateRefreshToken();
    db.RefreshTokens.Add(new RefreshToken
    {
        TenantId = user.TenantId,
        UserId = user.Id,
        TokenHash = AuthTokenService.HashRefreshToken(refreshToken),
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
    });
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new LoginResponse(access.Token, refreshToken, access.ExpiresAt, user.Email, user.Role));
}).RequireRateLimiting("login").AllowAnonymous();

auth.MapPost("/refresh", async (
    RefreshRequest request,
    AppDbContext db,
    AuthTokenService tokens,
    CancellationToken cancellationToken) =>
{
    if (request.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(request.RefreshToken))
    {
        return Results.BadRequest(new { message = "Workspace ID and refresh token are required." });
    }

    var tokenHash = AuthTokenService.HashRefreshToken(request.RefreshToken);
    var stored = await db.RefreshTokens.IgnoreQueryFilters().AsNoTracking()
        .SingleOrDefaultAsync(x => x.TenantId == request.TenantId && x.TokenHash == tokenHash
            && x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);
    if (stored is null)
    {
        return Results.Unauthorized();
    }

    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
    var now = DateTimeOffset.UtcNow;
    var revoked = await db.RefreshTokens.IgnoreQueryFilters()
        .Where(x => x.TenantId == request.TenantId && x.TokenHash == tokenHash
            && x.RevokedAt == null && x.ExpiresAt > now)
        .ExecuteUpdateAsync(updates => updates.SetProperty(x => x.RevokedAt, now), cancellationToken);
    if (revoked == 0)
    {
        return Results.Unauthorized();
    }

    var user = await db.Users.IgnoreQueryFilters().AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == stored.UserId
            && x.TenantId == request.TenantId && x.IsActive, cancellationToken);
    if (user is null)
    {
        await transaction.CommitAsync(cancellationToken);
        return Results.Unauthorized();
    }

    var newRefreshToken = AuthTokenService.CreateRefreshToken();
    db.RefreshTokens.Add(new RefreshToken
    {
        TenantId = user.TenantId,
        UserId = user.Id,
        TokenHash = AuthTokenService.HashRefreshToken(newRefreshToken),
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
    });
    await db.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    var access = tokens.CreateAccessToken(user);
    return Results.Ok(new LoginResponse(access.Token, newRefreshToken, access.ExpiresAt, user.Email, user.Role));
}).RequireRateLimiting("login").AllowAnonymous();

var api = app.MapGroup("/api/v1").RequireAuthorization();
var portalApi = api.MapGroup("").RequireAuthorization("PortalUsers");
var fleetApi = api.MapGroup("").RequireAuthorization("FleetManagers");
var financeApi = api.MapGroup("").RequireAuthorization("FinanceUsers");
var riderApi = api.MapGroup("/rider").RequireAuthorization(policy => policy.RequireRole(Roles.Rider));

riderApi.MapGet("/home", async (AppDbContext db, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("rider_id")?.Value, out var riderId))
    {
        return Results.Forbid();
    }

    var rider = await db.Drivers.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == riderId, cancellationToken);
    if (rider is null)
    {
        return Results.NotFound();
    }

    var bike = await db.Motorcycles.AsNoTracking()
        .Where(x => x.AssignedDriverId == riderId)
        .Select(x => new { x.Id, x.RegistrationNumber, x.Make, x.Model, x.Status })
        .SingleOrDefaultAsync(cancellationToken);
    var now = DateTimeOffset.UtcNow;
    var ugandaOffset = TimeSpan.FromHours(3);
    var today = new DateTimeOffset(now.ToOffset(ugandaOffset).Date, ugandaOffset).ToUniversalTime();
    var paidToday = await db.Payments
        .Where(x => x.DriverId == riderId && x.ReceivedAt >= today
            && (x.Status == "Recorded" || x.Status == "Successful"))
        .SumAsync(x => (long?)x.AmountUgx, cancellationToken) ?? 0;
    var assignedJobs = await db.Jobs.CountAsync(
        x => x.RiderId == riderId && x.Status == "Assigned", cancellationToken);
    var activeJobs = await db.Jobs.CountAsync(
        x => x.RiderId == riderId && (x.Status == "Accepted" || x.Status == "InProgress"), cancellationToken);
    var unreadNotifications = await db.Notifications.CountAsync(
        x => x.RiderId == riderId && x.Status == "Unread", cancellationToken);

    return Results.Ok(new
    {
        rider = new { rider.Id, rider.FullName, rider.PhoneNumber },
        motorcycle = bike,
        remittance = new
        {
            amountDueUgx = rider.DailyRemittanceUgx,
            amountPaidUgx = paidToday,
            outstandingUgx = Math.Max(0, rider.DailyRemittanceUgx - paidToday)
        },
        assignedJobs,
        activeJobs,
        unreadNotifications,
        date = now.ToOffset(ugandaOffset).Date
    });
});

riderApi.MapGet("/jobs", async (AppDbContext db, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("rider_id")?.Value, out var riderId))
    {
        return Results.Forbid();
    }

    var jobs = await db.Jobs.AsNoTracking()
        .Where(x => x.RiderId == riderId)
        .OrderBy(x => x.Deadline)
        .Take(100)
        .Select(x => new JobResponse(x.Id, x.PickupLocation, x.DropoffLocation, x.Instructions,
            x.AmountUgx, x.Deadline, x.Status, x.AcceptedAt, x.StartedAt, x.CompletedAt))
        .ToListAsync(cancellationToken);
    return Results.Ok(jobs);
});

riderApi.MapPost("/jobs/{id:guid}/{action}", async (
    Guid id, string action, AppDbContext db, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("rider_id")?.Value, out var riderId))
    {
        return Results.Forbid();
    }

    var now = DateTimeOffset.UtcNow;
    var riderJobs = db.Jobs.Where(x => x.Id == id && x.RiderId == riderId);
    var changed = action.ToLowerInvariant() switch
    {
        "accept" => await riderJobs.Where(x => x.Status == "Assigned")
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(x => x.Status, "Accepted")
                .SetProperty(x => x.AcceptedAt, now), cancellationToken),
        "start" => await riderJobs.Where(x => x.Status == "Accepted")
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(x => x.Status, "InProgress")
                .SetProperty(x => x.StartedAt, now), cancellationToken),
        "complete" => await riderJobs.Where(x => x.Status == "InProgress")
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(x => x.Status, "Completed")
                .SetProperty(x => x.CompletedAt, now), cancellationToken),
        "unable" => await riderJobs.Where(x =>
                x.Status == "Assigned" || x.Status == "Accepted" || x.Status == "InProgress")
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(x => x.Status, "UnableToComplete"), cancellationToken),
        _ => -1
    };
    if (changed < 0)
    {
        return Results.BadRequest(new { message = "Action must be accept, start, complete, or unable." });
    }
    if (changed == 0)
    {
        return Results.Conflict(new { message = "Job not found or action is not valid for its current status." });
    }

    return Results.NoContent();
});

riderApi.MapPost("/reports", async (
    CreateReportRequest request, AppDbContext db, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("rider_id")?.Value, out var riderId))
    {
        return Results.Forbid();
    }

    var issueTypes = new HashSet<string>(
        ["Accident", "Mechanical", "Tyre", "Engine", "Police", "Theft", "Fuel", "Electrical", "Other"],
        StringComparer.OrdinalIgnoreCase);
    if (!issueTypes.Contains(request.IssueType) || string.IsNullOrWhiteSpace(request.Description)
        || request.Description.Length > 4000
        || (request.Latitude.HasValue != request.Longitude.HasValue)
        || (request.Latitude is < -90 or > 90) || (request.Longitude is < -180 or > 180))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["report"] = ["Select a valid issue type, provide a description, and enter valid paired GPS coordinates."]
        });
    }

    var bikeId = await db.Motorcycles.Where(x => x.AssignedDriverId == riderId)
        .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
    var report = new RiderReport
    {
        RiderId = riderId,
        MotorcycleId = bikeId,
        IssueType = issueTypes.Single(x => x.Equals(request.IssueType, StringComparison.OrdinalIgnoreCase)),
        Description = request.Description.Trim(),
        Latitude = request.Latitude,
        Longitude = request.Longitude
    };
    db.Reports.Add(report);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/v1/rider/reports/{report.Id}", new { report.Id, report.Status, report.CreatedAt });
});

riderApi.MapGet("/payments", async (AppDbContext db, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("rider_id")?.Value, out var riderId))
    {
        return Results.Forbid();
    }

    return Results.Ok(await db.Payments.AsNoTracking()
        .Where(x => x.DriverId == riderId)
        .OrderByDescending(x => x.ReceivedAt)
        .Take(100)
        .Select(x => new PaymentResponse(x.Id, x.DriverId, x.MotorcycleId,
            x.AmountUgx, x.Method, x.Status, x.ReceivedAt))
        .ToListAsync(cancellationToken));
});

riderApi.MapPost("/location", async (
    LocationRequest request,
    AppDbContext db,
    ClaimsPrincipal principal,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("rider_id")?.Value, out var riderId))
    {
        return Results.Forbid();
    }
    if (!double.IsFinite(request.Latitude) || request.Latitude is < -90 or > 90
        || !double.IsFinite(request.Longitude) || request.Longitude is < -180 or > 180)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["location"] = ["Latitude must be -90 to 90 and longitude -180 to 180."]
        });
    }

    var motorcycle = await db.Motorcycles.SingleOrDefaultAsync(
        x => x.AssignedDriverId == riderId, cancellationToken);
    if (motorcycle is null)
    {
        return Results.Conflict(new { message = "No motorcycle is currently assigned to this rider." });
    }
    var recordedAt = DateTimeOffset.UtcNow;
    motorcycle.Latitude = request.Latitude;
    motorcycle.Longitude = request.Longitude;
    motorcycle.LocationRecordedAt = recordedAt;
    db.GpsLogs.Add(new GpsLog
    {
        TenantId = motorcycle.TenantId,
        MotorcycleId = motorcycle.Id,
        Latitude = request.Latitude,
        Longitude = request.Longitude,
        RecordedAt = recordedAt
    });
    await db.SaveChangesAsync(cancellationToken);
    return Results.Accepted(value: new { motorcycle.Id, recordedAt });
}).RequireRateLimiting("telemetry");

riderApi.MapPost("/devices", async (
    RegisterDeviceRequest request,
    AppDbContext db,
    ClaimsPrincipal principal,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("rider_id")?.Value, out var riderId))
    {
        return Results.Forbid();
    }
    if (!Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out var tenantId))
    {
        return Results.Forbid();
    }
    var platform = request.Platform?.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(request.FcmToken) || request.FcmToken.Length > 4096
        || platform is not ("android" or "ios" or "web"))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["device"] = ["A valid FCM token and platform (android, ios, or web) are required."]
        });
    }

    var device = await db.RiderDevices.IgnoreQueryFilters()
        .SingleOrDefaultAsync(x => x.FcmToken == request.FcmToken, cancellationToken);
    if (device is not null && (device.TenantId != tenantId || device.RiderId != riderId))
    {
        return Results.Conflict(new { message = "This device token is already registered to another rider." });
    }
    if (device is null)
    {
        device = new RiderDevice
        {
            TenantId = tenantId,
            RiderId = riderId,
            FcmToken = request.FcmToken,
            Platform = platform
        };
        db.RiderDevices.Add(device);
    }
    else
    {
        device.Platform = platform;
        device.LastSeenAt = DateTimeOffset.UtcNow;
    }
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

riderApi.MapGet("/notifications", async (AppDbContext db, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("rider_id")?.Value, out var riderId))
    {
        return Results.Forbid();
    }

    return Results.Ok(await db.Notifications.AsNoTracking()
        .Where(x => x.RiderId == riderId && x.Status != "Archived")
        .OrderByDescending(x => x.CreatedAt)
        .Take(100)
        .Select(x => new NotificationResponse(x.Id, x.Type, x.Title, x.Body, x.Status, x.CreatedAt))
        .ToListAsync(cancellationToken));
});

riderApi.MapPut("/notifications/{id:guid}/read", async (
    Guid id, AppDbContext db, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("rider_id")?.Value, out var riderId))
    {
        return Results.Forbid();
    }

    var now = DateTimeOffset.UtcNow;
    var changed = await db.Notifications
        .Where(x => x.Id == id && x.RiderId == riderId && x.Status == "Unread")
        .ExecuteUpdateAsync(updates => updates
            .SetProperty(x => x.Status, "Read")
            .SetProperty(x => x.ReadAt, now), cancellationToken);
    return changed == 0 ? Results.NotFound() : Results.NoContent();
});

riderApi.MapPut("/notifications/{id:guid}/archive", async (
    Guid id, AppDbContext db, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("rider_id")?.Value, out var riderId))
    {
        return Results.Forbid();
    }

    var now = DateTimeOffset.UtcNow;
    var changed = await db.Notifications
        .Where(x => x.Id == id && x.RiderId == riderId && x.Status != "Archived")
        .ExecuteUpdateAsync(updates => updates
            .SetProperty(x => x.Status, "Archived")
            .SetProperty(x => x.ArchivedAt, now), cancellationToken);
    return changed == 0 ? Results.NotFound() : Results.NoContent();
});

fleetApi.MapGet("/jobs", async (AppDbContext db, int page = 1, int pageSize = 50,
    CancellationToken cancellationToken = default) =>
{
    pageSize = Math.Clamp(pageSize, 1, 100);
    page = Math.Clamp(page, 1, int.MaxValue / pageSize);
    var query = db.Jobs.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id);
    var totalCount = await query.CountAsync(cancellationToken);
    var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
        .Select(x => new JobResponse(x.Id, x.PickupLocation, x.DropoffLocation, x.Instructions,
            x.AmountUgx, x.Deadline, x.Status, x.AcceptedAt, x.StartedAt, x.CompletedAt))
        .ToListAsync(cancellationToken);
    return Results.Ok(new PagedResponse<JobResponse>(items, page, pageSize, totalCount));
});

fleetApi.MapPost("/jobs", async (
    CreateJobRequest request,
    ISender sender,
    ClaimsPrincipal principal,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out var tenantId)
        || !Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value, out var userId))
    {
        return Results.Forbid();
    }

    var job = await sender.Send(new CreateJobCommand(
        tenantId,
        userId,
        request.PickupLocation,
        request.DropoffLocation,
        request.Instructions,
        request.AmountUgx,
        request.Deadline,
        request.RiderId), cancellationToken);
    return Results.Created($"/api/v1/jobs/{job.Id}",
        new JobResponse(job.Id, job.PickupLocation, job.DropoffLocation, job.Instructions,
            job.AmountUgx, job.Deadline, job.Status, job.AcceptedAt, job.StartedAt, job.CompletedAt));
});

fleetApi.MapPut("/jobs/{id:guid}/assign", async (
    Guid id,
    AssignJobRequest request,
    AppDbContext db,
    ClaimsPrincipal principal,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out var tenantId))
    {
        return Results.Forbid();
    }
    if (!await db.Drivers.AnyAsync(x => x.Id == request.RiderId && x.Status == "Active", cancellationToken))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["riderId"] = ["Select an active rider from this workspace."]
        });
    }

    var job = await db.Jobs.SingleOrDefaultAsync(
        x => x.Id == id && (x.Status == "Unassigned" || x.Status == "Assigned"), cancellationToken);
    if (job is null)
    {
        return Results.NotFound();
    }
    job.RiderId = request.RiderId;
    job.Status = "Assigned";
    db.Notifications.Add(new RiderNotification
    {
        TenantId = tenantId,
        RiderId = request.RiderId,
        Type = "JobAssigned",
        Title = "New job assigned",
        Body = $"Pickup: {job.PickupLocation}. Drop-off: {job.DropoffLocation}."
    });
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

fleetApi.MapGet("/reports", async (AppDbContext db, int page = 1, int pageSize = 50,
    CancellationToken cancellationToken = default) =>
{
    pageSize = Math.Clamp(pageSize, 1, 100);
    page = Math.Clamp(page, 1, int.MaxValue / pageSize);
    var query = db.Reports.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id);
    var totalCount = await query.CountAsync(cancellationToken);
    var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
        .Select(x => new ReportResponse(x.Id, x.RiderId, x.MotorcycleId, x.IssueType, x.Description,
            x.Latitude, x.Longitude, x.Status, x.Resolution, x.CreatedAt, x.ResolvedAt))
        .ToListAsync(cancellationToken);
    return Results.Ok(new PagedResponse<ReportResponse>(items, page, pageSize, totalCount));
});

fleetApi.MapPut("/reports/{id:guid}", async (
    Guid id,
    UpdateReportRequest request,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var allowedStatuses = new HashSet<string>(["Open", "InProgress", "Resolved", "Rejected"],
        StringComparer.OrdinalIgnoreCase);
    if (!allowedStatuses.Contains(request.Status)
        || (request.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(request.Resolution))
        || request.Resolution?.Length > 4000)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["report"] = ["Choose a valid status and provide a resolution of at most 4,000 characters when resolving."]
        });
    }

    var report = await db.Reports.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    if (report is null)
    {
        return Results.NotFound();
    }
    report.Status = allowedStatuses.Single(x => x.Equals(request.Status, StringComparison.OrdinalIgnoreCase));
    report.Resolution = string.IsNullOrWhiteSpace(request.Resolution) ? null : request.Resolution.Trim();
    report.ResolvedAt = report.Status == "Resolved" ? DateTimeOffset.UtcNow : null;
    if (report.Status is "Resolved" or "Rejected")
    {
        db.Notifications.Add(new RiderNotification
        {
            TenantId = report.TenantId,
            RiderId = report.RiderId,
            Type = "ReportUpdated",
            Title = "Issue report updated",
            Body = report.Resolution ?? $"Your {report.IssueType.ToLowerInvariant()} report was {report.Status.ToLowerInvariant()}."
        });
    }
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

fleetApi.MapGet("/maintenance", async (AppDbContext db, int page = 1, int pageSize = 50,
    CancellationToken cancellationToken = default) =>
{
    pageSize = Math.Clamp(pageSize, 1, 100);
    page = Math.Clamp(page, 1, int.MaxValue / pageSize);
    var query = db.MaintenanceRecords.OrderBy(x => x.DueAt).ThenBy(x => x.Id);
    var totalCount = await query.CountAsync(cancellationToken);
    var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
        .Select(x => new MaintenanceResponse(x.Id, x.MotorcycleId, x.ServiceType, x.DueAtMileageKm,
            x.CompletedAtMileageKm, x.CostUgx, x.Status, x.DueAt, x.CompletedAt, x.Notes))
        .ToListAsync(cancellationToken);
    return Results.Ok(new PagedResponse<MaintenanceResponse>(items, page, pageSize, totalCount));
});

fleetApi.MapPost("/maintenance", async (
    CreateMaintenanceRequest request,
    AppDbContext db,
    ClaimsPrincipal principal,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out var tenantId))
    {
        return Results.Forbid();
    }
    if (string.IsNullOrWhiteSpace(request.ServiceType) || request.ServiceType.Length > 80
        || !double.IsFinite(request.DueAtMileageKm) || request.DueAtMileageKm < 0
        || request.CostUgx < 0 || request.Notes?.Length > 2000
        || !await db.Motorcycles.AnyAsync(x => x.Id == request.MotorcycleId, cancellationToken))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["maintenance"] = ["Provide a valid motorcycle, service type, mileage, cost, and notes."]
        });
    }
    var record = new MaintenanceRecord
    {
        TenantId = tenantId,
        MotorcycleId = request.MotorcycleId,
        ServiceType = request.ServiceType.Trim(),
        DueAtMileageKm = request.DueAtMileageKm,
        CostUgx = request.CostUgx,
        DueAt = request.DueAt,
        Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
    };
    db.MaintenanceRecords.Add(record);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/v1/maintenance/{record.Id}",
        new MaintenanceResponse(record.Id, record.MotorcycleId, record.ServiceType, record.DueAtMileageKm,
            record.CompletedAtMileageKm, record.CostUgx, record.Status, record.DueAt, record.CompletedAt, record.Notes));
});

fleetApi.MapPut("/maintenance/{id:guid}/complete", async (
    Guid id,
    CompleteMaintenanceRequest request,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    if (request.CostUgx < 0 || request.Notes?.Length > 2000)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["maintenance"] = ["Cost cannot be negative and notes must be 2,000 characters or fewer."]
        });
    }
    var record = await db.MaintenanceRecords.SingleOrDefaultAsync(
        x => x.Id == id && x.Status == "Scheduled", cancellationToken);
    if (record is null)
    {
        return Results.NotFound();
    }
    var motorcycle = await db.Motorcycles.SingleOrDefaultAsync(
        x => x.Id == record.MotorcycleId, cancellationToken);
    if (motorcycle is null)
    {
        return Results.Conflict(new { message = "The motorcycle for this maintenance record no longer exists." });
    }
    record.Status = "Completed";
    record.CompletedAt = DateTimeOffset.UtcNow;
    record.CompletedAtMileageKm = motorcycle.MileageKm;
    record.CostUgx = request.CostUgx;
    if (!string.IsNullOrWhiteSpace(request.Notes))
    {
        record.Notes = request.Notes.Trim();
    }
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

portalApi.MapGet("/dashboard/summary", async (
    AppDbContext db, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
{
    var ugandaOffset = TimeSpan.FromHours(3);
    var today = new DateTimeOffset(
        DateTimeOffset.UtcNow.ToOffset(ugandaOffset).Date,
        ugandaOffset).ToUniversalTime();
    var motorcycles = db.Motorcycles;
    var trips = db.Trips;
    var payments = db.Payments;

    var fleetCount = await motorcycles.CountAsync(cancellationToken);
    var availableCount = await motorcycles.CountAsync(x => x.Status == "Available", cancellationToken);
    var activeTrips = await trips.CountAsync(x => x.Status == "InProgress", cancellationToken);
    var trackedCount = await motorcycles.CountAsync(
        x => x.LocationRecordedAt >= DateTimeOffset.UtcNow.AddMinutes(-5), cancellationToken);
    var canViewFinance = principal.IsInRole(Roles.SuperAdmin) || principal.IsInRole(Roles.FinanceOfficer);
    var revenueToday = canViewFinance
        ? await payments.Where(x => x.ReceivedAt >= today && x.Status == "Recorded")
            .SumAsync(x => (long?)x.AmountUgx, cancellationToken) ?? 0
        : 0;
    var recentPayments = canViewFinance
        ? await payments.OrderByDescending(x => x.ReceivedAt)
            .Take(5)
            .Select(x => new PaymentSummaryResponse(x.Id, x.AmountUgx, x.Method, x.Status, x.ReceivedAt))
            .ToListAsync(cancellationToken)
        : [];

    return Results.Ok(new
    {
        fleetCount,
        availableCount,
        activeTrips,
        trackedCount,
        revenueTodayUgx = revenueToday,
        recentPayments
    });
});

fleetApi.MapGet("/motorcycles", async (AppDbContext db, int page = 1, int pageSize = 50,
    CancellationToken cancellationToken = default) =>
{
    pageSize = Math.Clamp(pageSize, 1, 100);
    page = Math.Clamp(page, 1, int.MaxValue / pageSize);
    var query = db.Motorcycles.OrderBy(x => x.RegistrationNumber).ThenBy(x => x.Id);
    var totalCount = await query.CountAsync(cancellationToken);
    var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
        .Select(x => new MotorcycleResponse(x.Id, x.RegistrationNumber, x.Make, x.Model, x.Year, x.Status,
            x.Latitude, x.Longitude, x.LocationRecordedAt))
        .ToListAsync(cancellationToken);
    return Results.Ok(new PagedResponse<MotorcycleResponse>(items, page, pageSize, totalCount));
});

fleetApi.MapPost("/motorcycles", async (CreateMotorcycleRequest request, AppDbContext db, ClaimsPrincipal principal,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out var tenantId))
    {
        return Results.Forbid();
    }

    if (string.IsNullOrWhiteSpace(request.RegistrationNumber) || string.IsNullOrWhiteSpace(request.Make)
        || string.IsNullOrWhiteSpace(request.Model) || request.Year is < 1990 or > 2100)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["motorcycle"] = ["Registration, make, model, and a valid year are required."]
        });
    }

    var motorcycle = new Motorcycle
    {
        TenantId = tenantId,
        RegistrationNumber = request.RegistrationNumber.Trim().ToUpperInvariant(),
        Make = request.Make.Trim(),
        Model = request.Model.Trim(),
        Year = request.Year
    };
    db.Motorcycles.Add(motorcycle);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/v1/motorcycles/{motorcycle.Id}", motorcycle);
});

fleetApi.MapPut("/motorcycles/{id:guid}/assignment", async (
    Guid id,
    AssignMotorcycleRequest request,
    AppDbContext db,
    ClaimsPrincipal principal,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out var tenantId)
        || !Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value, out var userId))
    {
        return Results.Forbid();
    }

    var motorcycle = await db.Motorcycles.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    if (motorcycle is null)
    {
        return Results.NotFound();
    }

    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
    var now = DateTimeOffset.UtcNow;
    if (await db.Trips.AnyAsync(x => x.MotorcycleId == id && x.Status == "InProgress", cancellationToken))
    {
        return Results.Conflict(new { message = "A motorcycle in an active trip cannot be reassigned." });
    }

    var currentAssignments = await db.BikeAssignments
        .Where(x => x.ReturnedAt == null
            && (x.MotorcycleId == id || (request.RiderId.HasValue && x.RiderId == request.RiderId.Value)))
        .ToListAsync(cancellationToken);
    if (request.RiderId is Guid riderId)
    {
        if (!await db.Drivers.AnyAsync(x => x.Id == riderId && x.Status == "Active", cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["riderId"] = ["Select an active rider from this workspace."]
            });
        }
        if (await db.Trips.AnyAsync(x => x.DriverId == riderId && x.Status == "InProgress", cancellationToken)
            || await db.Jobs.AnyAsync(x => x.RiderId == riderId
                && (x.Status == "Accepted" || x.Status == "InProgress"), cancellationToken))
        {
            return Results.Conflict(new { message = "A rider with an active trip or job cannot be reassigned." });
        }
        if (motorcycle.AssignedDriverId == riderId
            && currentAssignments.Any(x => x.MotorcycleId == id && x.RiderId == riderId))
        {
            return Results.NoContent();
        }
    }

    foreach (var assignment in currentAssignments)
    {
        assignment.ReturnedAt = now;
        if (assignment.MotorcycleId != id)
        {
            var oldMotorcycle = await db.Motorcycles.SingleOrDefaultAsync(
                x => x.Id == assignment.MotorcycleId, cancellationToken);
            if (oldMotorcycle is not null)
            {
                oldMotorcycle.AssignedDriverId = null;
                oldMotorcycle.Status = "Available";
            }
        }
    }

    if (request.RiderId is Guid riderBeingAssigned)
    {
        var riderMotorcycles = await db.Motorcycles
            .Where(x => x.Id != id && x.AssignedDriverId == riderBeingAssigned)
            .ToListAsync(cancellationToken);
        foreach (var oldMotorcycle in riderMotorcycles)
        {
            oldMotorcycle.AssignedDriverId = null;
            oldMotorcycle.Status = "Available";
        }
    }

    motorcycle.AssignedDriverId = request.RiderId;
    motorcycle.Status = request.RiderId.HasValue ? "Assigned" : "Available";
    if (request.RiderId is Guid assignedRiderId)
    {
        db.BikeAssignments.Add(new BikeAssignment
        {
            TenantId = tenantId,
            MotorcycleId = motorcycle.Id,
            RiderId = assignedRiderId,
            AssignedByUserId = userId,
            AssignedAt = now
        });
    }
    await db.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return Results.NoContent();
});

fleetApi.MapGet("/riders", async (AppDbContext db, int page = 1, int pageSize = 50,
    CancellationToken cancellationToken = default) =>
{
    pageSize = Math.Clamp(pageSize, 1, 100);
    page = Math.Clamp(page, 1, int.MaxValue / pageSize);
    var query = db.Drivers.OrderBy(x => x.FullName).ThenBy(x => x.Id);
    var totalCount = await query.CountAsync(cancellationToken);
    var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
        .Select(x => new DriverResponse(x.Id, x.FullName, x.PhoneNumber, x.LicenseNumber, x.Status))
        .ToListAsync(cancellationToken);
    return Results.Ok(new PagedResponse<DriverResponse>(items, page, pageSize, totalCount));
});

fleetApi.MapPost("/riders", async (CreateDriverRequest request, AppDbContext db, ClaimsPrincipal principal,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out var tenantId))
    {
        return Results.Forbid();
    }

    if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.PhoneNumber)
        || string.IsNullOrWhiteSpace(request.LicenseNumber) || string.IsNullOrWhiteSpace(request.Password)
        || request.Password.Length < 12 || request.DailyRemittanceUgx < 0)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["rider"] = ["Name, phone number, license number, a password of at least 12 characters, and a non-negative daily remittance are required."]
        });
    }

    if (!UgandaPhoneNumber.TryNormalize(request.PhoneNumber, out var phoneNumber))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["phoneNumber"] = ["Enter a valid Ugandan phone number."]
        });
    }

    if (await db.Drivers.AnyAsync(x => x.PhoneNumber == phoneNumber, cancellationToken)
        || await db.Users.AnyAsync(x => x.PhoneNumber == phoneNumber, cancellationToken))
    {
        return Results.Conflict(new { message = "A rider account already uses this phone number." });
    }

    var driver = new Driver
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        FullName = request.FullName.Trim(),
        PhoneNumber = phoneNumber,
        LicenseNumber = request.LicenseNumber.Trim(),
        NationalId = string.IsNullOrWhiteSpace(request.NationalId) ? null : request.NationalId.Trim(),
        DailyRemittanceUgx = request.DailyRemittanceUgx
    };
    var riderUser = new FleetUser
    {
        TenantId = tenantId,
        PhoneNumber = phoneNumber,
        Role = Roles.Rider,
        RiderId = driver.Id,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
    };
    db.Drivers.Add(driver);
    db.Users.Add(riderUser);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/v1/riders/{driver.Id}",
        new DriverResponse(driver.Id, driver.FullName, driver.PhoneNumber, driver.LicenseNumber, driver.Status));
});

fleetApi.MapGet("/trips", async (AppDbContext db, int page = 1, int pageSize = 50,
    CancellationToken cancellationToken = default) =>
{
    pageSize = Math.Clamp(pageSize, 1, 100);
    page = Math.Clamp(page, 1, int.MaxValue / pageSize);
    var query = db.Trips.OrderByDescending(x => x.StartedAt).ThenBy(x => x.Id);
    var totalCount = await query.CountAsync(cancellationToken);
    var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
        .Select(x => new TripResponse(x.Id, x.MotorcycleId, x.DriverId, x.StartedAt, x.EndedAt, x.FareUgx, x.Status))
        .ToListAsync(cancellationToken);
    return Results.Ok(new PagedResponse<TripResponse>(items, page, pageSize, totalCount));
});

fleetApi.MapPost("/trips", async (CreateTripRequest request, AppDbContext db, ClaimsPrincipal principal,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out var tenantId))
    {
        return Results.Forbid();
    }

    if (request.FareUgx < 0
        || !await db.Drivers.AnyAsync(x => x.Id == request.DriverId && x.Status == "Active", cancellationToken))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["trip"] = ["Select an active driver and a non-negative fare."]
        });
    }

    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
    var motorcycleUpdated = await db.Motorcycles
        .Where(x => x.Id == request.MotorcycleId && x.Status == "Available")
        .ExecuteUpdateAsync(updates => updates
            .SetProperty(x => x.Status, "InUse")
            .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
    if (motorcycleUpdated == 0)
    {
        return Results.Conflict(new { message = "Motorcycle was not found or is already assigned to an active trip." });
    }

    var trip = new Trip
    {
        TenantId = tenantId,
        MotorcycleId = request.MotorcycleId,
        DriverId = request.DriverId,
        StartedAt = DateTimeOffset.UtcNow,
        FareUgx = request.FareUgx
    };
    db.Trips.Add(trip);
    try
    {
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
    catch (DbUpdateException exception) when (
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
    {
        await transaction.RollbackAsync(cancellationToken);
        return Results.Conflict(new { message = "Motorcycle or driver already has an active trip." });
    }

    return Results.Created($"/api/v1/trips/{trip.Id}", trip);
});

fleetApi.MapPost("/trips/{id:guid}/complete", async (Guid id, CompleteTripRequest request, AppDbContext db,
    CancellationToken cancellationToken) =>
{
    if (request.FareUgx < 0 || !double.IsFinite(request.EndLatitude) || request.EndLatitude is < -90 or > 90
        || !double.IsFinite(request.EndLongitude) || request.EndLongitude is < -180 or > 180)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["trip"] = ["Fare must not be negative and the ending GPS coordinates must be valid."]
        });
    }

    var trip = await db.Trips.SingleOrDefaultAsync(
        x => x.Id == id && x.Status == "InProgress", cancellationToken);
    if (trip is null)
    {
        return Results.NotFound();
    }

    var motorcycle = await db.Motorcycles.SingleOrDefaultAsync(
        x => x.Id == trip.MotorcycleId, cancellationToken);
    if (motorcycle is null)
    {
        return Results.Conflict(new { message = "The motorcycle assigned to this trip is no longer available." });
    }

    trip.Status = "Completed";
    trip.EndedAt = DateTimeOffset.UtcNow;
    trip.EndLatitude = request.EndLatitude;
    trip.EndLongitude = request.EndLongitude;
    trip.FareUgx = request.FareUgx;
    motorcycle.Status = "Available";
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new TripResponse(trip.Id, trip.MotorcycleId, trip.DriverId, trip.StartedAt,
        trip.EndedAt, trip.FareUgx, trip.Status));
});

financeApi.MapPost("/payments", async (CreatePaymentRequest request, AppDbContext db, ClaimsPrincipal principal,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out var tenantId))
    {
        return Results.Forbid();
    }

    var supportedMethods = new[] { "Cash", "MobileMoney", "Bank" };
    if (request.AmountUgx <= 0 || !supportedMethods.Contains(request.Method, StringComparer.OrdinalIgnoreCase)
        || !await db.Drivers.AnyAsync(x => x.Id == request.DriverId, cancellationToken))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["payment"] = ["Amount must be positive, method must be Cash, MobileMoney, or Bank, and driver must exist."]
        });
    }

    if (request.MotorcycleId is Guid motorcycleId
        && !await db.Motorcycles.AnyAsync(x => x.Id == motorcycleId, cancellationToken))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["motorcycleId"] = ["Motorcycle was not found in this fleet."]
        });
    }

    var payment = new Payment
    {
        TenantId = tenantId,
        DriverId = request.DriverId,
        MotorcycleId = request.MotorcycleId,
        AmountUgx = request.AmountUgx,
        Method = request.Method,
        Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim()
    };
    db.Payments.Add(payment);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/v1/payments/{payment.Id}", payment);
});

financeApi.MapGet("/payments", async (AppDbContext db, int page = 1, int pageSize = 50,
    CancellationToken cancellationToken = default) =>
{
    pageSize = Math.Clamp(pageSize, 1, 100);
    page = Math.Clamp(page, 1, int.MaxValue / pageSize);
    var query = db.Payments.OrderByDescending(x => x.ReceivedAt).ThenBy(x => x.Id);
    var totalCount = await query.CountAsync(cancellationToken);
    var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
        .Select(x => new PaymentResponse(x.Id, x.DriverId, x.MotorcycleId, x.AmountUgx, x.Method, x.Status, x.ReceivedAt))
        .ToListAsync(cancellationToken);
    return Results.Ok(new PagedResponse<PaymentResponse>(items, page, pageSize, totalCount));
});

fleetApi.MapPut("/motorcycles/{id:guid}/location", async (Guid id, LocationRequest request, AppDbContext db,
    CancellationToken cancellationToken) =>
{
    if (!double.IsFinite(request.Latitude) || request.Latitude is < -90 or > 90
        || !double.IsFinite(request.Longitude) || request.Longitude is < -180 or > 180)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["location"] = ["Latitude must be -90 to 90 and longitude -180 to 180."]
        });
    }

    var motorcycle = await db.Motorcycles.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    if (motorcycle is null)
    {
        return Results.NotFound();
    }

    motorcycle.Latitude = request.Latitude;
    motorcycle.Longitude = request.Longitude;
    motorcycle.LocationRecordedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

fleetApi.MapGet("/motorcycles/{id:guid}/locations", async (
    Guid id,
    AppDbContext db,
    DateTimeOffset? from,
    DateTimeOffset? to,
    int limit = 500,
    CancellationToken cancellationToken = default) =>
{
    if (from > to || limit < 1)
    {
        return Results.BadRequest(new { message = "Provide a valid time range and positive limit." });
    }

    limit = Math.Clamp(limit, 1, 2000);
    if (!await db.Motorcycles.AnyAsync(x => x.Id == id, cancellationToken))
    {
        return Results.NotFound();
    }
    var locations = await db.GpsLogs.AsNoTracking()
        .Where(x => x.MotorcycleId == id
            && (from == null || x.RecordedAt >= from)
            && (to == null || x.RecordedAt <= to))
        .OrderByDescending(x => x.RecordedAt)
        .Take(limit)
        .Select(x => new GpsLocationResponse(x.Latitude, x.Longitude, x.SpeedKph, x.RecordedAt))
        .ToListAsync(cancellationToken);
    return Results.Ok(locations);
});

var bootstrapEmail = builder.Configuration["Bootstrap:Email"];
var bootstrapPassword = builder.Configuration["Bootstrap:Password"];
if (string.IsNullOrWhiteSpace(bootstrapEmail) || string.IsNullOrWhiteSpace(bootstrapPassword))
{
    throw new InvalidOperationException("Bootstrap:Email and Bootstrap:Password are required to provision the first administrator.");
}

if (bootstrapPassword.Length < 12)
{
    throw new InvalidOperationException("Bootstrap:Password must contain at least 12 characters.");
}

if (!Guid.TryParse(builder.Configuration["Bootstrap:TenantId"], out var tenantId) || tenantId == Guid.Empty)
{
    throw new InvalidOperationException("Bootstrap:TenantId must be a non-empty GUID.");
}

await using var scope = app.Services.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
var normalizedEmail = bootstrapEmail.Trim().ToLowerInvariant();
var existingUser = await db.Users.IgnoreQueryFilters()
    .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Email == normalizedEmail);
if (existingUser is null)
{
    var tenant = await db.Tenants.FindAsync(tenantId);
    if (tenant is null)
    {
        tenant = new Tenant
        {
            Id = tenantId,
            Name = builder.Configuration["Bootstrap:TenantName"] ?? "BodaBoss Fleet"
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
    }

    var user = new FleetUser
    {
        TenantId = tenantId,
        Email = normalizedEmail,
        Role = Roles.SuperAdmin,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(bootstrapPassword)
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();
}

app.Run();

record LoginRequest(Guid TenantId, string Identifier, string Password);
record RefreshRequest(Guid TenantId, string RefreshToken);
record LoginResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, string? Email, string Role);
record CreateMotorcycleRequest(string RegistrationNumber, string Make, string Model, int Year);
record AssignMotorcycleRequest(Guid? RiderId);
record MotorcycleResponse(Guid Id, string RegistrationNumber, string Make, string Model, int Year, string Status,
    double? Latitude, double? Longitude, DateTimeOffset? LocationRecordedAt);
record CreateDriverRequest(
    string FullName,
    string PhoneNumber,
    string LicenseNumber,
    string? NationalId,
    string Password,
    long DailyRemittanceUgx = 0);
record DriverResponse(Guid Id, string FullName, string PhoneNumber, string LicenseNumber, string Status);
record JobResponse(Guid Id, string PickupLocation, string DropoffLocation, string? Instructions, long AmountUgx,
    DateTimeOffset Deadline, string Status, DateTimeOffset? AcceptedAt, DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);
record CreateJobRequest(string PickupLocation, string DropoffLocation, string? Instructions, long AmountUgx,
    DateTimeOffset Deadline, Guid? RiderId);
record AssignJobRequest(Guid RiderId);
record CreateReportRequest(string IssueType, string Description, double? Latitude, double? Longitude);
record RegisterDeviceRequest(string FcmToken, string Platform);
record ReportResponse(Guid Id, Guid RiderId, Guid? MotorcycleId, string IssueType, string Description,
    double? Latitude, double? Longitude, string Status, string? Resolution, DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);
record UpdateReportRequest(string Status, string? Resolution);
record MaintenanceResponse(Guid Id, Guid MotorcycleId, string ServiceType, double DueAtMileageKm,
    double? CompletedAtMileageKm, long CostUgx, string Status, DateTimeOffset? DueAt,
    DateTimeOffset? CompletedAt, string? Notes);
record CreateMaintenanceRequest(Guid MotorcycleId, string ServiceType, double DueAtMileageKm,
    long CostUgx, DateTimeOffset? DueAt, string? Notes);
record CompleteMaintenanceRequest(long CostUgx, string? Notes);
record GpsLocationResponse(double Latitude, double Longitude, double? SpeedKph, DateTimeOffset RecordedAt);
record NotificationResponse(Guid Id, string Type, string Title, string Body, string Status, DateTimeOffset CreatedAt);
record CreateTripRequest(Guid MotorcycleId, Guid DriverId, long FareUgx);
record CompleteTripRequest(long FareUgx, double EndLatitude, double EndLongitude);
record TripResponse(Guid Id, Guid MotorcycleId, Guid DriverId, DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt, long FareUgx, string Status);
record CreatePaymentRequest(Guid DriverId, Guid? MotorcycleId, long AmountUgx, string Method, string? Reference);
record PaymentResponse(Guid Id, Guid DriverId, Guid? MotorcycleId, long AmountUgx, string Method, string Status,
    DateTimeOffset ReceivedAt);
record PaymentSummaryResponse(Guid Id, long AmountUgx, string Method, string Status, DateTimeOffset ReceivedAt);
record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
record LocationRequest(double Latitude, double Longitude);
