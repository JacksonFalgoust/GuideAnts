using Microsoft.Extensions.Logging;

namespace Parity.Production;

/// <summary>
/// Production's searchers catch every exception, log it and return an empty list. This logger provider records the
/// errors logged by the search running on the current async flow, so the store can report a failure as a failure
/// instead of "no results", like the sample's other stores do.
/// </summary>
internal sealed class ErrorCapture : ILoggerProvider
{
    private readonly AsyncLocal<Recording?> _current = new();

    /// <summary>Starts recording for the calling flow; dispose to stop.</summary>
    public Recording Begin() => _current.Value = new Recording(this);

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    internal sealed class Recording(ErrorCapture owner) : IDisposable
    {
        public Exception? Error { get; private set; }

        public string? Message { get; private set; }

        /// <summary>
        /// The first error that carries an exception wins (EF's own connection-error line comes before the searcher's
        /// line with the exception); an error logged without one is kept only while nothing better has been seen.
        /// </summary>
        public void Record(Exception? exception, string message)
        {
            if (Error is null && exception is not null)
            {
                (Error, Message) = (exception, message);
            }
            else if (Message is null)
            {
                Message = message;
            }
        }

        public void Dispose() => owner._current.Value = null;
    }

    private sealed class Logger(ErrorCapture owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                owner._current.Value?.Record(exception, formatter(state, exception));
            }
        }
    }
}
