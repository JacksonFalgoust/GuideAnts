using Parity.Core.Ids;
using Parity.Core.Model;

namespace Parity.Tests.Support;

/// <summary>Builds results whose chunk IDs come from short readable keys ("a", "b", …).</summary>
public static class Hits
{
    public static Guid Id(string key) => DeterministicGuid.Create($"test:{key}");

    public static SearchHit Hit(string key, double score, double vectorScore = 0.5, double textScore = 0) =>
        new(Id(key), key, null, null, 0, "idx", vectorScore, textScore, score);

    public static VectorHit Vec(string key, double score) => new(Id(key), score);

    public static IReadOnlyDictionary<Guid, double> Scores(params (string Key, double Score)[] scores) =>
        scores.ToDictionary(s => Id(s.Key), s => s.Score);
}
