namespace DatabaseSpeed;

/// <summary>The statistics RESULTS.md reports. Inputs are never modified; an empty input gives NaN.</summary>
public static class Stats
{
    /// <summary>The middle value; for an even count, the mean of the two middle values.</summary>
    public static double Median(IReadOnlyCollection<double> samples)
    {
        if (samples.Count == 0)
        {
            return double.NaN;
        }

        var sorted = samples.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /// <summary>
    /// The nearest-rank percentile: the smallest sample with at least <paramref name="percentile"/>% of the samples at
    /// or below it, i.e. the value at rank <c>ceil(p / 100 * n)</c> (1-based) of the sorted samples. Always one of the
    /// samples; never interpolated.
    /// </summary>
    /// <param name="percentile">In (0, 100].</param>
    public static double PercentileNearestRank(IReadOnlyCollection<double> samples, double percentile)
    {
        if (percentile is <= 0 or > 100 || double.IsNaN(percentile))
        {
            throw new ArgumentOutOfRangeException(nameof(percentile), percentile, "The percentile must be in (0, 100].");
        }

        if (samples.Count == 0)
        {
            return double.NaN;
        }

        var sorted = samples.Order().ToArray();
        // The small tolerance keeps exact products such as 0.95 * 200 = 190 from becoming 191 through rounding error.
        var rank = (int)Math.Ceiling((percentile / 100.0 * sorted.Length) - 1e-9);
        return sorted[Math.Clamp(rank, 1, sorted.Length) - 1];
    }
}
