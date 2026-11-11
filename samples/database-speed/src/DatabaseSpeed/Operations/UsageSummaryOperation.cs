using System.Globalization;
using GuideAntsApi.Models;
using GuideAntsApi.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseSpeed.Operations;

/// <summary>The usage report: 30 days in daily buckets.</summary>
public sealed class UsageSummaryOperation(SeedData seed) : ScopedCallOperation<UsageSummaryDto>
{
    public override string Name => "usage summary over 30 days";

    public override string Description =>
        "`IUsageQueryService.GetOwnerUsageSummaryAsync(now - 30 days, now, Day)` (`UsageQueryService`): event count, cost total, daily " +
        $"buckets and per-category totals over {seed.Volumes.UsageEvents:N0} usage events spread across 45 days (SQL Server buckets with " +
        "`DATEDIFF`, Postgres with the date arithmetic that counts the same boundaries).";

    protected override Task<UsageSummaryDto> CallAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IUsageQueryService>().GetOwnerUsageSummaryAsync(seed.Now.AddDays(-30), seed.Now, UsageBucketDto.Day);

    protected override string Check(UsageSummaryDto result)
    {
        Expect(result.Totals.TotalEvents > 0, "The summary counted no events.");
        Expect(result.TimeSeries.Count > 0, "The summary has no daily buckets.");
        return string.Create(CultureInfo.InvariantCulture,
            $"{result.Totals.TotalEvents:N0} events, ${result.Totals.TotalCostUsd:F4}, {result.TimeSeries.Count} daily buckets, {result.ByCategory.Count} categories");
    }
}
