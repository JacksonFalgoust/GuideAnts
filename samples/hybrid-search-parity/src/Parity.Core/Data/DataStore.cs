using System.Text;
using System.Text.Json;
using Parity.Core.Model;

namespace Parity.Core.Data;

public static class DataStore
{
    public const string ChunksFile = "data/chunks.jsonl";
    public const string EmbeddingsFile = "data/embeddings.f32";
    public const string QueryEmbeddingsFile = "data/query-embeddings.f32";
    public const string GroundTruthFile = "data/ground-truth.json";
    public const string ManifestFile = "data/manifest.json";
    public const string EdgeCasesFile = "inputs/edge-cases.json";
    public const string QueriesFile = "inputs/queries.json";

    private static readonly string[] DataFiles = [ChunksFile, EmbeddingsFile, QueryEmbeddingsFile, GroundTruthFile];
    private static readonly string[] InputFiles = [EdgeCasesFile, QueriesFile];
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static IReadOnlyList<EdgeCaseDefinition> ReadEdgeCases(string sampleRoot) =>
        ReadJson<List<EdgeCaseDefinition>>(Path.Combine(sampleRoot, EdgeCasesFile));

    public static IReadOnlyList<QueryDefinition> ReadQueries(string sampleRoot) =>
        ReadJson<List<QueryDefinition>>(Path.Combine(sampleRoot, QueriesFile));

    /// <summary>
    /// Writes every file into a temporary folder, then moves them into data/ with the manifest last.
    /// Nothing in data/ changes unless every file was written.
    /// </summary>
    public static void WriteAtomically(string sampleRoot, GeneratedData data, CancellationToken ct)
    {
        var dataDir = Path.Combine(sampleRoot, "data");
        Directory.CreateDirectory(dataDir);
        var temp = Path.Combine(dataDir, $".tmp-{Guid.NewGuid():N}");
        var tempRoot = Path.Combine(temp, "root");
        try
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(tempRoot, "data"));
            WriteFiles(tempRoot, sampleRoot, data);
            ct.ThrowIfCancellationRequested();
            foreach (var file in DataFiles.Append(ManifestFile))
            {
                File.Move(Path.Combine(tempRoot, file), Path.Combine(sampleRoot, file), overwrite: true);
            }
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    public static DataSet Load(string sampleRoot)
    {
        var manifestPath = Path.Combine(sampleRoot, ManifestFile);
        if (!File.Exists(manifestPath))
        {
            throw new DataFileException($"{ManifestFile} not found. Run `generate-data`, or restore data/ from git.");
        }

        var manifest = ReadJson<Manifest>(manifestPath);
        foreach (var (file, expected) in manifest.Files)
        {
            var path = Path.Combine(sampleRoot, file);
            if (!File.Exists(path))
            {
                throw new DataFileException($"{file} is missing. Run `generate-data`, or restore data/ from git.");
            }

            var actual = Hashing.Sha256File(path);
            if (actual != expected)
            {
                throw new DataFileException(
                    $"{file} doesn't match data/manifest.json (sha256 {actual[..12]}…, expected {expected[..12]}…). " +
                    "Regenerate with `generate-data`, or restore data/ and inputs/ from git.");
            }
        }

        var lines = File.ReadAllLines(Path.Combine(sampleRoot, ChunksFile))
            .Where(line => line.Length > 0)
            .Select(line => JsonSerializer.Deserialize<ChunkLine>(line, ParityJson.Lines)
                ?? throw new DataFileException($"{ChunksFile} has an empty line."))
            .ToList();
        if (lines.Count != manifest.ChunkCount)
        {
            throw new DataFileException($"{ChunksFile} has {lines.Count} chunks; the manifest says {manifest.ChunkCount}.");
        }

        var vectors = VectorFile.Read(Path.Combine(sampleRoot, EmbeddingsFile), lines.Count);
        var chunks = lines
            .Select((l, i) => new ChunkRecord(l.Id, l.IndexName, l.ProjectId, l.NotebookId, l.DocumentId, l.ContentFileId, l.SourcePath, l.ChunkIndex, l.Kind, l.Content, vectors[i]))
            .ToList();

        var definitions = ReadQueries(sampleRoot);
        if (definitions.Count != manifest.QueryCount)
        {
            throw new DataFileException($"{QueriesFile} has {definitions.Count} queries; the manifest says {manifest.QueryCount}.");
        }

        var queryVectors = VectorFile.Read(Path.Combine(sampleRoot, QueryEmbeddingsFile), definitions.Count);
        var queries = definitions.Select((q, i) => new SavedQuery(q.Id, q.Group, q.Text, queryVectors[i])).ToList();
        var groundTruth = ReadJson<Dictionary<string, List<Guid>>>(Path.Combine(sampleRoot, GroundTruthFile))
            .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Guid>)kv.Value);

        return new DataSet(manifest, chunks, queries, groundTruth);
    }

    private static void WriteFiles(string targetRoot, string sampleRoot, GeneratedData data)
    {
        using (var writer = new StreamWriter(Path.Combine(targetRoot, ChunksFile), append: false, Utf8NoBom) { NewLine = "\n" })
        {
            foreach (var c in data.Chunks)
            {
                var line = new ChunkLine(c.Id, c.IndexName, c.ProjectId, c.NotebookId, c.DocumentId, c.ContentFileId, c.SourcePath, c.ChunkIndex, c.Kind, c.Content);
                writer.WriteLine(JsonSerializer.Serialize(line, ParityJson.Lines));
            }
        }

        VectorFile.Write(Path.Combine(targetRoot, EmbeddingsFile), data.Chunks.Select(c => c.Embedding).ToList());
        VectorFile.Write(Path.Combine(targetRoot, QueryEmbeddingsFile), data.QueryVectors);
        File.WriteAllText(Path.Combine(targetRoot, GroundTruthFile), JsonSerializer.Serialize(data.GroundTruth, ParityJson.Options), Utf8NoBom);

        var files = DataFiles.ToDictionary(f => f, f => Hashing.Sha256File(Path.Combine(targetRoot, f)));
        foreach (var input in InputFiles)
        {
            files[input] = Hashing.Sha256File(Path.Combine(sampleRoot, input));
        }

        var manifest = new Manifest(data.CorpusCommit, data.GeneratedUtc, data.Model, VectorFile.Dimensions, data.Chunks.Count, data.Queries.Count, files, data.ProductionSources);
        File.WriteAllText(Path.Combine(targetRoot, ManifestFile), JsonSerializer.Serialize(manifest, ParityJson.Options), Utf8NoBom);
    }

    private static T ReadJson<T>(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), ParityJson.Options)
                ?? throw new DataFileException($"{path} is empty.");
        }
        catch (JsonException ex)
        {
            throw new DataFileException($"{path} isn't valid JSON: {ex.Message}", ex);
        }
        catch (FileNotFoundException ex)
        {
            throw new DataFileException($"{path} not found.", ex);
        }
    }
}
