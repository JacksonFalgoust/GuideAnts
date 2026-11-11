using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DatabaseSpeed;

/// <summary>
/// Collects what the services log. The searchers catch their own errors, log them and return no results, so a failure
/// would otherwise look like a fast run. After each run the runner calls <see cref="ThrowIfFailed"/>: any Error a
/// service logs fails the run. Warnings are counted and reported.
/// </summary>
/// <remarks>
/// EF's own "Failed executing DbCommand" errors are counted, not fatal. EF logs every failed attempt: ones its retrying
/// execution strategy then retries, and ones a service catches by design, such as the SQL Server deadlock victims
/// (1205) that <c>JobQueueService.TryClaimAsync</c> turns into "no job". A failure that isn't recovered still surfaces:
/// as an exception out of the operation, or as the service's own error log. <see cref="CommandFailures"/> records each
/// one's error code for the report.
/// </remarks>
public sealed class LogSink : ILoggerProvider
{
    private const string EfCommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

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
        if (level >= LogLevel.Error && category == EfCommandCategory)
        {
            _warnings.AddOrUpdate($"{category}: Failed executing DbCommand (error codes under each operation)", 1, (_, n) => n + 1);
            return;
        }

        if (level >= LogLevel.Error)
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
