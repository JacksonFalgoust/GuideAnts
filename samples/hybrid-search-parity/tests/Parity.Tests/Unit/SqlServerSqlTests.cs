using Parity.SqlServer.Sql;

namespace Parity.Tests.Unit;

public class SqlServerSqlTests
{
    [Fact]
    public void QuirkFixedSqlFiltersByScopeBeforeTakingTheTopCandidates()
    {
        var sql = SqlServerSql.BuildQuirkFixedHybrid([0.5f], "alpha beta", 10);
        Assert.Contains("SELECT TOP (10) ct.[KEY], ct.[RANK]", sql);
        Assert.Contains("FROM CONTAINSTABLE(DocumentChunks, Content, '\"alpha beta\"') ct", sql);
        Assert.Contains("WHERE scoped.IndexName = @indexName", sql);
        Assert.DoesNotContain("'\"alpha beta\"', 10)", sql);
    }

    [Fact]
    public void ProductionTemplateContainsTheJoinTheVariantReplacesExactlyOnce()
    {
        var count = SqlServerSql.ProductionTemplate.Split(SqlServerSql.ProductionCandidateJoin).Length - 1;
        Assert.Equal(1, count);
    }
}
