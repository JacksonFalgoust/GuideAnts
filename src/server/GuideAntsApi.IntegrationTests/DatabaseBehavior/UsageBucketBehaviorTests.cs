using FluentAssertions;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.DataModel.Models;
using GuideAntsApi.IntegrationTests.Infrastructure;
using GuideAntsApi.Models;
using GuideAntsApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GuideAntsApi.IntegrationTests.DatabaseBehavior;

/// <summary>
/// Usage report time series must bucket events the same way on both providers. The expected values follow
/// SQL Server <c>DATEDIFF</c> semantics: day buckets split at UTC midnight, week buckets split on Sunday and
/// month buckets split on the first of the month. The tests talk to the suite's database directly (no web
/// host) and query both the owner and the project summary, the two methods that build the time series.
/// </summary>
/// <remarks>
/// Week anchors are always a Sunday: <c>NormalizeBucketStart</c> moves the range start back to the Sunday on or
/// before it, and every event is at or after the range start. So whole weeks since the anchor and whole weeks since
/// the fixed Sunday origin of the Postgres formula give the same index here. The week case starts its range on a
/// Wednesday, which tells Sunday-based buckets apart from buckets counted from the range start.
/// </remarks>
[TestClass]
public sealed class UsageBucketBehaviorTests
{
    private static readonly BucketScenario DayScenario = new(
        UsageBucketDto.Day,
        From: Utc(2026, 3, 1, 0, 0, 0),
        To: Utc(2026, 3, 4, 23, 59, 59),
        Events:
        [
            (Utc(2026, 3, 1, 0, 0, 0), 1m),
            (Utc(2026, 3, 1, 23, 59, 59), 2m),
            (Utc(2026, 3, 2, 0, 0, 0), 4m),
            (Utc(2026, 3, 4, 12, 0, 0), 8m)
        ],
        Expected:
        [
            new UsageTimePointDto(Utc(2026, 3, 1, 0, 0, 0), 2, 3m),
            new UsageTimePointDto(Utc(2026, 3, 2, 0, 0, 0), 1, 4m),
            new UsageTimePointDto(Utc(2026, 3, 4, 0, 0, 0), 1, 8m)
        ]);

    // 2026-03-07 is a Saturday and 2026-03-08 a Sunday. The range starts on Wednesday 2026-03-04, so the first bucket
    // is anchored on the Sunday before it, 2026-03-01.
    private static readonly BucketScenario WeekScenario = new(
        UsageBucketDto.Week,
        From: Utc(2026, 3, 4, 0, 0, 0),
        To: Utc(2026, 3, 23, 0, 0, 0),
        Events:
        [
            (Utc(2026, 3, 4, 9, 0, 0), 1m),
            (Utc(2026, 3, 7, 12, 0, 0), 2m),
            (Utc(2026, 3, 7, 23, 59, 59), 4m),
            (Utc(2026, 3, 8, 0, 0, 0), 8m),
            (Utc(2026, 3, 8, 12, 0, 0), 16m),
            (Utc(2026, 3, 22, 0, 0, 0), 32m)
        ],
        Expected:
        [
            new UsageTimePointDto(Utc(2026, 3, 1, 0, 0, 0), 3, 7m),
            new UsageTimePointDto(Utc(2026, 3, 8, 0, 0, 0), 2, 24m),
            new UsageTimePointDto(Utc(2026, 3, 22, 0, 0, 0), 1, 32m)
        ]);

    // The range starts mid-month, so the first bucket is anchored on 2026-01-01.
    private static readonly BucketScenario MonthScenario = new(
        UsageBucketDto.Month,
        From: Utc(2026, 1, 15, 0, 0, 0),
        To: Utc(2026, 2, 2, 0, 0, 0),
        Events:
        [
            (Utc(2026, 1, 20, 8, 0, 0), 1m),
            (Utc(2026, 1, 31, 23, 0, 0), 2m),
            (Utc(2026, 2, 1, 1, 0, 0), 4m)
        ],
        Expected:
        [
            new UsageTimePointDto(Utc(2026, 1, 1, 0, 0, 0), 2, 3m),
            new UsageTimePointDto(Utc(2026, 2, 1, 0, 0, 0), 1, 4m)
        ]);

    // Anchored on 2025-12-01: January 2026 is index 1 and February 2026 index 2.
    private static readonly BucketScenario MonthAcrossYearScenario = new(
        UsageBucketDto.Month,
        From: Utc(2025, 12, 10, 0, 0, 0),
        To: Utc(2026, 2, 28, 0, 0, 0),
        Events:
        [
            (Utc(2025, 12, 31, 23, 59, 59), 1m),
            (Utc(2026, 1, 1, 0, 0, 0), 2m),
            (Utc(2026, 2, 1, 0, 0, 0), 4m)
        ],
        Expected:
        [
            new UsageTimePointDto(Utc(2025, 12, 1, 0, 0, 0), 1, 1m),
            new UsageTimePointDto(Utc(2026, 1, 1, 0, 0, 0), 1, 2m),
            new UsageTimePointDto(Utc(2026, 2, 1, 0, 0, 0), 1, 4m)
        ]);

