namespace GuideAntsApi.DatabaseParity.Tests.Infrastructure;

[TestClass]
public static class ParityAssembly
{
    private static ParityDatabases? _databases;

    public static ParityDatabases Databases
        => _databases ?? throw new InvalidOperationException("The parity databases were not started.");

    [AssemblyInitialize]
    public static async Task StartDatabasesAsync(TestContext _)
    {
        _databases = await ParityDatabases.StartAsync();
    }

    [AssemblyCleanup]
    public static async Task StopDatabasesAsync()
    {
        if (_databases is not null)
        {
            await _databases.DisposeAsync();
            _databases = null;
        }
    }
}
