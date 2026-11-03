namespace GuideAntsApi.DataModel.Database;

public static class DatabaseProviderSettings
{
    public const string ConfigurationKey = "Database:Provider";

    public static DatabaseProvider Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DatabaseProvider.SqlServer;
        }

        var trimmed = value.Trim();
        if (trimmed.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProvider.SqlServer;
        }

        if (trimmed.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProvider.Postgres;
        }

        throw new InvalidOperationException($"Unknown {ConfigurationKey} '{value}'. Use 'SqlServer' or 'Postgres'.");
    }

    public static DatabaseProvider FromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(ConfigurationKey)
            ?? Environment.GetEnvironmentVariable("Database__Provider");
        return Parse(value);
    }
}