    private string _connectionString = null!;
    private ServiceProvider _services = null!;
    private readonly List<Guid> _projectIds = [];

    [TestInitialize]
    public async Task TestInitialize()
    {
        _connectionString = await TestContainerManager.Instance.GetConnectionStringAsync();
        _services = CreateServices(_connectionString);
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.UsageEvents
                .Where(e => e.ProjectId != null && _projectIds.Contains(e.ProjectId.Value))
                .ExecuteDeleteAsync();
        }

        await _services.DisposeAsync();
    }

    [TestMethod]
    public Task Day_buckets_split_at_utc_midnight() => RunScenarioAsync(_services, DayScenario);

    [TestMethod]
    public Task Week_buckets_split_on_sunday() => RunScenarioAsync(_services, WeekScenario);

    [TestMethod]
    public Task Month_buckets_split_on_the_first() => RunScenarioAsync(_services, MonthScenario);

    [TestMethod]
    public Task Month_buckets_count_across_a_year_boundary() => RunScenarioAsync(_services, MonthAcrossYearScenario);

    /// <summary>
    /// Postgres evaluates <c>timestamptz</c> functions in the session time zone unless told otherwise, so the buckets
    /// must stay on UTC boundaries whatever zone the session uses. Every scenario's boundary events would merge into
    /// one bucket under local-midnight math in both zones.
    /// </summary>
    [TestMethod]
    [DataRow("Pacific/Auckland")]
    [DataRow("America/Los_Angeles")]
    public async Task Buckets_split_at_utc_boundaries_whatever_the_postgres_session_time_zone(string sessionTimeZone)
    {
        DatabaseTestProvider.RequirePostgres();

        var zonedConnectionString = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            Timezone = sessionTimeZone
        }.ConnectionString;
        await using var zonedServices = CreateServices(zonedConnectionString);

        using (var scope = zonedServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sessionZone = await db.Database.SqlQueryRaw<string>("SELECT current_setting('TimeZone') AS \"Value\"")
                .SingleAsync();
            sessionZone.Should().Be(sessionTimeZone, "the scenarios must run in a non-UTC session");
        }

        foreach (var scenario in new[] { DayScenario, WeekScenario, MonthScenario, MonthAcrossYearScenario })
        {
            await RunScenarioAsync(zonedServices, scenario);
        }
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute, int second) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    private static ServiceProvider CreateServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            ApplicationDbContextOptions.Configure(
                options,
                TestContainerManager.Instance.Provider,
                connectionString,
                new DbProviderTuning(60, UseVectorSearch: true, ExplicitSingleQuery: true)));
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Seeds the scenario's events under a fresh project, checks the project and owner time series, and deletes the
    /// events again so the next scenario's owner summary sees only its own.
    /// </summary>
    private async Task RunScenarioAsync(ServiceProvider services, BucketScenario scenario)
    {
        var projectId = Guid.NewGuid();
        _projectIds.Add(projectId);

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var (created, chargeUsd) in scenario.Events)
            {
                db.UsageEvents.Add(new UsageEvent
                {
                    ProjectId = projectId,
                    Created = created,
                    Category = UsageCategory.ChatCompletion,
                    Service = "UsageBucketBehaviorTests",
                    Operation = "chat",
                    ChargeUsd = chargeUsd
                });
            }

            await db.SaveChangesAsync();
        }

        try
        {
            var usage = new UsageQueryService(services.GetRequiredService<IServiceScopeFactory>());
            var because = $"{scenario.Bucket} buckets from {scenario.From:O}";

            var projectSummary = await usage.GetProjectUsageSummaryAsync(projectId, scenario.From, scenario.To, scenario.Bucket);
            projectSummary.Should().NotBeNull();
            projectSummary!.TimeSeries.Should().Equal(scenario.Expected, because);

            // No other test writes usage events in late 2025 or early 2026, so the owner summary sees only these.
            var ownerSummary = await usage.GetOwnerUsageSummaryAsync(scenario.From, scenario.To, scenario.Bucket);
            ownerSummary.TimeSeries.Should().Equal(scenario.Expected, because);
        }
        finally
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.UsageEvents.Where(e => e.ProjectId == projectId).ExecuteDeleteAsync();
        }
    }

    private sealed record BucketScenario(
        UsageBucketDto Bucket,
        DateTime From,
        DateTime To,
        (DateTime Created, decimal ChargeUsd)[] Events,
        UsageTimePointDto[] Expected);
}
