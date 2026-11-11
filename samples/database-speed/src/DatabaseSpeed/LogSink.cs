using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DatabaseSpeed;

/// <summary>
/// Collects what the services log. Several of them catch their own errors and carry on (the searchers return no
/// results, <c>JobQueueService.TryClaimAsync</c> returns no job), so a failure would otherwise look like a fast run.
/// After each run the runner calls <see cref="ThrowIfFailed"/>: any Error, or any Warning from a category listed as
/// fatal, fails the run. Other warnings are only counted and reported.
/// </summary>
public sealed class LogSink : ILoggerProvider
{
    /// <summary>Categories whose warnings mean the operation didn't do its work.</summary>
    private static readonly string[] FatalWarningCategories = ["GuideAntsApi.BackgroundJobs.JobQueueService"];

    private readonly ConcurrentQueue<string> _failures = new();
    private readonly ConcurrentDictionary<string, int> _warnings = new();

    public IReadOnlyDictionary<string, int> WarningCounts => _warnings;

    public ILogger CreateLogger(string categoryName) => new SinkLogger(this, categoryName);

    public void ThrowIfFailed(string context)
    {
        if (_failures.IsEmpty)
        {
            return;
        }

        var messages = new List<string>();
        while (_failures.TryDequeue(out var message))
        {
            messages.Add(message);
        }

        throw new InvalidOperationException($"{context} logged {messages.Count} error(s); the first: {messages[0]}");
    }

    public void Dispose()
    {
    }

    private void Record(string category, LogLevel level, string message, Exception? exception)
    {
        if (level >= LogLevel.Error || (level == LogLevel.Warning && FatalWarningCategories.Contains(category)))
        {
            _failures.Enqueue($"[{level} {category}] {message}{(exception is null ? string.Empty : $" -> {exception.GetType().Name}: {exception.Message}")}");
            return;
        }

        if (level == LogLevel.Warning)
        {
            // Group by category and the message's first words, so per-query warnings don't make a key each.
            var head = string.Join(' ', message.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6));
            _warnings.AddOrUpdate($"{category}: {head}", 1, (_, n) => n + 1);
        }
    }

    private sealed class SinkLogger(LogSink sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                sink.Record(category, logLevel, formatter(state, exception), exception);
            }
        }
    }
}
