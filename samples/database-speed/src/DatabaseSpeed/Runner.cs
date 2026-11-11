using System.Diagnostics;
using DatabaseSpeed.Operations;
using GuideAntsApi.DataModel.Database;

namespace DatabaseSpeed;

/// <summary>
/// Runs one operation on both providers under the same conditions: the warm-up runs, then the measured runs, with the
/// providers interleaved run by run and the order alternating (SQL Server first on even runs, Postgres first on odd
/// ones), so drift in the host (thermal, background load, GC) lands on both alike.
/// </summary>
public static class Runner
{
    public static async Task<OperationResult> RunAsync(
        IOperation operation,
        IReadOnlyList<ProviderTarget> targets,
        RunPlan plan,
        Action<string> log,
        CancellationToken ct)
    {
        foreach (var target in targets)
        {
            await operation.PrepareAsync(target, ct);
            target.Log.ThrowIfFailed($"{operation.Name}: preparing {target.Name}");
        }

        var failuresBefore = CommandFailures.Snapshot();
        var warmup = operation.IsColdStart ? plan.ColdStartWarmupRuns : plan.WarmupRuns;
        var measuredRuns = operation.IsColdStart ? plan.ColdStartMeasuredRuns : plan.MeasuredRuns;
        var measured = targets.ToDictionary(t => t.Provider, _ => new Measurement());
        var total = warmup + measuredRuns;
        var progress = Stopwatch.StartNew();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        for (var run = 0; run < total; run++)
        {
            var order = run % 2 == 0 ? targets : targets.Reverse();
            foreach (var target in order)
            {
                ct.ThrowIfCancellationRequested();
                var measurement = run < warmup ? new Measurement() : measured[target.Provider];
                try
                {
                    await operation.RunAsync(target, measurement, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new InvalidOperationException($"{operation.Name} failed on {target.Name} (run {run + 1} of {total}): {ex.Message}", ex);
                }

                target.Log.ThrowIfFailed($"{operation.Name} on {target.Name} (run {run + 1} of {total})");
            }

            if (progress.Elapsed > TimeSpan.FromSeconds(30) || run == total - 1)
            {
                log($"  {operation.Name}: {run + 1}/{total} runs");
                progress.Restart();
            }
        }

        var metrics = operation.Metrics
            .Select(metric => new MetricSamples(operation.Name, metric, measured.ToDictionary(kv => kv.Key, kv => kv.Value.Samples(metric)), operation.Caveat(metric)))
            .ToList();
        foreach (var metric in metrics)
        {
            foreach (var target in targets)
            {
                if (metric.For(target.Provider).Count == 0)
                {
                    throw new InvalidOperationException($"{operation.Name} recorded no '{metric.Metric}' samples on {target.Name}.");
                }
            }
        }

        var notes = operation.Notes(measured).ToList();
        var failures = CommandFailures.Since(failuresBefore);
        notes.Add(failures.Count == 0
            ? "Failed database commands (warm-up and measured runs): none"
            : "Failed database commands (warm-up and measured runs; each was recovered (retried by EF, caught by the calling service, or " +
              "expected by EF's migrator), or the operation would have failed; the time is inside the measurement): " +
              string.Join("; ", failures));
        return new OperationResult(operation.Name, operation.Description, metrics, notes, operation.ResultMismatch);
    }

    public static string PerProvider(IReadOnlyDictionary<DatabaseProvider, Measurement> measured, Func<Measurement, string> describe) =>
        string.Join("; ", measured.OrderBy(kv => kv.Key).Select(kv => $"{Report.ProviderName(kv.Key)} {describe(kv.Value)}"));
}
