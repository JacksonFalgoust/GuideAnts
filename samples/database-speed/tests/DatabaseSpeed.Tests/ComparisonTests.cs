using FluentAssertions;

namespace DatabaseSpeed.Tests;

[TestClass]
public class ComparisonTests
{
    [TestMethod]
    public void Ratio_is_Postgres_over_SQL_Server()
    {
        Comparison.Ratio(sqlServerMedian: 4.0, postgresMedian: 2.0).Should().Be(0.5);
        Comparison.Ratio(sqlServerMedian: 2.0, postgresMedian: 4.0).Should().Be(2.0);
    }

    [TestMethod]
    public void A_zero_SQL_Server_median_gives_no_ratio_and_no_flag()
    {
        Comparison.Ratio(0.0, 5.0).Should().BeNull();
        Comparison.IsFlagged(Comparison.Ratio(0.0, 5.0)).Should().BeFalse();
    }

    [TestMethod]
    public void A_zero_Postgres_median_is_a_ratio_of_zero()
    {
        Comparison.Ratio(5.0, 0.0).Should().Be(0.0);
        Comparison.IsFlagged(0.0).Should().BeFalse();
    }

    [TestMethod]
    public void NaN_infinite_or_negative_medians_give_no_ratio()
    {
        Comparison.Ratio(double.NaN, 1.0).Should().BeNull();
        Comparison.Ratio(1.0, double.NaN).Should().BeNull();
        Comparison.Ratio(double.PositiveInfinity, 1.0).Should().BeNull();
        Comparison.Ratio(1.0, double.PositiveInfinity).Should().BeNull();
        Comparison.Ratio(-1.0, 1.0).Should().BeNull();
        Comparison.Ratio(1.0, -1.0).Should().BeNull();
    }

    [TestMethod]
    public void Only_a_ratio_above_1_2_is_flagged()
    {
        Comparison.IsFlagged(null).Should().BeFalse();
        Comparison.IsFlagged(1.0).Should().BeFalse();
        Comparison.IsFlagged(12.0 / 10.0).Should().BeFalse();
        Comparison.IsFlagged(12.1 / 10.0).Should().BeTrue();
        Comparison.IsFlagged(1.2000001).Should().BeTrue();
    }

    [TestMethod]
    public void Delta_is_Postgres_minus_SQL_Server_and_missing_when_a_median_is()
    {
        Comparison.Delta(sqlServerMedian: 10.0, postgresMedian: 12.1).Should().BeApproximately(2.1, 1e-9);
        Comparison.Delta(sqlServerMedian: 0.5, postgresMedian: 0.38).Should().BeApproximately(-0.12, 1e-9);
        Comparison.Delta(double.NaN, 1.0).Should().BeNull();
        Comparison.Delta(1.0, double.NaN).Should().BeNull();
    }
}
