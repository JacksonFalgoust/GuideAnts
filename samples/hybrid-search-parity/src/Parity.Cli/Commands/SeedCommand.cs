using Parity.Core;
using Parity.Core.Contracts;
using Parity.Core.Data;
using Parity.Core.Settings;

namespace Parity.Cli.Commands;

public sealed class SeedCommand(TextWriter output, string sampleRoot, Func<ParitySettings, string, SearchEngine> engineFactory)
{
    public async Task<int> RunAsync(CliArgs args, ParitySettings settings, CancellationToken ct)
    {
        IReadOnlyList<string> profiles = args.Has("--all")
            ? settings.Profiles.Keys.Order(StringComparer.Ordinal).ToList()
            : [args.Get("--profile") ?? throw new ParityConfigurationException("seed needs --profile <name> or --all.")];
        var data = DataStore.Load(sampleRoot);

        foreach (var name in profiles)
        {
            await using var engine = engineFactory(settings, name);
            await engine.Store.PingServerAsync(ct);
            output.WriteLine($"Seeding {name} ({engine.Provider}): recreating the database...");
            await engine.Store.EnsureSchemaAsync(ct);
            await engine.Store.LoadAsync(data.Chunks, data.Manifest.GeneratedUtc.UtcDateTime, ct);
            output.WriteLine($"  loaded {data.Chunks.Count} chunks; waiting until searchable...");
            await engine.Store.WaitUntilSearchableAsync(ct);
            var coverage = await engine.Diagnostics.GetIndexCoverageAsync(ct);
            output.WriteLine($"  ready: {coverage.IndexedRows}/{coverage.Rows} rows full-text indexed.");
        }

        return ExitCodes.Success;
    }
}
