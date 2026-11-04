using GuideAntsApi.DataModel.Database;

namespace GuideAntsApi.IntegrationTests.Infrastructure;

/// <summary>
/// The database provider the integration suite runs against, read once from
/// <c>GA_INTEGRATION_TEST_DB_PROVIDER</c>: <c>SqlServer</c> (the default when unset or empty) or <c>Postgres</c>,
/// case-insensitive.
/// </summary>
public static class DatabaseTestProvider
{
    public const string ProviderEnvVar = "GA_INTEGRATION_TEST_DB_PROVIDER";

    private static readonly Lazy<DatabaseProvider> _current = new(ReadFromEnvironment);

    public static DatabaseProvider Current => _current.Value;

    /// <summary>Marks the calling test inconclusive unless the suite is running on SQL Server.</summary>
    public static void RequireSqlServer()
    {
        if (Current != DatabaseProvider.SqlServer)
        {
            Assert.Inconclusive("SQL Server-only test");
        }
    }

    private static DatabaseProvider ReadFromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(ProviderEnvVar);
        try
        {
            return DatabaseProviderSettings.Parse(value);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Unknown {ProviderEnvVar} '{value}'. Use 'SqlServer' or 'Postgres'.");
        }
    }
}
