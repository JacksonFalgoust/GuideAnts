namespace Parity.Core;

public static class RepoPaths
{
    /// <summary>The folder containing HybridSearchParity.sln (samples/hybrid-search-parity).</summary>
    public static string FindSampleRoot(string? start = null) =>
        FindUp(start, dir => File.Exists(Path.Combine(dir, "HybridSearchParity.sln")), "HybridSearchParity.sln");

    /// <summary>The GuideAnts repository root (contains src/server/GuideAntsApi.sln).</summary>
    public static string FindRepoRoot(string? start = null) =>
        FindUp(start, dir => File.Exists(Path.Combine(dir, "src", "server", "GuideAntsApi.sln")), "src/server/GuideAntsApi.sln");

    private static string FindUp(string? start, Func<string, bool> isMatch, string marker)
    {
        var origin = start ?? AppContext.BaseDirectory;
        for (var dir = new DirectoryInfo(origin); dir is not null; dir = dir.Parent)
        {
            if (isMatch(dir.FullName))
            {
                return dir.FullName;
            }
        }

        throw new ParityConfigurationException($"Could not find {marker} in or above {origin}.");
    }
}
