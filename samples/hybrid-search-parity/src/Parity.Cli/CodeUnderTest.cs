using System.Xml.Linq;
using Parity.Core;

namespace Parity.Cli;

/// <summary>
/// What a production profile runs besides the sample: every project <c>Parity.Production</c> references, directly or
/// transitively, that lies outside the sample directory, plus the MSBuild files that apply to those projects
/// (<c>Directory.Build.props</c>, <c>Directory.Build.targets</c>, <c>Directory.Packages.props</c> up to the repository
/// root).
/// </summary>
public static class CodeUnderTest
{
    public const string ProductionProject = "src/Parity.Production/Parity.Production.csproj";

    private static readonly string[] BuildFiles = ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"];

    /// <returns>Paths relative to <paramref name="repoRoot"/>, with forward slashes, sorted ordinally.</returns>
    public static IReadOnlyList<string> Paths(string sampleRoot, string repoRoot)
    {
        var sample = Normalize(sampleRoot);
        var repo = Normalize(repoRoot);
        var start = Path.GetFullPath(Path.Combine(sample, ProductionProject));
        if (!File.Exists(start))
        {
            throw new ParityConfigurationException($"Can't find {ProductionProject} under {sampleRoot}, so the production code under test can't be identified.");
        }

        var projects = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([start]);
        while (pending.TryPop(out var project))
        {
            if (!File.Exists(project) || !projects.Add(project))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(project)!;
            foreach (var include in XDocument.Load(project).Descendants()
                         .Where(e => e.Name.LocalName == "ProjectReference")
                         .Select(e => (string?)e.Attribute("Include"))
                         .OfType<string>())
            {
                pending.Push(Path.GetFullPath(Path.Combine(directory, include.Replace('\\', '/'))));
            }
        }

        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var directory in projects.Select(p => Path.GetDirectoryName(p)!).Where(d => !IsUnder(d, sample)))
        {
            paths.Add(Relative(repo, directory));
            for (var dir = new DirectoryInfo(directory); dir is not null && IsUnder(dir.FullName, repo); dir = dir.Parent)
            {
                foreach (var file in BuildFiles.Select(f => Path.Combine(dir.FullName, f)).Where(File.Exists))
                {
                    paths.Add(Relative(repo, file));
                }
            }
        }

        return paths.ToList();
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

    private static bool IsUnder(string path, string root) =>
        string.Equals(path, root, StringComparison.Ordinal) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
