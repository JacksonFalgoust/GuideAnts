using FluentAssertions;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.DataModel.Models;
using GuideAntsApi.IntegrationTests.Infrastructure;
using GuideAntsApi.Models;
using GuideAntsApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GuideAntsApi.IntegrationTests.DatabaseBehavior;

/// <summary>
/// Usage report time series must bucket events the same way on both providers. The expected values follow
/// SQL Server <c>DATEDIFF</c> semantics: day buckets split at UTC midnight, week buckets split on Sunday and
/// month buckets split on the first of the month. The tests talk to the suite's database directly (no web
/// host) and query both the owner and the project summary, the two methods that build the time series.
/// </summary>
[TestClass]
public sealed class UsageBucketBehaviorTests
{
    private ServiceProvider _services = null!;
    private Guid _projectId;

    [TestInitialize]
    public async Task TestInitialize()
    {
        var containers = TestContainerManager.Instance;
        var connectionString = await containers.GetConnectionStringAsync();

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            ApplicationDbContextOptions.Configure(
                options,
                containers.Provider,
                connectionString,
                new DbProviderTuning(60, UseVectorSearch: true, ExplicitSingleQuery: true)));
        _services = services.BuildServiceProvider();
        _projectId = Guid.NewGuid();
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.UsageEvents.Where(e => e.ProjectId == _projectId).ExecuteDeleteAsync();
        }

        await _services.DisposeAsync();
    }

    [TestMethod]
    public async Task Day_buckets_split_at_utc_midnight()
    {
        await SeedEventsAsync(
            (Utc(2026, 3, 1, 0, 0, 0), 1m),
            (Utc(2026, 3, 1, 23, 59, 59), 2m),
            (Utc(2026, 3, 2, 0, 0, 0), 4m));

        await AssertTimeSeriesAsync(
            UsageBucketDto.Day,
            from: Utc(2026, 3, 1, 0, 0, 0),
            to: Utc(2026, 3, 2, 23, 59, 59),
            new UsageTimePointDto(Utc(2026, 3, 1, 0, 0, 0), 2, 3m),
            new UsageTimePointDto(Utc(2026, 3, 2, 0, 0, 0), 1, 4m));
    }

    [TestMethod]
    public async Task Week_buckets_split_on_sunday()
    {
        // 2026-03-07 is a Saturday and 2026-03-08 a Sunday. The range starts mid-week (Wednesday 2026-03-04), so
        // the first bucket is anchored on the Sunday before it, 2026-03-01.
        await SeedEventsAsync(
            (Utc(2026, 3, 4, 9, 0, 0), 1m),
            (Utc(2026, 3, 7, 12, 0, 0), 2m),
            (Utc(2026, 3, 8, 12, 0, 0), 4m));

        await AssertTimeSeriesAsync(
            UsageBucketDto.Week,
            from: Utc(2026, 3, 4, 0, 0, 0),
            to: Utc(2026, 3, 9, 0, 0, 0),
            new UsageTimePointDto(Utc(2026, 3, 1, 0, 0, 0), 2, 3m),
            new UsageTimePointDto(Utc(2026, 3, 8, 0, 0, 0), 1, 4m));
    }

    [TestMethod]
    public async Task Month_buckets_split_on_the_first()
    {
        // The range starts mid-month, so the first bucket is anchored on 2026-01-01.
        await SeedEventsAsync(
            (Utc(2026, 1, 20, 8, 0, 0), 1m),
            (Utc(2026, 1, 31, 23, 0, 0), 2m),
            (Utc(2026, 2, 1, 1, 0, 0), 4m));

        await AssertTimeSeriesAsync(
            UsageBucketDto.Month,
            from: Utc(2026, 1, 15, 0, 0, 0),
            to: Utc(2026, 2, 2, 0, 0, 0),
            new UsageTimePointDto(Utc(2026, 1, 1, 0, 0, 0), 2, 3m),
            new UsageTimePointDto(Utc(2026, 2, 1, 0, 0, 0), 1, 4m));
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute, int second) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    private async Task SeedEventsAsync(params (DateTime Created, decimal ChargeUsd)[] events)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        foreach (var (created, chargeUsd) in events)
        {
            db.UsageEvents.Add(new UsageEvent
            {
                ProjectId = _projectId,
                Created = created,
                Category = UsageCategory.ChatCompletion,
                Service = "UsageBucketBehaviorTests",
                Operation = "chat",
                ChargeUsd = chargeUsd
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task AssertTimeSeriesAsync(
        UsageBucketDto bucket,
        DateTime from,
        DateTime to,
        params UsageTimePointDto[] expected)
    {
        var usage = new UsageQueryService(_services.GetRequiredService<IServiceScopeFactory>());

        var projectSummary = await usage.GetProjectUsageSummaryAsync(_projectId, from, to, bucket);
        projectSummary.Should().NotBeNull();
        projectSummary!.TimeSeries.Should().Equal(expected);

        // No other test writes usage events in early 2026, so the owner summary sees only this test's events.
        var ownerSummary = await usage.GetOwnerUsageSummaryAsync(from, to, bucket);
        ownerSummary.TimeSeries.Should().Equal(expected);
    }
}
