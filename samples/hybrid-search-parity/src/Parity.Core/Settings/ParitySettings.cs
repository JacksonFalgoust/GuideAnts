namespace Parity.Core.Settings;

/// <summary>Bound from appsettings.json. Defaults equal the spec's values.</summary>
public sealed class ParitySettings
{
    public SearchSection Search { get; set; } = new();
    public Dictionary<string, ProfileSettings> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HybridSettings Hybrid { get; set; } = new();
    public ParitySection Parity { get; set; } = new();
    public ImageSettings Images { get; set; } = new();
    public OpenAiSettings OpenAI { get; set; } = new();
}

public sealed class SearchSection
{
    public string Profile { get; set; } = "sqlserver";
}

public sealed class ProfileSettings
{
    public string Provider { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string? TextSearchConfig { get; set; }

    /// <summary>
    /// <c>Copy</c> (the default): the sample's own stores, with production's SQL Server query copied verbatim and the
    /// Postgres port. <c>Production</c>: GuideAnts' real searchers on a schema built by the real migrations.
    /// </summary>
    public string Searcher { get; set; } = SearcherKinds.Copy;
}

public static class SearcherKinds
{
    public const string Copy = "Copy";
    public const string Production = "Production";
}

public sealed class HybridSettings
{
    public int MaxResults { get; set; } = 5;
    public double VectorWeight { get; set; } = 0.7;
    public double TextWeight { get; set; } = 0.3;
    public int CandidateMultiplier { get; set; } = 2;
}

public sealed class ParitySection
{
    public string Baseline { get; set; } = "sqlserver";
    public string Candidate { get; set; } = "postgres";
    public List<string> Informational { get; set; } = new();
    public int VectorTopK { get; set; } = 10;
    public double VectorTolerance { get; set; } = 1e-5;
    public double TieTolerance { get; set; } = 1e-6;
    public int FullTextPopulationTimeoutSeconds { get; set; } = 600;
    public ThresholdSettings Thresholds { get; set; } = new();
}

public sealed class ThresholdSettings
{
    public double MinMeanTop5Overlap { get; set; } = 0.9;
    public double MinSameTopOneRate { get; set; } = 0.8;
}

public sealed class ImageSettings
{
    public string SqlServer { get; set; } = "ghcr.io/elumenotion/mssql2025-express-fts:main";
    public string Postgres { get; set; } = "pgvector/pgvector:0.8.7-pg18";
}

public sealed class OpenAiSettings
{
    public string? ApiKey { get; set; }
    public string Endpoint { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "text-embedding-3-small";
}
