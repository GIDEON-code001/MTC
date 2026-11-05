using BodaBoss.Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BodaBoss.Api.Tests;

public sealed class AppDbContextModelTests
{
    [Fact]
    public void Model_builds_with_tenant_scoped_entities()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=bodaboss_test;Username=test;Password=test")
            .Options;

        using var db = new AppDbContext(options, new HttpContextAccessor());

        var tables = db.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("riders", tables);
        Assert.Contains("jobs", tables);
        Assert.Contains("report_media", tables);
        Assert.Contains("gps_logs", tables);
    }
}
