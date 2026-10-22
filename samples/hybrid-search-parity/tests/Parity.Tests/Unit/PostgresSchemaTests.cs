using Npgsql;
using Parity.Core;
using Parity.Postgres;

namespace Parity.Tests.Unit;

public class PostgresSchemaTests
{
    [Theory]
    [InlineData("parity_english_nostem")]
    [InlineData("english")]
    public void AcceptsPlainConfigurationNames(string name) => PostgresSchema.ValidateConfigName(name);

    [Theory]
    [InlineData("english; drop table x")]
    [InlineData("English")]
    [InlineData("")]
    public void RejectsAnythingThatCouldInjectSql(string name) =>
        Assert.Throws<ParityConfigurationException>(() => PostgresSchema.ValidateConfigName(name));

    [Fact]
    public void GeneratedColumnUsesTheConfiguredConfig() =>
        Assert.Contains("to_tsvector('english'::regconfig, \"Content\")", PostgresSchema.AddTsvColumn("english"));

    [Fact]
    public void ProductionVariantPicksCandidatesAcrossAllProjects()
    {
        var production = PostgresSql.Hybrid(filterBeforeTopN: false);
        var scoped = PostgresSql.Hybrid(filterBeforeTopN: true);
        var ftCte = (string sql) => sql[sql.IndexOf("ft AS (", StringComparison.Ordinal)..sql.IndexOf("scored AS (", StringComparison.Ordinal)];
        Assert.DoesNotContain("IndexName", ftCte(production));
        Assert.Contains("dc.\"IndexName\" = @indexName", ftCte(scoped));
    }

    [Theory]
    [InlineData("3D000", true)]
    [InlineData("42P01", false)]
    public void MissingDatabaseOrTableMeansNotSeeded(string state, bool connecting) =>
        Assert.IsType<NotSeededException>(PostgresErrors.Translate(Pg(state), "p", "h", connecting));

    [Theory]
    [InlineData("08006", false)]
    [InlineData("57P03", true)]
    [InlineData("28P01", true)]
    public void ConnectionLevelStatesStayUnreachable(string state, bool connecting) =>
        Assert.IsType<EngineUnavailableException>(PostgresErrors.Translate(Pg(state), "p", "h", connecting));

    [Theory]
    [InlineData("22023")]
    [InlineData("42601")]
    [InlineData("57014")]
    public void OrdinaryQueryErrorsAreReported(string state) =>
        Assert.Null(PostgresErrors.Translate(Pg(state), "p", "h", connecting: false));

    [Fact]
    public void CommandTimeoutWhileExecutingIsAQueryErrorButWhileConnectingIsUnreachable()
    {
        var timeout = new NpgsqlException("timeout", new TimeoutException());
        Assert.Null(PostgresErrors.Translate(timeout, "p", "h", connecting: false));
        Assert.IsType<EngineUnavailableException>(PostgresErrors.Translate(timeout, "p", "h", connecting: true));
    }

    private static PostgresException Pg(string state) => new("msg", "ERROR", "ERROR", state);
}
