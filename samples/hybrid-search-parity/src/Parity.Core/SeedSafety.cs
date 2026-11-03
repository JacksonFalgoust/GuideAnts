namespace Parity.Core;

/// <summary>
/// Seeding drops and recreates the profile's database. That's only acceptable for a demo database: one on this machine
/// (or Docker's host alias), or one whose name says it's a parity database. Anything else is refused before any connection.
/// </summary>
public static class SeedSafety
{
    private static readonly HashSet<string> LoopbackHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost", "127.0.0.1", "::1", "host.docker.internal",
    };

    public static bool IsSafeToDrop(string? host, string? database) =>
        LoopbackHosts.Contains(NormalizeHost(host)) ||
        (database?.Trim().StartsWith("parity", StringComparison.OrdinalIgnoreCase) ?? false);

    public static void RequireSafeToDrop(string profile, string? host, string? database)
    {
        if (!IsSafeToDrop(host, database))
        {
            throw new ParityConfigurationException(
                $"Profile '{profile}' would drop and recreate database '{database}' on '{host}', which is neither a local server " +
                "(localhost, 127.0.0.1, ::1, host.docker.internal) nor a database whose name starts with 'parity'. Refusing to seed.");
        }
    }

    /// <summary>"tcp:db.example,1433", "db\INSTANCE", "[::1]:5432" and "host:5432" reduce to the bare host name.</summary>
    internal static string NormalizeHost(string? host)
    {
        var value = (host ?? string.Empty).Trim();
        if (value.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            value = value[4..];
        }

        if (value.StartsWith('['))
        {
            var end = value.IndexOf(']');
            return end > 0 ? value[1..end] : value;
        }

        var comma = value.IndexOf(',');
        if (comma >= 0)
        {
            value = value[..comma];
        }

        var slash = value.IndexOf('\\');
        if (slash >= 0)
        {
            value = value[..slash];
        }

        if (value.Count(c => c == ':') == 1)
        {
            value = value[..value.IndexOf(':')];
        }

        return value;
    }
}
