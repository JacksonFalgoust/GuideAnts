using System.Diagnostics;
using GuideAntsApi.BackgroundJobs;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.DataModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseSpeed.Operations;

/// <summary>
/// Several workers draining the job queue at once through the production <see cref="IJobQueueService"/>: each worker
/// loops <c>TryClaimAsync</c> then <c>CompleteAsync</c> until every job is done. One run is one drain; the drain's wall
/// time gives the throughput, and every successful <c>TryClaimAsync</c> is a per-claim latency sample. Neither
/// provider's claim skips locked rows (Postgres has no <c>SKIP LOCKED</c>, by design), so a worker that picks the row
/// another worker is claiming waits for it and then gets no job; those empty claims are counted. A claim statement
/// that fails with a provider error, such as a SQL Server deadlock victim (1205), is caught by <c>TryClaimAsync</c>
/// (<c>JobQueueService.cs</c>, the <c>catch ... when DatabaseErrors.TryGetProviderError</c>): it logs a "Job claim failed"
/// warning and returns null, so the worker sees an empty claim and tries again. That is not a failed operation. In the
/// local full-volume check every 1205 reached that catch (4 failed commands, 4 warnings), so EF's retrying execution
/// strategy did not retry it. The runner lists the failed commands under the operation.
/// </summary>
public sealed class JobClaimOperation : IOperation
{
    public const string JobType = "DatabaseSpeedProbe";

    private static readonly TimeSpan DrainTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// <c>TryClaimAsync</c> turns a database error into "no job", so a broken claim would only end at the drain timeout.
    /// No completed job for this long means the claims keep failing.
    /// </summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromMinutes(2);
    private readonly int _jobs;
    private readonly int _workers;

    public JobClaimOperation(SeedVolumes volumes)
    {
        _jobs = volumes.JobsPerDrain;
        _workers = volumes.JobWorkers;
        DrainMetric = $"job claims: drain {_jobs:N0} jobs with {_workers} workers";
        ClaimMetric = "job claims: per-claim latency (successful TryClaimAsync)";
    }

    public string Name => "job claims";

    public string DrainMetric { get; }

    public string ClaimMetric { get; }

    public string Description =>
        $"{_workers} concurrent workers, each with its own scope and `IJobQueueService` (`JobQueueService`), loop " +
        $"`TryClaimAsync(\"{JobType}\", leaseSeconds: 300)` and `CompleteAsync(id, claimToken)` until all {_jobs:N0} pending jobs " +
        "are completed. Before each drain (not timed) the queue is emptied and refilled with the same jobs. A run is one drain; " +
        "the per-claim rows count every successful claim of the measured drains, so their \"runs\" column is a sample count.";

    public IReadOnlyList<string> Metrics => [DrainMetric, ClaimMetric];

    public async Task RunAsync(ProviderTarget target, Measurement measurement, CancellationToken ct)
    {
        await RefillAsync(target, ct);

        var completed = 0;
        var emptyClaims = 0;
        var claimSamples = new List<double>[_workers];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(DrainTimeout);

        var lastProgressTicks = Stopwatch.GetTimestamp();
        var drain = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, _workers).Select(worker => Task.Run(async () =>
        {
            var samples = claimSamples[worker] = new List<double>(_jobs / _workers * 2);
            await using var scope = target.Services.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();
            var claim = new Stopwatch();
            while (Volatile.Read(ref completed) < _jobs)
            {
                claim.Restart();
                var job = await queue.TryClaimAsync(JobType, 300, timeout.Token);
                claim.Stop();
                if (job is null)
                {
                    if (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastProgressTicks)) > StallTimeout)
                    {
                        throw new TimeoutException($"No job completed for {StallTimeout.TotalMinutes:0} minutes ({Volatile.Read(ref completed)} of {_jobs} done); the claims keep failing.");
                    }

                    Interlocked.Increment(ref emptyClaims);
                    await Task.Yield();
                    continue;
                }

                samples.Add(Measurement.Ms(claim));
                if (!await queue.CompleteAsync(job.Id, job.ClaimToken, timeout.Token))
                {
                    throw new InvalidOperationException($"CompleteAsync refused job {job.Id}.");
                }

                Interlocked.Increment(ref completed);
                Interlocked.Exchange(ref lastProgressTicks, Stopwatch.GetTimestamp());
            }
        }, timeout.Token)).ToList();
        await Task.WhenAll(workers);
        drain.Stop();

        await using (var db = await target.CreateContextAsync(ct))
        {
            var done = await db.JobQueue.CountAsync(j => j.JobType == JobType && j.Status == JobStatus.Completed, ct);
            if (done != _jobs || completed != _jobs)
            {
                throw new InvalidOperationException($"The drain completed {completed} jobs ({done} in the table); expected {_jobs}.");
            }
        }

        measurement.Add(DrainMetric, Measurement.Ms(drain));
        foreach (var sample in claimSamples.SelectMany(s => s))
        {
            measurement.Add(ClaimMetric, sample);
        }

        measurement.Count("empty claims", emptyClaims);
    }

    public IReadOnlyList<string> Notes(IReadOnlyDictionary<DatabaseProvider, Measurement> measured) =>
    [
        "Throughput from the median drain: " + Runner.PerProvider(measured, m =>
        {
            var median = Stats.Median(m.Samples(DrainMetric));
            return double.IsNaN(median) || median <= 0 ? "n/a" : $"{_jobs / (median / 1000.0):N0} jobs/s";
        }),
        "Empty claims per drain (a worker lost the race for a row, found nothing pending at the drain's tail, or was a SQL Server " +
            "deadlock victim that `TryClaimAsync` turned into no job), median / max: " +
            Runner.PerProvider(measured, m => $"{Stats.Median(m.Counter("empty claims")):N0} / {(m.Counter("empty claims").DefaultIfEmpty(0).Max()):N0}"),
    ];

    /// <summary>Empties the queue and adds the drain's pending jobs (oldest first, all available now). Not timed.</summary>
    private async Task RefillAsync(ProviderTarget target, CancellationToken ct)
    {
        await using var db = await target.CreateContextAsync(ct);
        await db.JobQueue.ExecuteDeleteAsync(ct);
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        var now = DateTime.UtcNow;
        for (var i = 0; i < _jobs; i++)
        {
            db.JobQueue.Add(new JobQueue
            {
                JobType = JobType,
                PayloadJson = $"{{\"n\":{i}}}",
                Priority = 0,
                Status = JobStatus.Pending,
                AvailableAt = now.AddSeconds(-5),
                Created = now.AddMilliseconds(i - _jobs),
                UpdatedUtc = now,
                MaxAttempts = 5,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
