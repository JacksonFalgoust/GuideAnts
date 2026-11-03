using System.Globalization;
using Parity.Core.Settings;

namespace Parity.Core.Reporting;

/// <summary>Finds the settings that decide the verdict and differ from the committed appsettings.json.</summary>
public static class SettingsDrift
{
    public static IReadOnlyList<string> Differences(ParitySettings committed, ParitySettings effective)
    {
        var differences = new List<string>();

        void Check<T>(string name, T was, T now)
        {
            if (!EqualityComparer<T>.Default.Equals(was, now))
            {
                differences.Add(string.Create(CultureInfo.InvariantCulture, $"{name}: committed {was}, effective {now}"));
            }
        }

        Check("Parity:Baseline", committed.Parity.Baseline, effective.Parity.Baseline);
        Check("Parity:Candidate", committed.Parity.Candidate, effective.Parity.Candidate);
        Check("Parity:VectorTopK", committed.Parity.VectorTopK, effective.Parity.VectorTopK);
        Check("Parity:VectorTolerance", committed.Parity.VectorTolerance, effective.Parity.VectorTolerance);
        Check("Parity:TieTolerance", committed.Parity.TieTolerance, effective.Parity.TieTolerance);
        Check("Parity:Thresholds:MinMeanTop5Overlap", committed.Parity.Thresholds.MinMeanTop5Overlap, effective.Parity.Thresholds.MinMeanTop5Overlap);
        Check("Parity:Thresholds:MinSameTopOneRate", committed.Parity.Thresholds.MinSameTopOneRate, effective.Parity.Thresholds.MinSameTopOneRate);
        Check("Hybrid:MaxResults", committed.Hybrid.MaxResults, effective.Hybrid.MaxResults);
        Check("Hybrid:VectorWeight", committed.Hybrid.VectorWeight, effective.Hybrid.VectorWeight);
        Check("Hybrid:TextWeight", committed.Hybrid.TextWeight, effective.Hybrid.TextWeight);
        Check("Hybrid:CandidateMultiplier", committed.Hybrid.CandidateMultiplier, effective.Hybrid.CandidateMultiplier);
        return differences;
    }
}
