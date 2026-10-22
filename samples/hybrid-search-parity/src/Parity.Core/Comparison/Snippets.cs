using System.Text.RegularExpressions;

namespace Parity.Core.Comparison;

public static class Snippets
{
    /// <summary>About 2 × <paramref name="radius"/> characters around the first occurrence of the query's longest word.</summary>
    public static string Around(string content, string query, int radius = 80)
    {
        var index = -1;
        foreach (var word in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).OrderByDescending(w => w.Length))
        {
            index = content.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                break;
            }
        }

        var start = Math.Max(0, (index < 0 ? 0 : index) - radius);
        var length = Math.Min(content.Length - start, radius * 2);
        return Regex.Replace(content.Substring(start, length), @"\s+", " ").Trim();
    }
}
