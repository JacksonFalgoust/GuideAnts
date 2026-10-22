using Parity.Core;
using Parity.SqlServer;

namespace Parity.Tests.Unit;

public class SqlServerErrorsTests
{
    [Fact]
    public void TimeoutWhileConnectingIsUnreachable() =>
        Assert.IsType<EngineUnavailableException>(SqlServerErrors.Translate(-2, 11, "timeout", "p", "h", connecting: true));

    [Fact]
    public void TimeoutWhileExecutingIsAQueryErrorNotUnreachable() =>
        Assert.Null(SqlServerErrors.Translate(-2, 11, "timeout", "p", "h", connecting: false));

    [Theory]
    [InlineData(4060)]
    [InlineData(208)]
    public void MissingDatabaseOrTableMeansNotSeeded(int number) =>
        Assert.IsType<NotSeededException>(SqlServerErrors.Translate(number, 16, "x", "p", "h", connecting: false));

    [Theory]
    [InlineData(53, true)]
    [InlineData(10061, false)]
    [InlineData(18456, true)]
    public void ConnectionLevelNumbersStayUnreachable(int number, bool connecting) =>
        Assert.IsType<EngineUnavailableException>(SqlServerErrors.Translate(number, 20, "x", "p", "h", connecting));

    [Fact]
    public void OrdinaryQueryErrorsAreReported() =>
        Assert.Null(SqlServerErrors.Translate(7630, 15, "syntax", "p", "h", connecting: false));
}
