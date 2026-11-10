using Parity.Cli;
using Parity.Core;

namespace Parity.Tests.Unit;

/// <summary>
/// A production profile runs src/server code, so a comparison involving one must record and dirty-check that code
/// too, not only the sample directory.
/// </summary>
public sealed class CodeUnderTestTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"parity-code-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void The_production_searchers_code_is_every_project_parity_production_references_outside_the_sample()
    {
        var paths = CodeUnderTest.Paths(RepoPaths.FindSampleRoot(), RepoPaths.FindRepoRoot());

        Assert.Contains("src/server/GuideAntsApi.BackgroundJobs", paths);
        Assert.Contains("src/server/GuideAntsApi.DataModel", paths);
        Assert.Contains("src/server/GuideAntsApi.DataModel.Postgres", paths);
        Assert.Contains("src/server/GuideAnts.Logging", paths); // referenced by BackgroundJobs, not by Parity.Production
        Assert.Contains("src/server/Directory.Build.props", paths);
        Assert.DoesNotContain(paths, p => p.StartsWith("samples/", StringComparison.Ordinal));
        Assert.Equal(paths.Order(StringComparer.Ordinal), paths);
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Project(params string[] references) =>
        $"<Project><ItemGroup>{string.Concat(references.Select(r => $"<ProjectReference Include=\"{r}\" />"))}</ItemGroup></Project>";

    [Fact]
    public void Follows_references_transitively_with_either_slash_and_stops_at_cycles()
    {
        Write("samples/s/src/Parity.Production/Parity.Production.csproj", Project("../Parity.Core/Parity.Core.csproj", "../../../../src/server/A/A.csproj"));
        Write("samples/s/src/Parity.Core/Parity.Core.csproj", Project());
        Write("src/server/A/A.csproj", Project(@"..\B\B.csproj"));
        Write("src/server/B/B.csproj", Project("../A/A.csproj"));
        Write("src/server/Directory.Build.props", "<Project />");
        Write("Directory.Packages.props", "<Project />");

        Assert.Equal(
            ["Directory.Packages.props", "src/server/A", "src/server/B", "src/server/Directory.Build.props"],
            CodeUnderTest.Paths(Path.Combine(_root, "samples", "s"), _root));
    }

    [Fact]
    public void A_missing_production_project_is_a_configuration_error()
    {
        Directory.CreateDirectory(Path.Combine(_root, "samples", "s"));
        Assert.Throws<ParityConfigurationException>(() => CodeUnderTest.Paths(Path.Combine(_root, "samples", "s"), _root));
    }

    [Fact]
    public async Task The_probe_reports_the_last_commit_and_uncommitted_changes_of_the_code_under_test()
    {
        Write("samples/s/src/Parity.Production/Parity.Production.csproj", Project("../../../../src/server/A/A.csproj"));
        Write("samples/s/notes.md", "x");
        Write("src/server/A/A.csproj", Project());
        Write("src/server/A/Code.cs", "class C {}");
        Write("src/client/app.ts", "x");
        Write("src/server/GuideAntsApi.sln", ""); // the repository root marker
        Git("init", "-q");
        Git("add", ".");
        Git("commit", "-q", "-m", "init");
        var sample = Path.Combine(_root, "samples", "s");
        var probe = new ProcessEnvironmentProbe();

        var clean = await probe.CodeUnderTestAsync(sample, CancellationToken.None);
        Assert.Equal(["src/server/A"], clean.Paths);
        Assert.Matches("^[0-9a-f]{8}$", clean.LastCommit);
        Assert.False(clean.Dirty);

        File.WriteAllText(Path.Combine(_root, "src", "client", "app.ts"), "changed");
        File.WriteAllText(Path.Combine(sample, "notes.md"), "changed"); // the sample has its own check
        Assert.False((await probe.CodeUnderTestAsync(sample, CancellationToken.None)).Dirty);

        File.WriteAllText(Path.Combine(_root, "src", "server", "A", "Code.cs"), "class D {}");
        Assert.True((await probe.CodeUnderTestAsync(sample, CancellationToken.None)).Dirty);
        File.WriteAllText(Path.Combine(_root, "src", "server", "A", "Code.cs"), "class C {}");
        File.WriteAllText(Path.Combine(_root, "src", "server", "A", "New.cs"), "class N {}");
        Assert.True((await probe.CodeUnderTestAsync(sample, CancellationToken.None)).Dirty); // untracked counts too
    }

    private void Git(params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = _root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false" }.Concat(args))
        {
            start.ArgumentList.Add(a);
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
