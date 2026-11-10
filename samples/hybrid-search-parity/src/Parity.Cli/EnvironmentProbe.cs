using System.ComponentModel;
using System.Diagnostics;
using Parity.Core;
using Parity.Core.Contracts;

namespace Parity.Cli;

/// <summary>
/// SampleDirty: any uncommitted change under the sample directory except RESULTS.md and RESULTS-production.md (the
/// reports compare itself rewrites).
/// </summary>
public sealed record GitState(string Commit, bool SampleDirty);

public interface IEnvironmentProbe
{
    Task<string> ImageDigestAsync(string image, CancellationToken ct);

    Task<GitState> GitStateAsync(string sampleRoot, CancellationToken ct);

    /// <summary>The production code a Searcher=Production profile runs (<see cref="CodeUnderTest"/>) and its git state.</summary>
    Task<CodeUnderTestState> CodeUnderTestAsync(string sampleRoot, CancellationToken ct);
}

/// <summary>Asks docker and git for the facts RESULTS.md records. A missing tool gives "unknown", never a failure.</summary>
public sealed class ProcessEnvironmentProbe(string dockerExecutable = "docker", string gitExecutable = "git") : IEnvironmentProbe
{
    public async Task<string> ImageDigestAsync(string image, CancellationToken ct)
    {
        var (ok, output) = await RunAsync(dockerExecutable, ["image", "inspect", "--format", "{{json .RepoDigests}}", image], null, ct);
        return ok && !string.IsNullOrWhiteSpace(output) ? output.Trim() : "unknown (docker image inspect failed)";
    }

    internal static readonly string[] GitStatusArguments = ["status", "--porcelain", "--", ".", ":!RESULTS.md", ":!RESULTS-production.md"];

    public async Task<GitState> GitStateAsync(string sampleRoot, CancellationToken ct)
    {
        var (commitOk, commit) = await RunAsync(gitExecutable, ["rev-parse", "--short=8", "HEAD"], sampleRoot, ct);
        var (statusOk, status) = await RunAsync(gitExecutable, GitStatusArguments, sampleRoot, ct);
        return new GitState(commitOk ? commit.Trim() : "unknown", statusOk && !string.IsNullOrWhiteSpace(status));
    }

    /// <summary>
    /// Untracked files count as changes, as they do for the sample. A missing production project is a configuration
    /// error; a missing git gives "unknown".
    /// </summary>
    public async Task<CodeUnderTestState> CodeUnderTestAsync(string sampleRoot, CancellationToken ct)
    {
        var repoRoot = RepoPaths.FindRepoRoot(sampleRoot);
        var paths = CodeUnderTest.Paths(sampleRoot, repoRoot);
        var (logOk, log) = await RunAsync(gitExecutable, ["log", "-1", "--format=%h", "--abbrev=8", "--", .. paths], repoRoot, ct);
        var (statusOk, status) = await RunAsync(gitExecutable, ["status", "--porcelain", "--", .. paths], repoRoot, ct);
        var commit = logOk && !string.IsNullOrWhiteSpace(log) ? log.Trim() : "unknown";
        return new CodeUnderTestState(paths, commit, statusOk && !string.IsNullOrWhiteSpace(status));
    }

    private static async Task<(bool Ok, string Output)> RunAsync(string executable, string[] args, string? workingDirectory, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (workingDirectory is not null)
        {
            start.WorkingDirectory = workingDirectory;
        }

        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return (false, string.Empty);
            }

            var stderr = process.StandardError.ReadToEndAsync(ct);
            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            await stderr;
            return (process.ExitCode == 0, stdout);
        }
        catch (Win32Exception)
        {
            return (false, string.Empty); // executable not found
        }
    }
}
