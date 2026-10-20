using Parity.Cli;
using Parity.Cli.Commands;
using Parity.Core;
using Parity.Core.Corpus;
using Parity.Core.Data;
using Parity.Core.Embeddings;
using Parity.Tests.Support;

namespace Parity.Tests.Unit;

public sealed class GenerateDataCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"parity-gen-{Guid.NewGuid():N}");
    private readonly FakeDocsSource _docs = new(
        new CorpusFile("docs/a.md", "The conversation lock is held by the worker."),
        new CorpusFile("docs/b.md", new string('x', 3000)),
        new CorpusFile("docs/c.md", "Docker Desktop on Apple Silicon."));

    public GenerateDataCommandTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "inputs"));
        File.WriteAllText(Path.Combine(_root, DataStore.EdgeCasesFile), """[ { "id": "E01", "content": "Don't restart the container." }, { "id": "E02", "content": "C# and F#" } ]""");
        File.WriteAllText(Path.Combine(_root, DataStore.QueriesFile), """
            [ { "id": "Q01", "group": "exact", "text": "conversation lock" },
              { "id": "Q02", "group": "paraphrase", "text": "who holds the chat" },
              { "id": "Q03", "group": "edge", "text": "C#" } ]
            """);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private GenerateDataCommand Command(IEmbeddingClient? embedder, Action<string>? onCommit = null) =>
        new(TextWriter.Null, _root, commit => { onCommit?.Invoke(commit); return _docs; }, embedder);

    [Fact]
    public async Task WritesDataThatLoadsBack()
    {
        var exit = await Command(new FakeEmbeddingClient()).RunAsync(CliArgs.Parse(["generate-data"]), CancellationToken.None);
        var data = DataStore.Load(_root);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(1 + 2 + 1 + 2, data.Chunks.Count); // a, b (2 chunks), c, two edge cases
        Assert.Equal("fake-hash", data.Manifest.Model);
        Assert.StartsWith("fakecommit", data.Manifest.CorpusCommit);
        Assert.Equal(new[] { "Q01", "Q03" }, data.GroundTruth.Keys.Order());
        Assert.Single(data.GroundTruth["Q01"]);
        Assert.Equal(3, data.Manifest.ProductionSources.Count);
    }

    [Fact]
    public async Task PassesTheRequestedCommitToTheDocsSource()
    {
        string? requested = null;
        await Command(new FakeEmbeddingClient(), c => requested = c).RunAsync(CliArgs.Parse(["generate-data", "--corpus-commit", "abc123"]), CancellationToken.None);
        Assert.Equal("abc123", requested);
    }

    [Fact]
    public async Task RequiresAnApiKey()
    {
        var error = await Assert.ThrowsAsync<ParityConfigurationException>(() => Command(null).RunAsync(CliArgs.Parse(["generate-data"]), CancellationToken.None));
        Assert.Contains("OPENAI_API_KEY", error.Message);
    }

    [Fact]
    public async Task CancellationLeavesExistingDataUntouched()
    {
        await Command(new FakeEmbeddingClient()).RunAsync(CliArgs.Parse(["generate-data"]), CancellationToken.None);
        var before = Hashing.Sha256File(Path.Combine(_root, DataStore.ManifestFile));

        using var cts = new CancellationTokenSource();
        var cancelling = new FakeEmbeddingClient(afterCall: _ => cts.Cancel()); // cancels after embedding the chunks
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Command(cancelling).RunAsync(CliArgs.Parse(["generate-data"]), cts.Token));

        Assert.Equal(before, Hashing.Sha256File(Path.Combine(_root, DataStore.ManifestFile)));
        Assert.Empty(Directory.GetDirectories(Path.Combine(_root, "data")));
    }

}
