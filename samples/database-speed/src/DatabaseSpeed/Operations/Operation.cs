using System.Diagnostics;
using GuideAntsApi.DataModel.Database;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseSpeed.Operations;

/// <summary>The samples one provider's measured runs produced, per metric (milliseconds), plus counters.</summary>
public sealed class Measurement
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<double>> _samples = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<double>> _counters = new(StringComparer.Ordinal);

    public void Add(string metric, double milliseconds)
    {
        lock (_gate)
        {
            (_samples.TryGetValue(metric, out var list) ? list : _samples[metric] = []).Add(milliseconds);
        }
    }

    /// <summary>A non-time value recorded once per run (e.g. lost claim races), reported as a note.</summary>
    public void Count(string counter, double value)
    {
        lock (_gate)
        {
            (_counters.TryGetValue(counter, out var list) ? list : _counters[counter] = []).Add(value);
        }
    }

    public IReadOnlyList<double> Samples(string metric)
    {
        lock (_gate)
        {
            return _samples.TryGetValue(metric, out var list) ? list.ToList() : [];
        }
    }

    public IReadOnlyList<double> Counter(string counter)
    {
        lock (_gate)
        {
            return _counters.TryGetValue(counter, out var list) ? list.ToList() : [];
        }
    }

    public static double Ms(Stopwatch stopwatch) => stopwatch.Elapsed.TotalMilliseconds;
}

/// <summary>
/// One thing to time. The runner calls <see cref="PrepareAsync"/> once per provider (untimed), then
/// <see cref="RunAsync"/> for every warm-up and measured run, alternating which provider goes first. An operation
/// times only the production call(s) it is about, and checks the result outside the timed part, so a run that did no
/// work fails instead of looking fast.
/// </summary>
public interface IOperation
{
    string Name { get; }

    /// <summary>What is called, with what data (one paragraph of RESULTS.md).</summary>
    string Description { get; }

    /// <summary>The metrics <see cref="RunAsync"/> records, in report order.</summary>
    IReadOnlyList<string> Metrics { get; }

    /// <summary>Uses the cold-start run counts instead of the normal ones.</summary>
    bool IsColdStart => false;

    Task PrepareAsync(ProviderTarget target, CancellationToken ct) => Task.CompletedTask;

    Task RunAsync(ProviderTarget target, Measurement measurement, CancellationToken ct);

    /// <summary>Extra lines for the report (throughput, counts, what the run checked).</summary>
    IReadOnlyList<string> Notes(IReadOnlyDictionary<DatabaseProvider, Measurement> measured) => [];

    /// <summary>What a metric's numbers do and don't compare, printed beside its summary row.</summary>
    string? Caveat(string metric) => null;

    /// <summary>Set when the two providers returned different results; the summary marks the row.</summary>
    string? ResultMismatch => null;
}

/// <summary>
/// An operation that is one production service call in a fresh DI scope (as one HTTP request would make). Only the
/// call is timed. <see cref="Check"/> runs after the timer stops, fails the run if the result is wrong, and returns a
/// short description of the result; the report shows it per provider, so a provider that returns something different
/// is visible.
/// </summary>
public abstract class ScopedCallOperation<TResult> : IOperation
{
    private readonly Dictionary<DatabaseProvider, string> _results = [];

    public abstract string Name { get; }

    public abstract string Description { get; }

    public IReadOnlyList<string> Metrics => [Name];

    public async Task PrepareAsync(ProviderTarget target, CancellationToken ct)
    {
        await using var scope = target.Services.CreateAsyncScope();
        _results[target.Provider] = Check(await CallAsync(scope.ServiceProvider, ct));
    }

    public async Task RunAsync(ProviderTarget target, Measurement measurement, CancellationToken ct)
    {
        await using var scope = target.Services.CreateAsyncScope();
        var stopwatch = Stopwatch.StartNew();
        var result = await CallAsync(scope.ServiceProvider, ct);
        stopwatch.Stop();
        Check(result);
        measurement.Add(Name, Measurement.Ms(stopwatch));
    }

    public string? ResultMismatch => _results.Values.Distinct().Count() > 1
        ? string.Join("; ", _results.OrderBy(kv => kv.Key).Select(kv => $"{Report.ProviderName(kv.Key)} {kv.Value}"))
        : null;

    public virtual IReadOnlyList<string> Notes(IReadOnlyDictionary<DatabaseProvider, Measurement> measured)
    {
        var distinct = _results.Values.Distinct().Count();
        return
        [
            "Result: " + string.Join("; ", _results.OrderBy(kv => kv.Key).Select(kv => $"{Report.ProviderName(kv.Key)} {kv.Value}")) +
                (distinct == 1 ? " (the same on both)" : " (**differs between providers**)"),
        ];
    }

    protected abstract Task<TResult> CallAsync(IServiceProvider services, CancellationToken ct);

    /// <summary>Throws when the result shows the call didn't do its work; otherwise describes it.</summary>
    protected abstract string Check(TResult result);

    protected static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
