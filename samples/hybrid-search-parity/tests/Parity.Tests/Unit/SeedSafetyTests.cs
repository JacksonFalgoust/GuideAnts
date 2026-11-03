using Parity.Core;
using Parity.Postgres;
using Parity.SqlServer;

namespace Parity.Tests.Unit;

public class SeedSafetyTests
{
    [Theory]
    [InlineData("localhost", "guideants")]
    [InlineData("LOCALHOST", "guideants")]
    [InlineData("localhost,14333", "guideants")]
    [InlineData("tcp:127.0.0.1,1433", "guideants")]
    [InlineData("127.0.0.1:5432", "guideants")]
    [InlineData("::1", "guideants")]
    [InlineData("[::1]:5432", "guideants")]
    [InlineData("host.docker.internal", "guideants")]
    [InlineData("localhost\\SQLEXPRESS", "guideants")]
    [InlineData("db.example.com", "parity")]
    [InlineData("db.example.com", "Parity_english")]
    [InlineData("db.example.com", "PARITY_mini")]
    public void LocalServersAndParityDatabasesMayBeDropped(string host, string database) =>
        Assert.True(SeedSafety.IsSafeToDrop(host, database));

    [Theory]
    [InlineData("db.example.com", "guideants")]
    [InlineData("10.0.0.5", "production")]
    [InlineData("localhost.evil.example", "guideants")]
    [InlineData("db.example.com", "")]
    [InlineData("db.example.com", null)]
    [InlineData("db.example.com", "my_parity")]
    public void OtherServersAndNamesAreRefused(string host, string? database) =>
        Assert.False(SeedSafety.IsSafeToDrop(host, database));

    [Fact]
    public void TheRefusalNamesTheProfile()
    {
        var error = Assert.Throws<ParityConfigurationException>(() => SeedSafety.RequireSafeToDrop("prod-sql", "db.example.com", "guideants"));
        Assert.Contains("Profile 'prod-sql'", error.Message);
        Assert.Contains("guideants", error.Message);
    }

    [Fact]
    public async Task BothStoresRefuseBeforeConnecting()
    {
        var sql = new SqlServerSearchStore("remote-sql", "Server=db.example.com,1433;Database=guideants;User ID=sa;Password=x", TimeSpan.FromSeconds(1));
        Assert.Contains("remote-sql", (await Assert.ThrowsAsync<ParityConfigurationException>(() => sql.EnsureSchemaAsync(CancellationToken.None))).Message);

        await using var pg = new PostgresSearchStore("remote-pg", "Host=db.example.com;Database=guideants;Username=postgres;Password=x", PostgresSchema.CustomConfigName);
        Assert.Contains("remote-pg", (await Assert.ThrowsAsync<ParityConfigurationException>(() => pg.EnsureSchemaAsync(CancellationToken.None))).Message);
    }
}
