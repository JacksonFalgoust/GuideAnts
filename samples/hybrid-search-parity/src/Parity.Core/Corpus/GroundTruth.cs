using System.Text.RegularExpressions;
using Parity.Core.Data;

namespace Parity.Core.Corpus;

/// <summary>
/// An engine-independent oracle: chunks containing the phrase as whole words, case-insensitive,
/// whitespace-flexible. Used only by the sanity checks, never by the parity rules.
/// </summary>
public static class GroundTruth
{
    public static Regex PhraseRegex(string phrase)
    {
        var words = phrase.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape);
        return new Regex($@"(?<!\w){string.Join(@"\s+", words)}(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<Guid>> Compute(IReadOnlyList<QueryDefinition> queries, IReadOnlyList<ChunkDraft> chunks)
    {
        var result = new Dictionary<string, IReadOnlyList<Guid>>();
        foreach (var query in queries.Where(q => QueryGroups.HasGroundTruth(q.Group)))
        {
            var regex = PhraseRegex(query.Text);
            result[query.Id] = chunks.Where(c => regex.IsMatch(c.Content)).Select(c => c.Id).OrderBy(id => id).ToList();
        }

        return result;
    }
}
