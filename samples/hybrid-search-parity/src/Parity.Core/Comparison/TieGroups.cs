namespace Parity.Core.Comparison;

public static class TieGroups
{
    /// <summary>
    /// Groups consecutive items (already sorted by score, descending) whose score is within
    /// <paramref name="tolerance"/> of the group's first item. Order within a group is arbitrary.
    /// </summary>
    public static List<List<T>> Build<T>(IReadOnlyList<T> sortedDescending, Func<T, double> score, double tolerance)
    {
        var groups = new List<List<T>>();
        foreach (var item in sortedDescending)
        {
            if (groups.Count > 0 && Math.Abs(score(groups[^1][0]) - score(item)) <= tolerance)
            {
                groups[^1].Add(item);
            }
            else
            {
                groups.Add([item]);
            }
        }

        return groups;
    }
}
