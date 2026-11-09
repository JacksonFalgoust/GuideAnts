using FluentAssertions;
using GuideAntsApi.Database;

namespace GuideAntsApi.Tests.DataModel;

/// <summary>
/// The Postgres initializer refuses a server older than 18 before it creates or migrates anything: on Postgres 17 the
/// baseline migrates, but every <c>LIKE</c>/<c>strpos</c> query fails at run time under the case-insensitive
/// collation (parity report #26). A real Postgres 17 server is covered by the parity tests.
/// </summary>
[TestClass]
public sealed class PostgresServerVersionGuardTests
{
    [TestMethod]
    [DataRow("180000", "18.0")]
    [DataRow("180001", "18.1 (Debian 18.1-1.pgdg13+2)")]
    [DataRow("190002", "19.2")]
    public void Accepts_Postgres_18_or_later(string versionNum, string version)
    {
        var act = () => PostgresDatabaseInitializer.EnsureSupportedServerVersion(versionNum, version);

        act.Should().NotThrow();
    }

    [TestMethod]
    [DataRow("170006", "17.6 (Debian 17.6-1.pgdg12+1)")]
    [DataRow("179999", "17.99")]
    [DataRow("90624", "9.6.24")]
    public void Rejects_Postgres_before_18(string versionNum, string version)
    {
        var act = () => PostgresDatabaseInitializer.EnsureSupportedServerVersion(versionNum, version);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Be($"PostgreSQL 18 or later is required (found {version}).");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("eighteen")]
    public void Rejects_an_unreadable_version_number(string versionNum)
    {
        var act = () => PostgresDatabaseInitializer.EnsureSupportedServerVersion(versionNum, "unknown");

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Be("PostgreSQL 18 or later is required (found unknown).");
    }
}
