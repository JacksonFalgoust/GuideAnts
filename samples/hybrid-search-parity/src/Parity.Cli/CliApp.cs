using Parity.Cli.Commands;
using Parity.Core;
using Parity.Core.Contracts;
using Parity.Core.Corpus;
using Parity.Core.Embeddings;
using Parity.Core.Settings;

namespace Parity.Cli;

public sealed class CliApp(
    TextWriter output,
    TextWriter error,
    string? sampleRoot = null,
    Func<ParitySettings, IEmbeddingClient?>? embedderFactory = null,
    Func<string, IDocsSource>? docsSourceFactory = null,
    IDictionary<string, string?>? configOverrides = null,
    Func<ParitySettings, string, SearchEngine>? engineFactory = null,
    IEnvironmentProbe? probe = null)
{
    public const string HelpText = """
        Hybrid search parity demo (SQL Server vs PostgreSQL)

          generate-data [--corpus-commit <sha>]      Build data/ from docs/*.md at the commit (needs OPENAI_API_KEY)
          seed (--profile <name> | --all)            Recreate the schema and load data/
          search "<text>" [--profile <name>] [--scope P1|P2|P3|P1/N1] [--quirk-fixed]
          compare [--out RESULTS.md] [--query "<text>"]

        Exit codes: 0 success (compare: switchable), 1 compare: not switchable, 2 configuration, data or connection error.
        """;

    private readonly string _sampleRoot = sampleRoot ?? RepoPaths.FindSampleRoot();
    private readonly Func<ParitySettings, string, SearchEngine> _engines = engineFactory ?? SearchStoreFactory.Create;
    private readonly IEnvironmentProbe _probe = probe ?? new ProcessEnvironmentProbe();

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        try
        {
            var cli = CliArgs.Parse(args);
            if (cli.Command == "help")
            {
                output.WriteLine(HelpText);
                return ExitCodes.Success;
            }

            var settings = AppConfig.Load(_sampleRoot, configOverrides);
            return cli.Command switch
            {
                "generate-data" => await new GenerateDataCommand(output, _sampleRoot, DocsSource, Embedder(settings)).RunAsync(cli, ct),
                "seed" => await new SeedCommand(output, _sampleRoot, _engines).RunAsync(cli, settings, ct),
                "search" => await new SearchCommand(output, _sampleRoot, _engines, Embedder(settings)).RunAsync(cli, settings, ct),
                "compare" => await new CompareCommand(output, _sampleRoot, _engines, Embedder(settings), _probe).RunAsync(cli, settings, ct),
                _ => throw new ParityConfigurationException($"Unknown command '{cli.Command}'. Commands: generate-data, seed, search, compare."),
            };
        }
        catch (ParityException ex)
        {
            error.WriteLine($"error: {ex.Message}");
            return ExitCodes.ConfigurationOrData;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Anything else is a bug in the sample: show the whole thing.
            error.WriteLine($"error: unexpected {ex.GetType().Name}: {ex}");
            return ExitCodes.ConfigurationOrData;
        }
    }

    private IDocsSource DocsSource(string commit) =>
        docsSourceFactory?.Invoke(commit) ?? new GitDocsSource(RepoPaths.FindRepoRoot(_sampleRoot), commit);

    private IEmbeddingClient? Embedder(ParitySettings settings) =>
        embedderFactory is not null
            ? embedderFactory(settings)
            : string.IsNullOrWhiteSpace(settings.OpenAI.ApiKey)
                ? null
                : new OpenAiEmbeddingClient(new HttpClient { Timeout = TimeSpan.FromMinutes(2) }, settings.OpenAI.ApiKey, settings.OpenAI.Endpoint, settings.OpenAI.Model);
}
