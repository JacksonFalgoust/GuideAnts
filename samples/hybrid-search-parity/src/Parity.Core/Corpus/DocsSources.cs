using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Parity.Core.Corpus;

public sealed record CorpusFile(string Path, string Content);

public interface IDocsSource
{
    string ResolvedCommit { get; }

    IReadOnlyList<CorpusFile> ReadDocs();
}

/// <summary>
/// Reads docs/*.md (top level only) from git at a fixed commit, so untracked files and local edits
/// can never enter the corpus.
/// </summary>
public sealed class GitDocsSource : IDocsSource
{
    private static readonly Regex TopLevelMarkdown = new(@"^docs/[^/]+\.md$", RegexOptions.Compiled);
    private readonly string _repoRoot;

    public GitDocsSource(string repoRoot, string commit)
    {
        _repoRoot = repoRoot;
        ResolvedCommit = Git("rev-parse", "--verify", $"{commit}^{{commit}}").Trim();
    }

    public string ResolvedCommit { get; }

    public IReadOnlyList<CorpusFile> ReadDocs() =>
        Git("ls-tree", "-z", "--name-only", ResolvedCommit, "docs/")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => TopLevelMarkdown.IsMatch(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new CorpusFile(path, Git("show", $"{ResolvedCommit}:{path}")))
            .ToList();

    private string Git(params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = _repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8, // decodes like File.ReadAllText, which production uses
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start) ?? throw new ParityConfigurationException("Could not start git.");
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new ParityConfigurationException($"git {string.Join(' ', args)} failed: {stderr.Result.Trim()}");
        }

        return stdout;
    }
}
