using FluentAssertions;

namespace DatabaseSpeed.Tests;

[TestClass]
public class StatsTests
{
    [TestMethod]
    public void Nearest_rank_p95_of_200_samples_is_the_190th_smallest()
    {
        var samples = Enumerable.Range(1, 200).Reverse().Select(i => (double)i).ToList();
        Stats.PercentileNearestRank(samples, 95).Should().Be(190);
    }

    [TestMethod]
    public void Nearest_rank_never_interpolates()
    {
        // 20 samples: rank ceil(0.95 * 20) = 19.
        var samples = Enumerable.Range(1, 20).Select(i => i * 10.0).ToList();
        Stats.PercentileNearestRank(samples, 95).Should().Be(190);
        // 10 samples: rank ceil(9.5) = 10, the maximum.
        Stats.PercentileNearestRank(samples.Take(10).ToList(), 95).Should().Be(100);
    }

    [TestMethod]
    public void Nearest_rank_of_a_single_sample_is_that_sample()
    {
        Stats.PercentileNearestRank([42.0], 95).Should().Be(42.0);
        Stats.PercentileNearestRank([42.0], 1).Should().Be(42.0);
    }

    [TestMethod]
    public void Nearest_rank_extremes_are_the_minimum_and_maximum()
    {
        var samples = new List<double> { 5, 3, 9, 1, 7 };
        Stats.PercentileNearestRank(samples, 100).Should().Be(9);
        Stats.PercentileNearestRank(samples, 0.1).Should().Be(1);
    }

    [TestMethod]
    public void Percentiles_of_no_samples_are_NaN()
    {
        double.IsNaN(Stats.PercentileNearestRank(Array.Empty<double>(), 95)).Should().BeTrue();
        double.IsNaN(Stats.Median(Array.Empty<double>())).Should().BeTrue();
    }

    [TestMethod]
    public void A_percentile_outside_0_to_100_is_rejected()
    {
        var samples = new List<double> { 1, 2 };
        FluentActions.Invoking(() => Stats.PercentileNearestRank(samples, 0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Stats.PercentileNearestRank(samples, 100.5)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Stats.PercentileNearestRank(samples, double.NaN)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void Median_of_an_odd_count_is_the_middle_and_of_an_even_count_the_mean_of_the_middle_two()
    {
        Stats.Median([3.0, 1.0, 2.0]).Should().Be(2.0);
        Stats.Median([4.0, 1.0, 3.0, 2.0]).Should().Be(2.5);
    }

    [TestMethod]
    public void Statistics_do_not_reorder_the_input()
    {
        var samples = new List<double> { 3, 1, 2 };
        _ = Stats.Median(samples);
        _ = Stats.PercentileNearestRank(samples, 95);
        samples.Should().Equal(3, 1, 2);
    }
}
