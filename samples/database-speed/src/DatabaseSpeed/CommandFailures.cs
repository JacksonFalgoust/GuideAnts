using System.Collections.Concurrent;
using System.Diagnostics;
using GuideAntsApi.DataModel.Database;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace DatabaseSpeed;

/// <summary>
/// Every database command that failed, by provider and error code, read from EF's <see cref="DiagnosticListener"/>
/// (so the host-like registrations stay unchanged). A failed command isn't necessarily a failed operation: the
/// retrying execution strategy retries transient errors such as SQL Server deadlock victims (1205), and
/// <c>JobQueueService.TryClaimAsync</c> turns the rest into "no job". RESULTS.md reports these per operation, because
/// their cost (detection, retry delay) is inside the measured time.
/// </summary>
public static class CommandFailures
{
    private static readonly ConcurrentDictionary<(DatabaseProvider Provider, string Error), int> Counts = new();
    private static IDisposable? _subscription;

    public static void Start() => _subscription ??= DiagnosticListener.AllListeners.Subscribe(new ListenerObserver());

    public static IReadOnlyDictionary<(DatabaseProvider Provider, string Error), int> Snapshot() =>
        new Dictionary<(DatabaseProvider, string), int>(Counts);

    /// <returns>The failures since <paramref name="before"/>, e.g. "SQL Server 8 x SqlException 1205".</returns>
    public static IReadOnlyList<string> Since(IReadOnlyDictionary<(DatabaseProvider Provider, string Error), int> before) =>
        Counts
            .Select(kv => (kv.Key, Count: kv.Value - before.GetValueOrDefault(kv.Key)))
            .Where(x => x.Count > 0)
            .OrderBy(x => x.Key.Provider).ThenByDescending(x => x.Count)
            .Select(x => $"{Report.ProviderName(x.Key.Provider)} {x.Count:N0} x {x.Key.Error}")
            .ToList();

    private static void Record(CommandErrorEventData data)
    {
        var exception = data.Exception;
        for (var e = exception; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case SqlException sql:
                    Counts.AddOrUpdate((DatabaseProvider.SqlServer, $"SqlException {sql.Number}{Describe(sql.Number)}"), 1, (_, n) => n + 1);
                    return;
                case PostgresException pg:
                    Counts.AddOrUpdate((DatabaseProvider.Postgres, $"PostgresException {pg.SqlState}"), 1, (_, n) => n + 1);
                    return;
                case NpgsqlException npgsql:
                    Counts.AddOrUpdate((DatabaseProvider.Postgres, $"NpgsqlException: {npgsql.Message}"), 1, (_, n) => n + 1);
                    return;
            }
        }

        var provider = data.Context?.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true
            ? DatabaseProvider.Postgres
            : DatabaseProvider.SqlServer;
        Counts.AddOrUpdate((provider, exception.GetType().Name), 1, (_, n) => n + 1);
    }

    private static string Describe(int number) => number switch
    {
        1205 => " (deadlock victim)",
        1222 => " (lock request timeout)",
        -2 => " (command timeout)",
        _ => string.Empty,
    };

    private sealed class ListenerObserver : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == DbLoggerCategory.Name)
            {
                listener.Subscribe(new EventObserver(), name => name == RelationalEventId.CommandError.Name);
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    private sealed class EventObserver : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is CommandErrorEventData data)
            {
                Record(data);
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
