# Hybrid Search Parity Demo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a standalone .NET 8 demo that loads identical data into SQL Server and PostgreSQL, runs GuideAnts' production hybrid search (copied verbatim) against a Postgres port, applies pre-agreed parity rules, and writes a verdict to `RESULTS.md`.

**Architecture:** A provider-neutral core (`Parity.Core`) defines `ISearchStore` (what a search app uses) and `ISearchDiagnostics` (what the parity comparison uses), plus data files, corpus building, comparison logic and reporting. `Parity.SqlServer` and `Parity.Postgres` implement the contracts; `Parity.Cli` picks an implementation from configuration only. Pure logic is unit-tested; each engine is integration-tested against real databases in Testcontainers.

**Tech Stack:** .NET 8, C# 12, EF Core 9.0.11, `EFCore.SqlServer.VectorSearch` 9.0.0, `Microsoft.Data.SqlClient` 6.1.3, `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4, `Pgvector.EntityFrameworkCore` 0.3.0, `Microsoft.Extensions.Configuration.*` 9.0.11, xUnit 2.9.3, Testcontainers 4.9.0, OpenAI embeddings REST API over `HttpClient`.

**Spec:** [`docs/superpowers/specs/2026-10-20-hybrid-search-parity-design.md`](../specs/2026-10-20-hybrid-search-parity-design.md). Read it before starting any task. When this plan and the spec disagree, stop and ask.

## Global Constraints

- Everything lives under `samples/hybrid-search-parity/`. Don't modify any file outside it, except `docs/superpowers/`. Production code is never changed.
- Run every command from `samples/hybrid-search-parity/` unless a step says otherwise.
- Target `net8.0`, C# 12, nullable enabled, implicit usings (from `Directory.Build.props`). A sample-local `global.json` pins SDK `8.0.400` with `rollForward: latestFeature`.
- Package versions come only from `Directory.Packages.props` (central package management): EF Core 9.0.11, `EFCore.SqlServer.VectorSearch` 9.0.0, `Microsoft.Data.SqlClient` 6.1.3, `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4, `Pgvector.EntityFrameworkCore` 0.3.0, `Microsoft.Extensions.Configuration.*` 9.0.11, `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.5, `Testcontainers.MsSql` / `Testcontainers.PostgreSql` 4.9.0.
- Production code is copied from commit `9f8cd102`. The SHA-256 values (LF line endings) are:
  - `HybridSearcher.cs`: `c8d0dabc3cd8a45c69e645862cc2bef0c8a39c848a37eeeb8b366c7a5750fc16`
  - `HybridIndexer.cs`: `0612e4d5d05fa049bd25efc0ade2a23266c79f93d37a4525e54aa0f42341e362`
  - `MarkdownInlineImageSanitizer.cs`: `e7b77e1854f6e6b72006f0eda7a7ca11e5ee8990b8f4abb6382b44ce2f7145ff`
- Images: `ghcr.io/elumenotion/mssql2025-express-fts:main` (amd64 only, so it runs emulated on Apple Silicon) and `pgvector/pgvector:0.8.7-pg18`. Compose host ports are 14333 (SQL Server) and 54329 (Postgres).
- Fixed parameters:
  - verdict thresholds: mean top-5 overlap ≥ 0.9, same #1 ≥ 0.8;
  - `VectorTolerance` 1e-5, `TieTolerance` 1e-6, `VectorTopK` 10;
  - hybrid: `MaxResults` 5, `VectorWeight` 0.7, `TextWeight` 0.3, `CandidateMultiplier` 2;
  - scopes: `P1`, `P2`, `P3`, `P1/N1`.
- Embeddings: OpenAI `text-embedding-3-small`, 1536 dimensions, stored as raw little-endian float32.
- Postgres text search configuration `parity_english_nostem`, ranked with `ts_rank_cd(…, 32)`.
- Exit codes: `0` success (`compare`: switchable), `1` `compare` ran but not switchable, `2` configuration, data or connection error.
- Tests use xUnit's built-in `Assert`. Do **not** add FluentAssertions or Moq. Docker-backed tests carry `[Trait("Category", "Integration")]`. The fast run is `dotnet test --filter "Category!=Integration"`.
- **Commits:** run a task's commit step only after the user has approved committing for this execution. Never add a `Co-Authored-By` trailer.
- The only network calls allowed are OpenAI (in `generate-data` and live queries), Docker and NuGet restore.

## Review Focus

These five conditions are implied by the spec but not exercised by its explicit requirements. Each has a pinning test in the owning task.

1. **Re-running `seed` on an already-seeded database** must recreate everything cleanly, with no "catalog already exists" or "database in use" errors. Pinned in Task 10 (`SeedingTwiceRecreatesCleanly`) and Task 12 (the same name).
2. **A command run while a database container is down or still starting** must exit `2` with `Can't reach profile '<name>' … at <host>`, not a stack trace. Pinned in Task 16 (`UnreachableDatabaseExitsTwoNamingProfileAndHost`).
3. **Queries with apostrophes, embedded double quotes, only stop words, or only punctuation** must never throw through the production SQL path. They return hits or a typed `EngineError`. Pinned in Task 10 and Task 12 (`AwkwardQueriesNeverThrow`).
4. **Ctrl+C during `generate-data`** must leave the existing `data/` untouched and no temp folder behind. Pinned in Task 7 (`CancellationLeavesExistingDataUntouched`).
5. **`docker` or `git` missing from PATH while `compare` gathers environment info**: the report shows `unknown` and `compare` still completes. Pinned in Task 16 (`MissingExecutablesReportUnknown`).

## File map

```
samples/hybrid-search-parity/
  global.json  Directory.Build.props  Directory.Packages.props  HybridSearchParity.sln  .gitattributes
  appsettings.json                      Task 7
  docker-compose.yml  README.md         Task 19
  known-differences.json  RESULTS.md    Task 19
  inputs/edge-cases.json  inputs/queries.json    Task 5
  data/*                                Task 17 (generated)
  src/Parity.Core/
    Errors.cs  RepoPaths.cs                                   Tasks 2, 3
    Ids/DeterministicGuid.cs                                  Task 1
    Model/*.cs  Contracts/*.cs  Settings/ParitySettings.cs    Task 2
    Production/*.cs                                           Task 3
    Data/*.cs                                                 Task 4
    Corpus/*.cs                                               Task 5
    Embeddings/*.cs                                           Task 6
    Comparison/*.cs                                           Tasks 8, 9, 14
    Reporting/*.cs                                            Task 15
  src/Parity.SqlServer/                                       Tasks 3, 10, 11
  src/Parity.Postgres/                                        Tasks 12, 13
  src/Parity.Cli/                                             Tasks 7, 16
  tests/Parity.Tests/
    Unit/*.cs  Support/*.cs  Integration/*.cs
```

---

### Task 1: Scaffold the solution and deterministic IDs

**Files:**
- Create: `samples/hybrid-search-parity/global.json`, `Directory.Build.props`, `Directory.Packages.props`, `HybridSearchParity.sln`
- Create: `src/Parity.Core/Parity.Core.csproj`, `src/Parity.Core/Ids/DeterministicGuid.cs`
- Create: `tests/Parity.Tests/Parity.Tests.csproj`
- Test: `tests/Parity.Tests/Unit/DeterministicGuidTests.cs`

**Interfaces:**
- Produces: `Parity.Core.Ids.DeterministicGuid.Create(string name) → Guid`, `DeterministicGuid.Create(Guid namespaceId, string name) → Guid`, `DeterministicGuid.ParityNamespace`.

- [ ] **Step 1: Create the build files**

`samples/hybrid-search-parity/global.json`:
```json
{
  "sdk": { "version": "8.0.400", "rollForward": "latestFeature" }
}
```

`samples/hybrid-search-parity/Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>12</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

`samples/hybrid-search-parity/Directory.Packages.props`:
```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="9.0.11" />
    <PackageVersion Include="EFCore.SqlServer.VectorSearch" Version="9.0.0" />
    <PackageVersion Include="Microsoft.Data.SqlClient" Version="6.1.3" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.4" />
    <PackageVersion Include="Pgvector.EntityFrameworkCore" Version="0.3.0" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Json" Version="9.0.11" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Binder" Version="9.0.11" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="9.0.11" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.UserSecrets" Version="9.0.11" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageVersion Include="Testcontainers.MsSql" Version="4.9.0" />
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.9.0" />
  </ItemGroup>
</Project>
```

`src/Parity.Core/Parity.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Parity.Core</RootNamespace>
  </PropertyGroup>
</Project>
```

`tests/Parity.Tests/Parity.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>Parity.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Parity.Core/Parity.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the solution**

Run:
```bash
mkdir -p samples/hybrid-search-parity && cd samples/hybrid-search-parity
dotnet new sln -n HybridSearchParity
dotnet sln HybridSearchParity.sln add src/Parity.Core/Parity.Core.csproj tests/Parity.Tests/Parity.Tests.csproj
```
Expected: `HybridSearchParity.sln` is created (not `.slnx`, because `global.json` selects SDK 8), and both projects are added.

- [ ] **Step 3: Write the failing test**

`tests/Parity.Tests/Unit/DeterministicGuidTests.cs`:
```csharp
using Parity.Core.Ids;

namespace Parity.Tests.Unit;

public class DeterministicGuidTests
{
    [Fact]
    public void MatchesTheRfc4122Version5ReferenceValue()
    {
        var dnsNamespace = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        Assert.Equal(new Guid("2ed6657d-e927-568b-95e1-2665a8aea6a2"), DeterministicGuid.Create(dnsNamespace, "www.example.com"));
    }

    [Fact]
    public void SameNameAlwaysGivesTheSameGuid()
    {
        Assert.Equal(DeterministicGuid.Create("doc:docs/auth-flow.md#3"), DeterministicGuid.Create("doc:docs/auth-flow.md#3"));
    }

    [Fact]
    public void DifferentNamesGiveDifferentGuids()
    {
        Assert.NotEqual(DeterministicGuid.Create("doc:a.md#0"), DeterministicGuid.Create("doc:a.md#1"));
    }

    [Fact]
    public void SetsVersionFiveAndTheRfcVariant()
    {
        var text = DeterministicGuid.Create("anything").ToString();
        Assert.Equal('5', text[14]);
        Assert.Contains(text[19], "89ab");
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~DeterministicGuidTests"`
Expected: build FAILS with `The type or namespace name 'Ids' does not exist`.

- [ ] **Step 5: Write the implementation**

`src/Parity.Core/Ids/DeterministicGuid.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;

namespace Parity.Core.Ids;

/// <summary>
/// Name-based (RFC 4122 version 5) GUIDs, so both databases hold identical, reproducible IDs.
/// </summary>
public static class DeterministicGuid
{
    /// <summary>
    /// Fixed namespace for every ID in this sample. Never change it: chunk IDs in data/ and
    /// known-differences.json depend on it.
    /// </summary>
    public static readonly Guid ParityNamespace = new("3d6f9a52-8b1e-4c47-9f0d-5a2e7c1b9e44");

    public static Guid Create(string name) => Create(ParityNamespace, name);

    public static Guid Create(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapToNetworkOrder(namespaceBytes);
        var nameBytes = Encoding.UTF8.GetBytes(name);

        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(input, 0);
        nameBytes.CopyTo(input, namespaceBytes.Length);

        var hash = SHA1.HashData(input);
        var guid = new byte[16];
        Array.Copy(hash, guid, 16);
        guid[6] = (byte)((guid[6] & 0x0F) | 0x50); // version 5
        guid[8] = (byte)((guid[8] & 0x3F) | 0x80); // RFC 4122 variant
        SwapToNetworkOrder(guid);
        return new Guid(guid);
    }

    // Guid.ToByteArray stores the first three fields little-endian; RFC 4122 hashes them big-endian.
    private static void SwapToNetworkOrder(byte[] b)
    {
        (b[0], b[3]) = (b[3], b[0]);
        (b[1], b[2]) = (b[2], b[1]);
        (b[4], b[5]) = (b[5], b[4]);
        (b[6], b[7]) = (b[7], b[6]);
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DeterministicGuidTests"`
Expected: PASS (4 tests).

- [ ] **Step 7: Commit**

```bash
git add global.json Directory.Build.props Directory.Packages.props HybridSearchParity.sln src/Parity.Core tests/Parity.Tests
git commit -m "feat(parity): scaffold hybrid search parity sample with deterministic IDs"
```

---

### Task 2: Core model, contracts, errors and settings

**Files:**
- Create: `src/Parity.Core/Errors.cs`
- Create: `src/Parity.Core/Model/ChunkRecord.cs`, `Model/Scope.cs`, `Model/HybridQuery.cs`, `Model/Results.cs`, `Model/EngineResult.cs`, `Model/DocumentChunkEntity.cs`
- Create: `src/Parity.Core/Contracts/ISearchStore.cs`, `Contracts/ISearchDiagnostics.cs`, `Contracts/SearchEngine.cs`
- Create: `src/Parity.Core/Settings/ParitySettings.cs`
- Test: `tests/Parity.Tests/Unit/ModelTests.cs`

**Interfaces:**
- Produces (everything below is used by later tasks with exactly these names):
  - `ChunkKind { Doc, Edge }`
  - `ChunkRecord(Guid Id, string IndexName, Guid ProjectId, Guid? NotebookId, string DocumentId, Guid ContentFileId, string SourcePath, int ChunkIndex, ChunkKind Kind, string Content, float[] Embedding)`
  - `Scope(string IndexName, Guid? NotebookId)`, `NamedScope(string Label, Scope Scope)`
  - `HybridQuery(string Text, float[] Vector, Scope Scope, int MaxResults, double VectorWeight, double TextWeight, int CandidateMultiplier, bool FilterBeforeTopN = false)` with `int CandidateLimit`
  - `SearchHit(Guid ChunkId, string Content, Guid? ContentFileId, Guid? NotebookFileId, int ChunkIndex, string IndexName, double VectorScore, double TextScore, double Score)`
  - `VectorHit(Guid ChunkId, double Score)`, `TextMatch(Guid ChunkId, double Rank)`
  - `ChunkScore(Guid ChunkId, double VectorScore, double TextScore, bool Matched, double? RawRank, bool InCandidates, double? NthCandidateRank)`
  - `Token(string Text, string? Normalized, string Kind, bool IsStopWord)`
  - `EngineInfo(string Provider, string Version, IReadOnlyDictionary<string, string> Details)`, `IndexCoverage(int Rows, int IndexedRows)`
  - `EngineError(string Code, string Message)`, and `EngineResult<T>` with `Ok`, `Fail`, `IsSuccess`, `Value`, `Error`, `Unwrap(string operation)`
  - `DocumentChunkEntity` with `From(ChunkRecord, DateTime)`
  - `ISearchStore`, `ISearchDiagnostics`, and `SearchEngine(string profile, string provider, ISearchStore store, ISearchDiagnostics diagnostics, IAsyncDisposable? resources = null)`
  - `ParityException`, `ParityConfigurationException`, `DataFileException`, `EngineUnavailableException`, `NotSeededException`
  - `ParitySettings` with `Search`, `Profiles`, `Hybrid`, `Parity`, `Images` and `OpenAI` sections

- [ ] **Step 1: Write the failing tests**

`tests/Parity.Tests/Unit/ModelTests.cs`:
```csharp
using Parity.Core.Model;
using Parity.Core.Settings;

namespace Parity.Tests.Unit;

public class ModelTests
{
    [Fact]
    public void CandidateLimitIsMaxResultsTimesMultiplier()
    {
        var query = new HybridQuery("q", new float[1536], new Scope("p", null), MaxResults: 5, VectorWeight: 0.7, TextWeight: 0.3, CandidateMultiplier: 2);
        Assert.Equal(10, query.CandidateLimit);
    }

    [Fact]
    public void EngineResultCarriesValueOrError()
    {
        var ok = EngineResult<int>.Ok(3);
        var failed = EngineResult<int>.Fail(new EngineError("SQL7619", "noise words only"));

        Assert.True(ok.IsSuccess);
        Assert.Equal(3, ok.Value);
        Assert.False(failed.IsSuccess);
        Assert.Equal("SQL7619", failed.Error!.Code);
        var thrown = Assert.Throws<InvalidOperationException>(() => failed.Unwrap("vector search"));
        Assert.Contains("vector search failed: [SQL7619] noise words only", thrown.Message);
    }

    [Fact]
    public void DocumentChunkEntityCopiesEveryColumnAndForcesUtc()
    {
        var chunk = new ChunkRecord(Guid.NewGuid(), "idx", Guid.NewGuid(), Guid.NewGuid(), "docs/a.md", Guid.NewGuid(), "docs/a.md", 3, ChunkKind.Doc, "text", [1f, 2f]);
        var entity = DocumentChunkEntity.From(chunk, new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Unspecified));

        Assert.Equal(chunk.Id, entity.Id);
        Assert.Equal("idx", entity.IndexName);
        Assert.Equal(chunk.ContentFileId, entity.ContentFileId);
        Assert.Null(entity.NotebookFileId);
        Assert.Null(entity.AssistantFileId);
        Assert.Equal(chunk.ProjectId, entity.ProjectId);
        Assert.Equal(chunk.NotebookId, entity.NotebookId);
        Assert.Equal("docs/a.md", entity.DocumentId);
        Assert.Equal(3, entity.ChunkIndex);
        Assert.Equal("text", entity.Content);
        Assert.Same(chunk.Embedding, entity.Embedding);
        Assert.Equal(DateTimeKind.Utc, entity.CreatedUtc.Kind);
    }

    [Fact]
    public void SettingDefaultsMatchTheSpec()
    {
        var settings = new ParitySettings();
        Assert.Equal(5, settings.Hybrid.MaxResults);
        Assert.Equal(0.7, settings.Hybrid.VectorWeight);
        Assert.Equal(0.3, settings.Hybrid.TextWeight);
        Assert.Equal(2, settings.Hybrid.CandidateMultiplier);
        Assert.Equal(10, settings.Parity.VectorTopK);
        Assert.Equal(1e-5, settings.Parity.VectorTolerance);
        Assert.Equal(1e-6, settings.Parity.TieTolerance);
        Assert.Equal(0.9, settings.Parity.Thresholds.MinMeanTop5Overlap);
        Assert.Equal(0.8, settings.Parity.Thresholds.MinSameTopOneRate);
        Assert.Equal("ghcr.io/elumenotion/mssql2025-express-fts:main", settings.Images.SqlServer);
        Assert.Equal("pgvector/pgvector:0.8.7-pg18", settings.Images.Postgres);
        Assert.Equal("text-embedding-3-small", settings.OpenAI.Model);
    }

    [Fact]
    public void ProfileLookupIgnoresCase()
    {
        var settings = new ParitySettings();
        settings.Profiles["Postgres"] = new ProfileSettings { Provider = "Postgres" };
        Assert.True(settings.Profiles.ContainsKey("postgres"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ModelTests"`
Expected: build FAILS (`Parity.Core.Model` not found).

- [ ] **Step 3: Write the implementation**

`src/Parity.Core/Errors.cs`:
```csharp
namespace Parity.Core;

/// <summary>An error the CLI reports as a one-line message with exit code 2, rather than a stack trace.</summary>
public abstract class ParityException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class ParityConfigurationException(string message, Exception? inner = null) : ParityException(message, inner);

public sealed class DataFileException(string message, Exception? inner = null) : ParityException(message, inner);

public sealed class EngineUnavailableException(string message, Exception? inner = null) : ParityException(message, inner);

public sealed class NotSeededException(string message, Exception? inner = null) : ParityException(message, inner);
```

`src/Parity.Core/Model/ChunkRecord.cs`:
```csharp
namespace Parity.Core.Model;

public enum ChunkKind { Doc, Edge }

/// <summary>One chunk exactly as both databases store it. IDs are deterministic (see DeterministicGuid).</summary>
public sealed record ChunkRecord(
    Guid Id,
    string IndexName,
    Guid ProjectId,
    Guid? NotebookId,
    string DocumentId,
    Guid ContentFileId,
    string SourcePath,
    int ChunkIndex,
    ChunkKind Kind,
    string Content,
    float[] Embedding);
```

`src/Parity.Core/Model/Scope.cs`:
```csharp
namespace Parity.Core.Model;

/// <summary>Production's search filter: IndexName (the project ID as a string) plus an optional notebook.</summary>
public sealed record Scope(string IndexName, Guid? NotebookId);

/// <summary>A scope with the label used in reports and known-differences.json (P1, P2, P3, P1/N1).</summary>
public sealed record NamedScope(string Label, Scope Scope);
```

`src/Parity.Core/Model/HybridQuery.cs`:
```csharp
namespace Parity.Core.Model;

/// <summary>
/// Production's HybridSearcher.SearchAsync inputs. FilterBeforeTopN = true is the informational
/// "quirk fixed" variant and is never production behaviour.
/// </summary>
public sealed record HybridQuery(
    string Text,
    float[] Vector,
    Scope Scope,
    int MaxResults,
    double VectorWeight,
    double TextWeight,
    int CandidateMultiplier,
    bool FilterBeforeTopN = false)
{
    public int CandidateLimit => MaxResults * CandidateMultiplier;
}
```

`src/Parity.Core/Model/Results.cs`:
```csharp
namespace Parity.Core.Model;

/// <summary>Same fields as production's HybridSearchResult.</summary>
public sealed record SearchHit(
    Guid ChunkId,
    string Content,
    Guid? ContentFileId,
    Guid? NotebookFileId,
    int ChunkIndex,
    string IndexName,
    double VectorScore,
    double TextScore,
    double Score);

public sealed record VectorHit(Guid ChunkId, double Score);

/// <summary>A full-text match. Rank is on the same 0–1 scale the hybrid query uses for TextScore.</summary>
public sealed record TextMatch(Guid ChunkId, double Rank);

/// <summary>
/// What the hybrid query computes for one chunk. RawRank is its text rank had it been a candidate
/// (null when it doesn't match). NthCandidateRank is the lowest rank in a full candidate set
/// (null when fewer than CandidateLimit chunks matched, so nothing was cut).
/// </summary>
public sealed record ChunkScore(
    Guid ChunkId,
    double VectorScore,
    double TextScore,
    bool Matched,
    double? RawRank,
    bool InCandidates,
    double? NthCandidateRank);

public sealed record Token(string Text, string? Normalized, string Kind, bool IsStopWord);

public sealed record EngineInfo(string Provider, string Version, IReadOnlyDictionary<string, string> Details);

public sealed record IndexCoverage(int Rows, int IndexedRows);
```

`src/Parity.Core/Model/EngineResult.cs`:
```csharp
using System.Diagnostics.CodeAnalysis;

namespace Parity.Core.Model;

/// <summary>A database-reported query failure (engine error number or SQLSTATE plus message).</summary>
public sealed record EngineError(string Code, string Message);

/// <summary>
/// Either a value or the error a database returned for that query. Production swallows these
/// errors and returns an empty list; the stores surface them so "failed" never looks like "no results".
/// </summary>
public sealed class EngineResult<T>
{
    private EngineResult(T? value, EngineError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public EngineError? Error { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static EngineResult<T> Ok(T value) => new(value, null);

    public static EngineResult<T> Fail(EngineError error) => new(default, error);

    public T Unwrap(string operation) =>
        IsSuccess ? Value : throw new InvalidOperationException($"{operation} failed: [{Error.Code}] {Error.Message}");
}
```

`src/Parity.Core/Model/DocumentChunkEntity.cs`:
```csharp
namespace Parity.Core.Model;

/// <summary>
/// Production's DocumentChunk columns (GuideAntsApi.DataModel/Models/DocumentChunk.cs @ 9f8cd102),
/// without navigation properties. Embedding stays float[], as in production.
/// </summary>
public sealed class DocumentChunkEntity
{
    public Guid Id { get; set; }
    public string IndexName { get; set; } = string.Empty;
    public Guid? ContentFileId { get; set; }
    public Guid? NotebookFileId { get; set; }
    public Guid? AssistantFileId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? NotebookId { get; set; }
    public string? DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public DateTime CreatedUtc { get; set; }

    public static DocumentChunkEntity From(ChunkRecord chunk, DateTime createdUtc) => new()
    {
        Id = chunk.Id,
        IndexName = chunk.IndexName,
        ContentFileId = chunk.ContentFileId,
        NotebookFileId = null,
        AssistantFileId = null,
        ProjectId = chunk.ProjectId,
        NotebookId = chunk.NotebookId,
        DocumentId = chunk.DocumentId,
        ChunkIndex = chunk.ChunkIndex,
        Content = chunk.Content,
        Embedding = chunk.Embedding,
        CreatedUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc),
    };
}
```

`src/Parity.Core/Contracts/ISearchStore.cs`:
```csharp
using Parity.Core.Model;

namespace Parity.Core.Contracts;

/// <summary>The only thing the search path depends on. Implementations are chosen by configuration.</summary>
public interface ISearchStore
{
    /// <summary>Connects to the server (not the demo database). Throws EngineUnavailableException.</summary>
    Task PingServerAsync(CancellationToken ct);

    /// <summary>Drops and recreates the database, table and full-text objects.</summary>
    Task EnsureSchemaAsync(CancellationToken ct);

    Task LoadAsync(IReadOnlyList<ChunkRecord> chunks, DateTime createdUtc, CancellationToken ct);

    /// <summary>Blocks until full-text indexing has finished (a real wait on SQL Server).</summary>
    Task WaitUntilSearchableAsync(CancellationToken ct);

    Task<EngineResult<IReadOnlyList<SearchHit>>> HybridSearchAsync(HybridQuery query, CancellationToken ct);
}
```

`src/Parity.Core/Contracts/ISearchDiagnostics.cs`:
```csharp
using Parity.Core.Model;

namespace Parity.Core.Contracts;

/// <summary>Diagnostic queries used only by compare and the tests. Production has no equivalent.</summary>
public interface ISearchDiagnostics
{
    Task<EngineResult<IReadOnlyList<VectorHit>>> VectorSearchAsync(float[] vector, Scope scope, int k, CancellationToken ct);

    /// <summary>The full phrase-match set within the scope, with no top-N limit.</summary>
    Task<EngineResult<IReadOnlyList<TextMatch>>> TextMatchAsync(string text, Scope scope, CancellationToken ct);

    Task<EngineResult<IReadOnlyList<ChunkScore>>> ScoreChunksAsync(HybridQuery query, IReadOnlyCollection<Guid> chunkIds, CancellationToken ct);

    Task<EngineResult<IReadOnlyList<Token>>> TokenizeAsync(string text, CancellationToken ct);

    Task<EngineInfo> DescribeAsync(CancellationToken ct);

    /// <summary>Throws NotSeededException when the database or table doesn't exist.</summary>
    Task<IndexCoverage> GetIndexCoverageAsync(CancellationToken ct);
}
```

`src/Parity.Core/Contracts/SearchEngine.cs`:
```csharp
namespace Parity.Core.Contracts;

/// <summary>A configured profile: its store, its diagnostics, and whatever must be disposed with them.</summary>
public sealed class SearchEngine(
    string profile,
    string provider,
    ISearchStore store,
    ISearchDiagnostics diagnostics,
    IAsyncDisposable? resources = null) : IAsyncDisposable
{
    public string Profile { get; } = profile;
    public string Provider { get; } = provider;
    public ISearchStore Store { get; } = store;
    public ISearchDiagnostics Diagnostics { get; } = diagnostics;

    public ValueTask DisposeAsync() => resources?.DisposeAsync() ?? ValueTask.CompletedTask;
}
```

`src/Parity.Core/Settings/ParitySettings.cs`:
```csharp
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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ModelTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Parity.Core tests/Parity.Tests/Unit/ModelTests.cs
git commit -m "feat(parity): add provider-neutral model, contracts and settings"
```

---
### Task 3: Copy production's chunker, sanitizer and SQL, with drift checks

**Files:**
- Create: `src/Parity.Core/RepoPaths.cs`
- Create: `src/Parity.Core/Production/ProductionSources.cs`, `Production/ProductionChunker.cs`, `Production/MarkdownInlineImageSanitizer.cs`, `Production/ProductionPreparation.cs`
- Create: `src/Parity.SqlServer/Parity.SqlServer.csproj`, `src/Parity.SqlServer/Sql/ProductionHybrid.sql` (generated by a script), `src/Parity.SqlServer/Sql/SqlServerSql.cs`
- Create: `samples/hybrid-search-parity/.gitattributes`
- Modify: `tests/Parity.Tests/Parity.Tests.csproj` (reference `Parity.SqlServer`), `HybridSearchParity.sln`
- Test: `tests/Parity.Tests/Unit/ProductionCopyTests.cs`

**Interfaces:**
- Consumes: `ParityConfigurationException` (Task 2).
- Produces:
  - `RepoPaths.FindSampleRoot(string? start = null) → string` and `RepoPaths.FindRepoRoot(string? start = null) → string`
  - `ProductionSources.Commit` (`"9f8cd102"`), the path constants `HybridSearcherPath`, `HybridIndexerPath` and `SanitizerPath`, `ProductionSources.Sha256AtCommit`, and `ProductionSources.HashNormalized(string path) → string`
  - `ProductionChunker.ChunkText(string text) → List<string>`
  - `MarkdownInlineImageSanitizer.ReplaceInlineImageDataUrls(string?) → (string SanitizedText, int ReplacementCount)`
  - `ProductionPreparation.PrepareChunks(string fileText) → List<string>`
  - `SqlServerSql.ProductionTemplate` (string), `SqlServerSql.Fill(string template, float[] vector, string query, int candidateLimit) → string`, and `SqlServerSql.BuildProductionHybrid(float[] vector, string query, int candidateLimit) → string`
  - `ProductionEscaping.VectorJson(float[])`, `ProductionEscaping.FullTextPhrase(string)` and `ProductionEscaping.SqlLiteral(string)`

- [ ] **Step 1: Create the SQL Server project and reference it from the tests**

`src/Parity.SqlServer/Parity.SqlServer.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Parity.SqlServer</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Parity.Core/Parity.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
    <PackageReference Include="EFCore.SqlServer.VectorSearch" />
    <PackageReference Include="Microsoft.Data.SqlClient" />
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="Sql/ProductionHybrid.sql" LogicalName="Parity.SqlServer.ProductionHybrid.sql" />
  </ItemGroup>
</Project>
```

Add to the `<ItemGroup>` of project references in `tests/Parity.Tests/Parity.Tests.csproj`:
```xml
    <ProjectReference Include="../../src/Parity.SqlServer/Parity.SqlServer.csproj" />
```

`samples/hybrid-search-parity/.gitattributes`. This stops git from changing the whitespace or line endings of the verbatim SQL copy:
```
src/Parity.SqlServer/Sql/*.sql -text
```

Run: `dotnet sln HybridSearchParity.sln add src/Parity.SqlServer/Parity.SqlServer.csproj`

- [ ] **Step 2: Extract production's SQL text exactly**

Retyping would lose trailing spaces, so extract the text with a script. Run:
```bash
mkdir -p src/Parity.SqlServer/Sql
git show 9f8cd102:src/server/GuideAntsApi.BackgroundJobs/Services/Search/HybridSearcher.cs \
  | python3 -I -c 'import sys; s=sys.stdin.read(); m="var sql = $@\""; a=s.index(m)+len(m); b=s.index("\";", a); sys.stdout.write(s[a:b])' \
  > src/Parity.SqlServer/Sql/ProductionHybrid.sql
grep -c "CONTAINSTABLE(DocumentChunks, Content, '{escapedFtQuery}', {candidateLimit})" src/Parity.SqlServer/Sql/ProductionHybrid.sql
```
Expected: the final `grep -c` prints `1`. The file begins with a newline, followed by `DECLARE @queryVec vector(1536) = CAST('{vectorJson}' AS vector(1536));`, and ends with `ORDER BY Score DESC`. The `{vectorJson}`, `{escapedFtQuery}` and `{candidateLimit}` placeholders are production's interpolation holes.

- [ ] **Step 3: Write the failing tests**

`tests/Parity.Tests/Unit/ProductionCopyTests.cs`:
```csharp
using Parity.Core;
using Parity.Core.Production;
using Parity.SqlServer.Sql;

namespace Parity.Tests.Unit;

public class ProductionCopyTests
{
    [Theory]
    [InlineData(ProductionSources.HybridSearcherPath)]
    [InlineData(ProductionSources.HybridIndexerPath)]
    [InlineData(ProductionSources.SanitizerPath)]
    public void CopiedProductionFileIsUnchangedSinceTheCopy(string path)
    {
        var actual = ProductionSources.HashNormalized(Path.Combine(RepoPaths.FindRepoRoot(), path));
        Assert.True(
            actual == ProductionSources.Sha256AtCommit[path],
            $"production changed since the copy at {ProductionSources.Commit}; re-check and re-sync ({path})");
    }

    [Fact]
    public void EmbeddedSqlIsProductionsQueryVerbatim()
    {
        var source = File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(), ProductionSources.HybridSearcherPath)).Replace("\r\n", "\n");
        const string marker = "var sql = $@\"";
        var from = source.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var to = source.IndexOf("\";", from, StringComparison.Ordinal);
        Assert.Equal(source[from..to], SqlServerSql.ProductionTemplate.Replace("\r\n", "\n"));
    }

    [Fact]
    public void FillReplacesPlaceholdersInOnePassWithoutRescanningUserText()
    {
        var sql = SqlServerSql.BuildProductionHybrid([0.5f], "it's {candidateLimit} \"x\"", 10);
        Assert.Contains("CAST('[0.5]' AS vector(1536))", sql);
        Assert.Contains("CONTAINSTABLE(DocumentChunks, Content, '\"it''s {candidateLimit} \"\"x\"\"\"', 10)", sql);
    }

    [Fact]
    public void BlankAndShortTextGiveZeroOrOneChunk()
    {
        Assert.Empty(ProductionChunker.ChunkText("   "));
        Assert.Single(ProductionChunker.ChunkText(new string('a', 1000)));
        Assert.Single(ProductionChunker.ChunkText(new string('a', 2048)));
    }

    [Fact]
    public void ChunksOverlapByHalfAndStopEarlyLikeProduction()
    {
        Assert.Equal(new[] { 2048, 1976 }, ProductionChunker.ChunkText(new string('a', 3000)).Select(c => c.Length).ToArray());
        Assert.Equal(new[] { 2048, 2048, 2048, 1928 }, ProductionChunker.ChunkText(new string('a', 5000)).Select(c => c.Length).ToArray());
    }

    [Fact]
    public void ByteChunkingSplitsMultiByteCharactersLikeProduction()
    {
        var chunks = ProductionChunker.ChunkText(string.Concat(Enumerable.Repeat("€", 1000))); // 3,000 bytes
        Assert.StartsWith("€€", chunks[0]);
        Assert.EndsWith("�", chunks[0]);
    }

    [Fact]
    public void SanitizerReplacesInlineImagePayloads()
    {
        var (text, count) = MarkdownInlineImageSanitizer.ReplaceInlineImageDataUrls("before ![x](data:image/png;base64,iVBORw0KGgo=) after");
        Assert.Equal(1, count);
        Assert.Equal("before ![x]([inline-image omitted]) after", text);
    }

    [Fact]
    public void PreparationSanitizesBeforeChunking()
    {
        // Production's character class includes whitespace, so the ')' is what ends the payload.
        var chunks = ProductionPreparation.PrepareChunks("a (data:image/png;base64,AAAA) b");
        Assert.Equal(new[] { "a ([inline-image omitted]) b" }, chunks);
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ProductionCopyTests"`
Expected: build FAILS (`Parity.Core.Production` not found).

- [ ] **Step 5: Write the implementation**

`src/Parity.Core/RepoPaths.cs`:
```csharp
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
```

`src/Parity.Core/Production/ProductionSources.cs`:
```csharp
using System.Security.Cryptography;

namespace Parity.Core.Production;

/// <summary>The production files this sample copies, and their SHA-256 (LF line endings) at the copied commit.</summary>
public static class ProductionSources
{
    public const string Commit = "9f8cd102";
    public const string HybridSearcherPath = "src/server/GuideAntsApi.BackgroundJobs/Services/Search/HybridSearcher.cs";
    public const string HybridIndexerPath = "src/server/GuideAntsApi.BackgroundJobs/Services/Indexing/HybridIndexer.cs";
    public const string SanitizerPath = "src/server/GuideAntsApi.BackgroundJobs/Services/Indexing/MarkdownInlineImageSanitizer.cs";

    public static readonly IReadOnlyDictionary<string, string> Sha256AtCommit = new Dictionary<string, string>
    {
        [HybridSearcherPath] = "c8d0dabc3cd8a45c69e645862cc2bef0c8a39c848a37eeeb8b366c7a5750fc16",
        [HybridIndexerPath] = "0612e4d5d05fa049bd25efc0ade2a23266c79f93d37a4525e54aa0f42341e362",
        [SanitizerPath] = "e7b77e1854f6e6b72006f0eda7a7ca11e5ee8990b8f4abb6382b44ce2f7145ff",
    };

    /// <summary>SHA-256 of the file's bytes with CRLF turned into LF, so Windows checkouts hash the same.</summary>
    public static string HashNormalized(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var normalized = new List<byte>(bytes.Length);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\r' && i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n')
            {
                continue;
            }

            normalized.Add(bytes[i]);
        }

        return Convert.ToHexString(SHA256.HashData(normalized.ToArray())).ToLowerInvariant();
    }
}
```

`src/Parity.Core/Production/ProductionChunker.cs`. The method body must match production character for character. Copy it with `git show 9f8cd102:src/server/GuideAntsApi.BackgroundJobs/Services/Indexing/HybridIndexer.cs | sed -n '240,267p'` and compare:
```csharp
namespace Parity.Core.Production;

// Copied verbatim from src/server/GuideAntsApi.BackgroundJobs/Services/Indexing/HybridIndexer.cs @ 9f8cd102
// (MaxChunkBytes and ChunkText). Only the class wrapper and the method's visibility differ.
public static class ProductionChunker
{
    private const int MaxChunkBytes = 2048;

    public static List<string> ChunkText(string text)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return chunks;

        // Byte-based chunking with 50% overlap
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        int start = 0;

        while (start < bytes.Length)
        {
            int chunkSize = Math.Min(MaxChunkBytes, bytes.Length - start);
            var chunkBytes = new byte[chunkSize];
            Array.Copy(bytes, start, chunkBytes, 0, chunkSize);
            
            var chunkText = System.Text.Encoding.UTF8.GetString(chunkBytes);
            chunks.Add(chunkText);

            // Move start position with 50% overlap
            int stepSize = Math.Max(1, chunkSize / 2);
            start += stepSize;
            
            // Break if remaining text is less than half a chunk
            if (start >= bytes.Length - stepSize) break;
        }

        return chunks;
    }
}
```

`src/Parity.Core/Production/MarkdownInlineImageSanitizer.cs` (copied verbatim; only the namespace differs):
```csharp
using System.Text.RegularExpressions;

namespace Parity.Core.Production;

// Copied verbatim from src/server/GuideAntsApi.BackgroundJobs/Services/Indexing/MarkdownInlineImageSanitizer.cs @ 9f8cd102.
/// <summary>
/// Replaces inline <c>data:image/...;base64,...</c> payloads in markdown/HTML-like text with a short placeholder.
/// </summary>
public static class MarkdownInlineImageSanitizer
{
    public const string InlineImagePlaceholder = "[inline-image omitted]";

    public static readonly Regex InlineImageDataUrlRegex = new(
        @"data:image\/[a-z0-9.+-]+;base64,[A-Za-z0-9+\/=%\\\s_-]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static (string SanitizedText, int ReplacementCount) ReplaceInlineImageDataUrls(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (text ?? string.Empty, 0);
        }

        var replacements = 0;
        var sanitized = InlineImageDataUrlRegex.Replace(text, _ =>
        {
            replacements++;
            return InlineImagePlaceholder;
        });
        return (sanitized, replacements);
    }
}
```

`src/Parity.Core/Production/ProductionPreparation.cs`:
```csharp
namespace Parity.Core.Production;

/// <summary>Mirrors HybridIndexer.IndexContentFileAsync: SanitizeForEmbedding, then ChunkText.</summary>
public static class ProductionPreparation
{
    public static List<string> PrepareChunks(string fileText)
    {
        var text = string.IsNullOrEmpty(fileText)
            ? fileText
            : MarkdownInlineImageSanitizer.ReplaceInlineImageDataUrls(fileText).SanitizedText;
        return ProductionChunker.ChunkText(text);
    }
}
```

`src/Parity.SqlServer/Sql/SqlServerSql.cs`:
```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace Parity.SqlServer.Sql;

// Copied from HybridSearcher.SearchAsync @ 9f8cd102: the three values production interpolates into its SQL.
public static class ProductionEscaping
{
    public static string VectorJson(float[] queryVector) => System.Text.Json.JsonSerializer.Serialize(queryVector);

    /// <summary>production: var ftQuery = $"\"{query.Replace("\"", "\"\"")}\"";</summary>
    public static string FullTextPhrase(string query) => $"\"{query.Replace("\"", "\"\"")}\"";

    /// <summary>production: var escapedFtQuery = ftQuery.Replace("'", "''");</summary>
    public static string SqlLiteral(string ftQuery) => ftQuery.Replace("'", "''");
}

public static class SqlServerSql
{
    private static readonly Regex Placeholder = new(@"\{(vectorJson|escapedFtQuery|candidateLimit)\}", RegexOptions.Compiled);

    /// <summary>Production's SQL text, extracted byte for byte from HybridSearcher.cs @ 9f8cd102.</summary>
    public static string ProductionTemplate { get; } = LoadResource("Parity.SqlServer.ProductionHybrid.sql");

    public static string BuildProductionHybrid(float[] vector, string query, int candidateLimit) =>
        Fill(ProductionTemplate, vector, query, candidateLimit);

    /// <summary>
    /// Fills the template the way production's $@"" interpolation does. A single regex pass means
    /// placeholder-like text inside the user's query is never re-scanned.
    /// </summary>
    public static string Fill(string template, float[] vector, string query, int candidateLimit)
    {
        var values = new Dictionary<string, string>
        {
            ["vectorJson"] = ProductionEscaping.VectorJson(vector),
            ["escapedFtQuery"] = ProductionEscaping.SqlLiteral(ProductionEscaping.FullTextPhrase(query)),
            ["candidateLimit"] = candidateLimit.ToString(CultureInfo.InvariantCulture),
        };
        return Placeholder.Replace(template, m => values[m.Groups[1].Value]);
    }

    private static string LoadResource(string name)
    {
        using var stream = typeof(SqlServerSql).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ProductionCopyTests"`
Expected: PASS (10 tests). If `ChunksOverlapByHalfAndStopEarlyLikeProduction` fails, the copied `ChunkText` body differs from production. Re-copy it, don't adjust the test.

- [ ] **Step 7: Commit**

```bash
git add .gitattributes HybridSearchParity.sln src/Parity.Core src/Parity.SqlServer tests/Parity.Tests
git commit -m "feat(parity): copy production chunker, sanitizer and hybrid SQL with drift checks"
```

---

### Task 4: Data files (vectors, chunks, manifest) with hash verification

**Files:**
- Create: `src/Parity.Core/Data/VectorFile.cs`, `Data/ParityJson.cs`, `Data/DataModels.cs`, `Data/DataSet.cs`, `Data/DataStore.cs`, `Data/Hashing.cs`
- Test: `tests/Parity.Tests/Unit/DataFileTests.cs`

**Interfaces:**
- Consumes: `ChunkRecord`, `ChunkKind`, `DataFileException` (Task 2).
- Produces:
  - `VectorFile.Dimensions` (1536), `VectorFile.Write(string path, IReadOnlyList<float[]>)`, `VectorFile.Read(string path, int expectedCount) → float[][]`, `VectorFile.Validate(float[] vector, int index)`
  - `ParityJson.Options` (indented, camelCase, string enums) and `ParityJson.Lines` (the same, not indented)
  - `ChunkLine`, `EdgeCaseDefinition(string Id, string Content)`, `QueryDefinition(string Id, string Group, string Text)`
  - `QueryGroups.{Exact, Paraphrase, Edge, Error, Stress, All}` and `QueryGroups.HasGroundTruth(string)`
  - `Manifest(string CorpusCommit, DateTimeOffset GeneratedUtc, string Model, int Dimensions, int ChunkCount, int QueryCount, IReadOnlyDictionary<string,string> Files, IReadOnlyDictionary<string,string> ProductionSources)`
  - `SavedQuery(string Id, string Group, string Text, float[] Vector)`
  - `DataSet` with `Manifest`, `Chunks`, `Queries`, `GroundTruth`, `FindChunk(Guid)` and `FindQueryByText(string)`
  - `GeneratedData(string CorpusCommit, DateTimeOffset GeneratedUtc, string Model, IReadOnlyList<ChunkRecord> Chunks, IReadOnlyList<QueryDefinition> Queries, IReadOnlyList<float[]> QueryVectors, IReadOnlyDictionary<string, IReadOnlyList<Guid>> GroundTruth, IReadOnlyDictionary<string,string> ProductionSources)`
  - `DataStore.{ReadEdgeCases, ReadQueries, WriteAtomically(string sampleRoot, GeneratedData, CancellationToken), Load(string sampleRoot)}`, plus the file path constants
  - `Hashing.Sha256File(string path)`

- [ ] **Step 1: Write the failing tests**

`tests/Parity.Tests/Unit/DataFileTests.cs`:
```csharp
using Parity.Core;
using Parity.Core.Data;
using Parity.Core.Model;

namespace Parity.Tests.Unit;

public sealed class DataFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"parity-data-{Guid.NewGuid():N}");

    public DataFileTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "inputs"));
        File.WriteAllText(Path.Combine(_root, DataStore.EdgeCasesFile), """[ { "id": "E01", "content": "edge text" } ]""");
        File.WriteAllText(Path.Combine(_root, DataStore.QueriesFile), """[ { "id": "Q01", "group": "exact", "text": "edge text" } ]""");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void VectorsRoundTripBitForBit()
    {
        var special = new[] { 0f, -0f, float.Epsilon, 1e-38f, float.MaxValue, float.MinValue, 0.1f, -0.333333343f };
        var vector = Enumerable.Range(0, VectorFile.Dimensions).Select(i => special[i % special.Length] * (i % 7 == 0 ? -1 : 1)).ToArray();
        var path = Path.Combine(_root, "v.f32");

        VectorFile.Write(path, [vector, vector.Reverse().ToArray()]);
        var read = VectorFile.Read(path, 2);

        Assert.Equal(vector.Select(BitConverter.SingleToInt32Bits), read[0].Select(BitConverter.SingleToInt32Bits));
        Assert.Equal(VectorFile.Dimensions * 4 * 2, new FileInfo(path).Length);
    }

    [Fact]
    public void RejectsWrongDimensionsNonFiniteValuesAndWrongLength()
    {
        var path = Path.Combine(_root, "bad.f32");
        Assert.Throws<DataFileException>(() => VectorFile.Write(path, [new float[3]]));
        var withNaN = new float[VectorFile.Dimensions];
        withNaN[5] = float.NaN;
        Assert.Throws<DataFileException>(() => VectorFile.Write(path, [withNaN]));

        VectorFile.Write(path, [new float[VectorFile.Dimensions]]);
        var error = Assert.Throws<DataFileException>(() => VectorFile.Read(path, 2));
        Assert.Contains("expected", error.Message);
    }

    [Fact]
    public void WrittenDataLoadsBackIdentically()
    {
        DataStore.WriteAtomically(_root, SampleData(), CancellationToken.None);
        var data = DataStore.Load(_root);

        var chunk = Assert.Single(data.Chunks);
        Assert.Equal("edge text", chunk.Content);
        Assert.Equal(ChunkKind.Edge, chunk.Kind);
        Assert.Equal(0.25f, chunk.Embedding[0]);
        var query = Assert.Single(data.Queries);
        Assert.Equal(("Q01", "exact", "edge text"), (query.Id, query.Group, query.Text));
        Assert.Equal(0.5f, query.Vector[0]);
        Assert.Equal(new[] { chunk.Id }, data.GroundTruth["Q01"]);
        Assert.Equal("abc1234", data.Manifest.CorpusCommit);
        Assert.Same(chunk, data.FindChunk(chunk.Id));
        Assert.Same(query, data.FindQueryByText("  edge text "));
        Assert.Null(data.FindQueryByText("Edge Text"));
        Assert.Empty(Directory.GetDirectories(Path.Combine(_root, "data")));
    }

    [Fact]
    public void LoadRefusesTamperedDataAndChangedInputs()
    {
        DataStore.WriteAtomically(_root, SampleData(), CancellationToken.None);
        File.AppendAllText(Path.Combine(_root, DataStore.QueriesFile), " ");

        var error = Assert.Throws<DataFileException>(() => DataStore.Load(_root));
        Assert.Contains("inputs/queries.json doesn't match data/manifest.json", error.Message);
    }

    [Fact]
    public void LoadExplainsMissingData()
    {
        var error = Assert.Throws<DataFileException>(() => DataStore.Load(_root));
        Assert.Contains("generate-data", error.Message);
    }

    [Fact]
    public void PreCancelledWriteLeavesNothingBehind()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => DataStore.WriteAtomically(_root, SampleData(), cts.Token));
        Assert.False(File.Exists(Path.Combine(_root, DataStore.ManifestFile)));
        Assert.Empty(Directory.GetDirectories(Path.Combine(_root, "data")));
    }

    private static GeneratedData SampleData()
    {
        var vector = new float[VectorFile.Dimensions];
        vector[0] = 0.25f;
        var queryVector = new float[VectorFile.Dimensions];
        queryVector[0] = 0.5f;
        var chunk = new ChunkRecord(Guid.NewGuid(), "idx", Guid.NewGuid(), null, "edge:E01", Guid.NewGuid(), "inputs/edge-cases.json#E01", 0, ChunkKind.Edge, "edge text", vector);
        return new GeneratedData(
            "abc1234",
            DateTimeOffset.Parse("2026-10-20T00:00:00Z"),
            "fake",
            [chunk],
            [new QueryDefinition("Q01", "exact", "edge text")],
            [queryVector],
            new Dictionary<string, IReadOnlyList<Guid>> { ["Q01"] = [chunk.Id] },
            new Dictionary<string, string> { ["x.cs"] = "hash" });
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~DataFileTests"`
Expected: build FAILS (`Parity.Core.Data` not found).

- [ ] **Step 3: Write the implementation**

`src/Parity.Core/Data/Hashing.cs`:
```csharp
using System.Security.Cryptography;

namespace Parity.Core.Data;

public static class Hashing
{
    public static string Sha256File(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
```

`src/Parity.Core/Data/ParityJson.cs`:
```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Parity.Core.Data;

public static class ParityJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep accents, CJK and U+FFFD readable
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static readonly JsonSerializerOptions Lines = new(Options) { WriteIndented = false };
}
```

`src/Parity.Core/Data/VectorFile.cs`:
```csharp
using System.Buffers.Binary;

namespace Parity.Core.Data;

/// <summary>Raw little-endian float32 vectors, Dimensions values each, back to back.</summary>
public static class VectorFile
{
    public const int Dimensions = 1536;

    public static void Write(string path, IReadOnlyList<float[]> vectors)
    {
        for (var i = 0; i < vectors.Count; i++)
        {
            Validate(vectors[i], i);
        }

        var bytes = new byte[(long)vectors.Count * Dimensions * 4];
        for (var i = 0; i < vectors.Count; i++)
        {
            for (var d = 0; d < Dimensions; d++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(((i * Dimensions) + d) * 4, 4), vectors[i][d]);
            }
        }

        File.WriteAllBytes(path, bytes);
    }

    public static float[][] Read(string path, int expectedCount)
    {
        var bytes = File.ReadAllBytes(path);
        var expectedLength = (long)expectedCount * Dimensions * 4;
        if (bytes.Length != expectedLength)
        {
            throw new DataFileException(
                $"{Path.GetFileName(path)} has {bytes.Length} bytes; expected {expectedLength} ({expectedCount} vectors × {Dimensions} × 4).");
        }

        var result = new float[expectedCount][];
        for (var i = 0; i < expectedCount; i++)
        {
            var vector = new float[Dimensions];
            for (var d = 0; d < Dimensions; d++)
            {
                vector[d] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(((i * Dimensions) + d) * 4, 4));
            }

            result[i] = vector;
        }

        return result;
    }

    public static void Validate(float[] vector, int index)
    {
        if (vector.Length != Dimensions)
        {
            throw new DataFileException($"Vector {index} has {vector.Length} dimensions; expected {Dimensions}.");
        }

        for (var d = 0; d < vector.Length; d++)
        {
            if (!float.IsFinite(vector[d]))
            {
                throw new DataFileException($"Vector {index} has a non-finite value at dimension {d}.");
            }
        }
    }
}
```

`src/Parity.Core/Data/DataModels.cs`:
```csharp
using Parity.Core.Model;

namespace Parity.Core.Data;

/// <summary>One line of data/chunks.jsonl. The vector lives in data/embeddings.f32 at the same position.</summary>
public sealed record ChunkLine(
    Guid Id,
    string IndexName,
    Guid ProjectId,
    Guid? NotebookId,
    string DocumentId,
    Guid ContentFileId,
    string SourcePath,
    int ChunkIndex,
    ChunkKind Kind,
    string Content);

public sealed record EdgeCaseDefinition(string Id, string Content);

public sealed record QueryDefinition(string Id, string Group, string Text);

public static class QueryGroups
{
    public const string Exact = "exact";
    public const string Paraphrase = "paraphrase";
    public const string Edge = "edge";
    public const string Error = "error";
    public const string Stress = "stress";

    public static readonly string[] All = [Exact, Paraphrase, Edge, Error, Stress];

    public static bool HasGroundTruth(string group) => group is Exact or Edge;
}

public sealed record Manifest(
    string CorpusCommit,
    DateTimeOffset GeneratedUtc,
    string Model,
    int Dimensions,
    int ChunkCount,
    int QueryCount,
    IReadOnlyDictionary<string, string> Files,
    IReadOnlyDictionary<string, string> ProductionSources);

public sealed record SavedQuery(string Id, string Group, string Text, float[] Vector);

public sealed record GeneratedData(
    string CorpusCommit,
    DateTimeOffset GeneratedUtc,
    string Model,
    IReadOnlyList<ChunkRecord> Chunks,
    IReadOnlyList<QueryDefinition> Queries,
    IReadOnlyList<float[]> QueryVectors,
    IReadOnlyDictionary<string, IReadOnlyList<Guid>> GroundTruth,
    IReadOnlyDictionary<string, string> ProductionSources);
```

`src/Parity.Core/Data/DataSet.cs`:
```csharp
using Parity.Core.Model;

namespace Parity.Core.Data;

public sealed class DataSet
{
    private readonly Dictionary<Guid, ChunkRecord> _chunksById;

    public DataSet(
        Manifest manifest,
        IReadOnlyList<ChunkRecord> chunks,
        IReadOnlyList<SavedQuery> queries,
        IReadOnlyDictionary<string, IReadOnlyList<Guid>> groundTruth)
    {
        Manifest = manifest;
        Chunks = chunks;
        Queries = queries;
        GroundTruth = groundTruth;
        _chunksById = chunks.ToDictionary(c => c.Id);
    }

    public Manifest Manifest { get; }
    public IReadOnlyList<ChunkRecord> Chunks { get; }
    public IReadOnlyList<SavedQuery> Queries { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<Guid>> GroundTruth { get; }

    public ChunkRecord? FindChunk(Guid id) => _chunksById.GetValueOrDefault(id);

    /// <summary>A saved query whose text equals <paramref name="text"/> after trimming (case-sensitive).</summary>
    public SavedQuery? FindQueryByText(string text) =>
        Queries.FirstOrDefault(q => string.Equals(q.Text, text.Trim(), StringComparison.Ordinal));
}
```

`src/Parity.Core/Data/DataStore.cs`:
```csharp
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
                    $"{file} doesn't match {ManifestFile} (sha256 {actual[..12]}…, expected {expected[..12]}…). " +
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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DataFileTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Parity.Core/Data tests/Parity.Tests/Unit/DataFileTests.cs
git commit -m "feat(parity): add verified data files for chunks, vectors and manifest"
```

---
### Task 5: Corpus building, ground truth and the reviewed inputs

**Files:**
- Create: `src/Parity.Core/Corpus/DocsSources.cs`, `Corpus/ParityScopes.cs`, `Corpus/CorpusBuilder.cs`, `Corpus/GroundTruth.cs`
- Create: `inputs/edge-cases.json`, `inputs/queries.json` (these are spec Appendix A and B, verbatim)
- Test: `tests/Parity.Tests/Unit/CorpusTests.cs`

**Interfaces:**
- Consumes: `DeterministicGuid` (Task 1); `ChunkRecord`, `ChunkKind`, `Scope`, `NamedScope` (Task 2); `ProductionPreparation`, `ProductionSources`, `RepoPaths` (Task 3); `EdgeCaseDefinition`, `QueryDefinition`, `QueryGroups`, `DataStore.ReadEdgeCases`, `DataStore.ReadQueries` (Task 4).
- Produces:
  - `CorpusFile(string Path, string Content)`
  - `IDocsSource` with `string ResolvedCommit` and `IReadOnlyList<CorpusFile> ReadDocs()`, implemented by `GitDocsSource(string repoRoot, string commit)`
  - `ChunkDraft(...)`, with the same fields as `ChunkRecord` minus `Embedding`, plus `WithEmbedding(float[]) → ChunkRecord`
  - `ParityScopes.ProjectLabels`, `ParityScopes.ProjectId(string label)`, `ParityScopes.NotebookN1`, `ParityScopes.All` (`IReadOnlyList<NamedScope>`) and `ParityScopes.Find(string label)`
  - `CorpusBuilder.Build(IReadOnlyList<CorpusFile> docs, IReadOnlyList<EdgeCaseDefinition> edges) → IReadOnlyList<ChunkDraft>`
  - `GroundTruth.PhraseRegex(string phrase) → Regex` and `GroundTruth.Compute(IReadOnlyList<QueryDefinition>, IReadOnlyList<ChunkDraft>) → IReadOnlyDictionary<string, IReadOnlyList<Guid>>`

- [ ] **Step 1: Write the reviewed inputs**

`inputs/edge-cases.json`:
```json
[
  { "id": "E01", "content": "Embeddings default to text-embedding-3-small with 1536 dimensions, so no padding is applied." },
  { "id": "E02", "content": "The local runtime wraps llama.cpp and stable-diffusion.cpp behind an nginx gateway." },
  { "id": "E03", "content": "Don't restart the container while a model download is running; it can't resume a partial file." },
  { "id": "E04", "content": "The O'Reilly Media book on search engines explains inverted indexes well." },
  { "id": "E05", "content": "The smoke test should say \"hello world\" twice before it exits with code zero." },
  { "id": "E06", "content": "Order a café crème before the naïve résumé workshop starts." },
  { "id": "E07", "content": "Order a cafe creme before the naive resume workshop starts." },
  { "id": "E08", "content": "Source and release notes: https://github.com/ggml-org/llama.cpp/releases" },
  { "id": "E09", "content": "Upgrade from v0.9.18 to v0.10 before running the storage migration." },
  { "id": "E10", "content": "Each skill ships a SKILL.md file and an optional scripts/ folder." },
  { "id": "E11", "content": "It is the end of the line for the old provider once the router is enabled." },
  { "id": "E12", "content": "published guide published guide published guide published guide published guide" },
  { "id": "E13", "content": "Notes on the published, guide-adjacent pages: punctuation sits between the words." },
  { "id": "E14", "content": "PUBLISHED GUIDE settings are shown in upper case on the admin page." },
  { "id": "E15", "content": "Set MSSQL_SA_PASSWORD and ACCEPT_EULA before starting the database container." },
  { "id": "E16", "content": "DocumentChunk.Embedding stores vector(1536) values for every chunk." },
  { "id": "E17", "content": "Chunks are 2048 bytes with 50% overlap, which is about 1.5x the raw text size." },
  { "id": "E18", "content": "Contact admin@guideants.local to request access to the shared notebook." },
  { "id": "E19", "content": "The server is written in C# and the build scripts in F# on .NET 8." },
  { "id": "E20", "content": "日本語のテキスト検索はトークン化の方法によって結果が変わります。" }
]
```

`inputs/queries.json`:
```json
[
  { "id": "Q01", "group": "exact", "text": "conversation lock" },
  { "id": "Q02", "group": "exact", "text": "router preset" },
  { "id": "Q03", "group": "exact", "text": "host folder mounts" },
  { "id": "Q04", "group": "exact", "text": "script execution agent" },
  { "id": "Q05", "group": "exact", "text": "tool call limits" },
  { "id": "Q06", "group": "exact", "text": "local model onboarding" },
  { "id": "Q07", "group": "exact", "text": "Docker Desktop" },
  { "id": "Q08", "group": "exact", "text": "bearer token" },
  { "id": "Q09", "group": "exact", "text": "GuideAnts Guide" },
  { "id": "Q10", "group": "exact", "text": "service toolbar" },
  { "id": "Q11", "group": "paraphrase", "text": "how do I stop two people editing the same chat at once" },
  { "id": "Q12", "group": "paraphrase", "text": "running models on an AMD graphics card" },
  { "id": "Q13", "group": "paraphrase", "text": "letting outside websites embed an assistant" },
  { "id": "Q14", "group": "paraphrase", "text": "what happens when the chat history gets too long for the model" },
  { "id": "Q15", "group": "paraphrase", "text": "sharing my computer's directories with a notebook" },
  { "id": "Q16", "group": "paraphrase", "text": "choosing files from a model repository before downloading" },
  { "id": "Q17", "group": "paraphrase", "text": "logging in from the terminal securely" },
  { "id": "Q18", "group": "paraphrase", "text": "stopping the assistant from calling tools forever" },
  { "id": "Q19", "group": "edge", "text": "text-embedding-3-small" },
  { "id": "Q20", "group": "edge", "text": "llama.cpp" },
  { "id": "Q21", "group": "edge", "text": "don't restart the container" },
  { "id": "Q22", "group": "edge", "text": "O'Reilly Media" },
  { "id": "Q23", "group": "edge", "text": "say \"hello world\" twice" },
  { "id": "Q24", "group": "edge", "text": "café crème" },
  { "id": "Q25", "group": "edge", "text": "https://github.com/ggml-org/llama.cpp" },
  { "id": "Q26", "group": "edge", "text": "v0.9.18" },
  { "id": "Q27", "group": "edge", "text": "SKILL.md" },
  { "id": "Q28", "group": "edge", "text": "end of the line" },
  { "id": "Q29", "group": "edge", "text": "MSSQL_SA_PASSWORD" },
  { "id": "Q30", "group": "edge", "text": "DocumentChunk.Embedding" },
  { "id": "Q31", "group": "edge", "text": "50% overlap" },
  { "id": "Q32", "group": "edge", "text": "admin@guideants.local" },
  { "id": "Q33", "group": "edge", "text": "C#" },
  { "id": "Q34", "group": "edge", "text": "テキスト検索" },
  { "id": "Q35", "group": "error", "text": "the and of" },
  { "id": "Q36", "group": "error", "text": "!!! ???" },
  { "id": "Q37", "group": "stress", "text": "published guide" },
  { "id": "Q38", "group": "stress", "text": "quantum entanglement teleportation" }
]
```

- [ ] **Step 2: Write the failing tests**

`tests/Parity.Tests/Unit/CorpusTests.cs`:
```csharp
using Parity.Core;
using Parity.Core.Corpus;
using Parity.Core.Data;
using Parity.Core.Ids;
using Parity.Core.Model;
using Parity.Core.Production;

namespace Parity.Tests.Unit;

public class CorpusTests
{
    [Fact]
    public void GitSourceReadsTheSeventyNineTopLevelDocsAtThePinnedCommit()
    {
        var source = new GitDocsSource(RepoPaths.FindRepoRoot(), ProductionSources.Commit);
        var docs = source.ReadDocs();

        Assert.StartsWith(ProductionSources.Commit, source.ResolvedCommit);
        Assert.Equal(79, docs.Count);
        Assert.All(docs, d => Assert.Matches(@"^docs/[^/]+\.md$", d.Path));
        Assert.Equal(docs.Select(d => d.Path).OrderBy(p => p, StringComparer.Ordinal), docs.Select(d => d.Path));
        Assert.DoesNotContain(docs, d => d.Path.EndsWith("efdoctor-findings-2026-10-15.md", StringComparison.Ordinal));
        Assert.Equal(1_194_882, docs.Sum(d => System.Text.Encoding.UTF8.GetByteCount(d.Content)));
    }

    [Fact]
    public void FilesAreAssignedRoundRobinAndEveryThirdP1FileGetsTheNotebook()
    {
        var docs = Enumerable.Range(0, 9).Select(i => new CorpusFile($"docs/{(char)('a' + i)}.md", $"file {i}")).Reverse().ToList();
        var drafts = CorpusBuilder.Build(docs, []);

        var projectOf = drafts.ToDictionary(d => d.SourcePath, d => d.ProjectId);
        Assert.Equal(ParityScopes.ProjectId("P1"), projectOf["docs/a.md"]);
        Assert.Equal(ParityScopes.ProjectId("P2"), projectOf["docs/b.md"]);
        Assert.Equal(ParityScopes.ProjectId("P3"), projectOf["docs/c.md"]);
        Assert.Equal(ParityScopes.ProjectId("P1"), projectOf["docs/d.md"]);
        var withNotebook = drafts.Where(d => d.NotebookId == ParityScopes.NotebookN1).Select(d => d.SourcePath).ToList();
        Assert.Equal(new[] { "docs/g.md" }, withNotebook);
    }

    [Fact]
    public void ChunkFieldsAreDeterministic()
    {
        var drafts = CorpusBuilder.Build([new CorpusFile("docs/a.md", new string('x', 3000))], [new EdgeCaseDefinition("E01", "edge")]);

        var doc = drafts[1];
        Assert.Equal(DeterministicGuid.Create("doc:docs/a.md#1"), doc.Id);
        Assert.Equal(ParityScopes.ProjectId("P1").ToString(), doc.IndexName);
        Assert.Equal(DeterministicGuid.Create("file:docs/a.md"), doc.ContentFileId);
        Assert.Equal(("docs/a.md", 1, ChunkKind.Doc), (doc.DocumentId, doc.ChunkIndex, doc.Kind));

        var edge = drafts[^1];
        Assert.Equal(DeterministicGuid.Create("edge:E01"), edge.Id);
        Assert.Equal(("edge:E01", "inputs/edge-cases.json#E01", 0, ChunkKind.Edge), (edge.DocumentId, edge.SourcePath, edge.ChunkIndex, edge.Kind));
        Assert.Null(edge.NotebookId);
    }

    [Fact]
    public void EdgeCasesAreSpreadAcrossProjectsInFileOrder()
    {
        var edges = Enumerable.Range(1, 4).Select(i => new EdgeCaseDefinition($"E0{i}", $"edge {i}")).ToList();
        var labels = CorpusBuilder.Build([], edges).Select(d => d.ProjectId).ToList();
        Assert.Equal(new[] { "P1", "P2", "P3", "P1" }.Select(ParityScopes.ProjectId), labels);
    }

    [Theory]
    [InlineData("lock", "the conversation lock is held", true)]
    [InlineData("lock", "a block of text", false)]
    [InlineData("conversation lock", "CONVERSATION\n  LOCK.", true)]
    [InlineData("C#", "written in C# and F#", true)]
    [InlineData("C#", "written in C#x", false)]
    [InlineData("say \"hello world\" twice", "should say \"hello world\" twice before", true)]
    [InlineData("published guide", "the published, guide-adjacent pages", false)]
    public void GroundTruthIsWholeWordCaseInsensitiveAndWhitespaceFlexible(string phrase, string text, bool expected)
    {
        Assert.Equal(expected, GroundTruth.PhraseRegex(phrase).IsMatch(text));
    }

    [Fact]
    public void ScopesAreTheFourFromTheSpec()
    {
        Assert.Equal(new[] { "P1", "P2", "P3", "P1/N1" }, ParityScopes.All.Select(s => s.Label));
        Assert.Equal(ParityScopes.NotebookN1, ParityScopes.Find("p1/n1").Scope.NotebookId);
        var error = Assert.Throws<ParityConfigurationException>(() => ParityScopes.Find("P9"));
        Assert.Contains("P1, P2, P3, P1/N1", error.Message);
    }

    [Fact]
    public void ReviewedInputsMatchTheSpecAppendices()
    {
        var root = RepoPaths.FindSampleRoot();
        var queries = DataStore.ReadQueries(root);
        var edges = DataStore.ReadEdgeCases(root);

        Assert.Equal(Enumerable.Range(1, 38).Select(i => $"Q{i:00}"), queries.Select(q => q.Id));
        Assert.Equal(Enumerable.Range(1, 20).Select(i => $"E{i:00}"), edges.Select(e => e.Id));
        Assert.All(queries, q => Assert.Contains(q.Group, QueryGroups.All));
        var counts = queries.GroupBy(q => q.Group).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(10, counts[QueryGroups.Exact]);
        Assert.Equal(8, counts[QueryGroups.Paraphrase]);
        Assert.Equal(16, counts[QueryGroups.Edge]);
        Assert.Equal(2, counts[QueryGroups.Error]);
        Assert.Equal(2, counts[QueryGroups.Stress]);
    }

    [Fact]
    public void EveryExactAndEdgeQueryHasGroundTruthExceptTheCjkPhrase()
    {
        var root = RepoPaths.FindSampleRoot();
        var docs = new GitDocsSource(RepoPaths.FindRepoRoot(), ProductionSources.Commit).ReadDocs();
        var drafts = CorpusBuilder.Build(docs, DataStore.ReadEdgeCases(root));
        var truth = GroundTruth.Compute(DataStore.ReadQueries(root), drafts);

        Assert.Equal(26, truth.Count);
        foreach (var (id, matches) in truth)
        {
            if (id == "Q34")
            {
                // \w boundaries never fire inside running CJK text, so the oracle is empty for Q34 by design.
                Assert.Empty(matches);
            }
            else
            {
                Assert.True(matches.Count > 0, $"{id} has no ground-truth match");
            }
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~CorpusTests"`
Expected: build FAILS (`Parity.Core.Corpus` not found).

- [ ] **Step 4: Write the implementation**

`src/Parity.Core/Corpus/DocsSources.cs`:
```csharp
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
```

`src/Parity.Core/Corpus/ParityScopes.cs`:
```csharp
using Parity.Core.Ids;
using Parity.Core.Model;

namespace Parity.Core.Corpus;

public static class ParityScopes
{
    public static readonly string[] ProjectLabels = ["P1", "P2", "P3"];

    public static readonly Guid NotebookN1 = DeterministicGuid.Create("notebook:P1/N1");

    public static IReadOnlyList<NamedScope> All { get; } =
    [
        new("P1", new Scope(ProjectId("P1").ToString(), null)),
        new("P2", new Scope(ProjectId("P2").ToString(), null)),
        new("P3", new Scope(ProjectId("P3").ToString(), null)),
        new("P1/N1", new Scope(ProjectId("P1").ToString(), NotebookN1)),
    ];

    public static Guid ProjectId(string label) => DeterministicGuid.Create($"project:{label}");

    public static NamedScope Find(string label) =>
        All.FirstOrDefault(s => string.Equals(s.Label, label, StringComparison.OrdinalIgnoreCase))
        ?? throw new ParityConfigurationException($"Unknown scope '{label}'. Valid scopes: {string.Join(", ", All.Select(s => s.Label))}.");
}
```

`src/Parity.Core/Corpus/CorpusBuilder.cs`:
```csharp
using Parity.Core.Data;
using Parity.Core.Ids;
using Parity.Core.Model;
using Parity.Core.Production;

namespace Parity.Core.Corpus;

public sealed record ChunkDraft(
    Guid Id,
    string IndexName,
    Guid ProjectId,
    Guid? NotebookId,
    string DocumentId,
    Guid ContentFileId,
    string SourcePath,
    int ChunkIndex,
    ChunkKind Kind,
    string Content)
{
    public ChunkRecord WithEmbedding(float[] embedding) =>
        new(Id, IndexName, ProjectId, NotebookId, DocumentId, ContentFileId, SourcePath, ChunkIndex, Kind, Content, embedding);
}

/// <summary>Spec section 5.2: project, notebook and ID assignment.</summary>
public static class CorpusBuilder
{
    public static IReadOnlyList<ChunkDraft> Build(IReadOnlyList<CorpusFile> docs, IReadOnlyList<EdgeCaseDefinition> edgeCases)
    {
        var drafts = new List<ChunkDraft>();
        var ordered = docs.OrderBy(d => d.Path, StringComparer.Ordinal).ToList();
        var p1Files = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            var file = ordered[i];
            var label = ParityScopes.ProjectLabels[i % ParityScopes.ProjectLabels.Length];
            Guid? notebookId = null;
            if (label == "P1")
            {
                if (p1Files % 3 == 2)
                {
                    notebookId = ParityScopes.NotebookN1; // every third file assigned to P1
                }

                p1Files++;
            }

            var projectId = ParityScopes.ProjectId(label);
            var chunks = ProductionPreparation.PrepareChunks(file.Content);
            for (var c = 0; c < chunks.Count; c++)
            {
                drafts.Add(new ChunkDraft(
                    DeterministicGuid.Create($"doc:{file.Path}#{c}"),
                    projectId.ToString(),
                    projectId,
                    notebookId,
                    file.Path,
                    DeterministicGuid.Create($"file:{file.Path}"),
                    file.Path,
                    c,
                    ChunkKind.Doc,
                    chunks[c]));
            }
        }

        for (var j = 0; j < edgeCases.Count; j++)
        {
            var edge = edgeCases[j];
            var projectId = ParityScopes.ProjectId(ParityScopes.ProjectLabels[j % ParityScopes.ProjectLabels.Length]);
            drafts.Add(new ChunkDraft(
                DeterministicGuid.Create($"edge:{edge.Id}"),
                projectId.ToString(),
                projectId,
                null,
                $"edge:{edge.Id}",
                DeterministicGuid.Create($"file:edge:{edge.Id}"),
                $"inputs/edge-cases.json#{edge.Id}",
                0,
                ChunkKind.Edge,
                edge.Content));
        }

        return drafts;
    }
}
```

`src/Parity.Core/Corpus/GroundTruth.cs`:
```csharp
using System.Text.RegularExpressions;
using Parity.Core.Data;

namespace Parity.Core.Corpus;

/// <summary>
/// An engine-independent oracle: chunks containing the phrase as whole words, case-insensitive,
/// whitespace-flexible. Used only by the sanity checks, never by the parity rules.
/// </summary>
public static class GroundTruth
{
    public static Regex PhraseRegex(string phrase)
    {
        var words = phrase.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape);
        return new Regex($@"(?<!\w){string.Join(@"\s+", words)}(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<Guid>> Compute(IReadOnlyList<QueryDefinition> queries, IReadOnlyList<ChunkDraft> chunks)
    {
        var result = new Dictionary<string, IReadOnlyList<Guid>>();
        foreach (var query in queries.Where(q => QueryGroups.HasGroundTruth(q.Group)))
        {
            var regex = PhraseRegex(query.Text);
            result[query.Id] = chunks.Where(c => regex.IsMatch(c.Content)).Select(c => c.Id).OrderBy(id => id).ToList();
        }

        return result;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~CorpusTests"`
Expected: PASS (14 test cases: 7 facts plus 7 theory rows). If the byte-count assertion fails, the docs were read from the working tree instead of git. Fix the source, not the number.

- [ ] **Step 6: Commit**

```bash
git add inputs src/Parity.Core/Corpus tests/Parity.Tests/Unit/CorpusTests.cs
git commit -m "feat(parity): build the docs corpus from git with ground truth and reviewed inputs"
```

---

### Task 6: OpenAI embedding client

**Files:**
- Create: `src/Parity.Core/Embeddings/OpenAiEmbeddingClient.cs`
- Create: `tests/Parity.Tests/Support/FakeHttpHandler.cs`
- Test: `tests/Parity.Tests/Unit/OpenAiEmbeddingClientTests.cs`

**Interfaces:**
- Consumes: `VectorFile.Validate` and `VectorFile.Dimensions` (Task 4); `ParityConfigurationException` (Task 2).
- Produces:
  - `IEmbeddingClient` with `string Model` and `Task<float[][]> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)`
  - `OpenAiEmbeddingClient(HttpClient http, string? apiKey, string endpointBase = "https://api.openai.com/v1", string model = "text-embedding-3-small", Func<TimeSpan, CancellationToken, Task>? delay = null)`, with `BatchSize = 100` and `MaxAttempts = 6`

- [ ] **Step 1: Write the test helper and the failing tests**

`tests/Parity.Tests/Support/FakeHttpHandler.cs`:
```csharp
using System.Net;
using System.Text;
using System.Text.Json;

namespace Parity.Tests.Support;

public sealed class FakeHttpHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request, body));
        return respond(Requests.Count);
    }

    /// <summary>A /embeddings response for <paramref name="count"/> inputs. Vector i has value (offset + i) in dimension 0.</summary>
    public static HttpResponseMessage Embeddings(int count, int offset = 0, int dimensions = 1536, bool reverse = false)
    {
        var data = Enumerable.Range(0, count).Select(i =>
        {
            var vector = new float[dimensions];
            vector[0] = offset + i;
            return new { @object = "embedding", index = i, embedding = vector };
        });
        if (reverse)
        {
            data = data.Reverse();
        }

        var json = JsonSerializer.Serialize(new { @object = "list", data, model = "text-embedding-3-small" });
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    public static HttpResponseMessage Status(HttpStatusCode code, string body = "{\"error\":\"x\"}", TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(code) { Content = new StringContent(body) };
        if (retryAfter is { } wait)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(wait);
        }

        return response;
    }
}
```

`tests/Parity.Tests/Unit/OpenAiEmbeddingClientTests.cs`:
```csharp
using System.Net;
using System.Text.Json;
using Parity.Core;
using Parity.Core.Embeddings;
using Parity.Tests.Support;

namespace Parity.Tests.Unit;

public class OpenAiEmbeddingClientTests
{
    private readonly List<TimeSpan> _delays = [];

    private OpenAiEmbeddingClient Client(FakeHttpHandler handler) =>
        new(new HttpClient(handler), "sk-test", delay: (wait, _) => { _delays.Add(wait); return Task.CompletedTask; });

    [Fact]
    public async Task SendsModelInputsAndBearerKey()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Embeddings(2));
        await Client(handler).EmbedAsync(["a", "b"], CancellationToken.None);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.openai.com/v1/embeddings", request.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", request.Headers.Authorization!.ToString());
        using var json = JsonDocument.Parse(body);
        Assert.Equal("text-embedding-3-small", json.RootElement.GetProperty("model").GetString());
        Assert.Equal("float", json.RootElement.GetProperty("encoding_format").GetString());
        Assert.Equal(new[] { "a", "b" }, json.RootElement.GetProperty("input").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task BatchesByOneHundredAndKeepsInputOrder()
    {
        var handler = new FakeHttpHandler(call => FakeHttpHandler.Embeddings(call < 3 ? 100 : 50, offset: (call - 1) * 100));
        var vectors = await Client(handler).EmbedAsync(Enumerable.Range(0, 250).Select(i => $"t{i}").ToList(), CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(250, vectors.Length);
        Assert.Equal(Enumerable.Range(0, 250).Select(i => (float)i), vectors.Select(v => v[0]));
    }

    [Fact]
    public async Task SortsResponseVectorsByIndex()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Embeddings(3, reverse: true));
        var vectors = await Client(handler).EmbedAsync(["a", "b", "c"], CancellationToken.None);
        Assert.Equal(new[] { 0f, 1f, 2f }, vectors.Select(v => v[0]));
    }

    [Fact]
    public async Task RetriesRateLimitsHonouringRetryAfter()
    {
        var handler = new FakeHttpHandler(call => call == 1
            ? FakeHttpHandler.Status(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(3))
            : FakeHttpHandler.Embeddings(1));
        await Client(handler).EmbedAsync(["a"], CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(3) }, _delays);
    }

    [Fact]
    public async Task RetriesServerErrorsWithExponentialBackoff()
    {
        var handler = new FakeHttpHandler(call => call <= 2 ? FakeHttpHandler.Status(HttpStatusCode.BadGateway) : FakeHttpHandler.Embeddings(1));
        await Client(handler).EmbedAsync(["a"], CancellationToken.None);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, _delays);
    }

    [Fact]
    public async Task GivesUpAfterSixAttempts()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.TooManyRequests));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler).EmbedAsync(["a"], CancellationToken.None));
        Assert.Contains("(429) after 6 attempt(s)", error.Message);
        Assert.Equal(6, handler.Requests.Count);
    }

    [Fact]
    public async Task DoesNotRetryClientErrorsAndShowsTheBody()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.BadRequest, "{\"error\":\"bad input\"}"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler).EmbedAsync(["a"], CancellationToken.None));
        Assert.Contains("bad input", error.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RejectsVectorsThatAreNot1536Dimensions()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Embeddings(1, dimensions: 1024));
        await Assert.ThrowsAsync<DataFileException>(() => Client(handler).EmbedAsync(["a"], CancellationToken.None));
    }

    [Fact]
    public void RequiresAnApiKey()
    {
        var error = Assert.Throws<ParityConfigurationException>(() => new OpenAiEmbeddingClient(new HttpClient(), " "));
        Assert.Contains("OPENAI_API_KEY", error.Message);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~OpenAiEmbeddingClientTests"`
Expected: build FAILS (`Parity.Core.Embeddings` not found).

- [ ] **Step 3: Write the implementation**

`src/Parity.Core/Embeddings/OpenAiEmbeddingClient.cs`:
```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Parity.Core.Data;

namespace Parity.Core.Embeddings;

public interface IEmbeddingClient
{
    string Model { get; }

    Task<float[][]> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct);
}

/// <summary>
/// POST {endpoint}/embeddings, the same REST call production's OpenAiEmbeddingService makes, with
/// batching and retry on 429 and 5xx responses.
/// </summary>
public sealed class OpenAiEmbeddingClient : IEmbeddingClient
{
    public const int BatchSize = 100;
    public const int MaxAttempts = 6;

    private static readonly JsonSerializerOptions ResponseOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly Uri _endpoint;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public OpenAiEmbeddingClient(
        HttpClient http,
        string? apiKey,
        string endpointBase = "https://api.openai.com/v1",
        string model = "text-embedding-3-small",
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ParityConfigurationException(
                "An OpenAI API key is required. Set OPENAI_API_KEY, or run `dotnet user-secrets set OpenAI:ApiKey <key>` in src/Parity.Cli.");
        }

        _http = http;
        _apiKey = apiKey;
        _endpoint = new Uri(endpointBase.TrimEnd('/') + "/embeddings");
        _delay = delay ?? Task.Delay;
        Model = model;
    }

    public string Model { get; }

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        var results = new float[inputs.Count][];
        for (var start = 0; start < inputs.Count; start += BatchSize)
        {
            var batch = inputs.Skip(start).Take(BatchSize).ToArray();
            var vectors = await EmbedBatchAsync(batch, ct);
            Array.Copy(vectors, 0, results, start, vectors.Length);
        }

        return results;
    }

    private async Task<float[][]> EmbedBatchAsync(string[] batch, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = JsonContent.Create(new { model = Model, input = batch, encoding_format = "float" }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                return Parse(body, batch.Length);
            }

            var status = (int)response.StatusCode;
            var retryable = status == 429 || status >= 500;
            if (!retryable || attempt >= MaxAttempts)
            {
                throw new InvalidOperationException(
                    $"OpenAI embeddings request failed ({status}) after {attempt} attempt(s): {(body.Length > 500 ? body[..500] : body)}");
            }

            var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
            await _delay(wait, ct);
        }
    }

    private static float[][] Parse(string body, int expected)
    {
        var parsed = JsonSerializer.Deserialize<EmbeddingResponse>(body, ResponseOptions)
            ?? throw new InvalidOperationException("OpenAI embeddings response was empty.");
        if (parsed.Data is null || parsed.Data.Count != expected)
        {
            throw new InvalidOperationException($"OpenAI returned {parsed.Data?.Count ?? 0} vectors for {expected} inputs.");
        }

        var ordered = parsed.Data.OrderBy(d => d.Index).Select(d => d.Embedding).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            VectorFile.Validate(ordered[i], i);
        }

        return ordered;
    }

    private sealed record EmbeddingResponse(List<EmbeddingData>? Data);

    private sealed record EmbeddingData(int Index, float[] Embedding);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~OpenAiEmbeddingClientTests"`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Parity.Core/Embeddings tests/Parity.Tests/Support/FakeHttpHandler.cs tests/Parity.Tests/Unit/OpenAiEmbeddingClientTests.cs
git commit -m "feat(parity): add batched OpenAI embedding client with retry"
```

---
### Task 7: CLI skeleton, configuration and `generate-data`

**Files:**
- Create: `appsettings.json`
- Create: `src/Parity.Cli/Parity.Cli.csproj`, `src/Parity.Cli/Program.cs`, `src/Parity.Cli/CliApp.cs`, `src/Parity.Cli/CliArgs.cs`, `src/Parity.Cli/AppConfig.cs`, `src/Parity.Cli/Commands/GenerateDataCommand.cs`
- Create: `tests/Parity.Tests/Support/HashEmbedding.cs`, `tests/Parity.Tests/Support/FakeDocsSource.cs`
- Modify: `tests/Parity.Tests/Parity.Tests.csproj` (reference `Parity.Cli`), `HybridSearchParity.sln`
- Test: `tests/Parity.Tests/Unit/CliTests.cs`, `tests/Parity.Tests/Unit/GenerateDataCommandTests.cs`

**Interfaces:**
- Consumes: Tasks 2–6 (`ParitySettings`, `RepoPaths`, `ProductionSources`, `DataStore`, `GeneratedData`, `CorpusBuilder`, `GroundTruth`, `IDocsSource`, `GitDocsSource`, `IEmbeddingClient`, `OpenAiEmbeddingClient`).
- Produces:
  - `ExitCodes.Success` (0), `ExitCodes.NotSwitchable` (1) and `ExitCodes.ConfigurationOrData` (2)
  - `CliArgs.Parse(string[]) → CliArgs`, with `Command`, `Positionals`, `Has(option)` and `Get(option)`
  - `AppConfig.Load(string sampleRoot, IDictionary<string,string?>? overrides = null, Func<string,string?>? environment = null) → ParitySettings`
  - `CliApp(TextWriter output, TextWriter error, string? sampleRoot = null, Func<ParitySettings, IEmbeddingClient?>? embedderFactory = null, Func<string, IDocsSource>? docsSourceFactory = null, IDictionary<string,string?>? configOverrides = null)`, with `Task<int> RunAsync(string[] args, CancellationToken ct)`
  - `GenerateDataCommand(TextWriter output, string sampleRoot, Func<string, IDocsSource> docsSourceFactory, IEmbeddingClient? embedder)`, with `RunAsync(CliArgs, CancellationToken)`
  - Test support: `HashEmbedding.Create(string) → float[]` (unit length, all components positive), `FakeEmbeddingClient`, `FakeDocsSource`

- [ ] **Step 1: Create the CLI project and the committed configuration**

`src/Parity.Cli/Parity.Cli.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>Parity.Cli</RootNamespace>
    <UserSecretsId>hybrid-search-parity</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Parity.Core/Parity.Core.csproj" />
    <ProjectReference Include="../Parity.SqlServer/Parity.SqlServer.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Binder" />
    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" />
    <PackageReference Include="Microsoft.Extensions.Configuration.UserSecrets" />
  </ItemGroup>
</Project>
```

Add to the project references in `tests/Parity.Tests/Parity.Tests.csproj`:
```xml
    <ProjectReference Include="../../src/Parity.Cli/Parity.Cli.csproj" />
```

Run: `dotnet sln HybridSearchParity.sln add src/Parity.Cli/Parity.Cli.csproj`

`appsettings.json` (the thresholds here are the committed, pre-agreed bar):
```json
{
  "Search": { "Profile": "sqlserver" },
  "Profiles": {
    "sqlserver": {
      "Provider": "SqlServer",
      "ConnectionString": "Server=localhost,14333;Database=parity;User ID=sa;Password=Parity_Passw0rd!;TrustServerCertificate=True;Encrypt=False"
    },
    "postgres": {
      "Provider": "Postgres",
      "ConnectionString": "Host=localhost;Port=54329;Database=parity;Username=postgres;Password=parity",
      "TextSearchConfig": "parity_english_nostem"
    },
    "postgres-english": {
      "Provider": "Postgres",
      "ConnectionString": "Host=localhost;Port=54329;Database=parity_english;Username=postgres;Password=parity",
      "TextSearchConfig": "english"
    }
  },
  "Hybrid": { "MaxResults": 5, "VectorWeight": 0.7, "TextWeight": 0.3, "CandidateMultiplier": 2 },
  "Parity": {
    "Baseline": "sqlserver",
    "Candidate": "postgres",
    "Informational": [ "postgres-english" ],
    "VectorTopK": 10,
    "VectorTolerance": 1e-5,
    "TieTolerance": 1e-6,
    "FullTextPopulationTimeoutSeconds": 600,
    "Thresholds": { "MinMeanTop5Overlap": 0.9, "MinSameTopOneRate": 0.8 }
  },
  "Images": {
    "SqlServer": "ghcr.io/elumenotion/mssql2025-express-fts:main",
    "Postgres": "pgvector/pgvector:0.8.7-pg18"
  },
  "OpenAI": { "Endpoint": "https://api.openai.com/v1", "Model": "text-embedding-3-small" }
}
```

- [ ] **Step 2: Add the test support types**

`tests/Parity.Tests/Support/HashEmbedding.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using Parity.Core.Data;
using Parity.Core.Embeddings;

namespace Parity.Tests.Support;

/// <summary>
/// Deterministic fake embeddings. Every component is positive, so every cosine similarity is above
/// zero and production's "distance &lt; 1.0" filter keeps every row.
/// </summary>
public static class HashEmbedding
{
    public static float[] Create(string text)
    {
        var rng = new Random(BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0));
        var vector = new float[VectorFile.Dimensions];
        double norm = 0;
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(rng.NextDouble() + 0.01);
            norm += vector[i] * vector[i];
        }

        var scale = (float)(1 / Math.Sqrt(norm));
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] *= scale;
        }

        return vector;
    }
}

public sealed class FakeEmbeddingClient(Action<int>? afterCall = null) : IEmbeddingClient
{
    public int Calls { get; private set; }

    public string Model => "fake-hash";

    public Task<float[][]> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls++;
        var vectors = inputs.Select(HashEmbedding.Create).ToArray();
        afterCall?.Invoke(Calls);
        return Task.FromResult(vectors);
    }
}
```

`tests/Parity.Tests/Support/FakeDocsSource.cs`:
```csharp
using Parity.Core.Corpus;

namespace Parity.Tests.Support;

public sealed class FakeDocsSource(params CorpusFile[] files) : IDocsSource
{
    public string ResolvedCommit => "fakecommit0000000000000000000000000000000";

    public IReadOnlyList<CorpusFile> ReadDocs() => files;
}
```

- [ ] **Step 3: Write the failing tests**

`tests/Parity.Tests/Unit/CliTests.cs`:
```csharp
using Parity.Cli;
using Parity.Core;
using Parity.Core.Settings;

namespace Parity.Tests.Unit;

public sealed class CliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"parity-cli-{Guid.NewGuid():N}");

    public CliTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), "{}");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ParsesCommandPositionalsFlagsAndValues()
    {
        var args = CliArgs.Parse(["search", "conversation", "lock", "--profile", "postgres", "--quirk-fixed"]);
        Assert.Equal("search", args.Command);
        Assert.Equal(new[] { "conversation", "lock" }, args.Positionals);
        Assert.Equal("postgres", args.Get("--profile"));
        Assert.True(args.Has("--quirk-fixed"));
        Assert.Null(args.Get("--scope"));
    }

    [Fact]
    public void RejectsUnknownOptionsAndMissingValues()
    {
        Assert.Contains("Unknown option --frobnicate", Assert.Throws<ParityConfigurationException>(() => CliArgs.Parse(["seed", "--frobnicate"])).Message);
        Assert.Contains("--profile needs a value", Assert.Throws<ParityConfigurationException>(() => CliArgs.Parse(["seed", "--profile"])).Message);
    }

    [Fact]
    public void CommittedAppsettingsMatchTheSpec()
    {
        var settings = AppConfig.Load(RepoPaths.FindSampleRoot(), environment: _ => null);

        Assert.Equal(new[] { "postgres", "postgres-english", "sqlserver" }, settings.Profiles.Keys.Order());
        Assert.Equal("SqlServer", settings.Profiles["sqlserver"].Provider);
        Assert.Equal("parity_english_nostem", settings.Profiles["postgres"].TextSearchConfig);
        Assert.Equal("english", settings.Profiles["postgres-english"].TextSearchConfig);
        Assert.Equal(new[] { "postgres-english" }, settings.Parity.Informational);
        Assert.Equal(0.9, settings.Parity.Thresholds.MinMeanTop5Overlap);
        Assert.Equal(0.8, settings.Parity.Thresholds.MinSameTopOneRate);
        // The Testcontainers fixture uses ImageSettings defaults; keep them identical to appsettings.json.
        Assert.Equal(new ImageSettings().SqlServer, settings.Images.SqlServer);
        Assert.Equal(new ImageSettings().Postgres, settings.Images.Postgres);
    }

    [Fact]
    public void OpenAiKeyFallsBackToTheEnvironment()
    {
        var settings = AppConfig.Load(_root, new Dictionary<string, string?> { ["OpenAI:ApiKey"] = "" }, name => name == "OPENAI_API_KEY" ? "sk-env" : null);
        Assert.Equal("sk-env", settings.OpenAI.ApiKey);
    }

    [Fact]
    public async Task HelpExitsZeroAndUnknownCommandExitsTwo()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var app = new CliApp(output, error, _root);

        Assert.Equal(ExitCodes.Success, await app.RunAsync(["--help"], CancellationToken.None));
        Assert.Contains("generate-data", output.ToString());
        Assert.Equal(ExitCodes.ConfigurationOrData, await app.RunAsync(["bogus"], CancellationToken.None));
        Assert.Contains("Unknown command 'bogus'", error.ToString());
    }
}
```

`tests/Parity.Tests/Unit/GenerateDataCommandTests.cs`:
```csharp
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
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~CliTests|FullyQualifiedName~GenerateDataCommandTests"`
Expected: build FAILS (`Parity.Cli` types not found).

- [ ] **Step 5: Write the implementation**

`src/Parity.Cli/CliArgs.cs`:
```csharp
using Parity.Core;

namespace Parity.Cli;

public static class ExitCodes
{
    public const int Success = 0;
    public const int NotSwitchable = 1;
    public const int ConfigurationOrData = 2;
}

public sealed record CliArgs(string Command, IReadOnlyList<string> Positionals, IReadOnlyDictionary<string, string?> Options)
{
    private static readonly HashSet<string> Flags = ["--all", "--quirk-fixed"];
    private static readonly HashSet<string> Valued = ["--profile", "--scope", "--out", "--query", "--corpus-commit"];

    public static CliArgs Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            return new CliArgs("help", [], new Dictionary<string, string?>());
        }

        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        var positionals = new List<string>();
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (Flags.Contains(arg))
            {
                options[arg] = null;
            }
            else if (Valued.Contains(arg))
            {
                if (i + 1 >= args.Length)
                {
                    throw new ParityConfigurationException($"Option {arg} needs a value.");
                }

                options[arg] = args[++i];
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ParityConfigurationException($"Unknown option {arg}. Run with --help for usage.");
            }
            else
            {
                positionals.Add(arg);
            }
        }

        return new CliArgs(args[0], positionals, options);
    }

    public bool Has(string option) => Options.ContainsKey(option);

    public string? Get(string option) => Options.TryGetValue(option, out var value) ? value : null;
}
```

`src/Parity.Cli/AppConfig.cs`:
```csharp
using Microsoft.Extensions.Configuration;
using Parity.Core.Settings;

namespace Parity.Cli;

public static class AppConfig
{
    /// <summary>
    /// appsettings.json in the sample root, then user secrets, then PARITY_-prefixed environment
    /// variables (for example PARITY_Profiles__postgres__ConnectionString), then <paramref name="overrides"/>.
    /// OPENAI_API_KEY fills OpenAI:ApiKey when nothing else set it.
    /// </summary>
    public static ParitySettings Load(string sampleRoot, IDictionary<string, string?>? overrides = null, Func<string, string?>? environment = null)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(sampleRoot)
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets(typeof(AppConfig).Assembly, optional: true)
            .AddEnvironmentVariables(prefix: "PARITY_");
        if (overrides is not null)
        {
            builder.AddInMemoryCollection(overrides);
        }

        var settings = builder.Build().Get<ParitySettings>() ?? new ParitySettings();
        if (string.IsNullOrWhiteSpace(settings.OpenAI.ApiKey))
        {
            settings.OpenAI.ApiKey = (environment ?? Environment.GetEnvironmentVariable)("OPENAI_API_KEY");
        }

        return settings;
    }
}
```

`src/Parity.Cli/Commands/GenerateDataCommand.cs`:
```csharp
using Parity.Core;
using Parity.Core.Corpus;
using Parity.Core.Data;
using Parity.Core.Embeddings;
using Parity.Core.Production;

namespace Parity.Cli.Commands;

public sealed class GenerateDataCommand(TextWriter output, string sampleRoot, Func<string, IDocsSource> docsSourceFactory, IEmbeddingClient? embedder)
{
    public async Task<int> RunAsync(CliArgs args, CancellationToken ct)
    {
        if (embedder is null)
        {
            throw new ParityConfigurationException(
                "generate-data needs an OpenAI API key. Set OPENAI_API_KEY, or run `dotnet user-secrets set OpenAI:ApiKey <key>` in src/Parity.Cli.");
        }

        var docs = docsSourceFactory(args.Get("--corpus-commit") ?? ProductionSources.Commit);
        var edgeCases = DataStore.ReadEdgeCases(sampleRoot);
        var queries = DataStore.ReadQueries(sampleRoot);
        var files = docs.ReadDocs();
        var drafts = CorpusBuilder.Build(files, edgeCases);
        output.WriteLine($"Corpus {docs.ResolvedCommit[..8]}: {files.Count} files, {drafts.Count} chunks ({edgeCases.Count} edge cases), {queries.Count} queries.");

        var chunkVectors = await embedder.EmbedAsync(drafts.Select(d => d.Content).ToList(), ct);
        output.WriteLine($"Embedded {chunkVectors.Length} chunks with {embedder.Model}.");
        var queryVectors = await embedder.EmbedAsync(queries.Select(q => q.Text).ToList(), ct);

        var data = new GeneratedData(
            docs.ResolvedCommit,
            DateTimeOffset.UtcNow,
            embedder.Model,
            drafts.Select((d, i) => d.WithEmbedding(chunkVectors[i])).ToList(),
            queries,
            queryVectors,
            GroundTruth.Compute(queries, drafts),
            ProductionSources.Sha256AtCommit);
        DataStore.WriteAtomically(sampleRoot, data, ct);
        output.WriteLine($"Wrote data/ ({drafts.Count} chunk vectors, {queries.Count} query vectors).");
        return ExitCodes.Success;
    }
}
```

`src/Parity.Cli/CliApp.cs`:
```csharp
using Parity.Cli.Commands;
using Parity.Core;
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
    IDictionary<string, string?>? configOverrides = null)
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
                _ => throw new ParityConfigurationException($"Unknown command '{cli.Command}'. Commands: generate-data, seed, search, compare."),
            };
        }
        catch (ParityException ex)
        {
            error.WriteLine($"error: {ex.Message}");
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
```

`src/Parity.Cli/Program.cs`:
```csharp
using Parity.Cli;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    return await new CliApp(Console.Out, Console.Error).RunAsync(args, cts.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("cancelled");
    return ExitCodes.ConfigurationOrData;
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~CliTests|FullyQualifiedName~GenerateDataCommandTests"`
Expected: PASS (9 tests).

- [ ] **Step 7: Commit**

```bash
git add appsettings.json HybridSearchParity.sln src/Parity.Cli tests/Parity.Tests
git commit -m "feat(parity): add CLI with configuration and generate-data command"
```

---
### Task 8: Comparison primitives: ties, vector parity, text diffs, ranking metrics

**Files:**
- Create: `src/Parity.Core/Comparison/TieGroups.cs`, `Comparison/VectorParity.cs`, `Comparison/TextParity.cs`, `Comparison/RankingMetrics.cs`
- Create: `tests/Parity.Tests/Support/Hits.cs`
- Test: `tests/Parity.Tests/Unit/ComparisonPrimitivesTests.cs`

**Interfaces:**
- Consumes: `SearchHit`, `VectorHit`, `TextMatch`, `ChunkScore` (Task 2).
- Produces:
  - `TieGroups.Build<T>(IReadOnlyList<T> sortedDescending, Func<T,double> score, double tolerance) → List<List<T>>`
  - `VectorParityResult(bool Pass, string? Failure)`, with `Passed` and `Failed(string)`
  - `VectorParity.Compare(IReadOnlyList<VectorHit> baseline, IReadOnlyList<VectorHit> candidate, double scoreTolerance, double tieTolerance)` and `VectorParity.CompareHybridVectorScores(IReadOnlyList<SearchHit>, IReadOnlyList<SearchHit>, double scoreTolerance)`
  - `TextDiff(IReadOnlyList<Guid> BaselineOnly, IReadOnlyList<Guid> CandidateOnly)` with `IsEqual`, and `TextParity.Diff(IReadOnlyList<TextMatch>, IReadOnlyList<TextMatch>)`
  - `HybridPairMetrics(double OverlapAt5, bool SameTopOne)` and `PairEvaluation(HybridPairMetrics Metrics, IReadOnlyList<Guid> DifferingChunks)`
  - `RankingMetrics.TopN` (5), `RankingMetrics.HybridScore(ChunkScore, double vectorWeight, double textWeight)` and `RankingMetrics.EvaluatePair(baselineHits, candidateHits, baselineSideScores, candidateSideScores, tieTolerance)`

- [ ] **Step 1: Add the test helper**

`tests/Parity.Tests/Support/Hits.cs`:
```csharp
using Parity.Core.Ids;
using Parity.Core.Model;

namespace Parity.Tests.Support;

/// <summary>Builds results whose chunk IDs come from short readable keys ("a", "b", …).</summary>
public static class Hits
{
    public static Guid Id(string key) => DeterministicGuid.Create($"test:{key}");

    public static SearchHit Hit(string key, double score, double vectorScore = 0.5, double textScore = 0) =>
        new(Id(key), key, null, null, 0, "idx", vectorScore, textScore, score);

    public static VectorHit Vec(string key, double score) => new(Id(key), score);

    public static IReadOnlyDictionary<Guid, double> Scores(params (string Key, double Score)[] scores) =>
        scores.ToDictionary(s => Id(s.Key), s => s.Score);
}
```

- [ ] **Step 2: Write the failing tests**

`tests/Parity.Tests/Unit/ComparisonPrimitivesTests.cs`:
```csharp
using Parity.Core.Comparison;
using Parity.Core.Model;
using static Parity.Tests.Support.Hits;

namespace Parity.Tests.Unit;

public class ComparisonPrimitivesTests
{
    private const double Tol = 1e-5;
    private const double Tie = 1e-6;

    [Fact]
    public void TieGroupsJoinScoresWithinToleranceOfTheGroupsFirstItem()
    {
        var groups = TieGroups.Build(new[] { 1.0, 1.0 - 5e-7, 0.9 }, s => s, Tie);
        Assert.Equal(new[] { 2, 1 }, groups.Select(g => g.Count));
    }

    [Fact]
    public void IdenticalVectorResultsPass() =>
        Assert.True(VectorParity.Compare([Vec("a", 0.9), Vec("b", 0.8)], [Vec("a", 0.9), Vec("b", 0.8)], Tol, Tie).Pass);

    [Fact]
    public void ReorderingTiedVectorHitsPasses() =>
        Assert.True(VectorParity.Compare([Vec("a", 0.9), Vec("b", 0.9), Vec("c", 0.5)], [Vec("b", 0.9), Vec("a", 0.9), Vec("c", 0.5)], Tol, Tie).Pass);

    [Fact]
    public void ReorderingUntiedVectorHitsFails()
    {
        var result = VectorParity.Compare([Vec("a", 0.9), Vec("b", 0.8), Vec("c", 0.5)], [Vec("b", 0.9), Vec("a", 0.8), Vec("c", 0.5)], Tol, Tie);
        Assert.False(result.Pass);
        Assert.Contains("position 1", result.Failure);
    }

    [Fact]
    public void ScoresOutsideToleranceFail()
    {
        var result = VectorParity.Compare([Vec("a", 0.9)], [Vec("a", 0.9 + 2e-5)], Tol, Tie);
        Assert.False(result.Pass);
        Assert.Contains(Id("a").ToString()[..8], result.Failure);
    }

    [Fact]
    public void ScoresWithinToleranceAcrossEnginesPass() =>
        Assert.True(VectorParity.Compare([Vec("a", 0.9)], [Vec("a", 0.9 + 4e-6)], Tol, Tie).Pass);

    [Fact]
    public void DifferentCountsFail() =>
        Assert.False(VectorParity.Compare([Vec("a", 0.9)], [], Tol, Tie).Pass);

    [Fact]
    public void ADifferentButTiedChunkAtTheCutOffPasses() =>
        Assert.True(VectorParity.Compare([Vec("a", 0.9), Vec("b", 0.5)], [Vec("a", 0.9), Vec("z", 0.5)], Tol, Tie).Pass);

    [Fact]
    public void ADifferentUntiedChunkAtTheCutOffFails() =>
        Assert.False(VectorParity.Compare([Vec("a", 0.9), Vec("b", 0.5)], [Vec("a", 0.9), Vec("z", 0.4)], Tol, Tie).Pass);

    [Fact]
    public void HybridVectorScoresMustAgreeForSharedChunks()
    {
        Assert.True(VectorParity.CompareHybridVectorScores([Hit("a", 0.9, vectorScore: 0.7)], [Hit("a", 0.8, vectorScore: 0.7 + 1e-6), Hit("b", 0.5)], Tol).Pass);
        Assert.False(VectorParity.CompareHybridVectorScores([Hit("a", 0.9, vectorScore: 0.7)], [Hit("a", 0.9, vectorScore: 0.71)], Tol).Pass);
    }

    [Fact]
    public void TextDiffListsEachSidesExtras()
    {
        var diff = TextParity.Diff([new TextMatch(Id("a"), 0.1), new TextMatch(Id("b"), 0.1)], [new TextMatch(Id("b"), 0.2), new TextMatch(Id("c"), 0.2)]);
        Assert.Equal(new[] { Id("a") }, diff.BaselineOnly);
        Assert.Equal(new[] { Id("c") }, diff.CandidateOnly);
        Assert.False(diff.IsEqual);
        Assert.True(TextParity.Diff([new TextMatch(Id("a"), 0.1)], [new TextMatch(Id("a"), 0.9)]).IsEqual);
    }

    private static SearchHit[] Five(string last, double lastScore) =>
        [Hit("a", 0.9), Hit("b", 0.8), Hit("c", 0.7), Hit("d", 0.6), Hit(last, lastScore)];

    [Fact]
    public void IdenticalHybridListsHaveFullOverlapAndNoDifferences()
    {
        var evaluation = RankingMetrics.EvaluatePair(Five("e", 0.5), Five("e", 0.5), Scores(), Scores(), Tie);
        Assert.Equal(new HybridPairMetrics(1.0, true), evaluation.Metrics);
        Assert.Empty(evaluation.DifferingChunks);
    }

    [Fact]
    public void ASwappedChunkLowersOverlapAndBothChunksDiffer()
    {
        var evaluation = RankingMetrics.EvaluatePair(Five("e", 0.5), Five("f", 0.55), Scores(("f", 0.45)), Scores(("e", 0.5)), Tie);
        Assert.Equal(0.8, evaluation.Metrics.OverlapAt5, 9);
        Assert.Equal(new[] { Id("e"), Id("f") }, evaluation.DifferingChunks);
    }

    [Fact]
    public void AChunkTiedWithTheOtherSidesFifthHitIsNotADifference()
    {
        var evaluation = RankingMetrics.EvaluatePair(Five("e", 0.5), Five("f", 0.5), Scores(("f", 0.5)), Scores(("e", 0.5)), Tie);
        Assert.Equal(1.0, evaluation.Metrics.OverlapAt5);
        Assert.Empty(evaluation.DifferingChunks);
    }

    [Fact]
    public void ReorderingWithinATieIsNotADifference()
    {
        var sides = Scores(("a", 0.9), ("b", 0.9));
        var evaluation = RankingMetrics.EvaluatePair([Hit("a", 0.9), Hit("b", 0.9)], [Hit("b", 0.9), Hit("a", 0.9)], sides, sides, Tie);
        Assert.True(evaluation.Metrics.SameTopOne);
        Assert.Empty(evaluation.DifferingChunks);
    }

    [Fact]
    public void ReorderingOutsideATieMakesBothChunksDiffer()
    {
        var evaluation = RankingMetrics.EvaluatePair([Hit("a", 0.9), Hit("b", 0.8)], [Hit("b", 0.9), Hit("a", 0.8)], Scores(("b", 0.8)), Scores(("a", 0.8)), Tie);
        Assert.False(evaluation.Metrics.SameTopOne);
        Assert.Equal(new[] { Id("a"), Id("b") }.Order(), evaluation.DifferingChunks.Order());
    }

    [Fact]
    public void EmptyAndShortListsUseTheLargerCountAsDenominator()
    {
        Assert.Equal(new HybridPairMetrics(1.0, true), RankingMetrics.EvaluatePair([], [], Scores(), Scores(), Tie).Metrics);
        Assert.Equal(new HybridPairMetrics(0.0, false), RankingMetrics.EvaluatePair([Hit("a", 0.9)], [], Scores(), Scores(), Tie).Metrics);
        Assert.Equal(0.5, RankingMetrics.EvaluatePair([Hit("a", 0.9), Hit("b", 0.8)], [Hit("a", 0.9)], Scores(), Scores(), Tie).Metrics.OverlapAt5);
    }

    [Fact]
    public void SameTopOneAcceptsATieOnTheBaselineSide()
    {
        // b scores just under a on the baseline side, but within the tie tolerance.
        var evaluation = RankingMetrics.EvaluatePair([Hit("a", 0.9), Hit("b", 0.9 - 5e-7)], [Hit("b", 0.9), Hit("a", 0.9)], Scores(("b", 0.9 - 5e-7)), Scores(("a", 0.9)), Tie);
        Assert.True(evaluation.Metrics.SameTopOne);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ComparisonPrimitivesTests"`
Expected: build FAILS (`Parity.Core.Comparison` not found).

- [ ] **Step 4: Write the implementation**

`src/Parity.Core/Comparison/TieGroups.cs`:
```csharp
namespace Parity.Core.Comparison;

public static class TieGroups
{
    /// <summary>
    /// Groups consecutive items (already sorted by score, descending) whose score is within
    /// <paramref name="tolerance"/> of the group's first item. Order within a group is arbitrary.
    /// </summary>
    public static List<List<T>> Build<T>(IReadOnlyList<T> sortedDescending, Func<T, double> score, double tolerance)
    {
        var groups = new List<List<T>>();
        foreach (var item in sortedDescending)
        {
            if (groups.Count > 0 && Math.Abs(score(groups[^1][0]) - score(item)) <= tolerance)
            {
                groups[^1].Add(item);
            }
            else
            {
                groups.Add([item]);
            }
        }

        return groups;
    }
}
```

`src/Parity.Core/Comparison/VectorParity.cs`:
```csharp
using System.Globalization;
using Parity.Core.Model;

namespace Parity.Core.Comparison;

public sealed record VectorParityResult(bool Pass, string? Failure)
{
    public static readonly VectorParityResult Passed = new(true, null);

    public static VectorParityResult Failed(string reason) => new(false, reason);
}

/// <summary>Spec section 7.1, rule 1: vector parity must pass.</summary>
public static class VectorParity
{
    public static VectorParityResult Compare(IReadOnlyList<VectorHit> baseline, IReadOnlyList<VectorHit> candidate, double scoreTolerance, double tieTolerance)
    {
        if (baseline.Count != candidate.Count)
        {
            return VectorParityResult.Failed($"result count differs: {baseline.Count} vs {candidate.Count}");
        }

        var groups = TieGroups.Build(baseline, h => h.Score, tieTolerance);
        var position = 0;
        for (var g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            var slice = candidate.Skip(position).Take(group.Count).ToList();
            if (g < groups.Count - 1)
            {
                var expected = group.Select(h => h.ChunkId).ToHashSet();
                if (!slice.All(h => expected.Contains(h.ChunkId)))
                {
                    return VectorParityResult.Failed(
                        $"position {position + 1}{(group.Count > 1 ? $"-{position + group.Count}" : "")}: expected {Ids(group)}, got {Ids(slice)}");
                }
            }
            else
            {
                // The last group may be cut off by k differently on each engine; it only has to be tied.
                var anchor = group[0].Score;
                if (slice.Any(h => Math.Abs(h.Score - anchor) > tieTolerance + scoreTolerance))
                {
                    return VectorParityResult.Failed($"position {position + 1}: expected scores tied at {Format(anchor)}, got {Ids(slice)}");
                }
            }

            position += group.Count;
        }

        var candidateScores = candidate.ToDictionary(h => h.ChunkId, h => h.Score);
        foreach (var hit in baseline)
        {
            if (candidateScores.TryGetValue(hit.ChunkId, out var other) && Math.Abs(other - hit.Score) > scoreTolerance)
            {
                return VectorParityResult.Failed($"chunk {Short(hit.ChunkId)}: score {Format(hit.Score)} vs {Format(other)}");
            }
        }

        return VectorParityResult.Passed;
    }

    public static VectorParityResult CompareHybridVectorScores(IReadOnlyList<SearchHit> baseline, IReadOnlyList<SearchHit> candidate, double scoreTolerance)
    {
        var candidateScores = candidate.ToDictionary(h => h.ChunkId, h => h.VectorScore);
        foreach (var hit in baseline)
        {
            if (candidateScores.TryGetValue(hit.ChunkId, out var other) && Math.Abs(other - hit.VectorScore) > scoreTolerance)
            {
                return VectorParityResult.Failed($"hybrid hit {Short(hit.ChunkId)}: vector score {Format(hit.VectorScore)} vs {Format(other)}");
            }
        }

        return VectorParityResult.Passed;
    }

    private static string Ids<T>(IEnumerable<T> hits) where T : notnull =>
        "{" + string.Join(", ", hits.Select(h => h switch
        {
            VectorHit v => $"{Short(v.ChunkId)}={Format(v.Score)}",
            _ => h.ToString(),
        })) + "}";

    private static string Short(Guid id) => id.ToString()[..8];

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
```

`src/Parity.Core/Comparison/TextParity.cs`:
```csharp
using Parity.Core.Model;

namespace Parity.Core.Comparison;

public sealed record TextDiff(IReadOnlyList<Guid> BaselineOnly, IReadOnlyList<Guid> CandidateOnly)
{
    public bool IsEqual => BaselineOnly.Count == 0 && CandidateOnly.Count == 0;
}

/// <summary>Spec section 7.2, rule 2: the two match sets must be equal.</summary>
public static class TextParity
{
    public static TextDiff Diff(IReadOnlyList<TextMatch> baseline, IReadOnlyList<TextMatch> candidate)
    {
        var a = baseline.Select(m => m.ChunkId).ToHashSet();
        var b = candidate.Select(m => m.ChunkId).ToHashSet();
        return new TextDiff(a.Except(b).Order().ToList(), b.Except(a).Order().ToList());
    }
}
```

`src/Parity.Core/Comparison/RankingMetrics.cs`:
```csharp
using Parity.Core.Model;

namespace Parity.Core.Comparison;

public sealed record HybridPairMetrics(double OverlapAt5, bool SameTopOne);

public sealed record PairEvaluation(HybridPairMetrics Metrics, IReadOnlyList<Guid> DifferingChunks);

/// <summary>Spec section 7.3, rule 3: overlap@5, same #1, and which chunks differ.</summary>
public static class RankingMetrics
{
    public const int TopN = 5;

    public static double HybridScore(ChunkScore score, double vectorWeight, double textWeight) =>
        (vectorWeight * score.VectorScore) + (textWeight * score.TextScore);

    /// <param name="baselineSideScores">Hybrid score on the baseline engine for every chunk in either top 5.</param>
    /// <param name="candidateSideScores">Hybrid score on the candidate engine for every chunk in either top 5.</param>
    public static PairEvaluation EvaluatePair(
        IReadOnlyList<SearchHit> baseline,
        IReadOnlyList<SearchHit> candidate,
        IReadOnlyDictionary<Guid, double> baselineSideScores,
        IReadOnlyDictionary<Guid, double> candidateSideScores,
        double tieTolerance)
    {
        var a = baseline.Take(TopN).ToList();
        var b = candidate.Take(TopN).ToList();
        var idsA = a.Select(h => h.ChunkId).ToHashSet();
        var idsB = b.Select(h => h.ChunkId).ToHashSet();

        bool TiesWithFifth(IReadOnlyList<SearchHit> list, IReadOnlyDictionary<Guid, double> sideScores, Guid id) =>
            list.Count == TopN && sideScores.TryGetValue(id, out var s) && Math.Abs(s - list[TopN - 1].Score) <= tieTolerance;

        var denominator = Math.Max(a.Count, b.Count);
        var overlap = denominator == 0
            ? 1.0
            : a.Count(h => idsB.Contains(h.ChunkId) || TiesWithFifth(b, candidateSideScores, h.ChunkId)) / (double)denominator;

        var sameTop = (a.Count, b.Count) switch
        {
            (0, 0) => true,
            (0, _) or (_, 0) => false,
            _ => a[0].ChunkId == b[0].ChunkId
                 || (baselineSideScores.TryGetValue(b[0].ChunkId, out var s) && Math.Abs(s - a[0].Score) <= tieTolerance),
        };

        var differing = new List<Guid>();
        differing.AddRange(a.Where(h => !idsB.Contains(h.ChunkId) && !TiesWithFifth(b, candidateSideScores, h.ChunkId)).Select(h => h.ChunkId));
        differing.AddRange(b.Where(h => !idsA.Contains(h.ChunkId) && !TiesWithFifth(a, baselineSideScores, h.ChunkId)).Select(h => h.ChunkId));
        foreach (var hit in a.Where(h => idsB.Contains(h.ChunkId)))
        {
            var other = b.First(x => x.ChunkId == hit.ChunkId);
            if (StrictlyAbove(a, hit.Score, tieTolerance) != StrictlyAbove(b, other.Score, tieTolerance))
            {
                differing.Add(hit.ChunkId);
            }
        }

        return new PairEvaluation(new HybridPairMetrics(overlap, sameTop), differing);
    }

    // Rank as "how many hits score strictly higher", which ignores reordering inside a tie.
    private static int StrictlyAbove(IReadOnlyList<SearchHit> list, double score, double tolerance) =>
        list.Count(x => x.Score > score + tolerance);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ComparisonPrimitivesTests"`
Expected: PASS (18 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Parity.Core/Comparison tests/Parity.Tests/Support/Hits.cs tests/Parity.Tests/Unit/ComparisonPrimitivesTests.cs
git commit -m "feat(parity): add tie-aware vector parity, text diffs and ranking metrics"
```

---

### Task 9: Difference explanations, known differences and the verdict

**Files:**
- Create: `src/Parity.Core/Comparison/PairResult.cs`, `Comparison/DifferenceClassifier.cs`, `Comparison/KnownDifferences.cs`, `Comparison/Verdict.cs`
- Test: `tests/Parity.Tests/Unit/VerdictTests.cs`

**Interfaces:**
- Consumes: Task 8 types, `ChunkScore`, `SearchHit`, `EngineError`, `Token` (Task 2), `ThresholdSettings` (Task 2), `ParityJson` (Task 4).
- Produces:
  - `DifferenceCategory { ScoredDifferently, MatchedOneSide, TopNCut, TopNBoundaryTie, Displaced, Unexplained }`
  - `RankDifference(Guid ChunkId, int? BaselineRank, int? CandidateRank, DifferenceCategory Category, string Detail)`
  - `DifferenceClassifier.Classify(ChunkScore baseline, ChunkScore candidate, double vectorTolerance, double tieTolerance)` and `DifferenceClassifier.Explain(differing, baselineScores, candidateScores, baselineHits, candidateHits, vectorTolerance, tieTolerance)`
  - `QueryRef(string Id, string Group, string Text)`
  - `ErrorOutcome(EngineError? Baseline, EngineError? Candidate)` with `Side`, `IsOneSided` and `Code`
  - `SnippetEvidence(Guid ChunkId, string Side, string Snippet, IReadOnlyList<Token> BaselineTokens, IReadOnlyList<Token> CandidateTokens)`
  - `TextEvidence(IReadOnlyList<Token> BaselineQueryTokens, IReadOnlyList<Token> CandidateQueryTokens, IReadOnlyList<SnippetEvidence> Snippets)`
  - `PairResult(QueryRef Query, string Scope, VectorParityResult Vector, TextDiff? Text, ReviewStatus? TextReview, TextEvidence? Evidence, ErrorOutcome? Error, ReviewStatus? ErrorReview, HybridPairMetrics? Hybrid, IReadOnlyList<RankDifference> Differences, IReadOnlyList<SearchHit> BaselineHits, IReadOnlyList<SearchHit> CandidateHits)`
  - `KnownDifference(string QueryId, string Scope, string Kind, IReadOnlyList<Guid>? BaselineOnly, IReadOnlyList<Guid>? CandidateOnly, string? ErrorSide, string? ErrorCode, string Category, string Reason, string Decision, string? ReviewedBy, string? ReviewedOn)`
  - `ReviewOutcome { Unreviewed, Acceptable, Blocker, SnapshotMismatch }` and `ReviewStatus(ReviewOutcome Outcome, KnownDifference? Entry)` with `IsOpen`
  - `KnownDifferences.Load(string path)`, `ReviewText(entries, queryId, scope, TextDiff)`, `ReviewError(entries, queryId, scope, ErrorOutcome)` and `FindStale(entries, pairs)`
  - `VerdictCondition(string Name, bool Pass, string Value)`, `VerdictResult(bool Switchable, IReadOnlyList<VerdictCondition> Conditions)` and `Verdict.Evaluate(IReadOnlyList<PairResult>, ThresholdSettings)`

- [ ] **Step 1: Write the failing tests**

`tests/Parity.Tests/Unit/VerdictTests.cs`:
```csharp
using Parity.Core;
using Parity.Core.Comparison;
using Parity.Core.Model;
using Parity.Core.Settings;
using static Parity.Tests.Support.Hits;

namespace Parity.Tests.Unit;

public sealed class VerdictTests : IDisposable
{
    private const double Tol = 1e-5;
    private const double Tie = 1e-6;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"parity-known-{Guid.NewGuid():N}");

    public VerdictTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ChunkScore Score(double vector, double text, bool matched, bool inCandidates, double? raw = null, double? nth = null) =>
        new(Id("x"), vector, text, matched, raw, inCandidates, nth);

    private static IReadOnlyDictionary<Guid, ChunkScore> Map(params ChunkScore[] scores) => scores.ToDictionary(s => s.ChunkId);

    [Fact]
    public void ClassifiesEachCause()
    {
        Assert.Equal(DifferenceCategory.ScoredDifferently, DifferenceClassifier.Classify(Score(0.5, 0.4, true, true), Score(0.5, 0.2, true, true), Tol, Tie));
        Assert.Equal(DifferenceCategory.MatchedOneSide, DifferenceClassifier.Classify(Score(0.5, 0.4, true, true), Score(0.5, 0, false, false), Tol, Tie));
        Assert.Equal(DifferenceCategory.TopNCut, DifferenceClassifier.Classify(Score(0.5, 0.4, true, true, 0.4), Score(0.5, 0, true, false, 0.3, 0.35), Tol, Tie));
        Assert.Equal(DifferenceCategory.TopNBoundaryTie, DifferenceClassifier.Classify(Score(0.5, 0.4, true, true, 0.4), Score(0.5, 0, true, false, 0.35, 0.35), Tol, Tie));
        Assert.Equal(DifferenceCategory.Displaced, DifferenceClassifier.Classify(Score(0.5, 0.4, true, true), Score(0.5, 0.4, true, true), Tol, Tie));
        Assert.Equal(DifferenceCategory.Unexplained, DifferenceClassifier.Classify(Score(0.5, 0.4, true, true), Score(0.6, 0.4, true, true), Tol, Tie));
    }

    [Fact]
    public void DisplacementIsExplainedOnlyWhenAnotherDifferenceCausesIt()
    {
        var hitsA = new[] { Hit("a", 0.9), Hit("d", 0.5) };
        var hitsB = new[] { Hit("c", 0.95), Hit("a", 0.9) };
        var same = Score(0.5, 0.4, true, true) with { ChunkId = Id("a") };
        var cause = (Score(0.5, 0, false, false) with { ChunkId = Id("c") }, Score(0.5, 0.9, true, true) with { ChunkId = Id("c") });

        var alone = DifferenceClassifier.Explain([Id("a")], Map(same), Map(same), hitsA, hitsB, Tol, Tie);
        Assert.Equal(DifferenceCategory.Unexplained, Assert.Single(alone).Category);

        var withCause = DifferenceClassifier.Explain(
            [Id("a"), Id("c")],
            Map(same, cause.Item1),
            Map(same, cause.Item2),
            hitsA, hitsB, Tol, Tie);
        Assert.Equal(new[] { DifferenceCategory.Displaced, DifferenceCategory.MatchedOneSide }, withCause.Select(d => d.Category));
        Assert.Equal((1, 2), (withCause[0].BaselineRank, withCause[0].CandidateRank));
    }

    private string WriteKnown(string json)
    {
        var path = Path.Combine(_dir, "known-differences.json");
        File.WriteAllText(path, json);
        return path;
    }

    private string Entry(string decision, params Guid[] candidateOnly) => $$"""
        [ { "queryId": "Q19", "scope": "P2", "kind": "text-match", "baselineOnly": [],
            "candidateOnly": [{{string.Join(",", candidateOnly.Select(g => $"\"{g}\""))}}],
            "category": "tokenization", "reason": "hyphens", "decision": "{{decision}}" } ]
        """;

    [Fact]
    public void ReviewMatchesTheExactSnapshot()
    {
        var diff = new TextDiff([], [Id("b"), Id("a")]);
        Assert.Equal(ReviewOutcome.Unreviewed, KnownDifferences.ReviewText([], "Q19", "P2", diff).Outcome);
        Assert.Equal(ReviewOutcome.Acceptable, KnownDifferences.ReviewText(KnownDifferences.Load(WriteKnown(Entry("acceptable", Id("a"), Id("b")))), "Q19", "P2", diff).Outcome);
        Assert.Equal(ReviewOutcome.Blocker, KnownDifferences.ReviewText(KnownDifferences.Load(WriteKnown(Entry("blocker", Id("a"), Id("b")))), "Q19", "P2", diff).Outcome);
        Assert.Equal(ReviewOutcome.SnapshotMismatch, KnownDifferences.ReviewText(KnownDifferences.Load(WriteKnown(Entry("acceptable", Id("a")))), "Q19", "P2", diff).Outcome);
    }

    [Fact]
    public void ErrorEntriesMatchSideAndCode()
    {
        var entries = KnownDifferences.Load(WriteKnown("""
            [ { "queryId": "Q36", "scope": "P1", "kind": "error", "errorSide": "baseline", "errorCode": "SQL7619",
                "category": "error-handling", "reason": "punctuation only", "decision": "acceptable" } ]
            """));
        Assert.Equal(ReviewOutcome.Acceptable, KnownDifferences.ReviewError(entries, "Q36", "P1", new ErrorOutcome(new EngineError("SQL7619", "x"), null))!.Outcome);
        Assert.Equal(ReviewOutcome.SnapshotMismatch, KnownDifferences.ReviewError(entries, "Q36", "P1", new ErrorOutcome(null, new EngineError("PG42601", "x")))!.Outcome);
        Assert.Null(KnownDifferences.ReviewError(entries, "Q36", "P1", new ErrorOutcome(new EngineError("a", "x"), new EngineError("b", "y"))));
    }

    [Fact]
    public void MissingFileMeansNoEntriesAndInvalidValuesAreRejected()
    {
        Assert.Empty(KnownDifferences.Load(Path.Combine(_dir, "absent.json")));
        var error = Assert.Throws<ParityConfigurationException>(() => KnownDifferences.Load(WriteKnown(Entry("maybe"))));
        Assert.Contains("decision 'maybe'", error.Message);
    }

    private static PairResult Pair(
        string query = "Q01",
        bool vectorPass = true,
        TextDiff? text = null,
        ReviewStatus? textReview = null,
        ErrorOutcome? error = null,
        ReviewStatus? errorReview = null,
        HybridPairMetrics? hybrid = null,
        RankDifference[]? differences = null) =>
        new(new QueryRef(query, "exact", "q"), "P1", vectorPass ? VectorParityResult.Passed : VectorParityResult.Failed("x"),
            text, textReview, null, error, errorReview, error is null ? hybrid ?? new HybridPairMetrics(1.0, true) : null, differences ?? [], [], []);

    [Fact]
    public void AllConditionsPassingIsSwitchable()
    {
        var verdict = Verdict.Evaluate([Pair(), Pair(hybrid: new(0.8, true))], new ThresholdSettings());
        Assert.True(verdict.Switchable);
        Assert.Equal(5, verdict.Conditions.Count);
        Assert.Equal("0.900", verdict.Conditions[2].Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EachFailingConditionBlocksTheVerdict(int failing)
    {
        var pair = failing switch
        {
            0 => Pair(vectorPass: false),
            1 => Pair(text: new TextDiff([Id("a")], []), textReview: new ReviewStatus(ReviewOutcome.Unreviewed, null)),
            2 => Pair(hybrid: new HybridPairMetrics(0.6, true)),
            3 => Pair(hybrid: new HybridPairMetrics(1.0, false)),
            _ => Pair(differences: [new RankDifference(Id("a"), 1, null, DifferenceCategory.Unexplained, "x")]),
        };
        var verdict = Verdict.Evaluate([pair], new ThresholdSettings());
        Assert.False(verdict.Switchable);
        Assert.False(verdict.Conditions[failing].Pass);
        Assert.Equal(4, verdict.Conditions.Count(c => c.Pass));
    }

    [Fact]
    public void ErrorPairsAreLeftOutOfTheMetricsButMustBeReviewed()
    {
        var error = new ErrorOutcome(new EngineError("SQL7619", "x"), null);
        var accepted = Verdict.Evaluate([Pair(), Pair(error: error, errorReview: new ReviewStatus(ReviewOutcome.Acceptable, null))], new ThresholdSettings());
        Assert.True(accepted.Switchable);
        Assert.Equal("1.000", accepted.Conditions[2].Value);

        var open = Verdict.Evaluate([Pair(), Pair(error: error, errorReview: new ReviewStatus(ReviewOutcome.SnapshotMismatch, null))], new ThresholdSettings());
        Assert.False(open.Conditions[1].Pass);
    }

    [Fact]
    public void StaleEntriesAreThoseWithNoMatchingDifference()
    {
        var entries = KnownDifferences.Load(WriteKnown(Entry("acceptable", Id("a"))));
        Assert.Single(KnownDifferences.FindStale(entries, [Pair(query: "Q19")]));
        Assert.Empty(KnownDifferences.FindStale(entries, [Pair(query: "Q19", text: new TextDiff([], [Id("a")])) with { Scope = "P2" }]));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~VerdictTests"`
Expected: build FAILS (`DifferenceClassifier`, `KnownDifferences` and the others not found).

- [ ] **Step 3: Write the implementation**

`src/Parity.Core/Comparison/PairResult.cs`:
```csharp
using Parity.Core.Model;

namespace Parity.Core.Comparison;

public sealed record QueryRef(string Id, string Group, string Text);

public sealed record ErrorOutcome(EngineError? Baseline, EngineError? Candidate)
{
    public string Side => (Baseline, Candidate) switch
    {
        (not null, not null) => "both",
        (not null, null) => "baseline",
        (null, not null) => "candidate",
        _ => "none",
    };

    public bool IsOneSided => Side is "baseline" or "candidate";

    public string Code => (Baseline ?? Candidate)?.Code ?? string.Empty;
}

public sealed record SnippetEvidence(
    Guid ChunkId,
    string Side,
    string Snippet,
    IReadOnlyList<Token> BaselineTokens,
    IReadOnlyList<Token> CandidateTokens);

public sealed record TextEvidence(
    IReadOnlyList<Token> BaselineQueryTokens,
    IReadOnlyList<Token> CandidateQueryTokens,
    IReadOnlyList<SnippetEvidence> Snippets);

/// <summary>
/// Everything compare learned about one query in one scope. Hybrid is null when either engine
/// returned an error, which keeps the pair out of the overlap and same-#1 metrics.
/// </summary>
public sealed record PairResult(
    QueryRef Query,
    string Scope,
    VectorParityResult Vector,
    TextDiff? Text,
    ReviewStatus? TextReview,
    TextEvidence? Evidence,
    ErrorOutcome? Error,
    ReviewStatus? ErrorReview,
    HybridPairMetrics? Hybrid,
    IReadOnlyList<RankDifference> Differences,
    IReadOnlyList<SearchHit> BaselineHits,
    IReadOnlyList<SearchHit> CandidateHits);
```

`src/Parity.Core/Comparison/DifferenceClassifier.cs`:
```csharp
using System.Globalization;
using Parity.Core.Model;

namespace Parity.Core.Comparison;

public enum DifferenceCategory { ScoredDifferently, MatchedOneSide, TopNCut, TopNBoundaryTie, Displaced, Unexplained }

public sealed record RankDifference(Guid ChunkId, int? BaselineRank, int? CandidateRank, DifferenceCategory Category, string Detail);

/// <summary>Spec section 7.3: why a chunk's hybrid placement differs between the engines.</summary>
public static class DifferenceClassifier
{
    public static DifferenceCategory Classify(ChunkScore baseline, ChunkScore candidate, double vectorTolerance, double tieTolerance)
    {
        if (Math.Abs(baseline.VectorScore - candidate.VectorScore) > vectorTolerance)
        {
            return DifferenceCategory.Unexplained; // rule 1 should have caught this
        }

        if (Math.Abs(baseline.TextScore - candidate.TextScore) <= tieTolerance)
        {
            return DifferenceCategory.Displaced; // its own scores agree; something else moved it
        }

        if (baseline.Matched != candidate.Matched)
        {
            return DifferenceCategory.MatchedOneSide;
        }

        if (baseline.InCandidates != candidate.InCandidates)
        {
            var cutSide = baseline.InCandidates ? candidate : baseline;
            return cutSide.RawRank is double raw && cutSide.NthCandidateRank is double nth && Math.Abs(raw - nth) <= tieTolerance
                ? DifferenceCategory.TopNBoundaryTie
                : DifferenceCategory.TopNCut;
        }

        return baseline.InCandidates && candidate.InCandidates
            ? DifferenceCategory.ScoredDifferently
            : DifferenceCategory.Unexplained;
    }

    public static IReadOnlyList<RankDifference> Explain(
        IReadOnlyList<Guid> differing,
        IReadOnlyDictionary<Guid, ChunkScore> baselineScores,
        IReadOnlyDictionary<Guid, ChunkScore> candidateScores,
        IReadOnlyList<SearchHit> baselineHits,
        IReadOnlyList<SearchHit> candidateHits,
        double vectorTolerance,
        double tieTolerance)
    {
        var classified = differing.Select(id =>
        {
            var hasBoth = baselineScores.TryGetValue(id, out var b) & candidateScores.TryGetValue(id, out var c);
            var category = hasBoth ? Classify(b!, c!, vectorTolerance, tieTolerance) : DifferenceCategory.Unexplained;
            var detail = hasBoth ? Describe(b!, c!) : "no score from one engine";
            return (Id: id, Category: category, Detail: detail);
        }).ToList();

        var hasCause = classified.Any(x => x.Category is not (DifferenceCategory.Displaced or DifferenceCategory.Unexplained));
        return classified
            .Select(x => new RankDifference(
                x.Id,
                Rank(baselineHits, x.Id),
                Rank(candidateHits, x.Id),
                x.Category == DifferenceCategory.Displaced && !hasCause ? DifferenceCategory.Unexplained : x.Category,
                x.Detail))
            .ToList();
    }

    private static int? Rank(IReadOnlyList<SearchHit> hits, Guid id)
    {
        for (var i = 0; i < Math.Min(hits.Count, RankingMetrics.TopN); i++)
        {
            if (hits[i].ChunkId == id)
            {
                return i + 1;
            }
        }

        return null;
    }

    private static string Describe(ChunkScore b, ChunkScore c) => string.Create(
        CultureInfo.InvariantCulture,
        $"text {b.TextScore:F4} vs {c.TextScore:F4}; matched {b.Matched}/{c.Matched}; candidate {b.InCandidates}/{c.InCandidates}");
}
```

`src/Parity.Core/Comparison/KnownDifferences.cs`:
```csharp
using System.Text.Json;
using Parity.Core.Data;

namespace Parity.Core.Comparison;

public sealed record KnownDifference(
    string QueryId,
    string Scope,
    string Kind,
    IReadOnlyList<Guid>? BaselineOnly,
    IReadOnlyList<Guid>? CandidateOnly,
    string? ErrorSide,
    string? ErrorCode,
    string Category,
    string Reason,
    string Decision,
    string? ReviewedBy,
    string? ReviewedOn);

public enum ReviewOutcome { Unreviewed, Acceptable, Blocker, SnapshotMismatch }

public sealed record ReviewStatus(ReviewOutcome Outcome, KnownDifference? Entry)
{
    /// <summary>Anything other than an accepted, matching entry keeps the verdict from passing.</summary>
    public bool IsOpen => Outcome != ReviewOutcome.Acceptable;
}

/// <summary>Spec section 7.4: reviewed differences, matched only against their exact snapshot.</summary>
public static class KnownDifferences
{
    private static readonly HashSet<string> Kinds = ["text-match", "error"];
    private static readonly HashSet<string> Categories = ["tokenization", "stop-words", "punctuation", "accents", "error-handling", "other"];
    private static readonly HashSet<string> Decisions = ["acceptable", "blocker"];

    public static IReadOnlyList<KnownDifference> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        List<KnownDifference> entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<KnownDifference>>(File.ReadAllText(path), ParityJson.Options) ?? [];
        }
        catch (JsonException ex)
        {
            throw new ParityConfigurationException($"{Path.GetFileName(path)} isn't valid JSON: {ex.Message}", ex);
        }

        foreach (var e in entries)
        {
            if (!Kinds.Contains(e.Kind) || !Categories.Contains(e.Category) || !Decisions.Contains(e.Decision))
            {
                throw new ParityConfigurationException(
                    $"known-differences.json entry {e.QueryId}/{e.Scope} has kind '{e.Kind}', category '{e.Category}', decision '{e.Decision}'. " +
                    $"Valid kinds: {string.Join(", ", Kinds)}; categories: {string.Join(", ", Categories)}; decisions: {string.Join(", ", Decisions)}.");
            }
        }

        return entries;
    }

    public static ReviewStatus ReviewText(IReadOnlyList<KnownDifference> entries, string queryId, string scope, TextDiff diff)
    {
        var entry = entries.FirstOrDefault(e => e.Kind == "text-match" && e.QueryId == queryId && e.Scope == scope);
        if (entry is null)
        {
            return new ReviewStatus(ReviewOutcome.Unreviewed, null);
        }

        var matches = SameSet(entry.BaselineOnly, diff.BaselineOnly) && SameSet(entry.CandidateOnly, diff.CandidateOnly);
        return new ReviewStatus(matches ? Decided(entry) : ReviewOutcome.SnapshotMismatch, entry);
    }

    /// <summary>Null when no review is needed (no error, or both engines failed).</summary>
    public static ReviewStatus? ReviewError(IReadOnlyList<KnownDifference> entries, string queryId, string scope, ErrorOutcome error)
    {
        if (!error.IsOneSided)
        {
            return null;
        }

        var entry = entries.FirstOrDefault(e => e.Kind == "error" && e.QueryId == queryId && e.Scope == scope);
        if (entry is null)
        {
            return new ReviewStatus(ReviewOutcome.Unreviewed, null);
        }

        var matches = entry.ErrorSide == error.Side && entry.ErrorCode == error.Code;
        return new ReviewStatus(matches ? Decided(entry) : ReviewOutcome.SnapshotMismatch, entry);
    }

    /// <summary>Entries that no longer correspond to any observed difference. They're reported, never failing.</summary>
    public static IReadOnlyList<KnownDifference> FindStale(IReadOnlyList<KnownDifference> entries, IReadOnlyList<PairResult> pairs) =>
        entries.Where(e => !pairs.Any(p =>
                p.Query.Id == e.QueryId && p.Scope == e.Scope &&
                (e.Kind == "text-match" ? p.Text is { IsEqual: false } : p.Error is { IsOneSided: true })))
            .ToList();

    private static ReviewOutcome Decided(KnownDifference entry) =>
        entry.Decision == "acceptable" ? ReviewOutcome.Acceptable : ReviewOutcome.Blocker;

    private static bool SameSet(IReadOnlyList<Guid>? recorded, IReadOnlyList<Guid> observed) =>
        (recorded ?? Array.Empty<Guid>()).ToHashSet().SetEquals(observed);
}
```

`src/Parity.Core/Comparison/Verdict.cs`:
```csharp
using System.Globalization;
using Parity.Core.Settings;

namespace Parity.Core.Comparison;

public sealed record VerdictCondition(string Name, bool Pass, string Value);

public sealed record VerdictResult(bool Switchable, IReadOnlyList<VerdictCondition> Conditions);

/// <summary>Spec section 2: switchable only when all five conditions hold.</summary>
public static class Verdict
{
    public static VerdictResult Evaluate(IReadOnlyList<PairResult> pairs, ThresholdSettings thresholds)
    {
        var inv = CultureInfo.InvariantCulture;
        var vectorPasses = pairs.Count(p => p.Vector.Pass);
        var open = pairs.Count(p => (p.TextReview?.IsOpen ?? false) || (p.ErrorReview?.IsOpen ?? false));
        var counted = pairs.Where(p => p.Hybrid is not null).Select(p => p.Hybrid!).ToList();
        var meanOverlap = counted.Count == 0 ? 0.0 : counted.Average(m => m.OverlapAt5);
        var sameTopRate = counted.Count == 0 ? 0.0 : counted.Count(m => m.SameTopOne) / (double)counted.Count;
        var unexplained = pairs.Sum(p => p.Differences.Count(d => d.Category == DifferenceCategory.Unexplained));

        var conditions = new List<VerdictCondition>
        {
            new("Vector parity", vectorPasses == pairs.Count, $"{vectorPasses}/{pairs.Count} pairs"),
            new("Full-text and error differences reviewed and accepted", open == 0, $"{open} open"),
            new($"Mean top-5 overlap ≥ {thresholds.MinMeanTop5Overlap.ToString(inv)}", meanOverlap >= thresholds.MinMeanTop5Overlap, meanOverlap.ToString("F3", inv)),
            new($"Same #1 ≥ {(thresholds.MinSameTopOneRate * 100).ToString("0", inv)}%", sameTopRate >= thresholds.MinSameTopOneRate, $"{(sameTopRate * 100).ToString("0.0", inv)}%"),
            new("Zero unexplained differences", unexplained == 0, unexplained.ToString(inv)),
        };
        return new VerdictResult(conditions.All(c => c.Pass), conditions);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~VerdictTests"`
Expected: PASS (13 test cases).

- [ ] **Step 5: Commit**

```bash
git add src/Parity.Core/Comparison tests/Parity.Tests/Unit/VerdictTests.cs
git commit -m "feat(parity): explain hybrid differences, match reviewed differences, compute the verdict"
```

---
### Task 10: SQL Server store

**Files:**
- Create: `src/Parity.SqlServer/SqlServerParityDbContext.cs`, `src/Parity.SqlServer/SqlServerSchema.cs`, `src/Parity.SqlServer/SqlServerErrors.cs`, `src/Parity.SqlServer/SqlServerSearchStore.cs`
- Modify: `src/Parity.SqlServer/Sql/SqlServerSql.cs` (add `BuildQuirkFixedHybrid`)
- Create: `tests/Parity.Tests/Support/MiniCorpus.cs`, `tests/Parity.Tests/Integration/IntegrationFixture.cs`
- Modify: `tests/Parity.Tests/Parity.Tests.csproj` (add `Testcontainers.MsSql`)
- Test: `tests/Parity.Tests/Unit/SqlServerSqlTests.cs`, `tests/Parity.Tests/Integration/SqlServerStoreTests.cs`

**Interfaces:**
- Consumes: `ISearchStore`, `DocumentChunkEntity`, `EngineResult`, `HybridQuery`, `SearchHit` and the error types (Task 2); `SqlServerSql`, `ProductionEscaping` (Task 3); `ParityScopes`, `DeterministicGuid` (Tasks 1 and 5); `HashEmbedding` (Task 7).
- Produces:
  - `SqlServerParityDbContext.Create(string connectionString)`
  - `SqlServerSchema.CreateFullTextCatalog`, `SqlServerSchema.CreateFullTextIndex` and `SqlServerSchema.PopulationStatus`
  - `SqlServerErrors.Translate(SqlException, string profile, string host) → ParityException?` and `SqlServerErrors.Host(string connectionString)`
  - `SqlServerSearchStore(string profile, string connectionString, TimeSpan populationTimeout) : ISearchStore`, plus `internal Task<SqlConnection> OpenAsync(CancellationToken)`, made visible to tests and diagnostics through `InternalsVisibleTo`
  - `SqlServerSql.QuirkFixedTemplate` and `SqlServerSql.BuildQuirkFixedHybrid(float[] vector, string query, int candidateLimit)`
  - Tests: `MiniCorpus` (`Chunks`, `CreatedUtc`, `Id(key)`, `ScopeP1`, `ScopeP2`, `ScopeN1`, `Query(text, scope, filterBeforeTopN = false)`), `IntegrationFixture` (`SqlServerConnectionString(database)`, `MiniSqlServerAsync()`, `SeedOnceAsync(...)`) and `DatabaseCollection.Name`

- [ ] **Step 1: Add the test package and the mini corpus**

Add to the package references in `tests/Parity.Tests/Parity.Tests.csproj`:
```xml
    <PackageReference Include="Testcontainers.MsSql" />
```

`tests/Parity.Tests/Support/MiniCorpus.cs`:
```csharp
using Parity.Core.Corpus;
using Parity.Core.Ids;
using Parity.Core.Model;

namespace Parity.Tests.Support;

/// <summary>
/// 18 hand-made chunks across P1 and P2 for engine tests. The "alpha beta" chunks in P2 outrank
/// m17 (the only P1 match), so production's cross-project top-10 cut drops m17's text score.
/// </summary>
public static class MiniCorpus
{
    public static readonly Guid P1 = ParityScopes.ProjectId("P1");
    public static readonly Guid P2 = ParityScopes.ProjectId("P2");
    public static readonly DateTime CreatedUtc = new(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);

    public static Scope ScopeP1 { get; } = new(P1.ToString(), null);
    public static Scope ScopeP2 { get; } = new(P2.ToString(), null);
    public static Scope ScopeN1 { get; } = new(P1.ToString(), ParityScopes.NotebookN1);

    public static IReadOnlyList<ChunkRecord> Chunks { get; } = Build();

    public static Guid Id(string key) => DeterministicGuid.Create($"mini:{key}");

    public static HybridQuery Query(string text, Scope scope, bool filterBeforeTopN = false) =>
        new(text, HashEmbedding.Create(text), scope, MaxResults: 5, VectorWeight: 0.7, TextWeight: 0.3, CandidateMultiplier: 2, filterBeforeTopN);

    private static IReadOnlyList<ChunkRecord> Build()
    {
        var chunks = new List<ChunkRecord>
        {
            Make("m01", P1, null, "The conversation lock is held by the worker until the lease expires."),
            Make("m02", P1, ParityScopes.NotebookN1, "Conversation locks expire after five minutes."),
            Make("m03", P1, null, "Don't restart the container; it can't resume. O'Reilly Media wrote about it."),
            Make("m04", P1, null, "The smoke test should say \"hello world\" twice."),
            Make("m17", P1, null, "A single alpha beta mention in project one, surrounded by many other words that dilute this chunk considerably."),
            Make("m18", P2, null, "Vectors only: nothing lexical here."),
        };
        for (var i = 5; i <= 16; i++)
        {
            chunks.Add(Make($"m{i:00}", P2, null, $"alpha beta alpha beta alpha beta topic {i}"));
        }

        return chunks;
    }

    private static ChunkRecord Make(string key, Guid project, Guid? notebook, string content) =>
        new(Id(key), project.ToString(), project, notebook, $"mini/{key}", DeterministicGuid.Create($"mini-file:{key}"), $"mini/{key}", 0, ChunkKind.Doc, content, HashEmbedding.Create(content));
}
```

- [ ] **Step 2: Write the failing unit test for the quirk-fixed SQL**

`tests/Parity.Tests/Unit/SqlServerSqlTests.cs`:
```csharp
using Parity.SqlServer.Sql;

namespace Parity.Tests.Unit;

public class SqlServerSqlTests
{
    [Fact]
    public void QuirkFixedSqlFiltersByScopeBeforeTakingTheTopCandidates()
    {
        var sql = SqlServerSql.BuildQuirkFixedHybrid([0.5f], "alpha beta", 10);
        Assert.Contains("SELECT TOP (10) ct.[KEY], ct.[RANK]", sql);
        Assert.Contains("FROM CONTAINSTABLE(DocumentChunks, Content, '\"alpha beta\"') ct", sql);
        Assert.Contains("WHERE scoped.IndexName = @indexName", sql);
        Assert.DoesNotContain("'\"alpha beta\"', 10)", sql);
    }

    [Fact]
    public void ProductionTemplateContainsTheJoinTheVariantReplacesExactlyOnce()
    {
        var count = SqlServerSql.ProductionTemplate.Split(SqlServerSql.ProductionCandidateJoin).Length - 1;
        Assert.Equal(1, count);
    }
}
```

Run: `dotnet test --filter "FullyQualifiedName~SqlServerSqlTests"`
Expected: build FAILS (`BuildQuirkFixedHybrid` not found).

- [ ] **Step 3: Add the quirk-fixed variant to `SqlServerSql.cs`**

Add these members inside `public static class SqlServerSql`, **after** the `ProductionTemplate` property. Static initializers run in declaration order, and `QuirkFixedTemplate` reads `ProductionTemplate`:
```csharp
    /// <summary>The production line that picks the top-N text candidates across every project.</summary>
    public const string ProductionCandidateJoin = "LEFT JOIN CONTAINSTABLE(DocumentChunks, Content, '{escapedFtQuery}', {candidateLimit}) ft";

    private const string ScopedCandidateJoin = """
        LEFT JOIN (
                            SELECT TOP ({candidateLimit}) ct.[KEY], ct.[RANK]
                            FROM CONTAINSTABLE(DocumentChunks, Content, '{escapedFtQuery}') ct
                            INNER JOIN DocumentChunks scoped ON scoped.Id = ct.[KEY]
                            WHERE scoped.IndexName = @indexName
                                AND (@notebookId IS NULL OR scoped.NotebookId = @notebookId)
                            ORDER BY ct.[RANK] DESC
                        ) ft
        """;

    /// <summary>Informational only, never production: the scope filter is applied before the top-N cut.</summary>
    public static string QuirkFixedTemplate { get; } = ProductionTemplate.Replace(ProductionCandidateJoin, ScopedCandidateJoin);

    public static string BuildQuirkFixedHybrid(float[] vector, string query, int candidateLimit) =>
        Fill(QuirkFixedTemplate, vector, query, candidateLimit);
```

Run: `dotnet test --filter "FullyQualifiedName~SqlServerSqlTests"`
Expected: PASS (2 tests).

- [ ] **Step 4: Write the integration fixture and the failing store tests**

`tests/Parity.Tests/Integration/IntegrationFixture.cs`:
```csharp
using Microsoft.Data.SqlClient;
using Parity.Core.Contracts;
using Parity.Core.Model;
using Parity.Core.Settings;
using Parity.SqlServer;
using Parity.Tests.Support;
using Testcontainers.MsSql;

namespace Parity.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<IntegrationFixture>
{
    public const string Name = "databases";
}

/// <summary>
/// Starts the containers once per test run. Databases are seeded lazily, once per key. The SQL
/// Server image is amd64-only, so on Apple Silicon it runs emulated and takes a few minutes to start.
/// </summary>
public sealed class IntegrationFixture : IAsyncLifetime
{
    public static readonly TimeSpan PopulationTimeout = TimeSpan.FromMinutes(10);

    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder()
        .WithImage(new ImageSettings().SqlServer)
        .WithPassword("Parity_Passw0rd!")
        .Build();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, string> _seeded = new();

    public Task InitializeAsync() => _sqlServer.StartAsync();

    public async Task DisposeAsync() => await _sqlServer.DisposeAsync();

    public string SqlServerConnectionString(string database) =>
        new SqlConnectionStringBuilder(_sqlServer.GetConnectionString())
        {
            InitialCatalog = database,
            TrustServerCertificate = true,
            Encrypt = false,
        }.ConnectionString;

    public Task<string> MiniSqlServerAsync() => SeedOnceAsync(
        "mini-sqlserver",
        () => SqlServerConnectionString("parity_mini"),
        cs => new SqlServerSearchStore("mini-sqlserver", cs, PopulationTimeout),
        MiniCorpus.Chunks,
        MiniCorpus.CreatedUtc);

    /// <summary>Seeds <paramref name="chunks"/> into a fresh database the first time <paramref name="key"/> is asked for.</summary>
    public async Task<string> SeedOnceAsync(
        string key,
        Func<string> connectionString,
        Func<string, ISearchStore> createStore,
        IReadOnlyList<ChunkRecord> chunks,
        DateTime createdUtc)
    {
        await _gate.WaitAsync();
        try
        {
            if (_seeded.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var cs = connectionString();
            var store = createStore(cs);
            await store.EnsureSchemaAsync(CancellationToken.None);
            await store.LoadAsync(chunks, createdUtc, CancellationToken.None);
            await store.WaitUntilSearchableAsync(CancellationToken.None);
            if (store is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }

            _seeded[key] = cs;
            return cs;
        }
        finally
        {
            _gate.Release();
        }
    }
}
```

`tests/Parity.Tests/Integration/SqlServerStoreTests.cs`:
```csharp
using Parity.Core.Model;
using Parity.SqlServer;
using Parity.Tests.Support;

namespace Parity.Tests.Integration;

[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SqlServerStoreTests(IntegrationFixture fixture)
{
    private async Task<SqlServerSearchStore> StoreAsync() =>
        new("mini-sqlserver", await fixture.MiniSqlServerAsync(), IntegrationFixture.PopulationTimeout);

    private static SearchHit Hit(IReadOnlyList<SearchHit> hits, string key) => hits.Single(h => h.ChunkId == MiniCorpus.Id(key));

    [Fact]
    public async Task SeedingTwiceRecreatesCleanly()
    {
        var store = new SqlServerSearchStore("reseed", fixture.SqlServerConnectionString("parity_reseed"), IntegrationFixture.PopulationTimeout);
        for (var run = 0; run < 2; run++)
        {
            await store.EnsureSchemaAsync(CancellationToken.None);
            await store.LoadAsync(MiniCorpus.Chunks, MiniCorpus.CreatedUtc, CancellationToken.None);
            await store.WaitUntilSearchableAsync(CancellationToken.None);
        }

        var result = await store.HybridSearchAsync(MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.Count);
    }

    [Fact]
    public async Task PhraseMatchesWholeWordsOnly()
    {
        var result = await (await StoreAsync()).HybridSearchAsync(MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(Hit(result.Value, "m01").TextScore > 0);
        Assert.Equal(0, Hit(result.Value, "m02").TextScore); // "locks": no inflection in phrase search
        Assert.All(result.Value, h => Assert.Equal(0.7 * h.VectorScore + 0.3 * h.TextScore, h.Score, 9));
    }

    [Fact]
    public async Task NotebookScopeReturnsOnlyNotebookChunks()
    {
        var result = await (await StoreAsync()).HybridSearchAsync(MiniCorpus.Query("conversation", MiniCorpus.ScopeN1), CancellationToken.None);
        Assert.Equal(new[] { MiniCorpus.Id("m02") }, result.Value!.Select(h => h.ChunkId));
    }

    [Theory]
    [InlineData("O'Reilly Media")]
    [InlineData("don't restart")]
    [InlineData("say \"hello world\" twice")]
    [InlineData("the and of")]
    [InlineData("!!! ???")]
    public async Task AwkwardQueriesNeverThrow(string text)
    {
        var result = await (await StoreAsync()).HybridSearchAsync(MiniCorpus.Query(text, MiniCorpus.ScopeP1), CancellationToken.None);
        Assert.True(result.IsSuccess || result.Error is not null);
        if (text == "O'Reilly Media")
        {
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.True(Hit(result.Value, "m03").TextScore > 0);
        }
    }

    [Fact]
    public async Task ProductionTopNCutDropsTheInScopeMatchAndTheVariantKeepsIt()
    {
        var store = await StoreAsync();
        var asIs = await store.HybridSearchAsync(MiniCorpus.Query("alpha beta", MiniCorpus.ScopeP1), CancellationToken.None);
        var fixedVariant = await store.HybridSearchAsync(MiniCorpus.Query("alpha beta", MiniCorpus.ScopeP1, filterBeforeTopN: true), CancellationToken.None);

        Assert.Equal(0, Hit(asIs.Value!, "m17").TextScore);
        Assert.True(Hit(fixedVariant.Value!, "m17").TextScore > 0);
    }

    [Fact]
    public async Task BlankQueriesReturnNothingLikeProduction()
    {
        var result = await (await StoreAsync()).HybridSearchAsync(MiniCorpus.Query("   ", MiniCorpus.ScopeP1), CancellationToken.None);
        Assert.Empty(result.Value!);
    }
}
```

- [ ] **Step 5: Run the store tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SqlServerStoreTests"`
Expected: build FAILS (`SqlServerSearchStore` not found).

- [ ] **Step 6: Write the implementation**

`src/Parity.SqlServer/SqlServerParityDbContext.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Parity.Core.Model;

namespace Parity.SqlServer;

/// <summary>Production's DocumentChunks table: same columns, types and primary key name.</summary>
public sealed class SqlServerParityDbContext(DbContextOptions<SqlServerParityDbContext> options) : DbContext(options)
{
    public DbSet<DocumentChunkEntity> DocumentChunks => Set<DocumentChunkEntity>();

    public static SqlServerParityDbContext Create(string connectionString) =>
        new(new DbContextOptionsBuilder<SqlServerParityDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.UseVectorSearch();
                sql.CommandTimeout(120);
            })
            .Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentChunkEntity>(b =>
        {
            b.ToTable("DocumentChunks");
            b.HasKey(c => c.Id).HasName("PK_DocumentChunks");
            b.Property(c => c.Id).ValueGeneratedNever(); // deterministic IDs, identical on both engines
            b.Property(c => c.IndexName).IsRequired().HasMaxLength(255);
            b.Property(c => c.DocumentId).HasMaxLength(255);
            b.Property(c => c.Content).IsRequired();
            b.Property(c => c.Embedding).HasColumnType("vector(1536)");
            b.HasIndex(c => c.IndexName);
            b.HasIndex(c => c.ProjectId);
            b.HasIndex(c => new { c.ProjectId, c.NotebookId });
        });
    }
}
```

`src/Parity.SqlServer/SqlServerSchema.cs`:
```csharp
namespace Parity.SqlServer;

public static class SqlServerSchema
{
    // Copied verbatim from GuideAntsApi.DataModel/Migrations/20260325193437_LiteBaselineV1.cs @ 9f8cd102.
    public const string CreateFullTextCatalog = @"
                IF NOT EXISTS (SELECT * FROM sys.fulltext_catalogs WHERE name = 'DocumentChunks_Catalog')
                IF FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') = 1
                BEGIN
                    CREATE FULLTEXT CATALOG DocumentChunks_Catalog;
                END
                ";

    // Copied verbatim from the same migration: default language, system stoplist, automatic change tracking.
    public const string CreateFullTextIndex = @"
                IF FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') = 1 AND NOT EXISTS (SELECT * FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('DocumentChunks'))
                BEGIN
                    CREATE FULLTEXT INDEX ON DocumentChunks(Content)
                    KEY INDEX PK_DocumentChunks
                    ON DocumentChunks_Catalog;
                END
                ";

    public const string IsFullTextInstalled = "SELECT CAST(FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') AS int)";

    /// <summary>Populate status (0 = idle), pending changes, indexed rows, total rows.</summary>
    public const string PopulationStatus = """
        SELECT ISNULL(CAST(FULLTEXTCATALOGPROPERTY('DocumentChunks_Catalog', 'PopulateStatus') AS int), -1),
               ISNULL(CAST(OBJECTPROPERTYEX(OBJECT_ID('DocumentChunks'), 'TableFulltextPendingChanges') AS int), -1),
               ISNULL(CAST(OBJECTPROPERTYEX(OBJECT_ID('DocumentChunks'), 'TableFulltextItemCount') AS int), -1),
               (SELECT COUNT(*) FROM DocumentChunks)
        """;
}
```

`src/Parity.SqlServer/SqlServerErrors.cs`:
```csharp
using Microsoft.Data.SqlClient;
using Parity.Core;

namespace Parity.SqlServer;

public static class SqlServerErrors
{
    // Network, timeout, login and server-unavailable error numbers.
    private static readonly HashSet<int> Unreachable = [-2, 2, 40, 53, 121, 233, 1225, 10053, 10054, 10060, 10061, 18456];

    public static string Host(string connectionString) => new SqlConnectionStringBuilder(connectionString).DataSource;

    /// <summary>The exception to throw for infrastructure failures, or null when this is a query error to report.</summary>
    public static ParityException? Translate(SqlException ex, string profile, string host) => ex.Number switch
    {
        4060 or 208 => new NotSeededException($"Profile '{profile}' isn't seeded. Run `seed --profile {profile}`.", ex),
        _ when Unreachable.Contains(ex.Number) || ex.Class >= 20 => new EngineUnavailableException(
            $"Can't reach profile '{profile}' (SqlServer) at {host}: {ex.Message} Is `docker compose up -d` running, and has SQL Server finished starting?", ex),
        _ => null,
    };
}
```

`src/Parity.SqlServer/SqlServerSearchStore.cs`:
```csharp
using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Parity.Core;
using Parity.Core.Contracts;
using Parity.Core.Model;
using Parity.SqlServer.Sql;

[assembly: InternalsVisibleTo("Parity.Tests")]

namespace Parity.SqlServer;

public sealed class SqlServerSearchStore(string profile, string connectionString, TimeSpan populationTimeout) : ISearchStore
{
    private readonly string _host = SqlServerErrors.Host(connectionString);

    public async Task PingServerAsync(CancellationToken ct)
    {
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master", ConnectTimeout = 5 };
        try
        {
            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync(ct);
        }
        catch (SqlException ex)
        {
            throw new EngineUnavailableException(
                $"Can't reach profile '{profile}' (SqlServer) at {_host}: {ex.Message} Is `docker compose up -d` running, and has SQL Server finished starting?", ex);
        }
    }

    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        await using (var context = SqlServerParityDbContext.Create(connectionString))
        {
            await context.Database.EnsureDeletedAsync(ct);
            await context.Database.EnsureCreatedAsync(ct);
        }

        SqlConnection.ClearAllPools();
        await using var connection = await OpenAsync(ct);
        if (await ScalarIntAsync(connection, SqlServerSchema.IsFullTextInstalled, ct) != 1)
        {
            // Production would silently skip the index; here that would make every text match empty on both sides.
            throw new ParityConfigurationException(
                $"Full-text search isn't installed on profile '{profile}'. Use the ghcr.io/elumenotion/mssql2025-express-fts image.");
        }

        await ExecuteAsync(connection, SqlServerSchema.CreateFullTextCatalog, ct);
        await ExecuteAsync(connection, SqlServerSchema.CreateFullTextIndex, ct);
    }

    public async Task LoadAsync(IReadOnlyList<ChunkRecord> chunks, DateTime createdUtc, CancellationToken ct)
    {
        foreach (var batch in chunks.Chunk(200))
        {
            await using var context = SqlServerParityDbContext.Create(connectionString);
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            context.DocumentChunks.AddRange(batch.Select(c => DocumentChunkEntity.From(c, createdUtc)));
            await context.SaveChangesAsync(ct);
        }
    }

    public async Task WaitUntilSearchableAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + populationTimeout;
        await using var connection = await OpenAsync(ct);
        while (true)
        {
            int status, pending, indexed, rows;
            await using (var command = new SqlCommand(SqlServerSchema.PopulationStatus, connection))
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                await reader.ReadAsync(ct);
                (status, pending, indexed, rows) = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
            }

            if (status == 0 && pending == 0 && indexed == rows)
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new ParityConfigurationException(
                    $"Full-text population on profile '{profile}' didn't finish within {populationTimeout.TotalSeconds:0}s " +
                    $"(status {status}, pending {pending}, indexed {indexed}/{rows}).");
            }

            await Task.Delay(500, ct);
        }
    }

    public async Task<EngineResult<IReadOnlyList<SearchHit>>> HybridSearchAsync(HybridQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Text))
        {
            return EngineResult<IReadOnlyList<SearchHit>>.Ok(Array.Empty<SearchHit>()); // production returns empty for blank queries
        }

        var sql = query.FilterBeforeTopN
            ? SqlServerSql.BuildQuirkFixedHybrid(query.Vector, query.Text, query.CandidateLimit)
            : SqlServerSql.BuildProductionHybrid(query.Vector, query.Text, query.CandidateLimit);
        try
        {
            await using var connection = await OpenAsync(ct);
            await using var command = new SqlCommand(sql, connection);
            // The same parameters, types and sizes as production.
            command.Parameters.Add(new SqlParameter("@indexName", SqlDbType.NVarChar, 255) { Value = query.Scope.IndexName });
            command.Parameters.Add(new SqlParameter("@notebookId", SqlDbType.UniqueIdentifier) { Value = (object?)query.Scope.NotebookId ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@maxResults", SqlDbType.Int) { Value = query.MaxResults });
            command.Parameters.Add(new SqlParameter("@vectorWeight", SqlDbType.Float) { Value = query.VectorWeight });
            command.Parameters.Add(new SqlParameter("@textWeight", SqlDbType.Float) { Value = query.TextWeight });

            var hits = new List<SearchHit>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                hits.Add(new SearchHit(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    reader.GetDouble(6),
                    reader.GetDouble(7),
                    reader.GetDouble(8)));
            }

            return EngineResult<IReadOnlyList<SearchHit>>.Ok(hits);
        }
        catch (SqlException ex)
        {
            if (SqlServerErrors.Translate(ex, profile, _host) is { } fatal)
            {
                throw fatal;
            }

            return EngineResult<IReadOnlyList<SearchHit>>.Fail(new EngineError($"SQL{ex.Number}", ex.Message));
        }
    }

    internal async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch (SqlException ex)
        {
            await connection.DisposeAsync();
            throw SqlServerErrors.Translate(ex, profile, _host) ?? (Exception)ex;
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> ScalarIntAsync(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SqlServerStoreTests|FullyQualifiedName~SqlServerSqlTests"`
Expected: PASS (12 test cases). The first run pulls nothing, since the image is already local, but starting it emulated takes a few minutes. If `ProductionTopNCutDropsTheInScopeMatchAndTheVariantKeepsIt` fails, print `CONTAINSTABLE` ranks for `"alpha beta"` before changing anything. The test's premise is that the P2 chunks outrank m17.

- [ ] **Step 8: Commit**

```bash
git add src/Parity.SqlServer tests/Parity.Tests
git commit -m "feat(parity): add SQL Server store running production's hybrid query"
```

---
### Task 11: SQL Server diagnostics

**Files:**
- Create: `src/Parity.SqlServer/SqlServerConnections.cs`, `src/Parity.SqlServer/SqlServerSearchDiagnostics.cs`
- Modify: `src/Parity.SqlServer/SqlServerSearchStore.cs` (its `OpenAsync` delegates to `SqlServerConnections`)
- Test: `tests/Parity.Tests/Integration/SqlServerDiagnosticsTests.cs`

**Interfaces:**
- Consumes: `ISearchDiagnostics`, `EngineResult`, `VectorHit`, `TextMatch`, `ChunkScore`, `Token`, `EngineInfo`, `IndexCoverage` (Task 2); `ProductionEscaping` (Task 3); `SqlServerErrors` (Task 10); `IntegrationFixture`, `MiniCorpus` (Task 10).
- Produces:
  - `SqlServerConnections.OpenAsync(string connectionString, string profile, CancellationToken) → Task<SqlConnection>`
  - `SqlServerSearchDiagnostics(string profile, string connectionString) : ISearchDiagnostics`
  - `EngineInfo.Details` keys: `fullTextLanguage`, `accentSensitive`, `stoplist`, `compatibilityLevel`

- [ ] **Step 1: Write the failing tests**

`tests/Parity.Tests/Integration/SqlServerDiagnosticsTests.cs`:
```csharp
using Parity.Core;
using Parity.SqlServer;
using Parity.Tests.Support;

namespace Parity.Tests.Integration;

[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SqlServerDiagnosticsTests(IntegrationFixture fixture)
{
    private async Task<SqlServerSearchDiagnostics> DiagnosticsAsync() => new("mini-sqlserver", await fixture.MiniSqlServerAsync());

    private static float[] VectorOf(string key) => MiniCorpus.Chunks.Single(c => c.Id == MiniCorpus.Id(key)).Embedding;

    [Fact]
    public async Task VectorSearchRanksAChunksOwnVectorFirst()
    {
        var hits = (await (await DiagnosticsAsync()).VectorSearchAsync(VectorOf("m01"), MiniCorpus.ScopeP1, 10, CancellationToken.None)).Value!;
        Assert.Equal(5, hits.Count); // P1 has five chunks
        Assert.Equal(MiniCorpus.Id("m01"), hits[0].ChunkId);
        Assert.True(hits[0].Score >= 1 - 1e-6);
    }

    [Fact]
    public async Task TextMatchReturnsTheFullSetWithoutTopN()
    {
        var diagnostics = await DiagnosticsAsync();
        Assert.Equal(12, (await diagnostics.TextMatchAsync("alpha beta", MiniCorpus.ScopeP2, CancellationToken.None)).Value!.Count);
        Assert.Equal(new[] { MiniCorpus.Id("m17") }, (await diagnostics.TextMatchAsync("alpha beta", MiniCorpus.ScopeP1, CancellationToken.None)).Value!.Select(m => m.ChunkId));
    }

    [Fact]
    public async Task ScoreChunksReportsTheTopNCutAndTheVariantKeepsTheChunk()
    {
        var diagnostics = await DiagnosticsAsync();
        var ids = new[] { MiniCorpus.Id("m17") };

        var asIs = Assert.Single((await diagnostics.ScoreChunksAsync(MiniCorpus.Query("alpha beta", MiniCorpus.ScopeP1), ids, CancellationToken.None)).Value!);
        Assert.True(asIs.Matched);
        Assert.False(asIs.InCandidates);
        Assert.Equal(0, asIs.TextScore);
        Assert.NotNull(asIs.NthCandidateRank);
        Assert.True(asIs.RawRank < asIs.NthCandidateRank);

        var scoped = Assert.Single((await diagnostics.ScoreChunksAsync(MiniCorpus.Query("alpha beta", MiniCorpus.ScopeP1, filterBeforeTopN: true), ids, CancellationToken.None)).Value!);
        Assert.True(scoped.InCandidates);
        Assert.Equal(scoped.RawRank!.Value, scoped.TextScore, 12);
    }

    [Fact]
    public async Task ScoreChunksAgreesWithTheHybridQuery()
    {
        var cs = await fixture.MiniSqlServerAsync();
        var query = MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1);
        var hits = (await new SqlServerSearchStore("mini-sqlserver", cs, IntegrationFixture.PopulationTimeout).HybridSearchAsync(query, CancellationToken.None)).Value!;
        var scores = (await new SqlServerSearchDiagnostics("mini-sqlserver", cs).ScoreChunksAsync(query, hits.Select(h => h.ChunkId).ToList(), CancellationToken.None)).Value!
            .ToDictionary(s => s.ChunkId);

        Assert.All(hits, h =>
        {
            Assert.Equal(h.VectorScore, scores[h.ChunkId].VectorScore, 9);
            Assert.Equal(h.TextScore, scores[h.ChunkId].TextScore, 9);
        });
    }

    [Fact]
    public async Task TokenizeMarksNoiseWords()
    {
        var tokens = (await (await DiagnosticsAsync()).TokenizeAsync("the end of the line", CancellationToken.None)).Value!;
        Assert.Contains(tokens, t => t.IsStopWord);
        Assert.Contains(tokens, t => t.Text == "end" && !t.IsStopWord);
    }

    [Fact]
    public async Task DescribeReportsVersionAndFullTextSettings()
    {
        var info = await (await DiagnosticsAsync()).DescribeAsync(CancellationToken.None);
        Assert.Equal("SqlServer", info.Provider);
        Assert.Contains("Microsoft SQL Server 2025", info.Version);
        Assert.Equal("1033", info.Details["fullTextLanguage"]);
        Assert.Equal("system", info.Details["stoplist"]);
    }

    [Fact]
    public async Task CoverageCountsEveryRow()
    {
        var coverage = await (await DiagnosticsAsync()).GetIndexCoverageAsync(CancellationToken.None);
        Assert.Equal((18, 18), (coverage.Rows, coverage.IndexedRows));
    }

    [Fact]
    public async Task MissingDatabaseIsReportedAsNotSeeded()
    {
        var diagnostics = new SqlServerSearchDiagnostics("absent", fixture.SqlServerConnectionString("parity_absent"));
        var error = await Assert.ThrowsAsync<NotSeededException>(() => diagnostics.GetIndexCoverageAsync(CancellationToken.None));
        Assert.Contains("seed --profile absent", error.Message);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SqlServerDiagnosticsTests"`
Expected: build FAILS (`SqlServerSearchDiagnostics` not found).

- [ ] **Step 3: Write the implementation**

`src/Parity.SqlServer/SqlServerConnections.cs`:
```csharp
using Microsoft.Data.SqlClient;

namespace Parity.SqlServer;

internal static class SqlServerConnections
{
    /// <summary>Opens a connection, turning "can't connect" and "no such database" into the CLI's exceptions.</summary>
    public static async Task<SqlConnection> OpenAsync(string connectionString, string profile, CancellationToken ct)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch (SqlException ex)
        {
            await connection.DisposeAsync();
            throw SqlServerErrors.Translate(ex, profile, SqlServerErrors.Host(connectionString)) ?? (Exception)ex;
        }
    }
}
```

In `src/Parity.SqlServer/SqlServerSearchStore.cs`, replace the whole `internal async Task<SqlConnection> OpenAsync(CancellationToken ct)` method with:
```csharp
    internal Task<SqlConnection> OpenAsync(CancellationToken ct) => SqlServerConnections.OpenAsync(connectionString, profile, ct);
```

`src/Parity.SqlServer/SqlServerSearchDiagnostics.cs`:
```csharp
using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Parity.Core.Contracts;
using Parity.Core.Model;
using Parity.SqlServer.Sql;

namespace Parity.SqlServer;

public sealed class SqlServerSearchDiagnostics(string profile, string connectionString) : ISearchDiagnostics
{
    private const string VectorSql = """
        DECLARE @queryVec vector(1536) = CAST(@vectorJson AS vector(1536));
        SELECT TOP (@k) dc.Id, CAST(1.0 - vector_distance('cosine', dc.Embedding, @queryVec) AS FLOAT) AS Score
        FROM DocumentChunks dc
        WHERE dc.IndexName = @indexName AND (@notebookId IS NULL OR dc.NotebookId = @notebookId)
        ORDER BY vector_distance('cosine', dc.Embedding, @queryVec), dc.Id;
        """;

    private const string TextMatchSql = """
        SELECT ft.[KEY], CAST(ft.[RANK] AS FLOAT) / 1000.0
        FROM CONTAINSTABLE(DocumentChunks, Content, @phrase) ft
        INNER JOIN DocumentChunks dc ON dc.Id = ft.[KEY]
        WHERE dc.IndexName = @indexName AND (@notebookId IS NULL OR dc.NotebookId = @notebookId);
        """;

    // {candidates} is replaced by one of the two candidate definitions below.
    private const string ScoreSql = """
        DECLARE @queryVec vector(1536) = CAST(@vectorJson AS vector(1536));
        WITH AllMatches AS (
            SELECT ft.[KEY] AS Id, CAST(ft.[RANK] AS FLOAT) / 1000.0 AS R
            FROM CONTAINSTABLE(DocumentChunks, Content, @phrase) ft
        ),
        Cand AS (
            {candidates}
        ),
        CandStats AS (
            SELECT CASE WHEN COUNT(*) = @candidateLimit THEN MIN(R) END AS Nth FROM Cand
        )
        SELECT dc.Id,
               CAST(1.0 - vector_distance('cosine', dc.Embedding, @queryVec) AS FLOAT),
               CAST(COALESCE(c.R, 0) AS FLOAT),
               CAST(CASE WHEN m.Id IS NULL THEN 0 ELSE 1 END AS bit),
               m.R,
               CAST(CASE WHEN c.Id IS NULL THEN 0 ELSE 1 END AS bit),
               s.Nth
        FROM DocumentChunks dc
        CROSS JOIN CandStats s
        LEFT JOIN AllMatches m ON m.Id = dc.Id
        LEFT JOIN Cand c ON c.Id = dc.Id
        WHERE dc.Id IN (SELECT CAST([value] AS uniqueidentifier) FROM OPENJSON(@ids));
        """;

    // Production: CONTAINSTABLE's own top_n, across the whole table.
    private const string ProductionCandidates =
        "SELECT ft.[KEY] AS Id, CAST(ft.[RANK] AS FLOAT) / 1000.0 AS R FROM CONTAINSTABLE(DocumentChunks, Content, @phrase, {n}) ft";

    // Quirk fixed: scope filter first, then the top N.
    private const string ScopedCandidates = """
        SELECT TOP ({n}) m.Id, m.R FROM AllMatches m
                INNER JOIN DocumentChunks scoped ON scoped.Id = m.Id
                WHERE scoped.IndexName = @indexName AND (@notebookId IS NULL OR scoped.NotebookId = @notebookId)
                ORDER BY m.R DESC
        """;

    private const string TokenizeSql = """
        DECLARE @lcid int = (SELECT CAST(value_in_use AS int) FROM sys.configurations WHERE name = 'default full-text language');
        DECLARE @accent int = ISNULL(CAST(FULLTEXTCATALOGPROPERTY('DocumentChunks_Catalog', 'AccentSensitivity') AS int), 1);
        SELECT display_term, special_term FROM sys.dm_fts_parser(@phrase, @lcid, 0, @accent) ORDER BY occurrence;
        """;

    private const string DescribeSql = """
        SELECT @@VERSION,
               (SELECT CAST(value_in_use AS int) FROM sys.configurations WHERE name = 'default full-text language'),
               CAST(FULLTEXTCATALOGPROPERTY('DocumentChunks_Catalog', 'AccentSensitivity') AS int),
               (SELECT CAST(stoplist_id AS int) FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('DocumentChunks')),
               (SELECT CAST(compatibility_level AS int) FROM sys.databases WHERE name = DB_NAME());
        """;

    private const string CoverageSql = """
        SELECT COUNT(*), ISNULL(CAST(OBJECTPROPERTYEX(OBJECT_ID('DocumentChunks'), 'TableFulltextItemCount') AS int), 0)
        FROM DocumentChunks;
        """;

    private readonly string _host = SqlServerErrors.Host(connectionString);

    public Task<EngineResult<IReadOnlyList<VectorHit>>> VectorSearchAsync(float[] vector, Scope scope, int k, CancellationToken ct) =>
        QueryAsync(VectorSql, p =>
        {
            p.Add(new SqlParameter("@vectorJson", SqlDbType.NVarChar, -1) { Value = ProductionEscaping.VectorJson(vector) });
            p.Add(new SqlParameter("@k", SqlDbType.Int) { Value = k });
            AddScope(p, scope);
        }, r => new VectorHit(r.GetGuid(0), r.GetDouble(1)), ct);

    public Task<EngineResult<IReadOnlyList<TextMatch>>> TextMatchAsync(string text, Scope scope, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(text)
            ? Task.FromResult(EngineResult<IReadOnlyList<TextMatch>>.Ok(Array.Empty<TextMatch>()))
            : QueryAsync(TextMatchSql, p =>
            {
                AddPhrase(p, text);
                AddScope(p, scope);
            }, r => new TextMatch(r.GetGuid(0), r.GetDouble(1)), ct);

    public Task<EngineResult<IReadOnlyList<ChunkScore>>> ScoreChunksAsync(HybridQuery query, IReadOnlyCollection<Guid> chunkIds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Text) || chunkIds.Count == 0)
        {
            return Task.FromResult(EngineResult<IReadOnlyList<ChunkScore>>.Ok(Array.Empty<ChunkScore>()));
        }

        var limit = query.CandidateLimit.ToString(CultureInfo.InvariantCulture);
        var candidates = (query.FilterBeforeTopN ? ScopedCandidates : ProductionCandidates).Replace("{n}", limit);
        return QueryAsync(ScoreSql.Replace("{candidates}", candidates), p =>
        {
            p.Add(new SqlParameter("@vectorJson", SqlDbType.NVarChar, -1) { Value = ProductionEscaping.VectorJson(query.Vector) });
            p.Add(new SqlParameter("@candidateLimit", SqlDbType.Int) { Value = query.CandidateLimit });
            p.Add(new SqlParameter("@ids", SqlDbType.NVarChar, -1) { Value = JsonSerializer.Serialize(chunkIds) });
            AddPhrase(p, query.Text);
            AddScope(p, query.Scope);
        }, r => new ChunkScore(
            r.GetGuid(0),
            r.GetDouble(1),
            r.GetDouble(2),
            r.GetBoolean(3),
            r.IsDBNull(4) ? null : r.GetDouble(4),
            r.GetBoolean(5),
            r.IsDBNull(6) ? null : r.GetDouble(6)), ct);
    }

    public Task<EngineResult<IReadOnlyList<Token>>> TokenizeAsync(string text, CancellationToken ct) =>
        QueryAsync(TokenizeSql, p => AddPhrase(p, text), r =>
        {
            var term = r.IsDBNull(0) ? string.Empty : r.GetString(0);
            var kind = r.IsDBNull(1) ? string.Empty : r.GetString(1);
            return new Token(term, term, kind, kind == "Noise Word");
        }, ct);

    public async Task<EngineInfo> DescribeAsync(CancellationToken ct)
    {
        await using var connection = await SqlServerConnections.OpenAsync(connectionString, profile, ct);
        await using var command = new SqlCommand(DescribeSql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var version = reader.GetString(0).Split('\n')[0].Trim();
        var details = new Dictionary<string, string>
        {
            ["fullTextLanguage"] = reader.IsDBNull(1) ? "unknown" : reader.GetInt32(1).ToString(CultureInfo.InvariantCulture),
            ["accentSensitive"] = reader.IsDBNull(2) ? "unknown" : (reader.GetInt32(2) == 1 ? "yes" : "no"),
            ["stoplist"] = reader.IsDBNull(3) ? "off" : reader.GetInt32(3) == 0 ? "system" : $"custom ({reader.GetInt32(3)})",
            ["compatibilityLevel"] = reader.IsDBNull(4) ? "unknown" : reader.GetInt32(4).ToString(CultureInfo.InvariantCulture),
        };
        return new EngineInfo("SqlServer", version, details);
    }

    public async Task<IndexCoverage> GetIndexCoverageAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = await SqlServerConnections.OpenAsync(connectionString, profile, ct);
            await using var command = new SqlCommand(CoverageSql, connection);
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return new IndexCoverage(reader.GetInt32(0), reader.GetInt32(1));
        }
        catch (SqlException ex) when (SqlServerErrors.Translate(ex, profile, _host) is { } fatal)
        {
            throw fatal;
        }
    }

    private async Task<EngineResult<IReadOnlyList<T>>> QueryAsync<T>(string sql, Action<SqlParameterCollection> bind, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        try
        {
            await using var connection = await SqlServerConnections.OpenAsync(connectionString, profile, ct);
            await using var command = new SqlCommand(sql, connection);
            bind(command.Parameters);
            var rows = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(map(reader));
            }

            return EngineResult<IReadOnlyList<T>>.Ok(rows);
        }
        catch (SqlException ex)
        {
            if (SqlServerErrors.Translate(ex, profile, _host) is { } fatal)
            {
                throw fatal;
            }

            return EngineResult<IReadOnlyList<T>>.Fail(new EngineError($"SQL{ex.Number}", ex.Message));
        }
    }

    // The same phrase production builds, passed as a parameter, so no single-quote escaping is needed.
    private static void AddPhrase(SqlParameterCollection p, string text) =>
        p.Add(new SqlParameter("@phrase", SqlDbType.NVarChar, 4000) { Value = ProductionEscaping.FullTextPhrase(text) });

    private static void AddScope(SqlParameterCollection p, Scope scope)
    {
        p.Add(new SqlParameter("@indexName", SqlDbType.NVarChar, 255) { Value = scope.IndexName });
        p.Add(new SqlParameter("@notebookId", SqlDbType.UniqueIdentifier) { Value = (object?)scope.NotebookId ?? DBNull.Value });
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SqlServerDiagnosticsTests|FullyQualifiedName~SqlServerStoreTests"`
Expected: PASS (all SQL Server integration tests). If `TokenizeMarksNoiseWords` finds no stop word, check that `dm_fts_parser`'s third argument `0` selects the system stoplist on this build. Report it rather than weakening the assertion.

- [ ] **Step 5: Commit**

```bash
git add src/Parity.SqlServer tests/Parity.Tests/Integration/SqlServerDiagnosticsTests.cs
git commit -m "feat(parity): add SQL Server diagnostics for vectors, matches, scores and tokens"
```

---
### Task 12: Postgres store (the port)

**Files:**
- Create: `src/Parity.Postgres/Parity.Postgres.csproj`, `src/Parity.Postgres/PostgresParityDbContext.cs`, `src/Parity.Postgres/PostgresSchema.cs`, `src/Parity.Postgres/PostgresErrors.cs`, `src/Parity.Postgres/PostgresSql.cs`, `src/Parity.Postgres/PostgresSearchStore.cs`
- Modify: `tests/Parity.Tests/Parity.Tests.csproj` (add the `Parity.Postgres` reference and `Testcontainers.PostgreSql`), `src/Parity.Cli/Parity.Cli.csproj` (add the `Parity.Postgres` reference), `HybridSearchParity.sln`
- Replace: `tests/Parity.Tests/Integration/IntegrationFixture.cs` (adds the Postgres container)
- Test: `tests/Parity.Tests/Unit/PostgresSchemaTests.cs`, `tests/Parity.Tests/Integration/PostgresStoreTests.cs`

**Interfaces:**
- Consumes: Task 2 contracts and model; `IntegrationFixture`, `MiniCorpus` (Task 10).
- Produces:
  - `PostgresSchema.CustomConfigName` (`"parity_english_nostem"`), `PostgresSchema.ValidateConfigName(string)`, `PostgresSchema.CreateCustomTextSearchConfig`, `PostgresSchema.AddTsvColumn(string config)` and `PostgresSchema.CreateTsvIndex`
  - `PostgresErrors.Translate(Exception, string profile, string host) → ParityException?`, `PostgresErrors.Host(string connectionString)` and `PostgresErrors.ToEngineError(PostgresException)`
  - `PostgresDataSources.Build(string connectionString) → NpgsqlDataSource` (with `UseVector()`)
  - `PostgresSql.Hybrid(bool filterBeforeTopN) → string`
  - `PostgresSearchStore(string profile, string connectionString, string textSearchConfig) : ISearchStore, IAsyncDisposable`
  - `IntegrationFixture.PostgresConnectionString(string database)` and `IntegrationFixture.MiniPostgresAsync(string config = PostgresSchema.CustomConfigName)`

- [ ] **Step 1: Create the project and wire up the references**

`src/Parity.Postgres/Parity.Postgres.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Parity.Postgres</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Parity.Core/Parity.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Pgvector.EntityFrameworkCore" />
  </ItemGroup>
</Project>
```

Add `<ProjectReference Include="../Parity.Postgres/Parity.Postgres.csproj" />` to `src/Parity.Cli/Parity.Cli.csproj`. Add `<ProjectReference Include="../../src/Parity.Postgres/Parity.Postgres.csproj" />` and `<PackageReference Include="Testcontainers.PostgreSql" />` to `tests/Parity.Tests/Parity.Tests.csproj`.

Run: `dotnet sln HybridSearchParity.sln add src/Parity.Postgres/Parity.Postgres.csproj`

- [ ] **Step 2: Write the failing unit test**

`tests/Parity.Tests/Unit/PostgresSchemaTests.cs`:
```csharp
using Parity.Core;
using Parity.Postgres;

namespace Parity.Tests.Unit;

public class PostgresSchemaTests
{
    [Theory]
    [InlineData("parity_english_nostem")]
    [InlineData("english")]
    public void AcceptsPlainConfigurationNames(string name) => PostgresSchema.ValidateConfigName(name);

    [Theory]
    [InlineData("english; drop table x")]
    [InlineData("English")]
    [InlineData("")]
    public void RejectsAnythingThatCouldInjectSql(string name) =>
        Assert.Throws<ParityConfigurationException>(() => PostgresSchema.ValidateConfigName(name));

    [Fact]
    public void GeneratedColumnUsesTheConfiguredConfig() =>
        Assert.Contains("to_tsvector('english'::regconfig, \"Content\")", PostgresSchema.AddTsvColumn("english"));

    [Fact]
    public void ProductionVariantPicksCandidatesAcrossAllProjects()
    {
        var production = PostgresSql.Hybrid(filterBeforeTopN: false);
        var scoped = PostgresSql.Hybrid(filterBeforeTopN: true);
        var ftCte = (string sql) => sql[sql.IndexOf("ft AS (", StringComparison.Ordinal)..sql.IndexOf("scored AS (", StringComparison.Ordinal)];
        Assert.DoesNotContain("IndexName", ftCte(production));
        Assert.Contains("dc.\"IndexName\" = @indexName", ftCte(scoped));
    }
}
```

Run: `dotnet test --filter "FullyQualifiedName~PostgresSchemaTests"`
Expected: build FAILS (`Parity.Postgres` types not found).

- [ ] **Step 3: Write the schema, errors, SQL and data source helpers**

`src/Parity.Postgres/PostgresSchema.cs`:
```csharp
using System.Text.RegularExpressions;
using Parity.Core;

namespace Parity.Postgres;

public static class PostgresSchema
{
    /// <summary>English stop words like SQL Server's system stoplist, with no stemming (phrase search matches whole words).</summary>
    public const string CustomConfigName = "parity_english_nostem";

    public static readonly string[] CreateCustomTextSearchConfig =
    [
        "CREATE TEXT SEARCH DICTIONARY parity_simple_english (TEMPLATE = pg_catalog.simple, STOPWORDS = english)",
        "CREATE TEXT SEARCH CONFIGURATION parity_english_nostem (COPY = pg_catalog.simple)",
        "ALTER TEXT SEARCH CONFIGURATION parity_english_nostem ALTER MAPPING FOR asciiword, word, numword, asciihword, hword, numhword, hword_asciipart, hword_part, hword_numpart WITH parity_simple_english",
    ];

    public const string CreateTsvIndex = """CREATE INDEX "IX_DocumentChunks_ContentTsv" ON "DocumentChunks" USING GIN ("ContentTsv")""";

    private static readonly Regex ConfigName = new("^[a-z_][a-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>The config name is spliced into DDL, so only plain lowercase identifiers are allowed.</summary>
    public static void ValidateConfigName(string config)
    {
        if (!ConfigName.IsMatch(config))
        {
            throw new ParityConfigurationException(
                $"TextSearchConfig '{config}' isn't a valid configuration name (lowercase letters, digits and underscores only).");
        }
    }

    public static string AddTsvColumn(string config) =>
        $"""ALTER TABLE "DocumentChunks" ADD COLUMN "ContentTsv" tsvector GENERATED ALWAYS AS (to_tsvector('{config}'::regconfig, "Content")) STORED""";
}
```

`src/Parity.Postgres/PostgresErrors.cs`:
```csharp
using System.Net.Sockets;
using Npgsql;
using Parity.Core;
using Parity.Core.Model;

namespace Parity.Postgres;

public static class PostgresErrors
{
    public static string Host(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return $"{builder.Host}:{builder.Port}";
    }

    /// <summary>The exception to throw for infrastructure failures, or null when this is a query error to report.</summary>
    public static ParityException? Translate(Exception ex, string profile, string host) => ex switch
    {
        PostgresException { SqlState: "3D000" or "42P01" } =>
            new NotSeededException($"Profile '{profile}' isn't seeded. Run `seed --profile {profile}`.", ex),
        PostgresException pg when pg.SqlState.StartsWith("08", StringComparison.Ordinal) || pg.SqlState is "57P03" or "28P01" =>
            Unreachable(ex, profile, host),
        PostgresException => null,
        NpgsqlException or SocketException or TimeoutException => Unreachable(ex, profile, host),
        _ => null,
    };

    public static EngineError ToEngineError(PostgresException ex) => new($"PG{ex.SqlState}", ex.MessageText);

    private static EngineUnavailableException Unreachable(Exception ex, string profile, string host) =>
        new($"Can't reach profile '{profile}' (Postgres) at {host}: {ex.Message} Is `docker compose up -d` running?", ex);
}

public static class PostgresDataSources
{
    /// <summary>A data source with pgvector's type mapping, needed to pass Pgvector.Vector parameters.</summary>
    public static NpgsqlDataSource Build(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector(); // Npgsql.VectorExtensions, from the Pgvector package
        return builder.Build();
    }
}
```

`src/Parity.Postgres/PostgresSql.cs`:
```csharp
namespace Parity.Postgres;

/// <summary>
/// The Postgres port of production's hybrid query, built step for step like HybridSearcher's SQL.
/// The phrase and the vector are passed as parameters instead of being spliced into the text.
/// </summary>
public static class PostgresSql
{
    private const string ScopeFilter = """
        AND dc."IndexName" = @indexName
              AND (@notebookId IS NULL OR dc."NotebookId" = @notebookId)
        """;

    private const string Template = """
        WITH q AS (
            SELECT phraseto_tsquery(@cfg::regconfig, @query) AS tsq
        ),
        ft AS (
            SELECT dc."Id", ts_rank_cd(dc."ContentTsv", q.tsq, 32) AS rank
            FROM "DocumentChunks" dc, q
            WHERE dc."ContentTsv" @@ q.tsq
              {ftScope}
            ORDER BY rank DESC
            LIMIT @candidateLimit
        ),
        scored AS (
            SELECT dc."Id", dc."Content", dc."ContentFileId", dc."NotebookFileId", dc."ChunkIndex", dc."IndexName",
                   CAST(COALESCE(ft.rank, 0) AS float8) AS text_score,
                   CAST(dc."Embedding" <=> @vec AS float8) AS distance
            FROM "DocumentChunks" dc
            LEFT JOIN ft ON ft."Id" = dc."Id"
            WHERE dc."IndexName" = @indexName
              AND (@notebookId IS NULL OR dc."NotebookId" = @notebookId)
        )
        SELECT "Id", "Content", "ContentFileId", "NotebookFileId", "ChunkIndex", "IndexName",
               1.0 - distance AS vector_score,
               text_score,
               @vectorWeight * (1.0 - distance) + @textWeight * text_score AS score
        FROM scored
        WHERE text_score > 0 OR distance < 1.0
        ORDER BY score DESC
        LIMIT @maxResults
        """;

    /// <param name="filterBeforeTopN">false = production behaviour (top-N across all projects); true = informational variant.</param>
    public static string Hybrid(bool filterBeforeTopN) => Template.Replace("{ftScope}", filterBeforeTopN ? ScopeFilter : string.Empty);
}
```

`src/Parity.Postgres/PostgresParityDbContext.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql;
using Parity.Core.Model;
using Pgvector;

namespace Parity.Postgres;

/// <summary>
/// The same DocumentChunks entity as SQL Server. Embedding stays float[] and maps to vector(1536)
/// through a value converter: the smallest entity change a real migration would need.
/// </summary>
public sealed class PostgresParityDbContext(DbContextOptions<PostgresParityDbContext> options) : DbContext(options)
{
    public DbSet<DocumentChunkEntity> DocumentChunks => Set<DocumentChunkEntity>();

    public static PostgresParityDbContext Create(NpgsqlDataSource dataSource) =>
        new(new DbContextOptionsBuilder<PostgresParityDbContext>()
            .UseNpgsql(dataSource, o => o.UseVector()) // Microsoft.EntityFrameworkCore.VectorDbContextOptionsBuilderExtensions
            .Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.Entity<DocumentChunkEntity>(b =>
        {
            b.ToTable("DocumentChunks");
            b.HasKey(c => c.Id).HasName("PK_DocumentChunks");
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.IndexName).IsRequired().HasMaxLength(255);
            b.Property(c => c.DocumentId).HasMaxLength(255);
            b.Property(c => c.Content).IsRequired();
            b.Property(c => c.Embedding)
                .HasColumnType("vector(1536)")
                .HasConversion(
                    new ValueConverter<float[], Vector>(v => new Vector(new ReadOnlyMemory<float>(v)), v => v.ToArray()),
                    new ValueComparer<float[]>((a, b) => a!.SequenceEqual(b!), v => v.Length, v => v.ToArray()));
            b.HasIndex(c => c.IndexName);
            b.HasIndex(c => c.ProjectId);
            b.HasIndex(c => new { c.ProjectId, c.NotebookId });
        });
    }
}
```

- [ ] **Step 4: Replace the integration fixture so it also starts Postgres**

`tests/Parity.Tests/Integration/IntegrationFixture.cs` (full replacement):
```csharp
using Microsoft.Data.SqlClient;
using Npgsql;
using Parity.Core.Contracts;
using Parity.Core.Model;
using Parity.Core.Settings;
using Parity.Postgres;
using Parity.SqlServer;
using Parity.Tests.Support;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Parity.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<IntegrationFixture>
{
    public const string Name = "databases";
}

/// <summary>
/// Starts both containers once per test run. Databases are seeded lazily, once per key. The SQL
/// Server image is amd64-only, so on Apple Silicon it runs emulated and takes a few minutes to start.
/// </summary>
public sealed class IntegrationFixture : IAsyncLifetime
{
    public static readonly TimeSpan PopulationTimeout = TimeSpan.FromMinutes(10);

    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder()
        .WithImage(new ImageSettings().SqlServer)
        .WithPassword("Parity_Passw0rd!")
        .Build();

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage(new ImageSettings().Postgres)
        .WithPassword("parity")
        .Build();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, string> _seeded = new();

    public Task InitializeAsync() => Task.WhenAll(_sqlServer.StartAsync(), _postgres.StartAsync());

    public async Task DisposeAsync()
    {
        await _sqlServer.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public string SqlServerConnectionString(string database) =>
        new SqlConnectionStringBuilder(_sqlServer.GetConnectionString())
        {
            InitialCatalog = database,
            TrustServerCertificate = true,
            Encrypt = false,
        }.ConnectionString;

    public string PostgresConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;

    public Task<string> MiniSqlServerAsync() => SeedOnceAsync(
        "mini-sqlserver",
        () => SqlServerConnectionString("parity_mini"),
        cs => new SqlServerSearchStore("mini-sqlserver", cs, PopulationTimeout),
        MiniCorpus.Chunks,
        MiniCorpus.CreatedUtc);

    public Task<string> MiniPostgresAsync(string config = PostgresSchema.CustomConfigName) => SeedOnceAsync(
        $"mini-postgres-{config}",
        () => PostgresConnectionString($"parity_mini_{config}"),
        cs => new PostgresSearchStore($"mini-postgres-{config}", cs, config),
        MiniCorpus.Chunks,
        MiniCorpus.CreatedUtc);

    /// <summary>Seeds <paramref name="chunks"/> into a fresh database the first time <paramref name="key"/> is asked for.</summary>
    public async Task<string> SeedOnceAsync(
        string key,
        Func<string> connectionString,
        Func<string, ISearchStore> createStore,
        IReadOnlyList<ChunkRecord> chunks,
        DateTime createdUtc)
    {
        await _gate.WaitAsync();
        try
        {
            if (_seeded.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var cs = connectionString();
            var store = createStore(cs);
            await store.EnsureSchemaAsync(CancellationToken.None);
            await store.LoadAsync(chunks, createdUtc, CancellationToken.None);
            await store.WaitUntilSearchableAsync(CancellationToken.None);
            if (store is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }

            _seeded[key] = cs;
            return cs;
        }
        finally
        {
            _gate.Release();
        }
    }
}
```

- [ ] **Step 5: Write the failing store tests**

`tests/Parity.Tests/Integration/PostgresStoreTests.cs`:
```csharp
using Parity.Core.Model;
using Parity.Postgres;
using Parity.Tests.Support;

namespace Parity.Tests.Integration;

[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresStoreTests(IntegrationFixture fixture)
{
    private async Task<PostgresSearchStore> StoreAsync(string config = PostgresSchema.CustomConfigName) =>
        new($"mini-postgres-{config}", await fixture.MiniPostgresAsync(config), config);

    private static SearchHit Hit(IReadOnlyList<SearchHit> hits, string key) => hits.Single(h => h.ChunkId == MiniCorpus.Id(key));

    [Fact]
    public async Task SeedingTwiceRecreatesCleanly()
    {
        await using var store = new PostgresSearchStore("reseed", fixture.PostgresConnectionString("parity_reseed"), PostgresSchema.CustomConfigName);
        for (var run = 0; run < 2; run++)
        {
            await store.EnsureSchemaAsync(CancellationToken.None);
            await store.LoadAsync(MiniCorpus.Chunks, MiniCorpus.CreatedUtc, CancellationToken.None);
            await store.WaitUntilSearchableAsync(CancellationToken.None);
        }

        var result = await store.HybridSearchAsync(MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(5, result.Value.Count);
    }

    [Fact]
    public async Task CustomConfigMatchesWholeWordsWithoutStemming()
    {
        await using var store = await StoreAsync();
        var result = await store.HybridSearchAsync(MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(Hit(result.Value, "m01").TextScore > 0);
        Assert.Equal(0, Hit(result.Value, "m02").TextScore);
        Assert.All(result.Value, h => Assert.Equal(0.7 * h.VectorScore + 0.3 * h.TextScore, h.Score, 9));
    }

    [Fact]
    public async Task EnglishConfigStems()
    {
        await using var store = await StoreAsync("english");
        var result = await store.HybridSearchAsync(MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1), CancellationToken.None);
        Assert.True(Hit(result.Value!, "m02").TextScore > 0); // "locks" stems to "lock"
    }

    [Fact]
    public async Task NotebookScopeReturnsOnlyNotebookChunks()
    {
        await using var store = await StoreAsync();
        var result = await store.HybridSearchAsync(MiniCorpus.Query("conversation", MiniCorpus.ScopeN1), CancellationToken.None);
        Assert.Equal(new[] { MiniCorpus.Id("m02") }, result.Value!.Select(h => h.ChunkId));
    }

    [Theory]
    [InlineData("O'Reilly Media")]
    [InlineData("don't restart")]
    [InlineData("say \"hello world\" twice")]
    [InlineData("the and of")]
    [InlineData("!!! ???")]
    public async Task AwkwardQueriesNeverThrow(string text)
    {
        await using var store = await StoreAsync();
        var result = await store.HybridSearchAsync(MiniCorpus.Query(text, MiniCorpus.ScopeP1), CancellationToken.None);
        Assert.True(result.IsSuccess || result.Error is not null);
        if (text == "O'Reilly Media")
        {
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.True(Hit(result.Value, "m03").TextScore > 0);
        }

        if (text == "the and of")
        {
            Assert.True(result.IsSuccess, result.Error?.Message); // an empty tsquery matches nothing; it isn't an error
            Assert.All(result.Value, h => Assert.Equal(0, h.TextScore));
        }
    }

    [Fact]
    public async Task ProductionTopNCutDropsTheInScopeMatchAndTheVariantKeepsIt()
    {
        await using var store = await StoreAsync();
        var asIs = await store.HybridSearchAsync(MiniCorpus.Query("alpha beta", MiniCorpus.ScopeP1), CancellationToken.None);
        var fixedVariant = await store.HybridSearchAsync(MiniCorpus.Query("alpha beta", MiniCorpus.ScopeP1, filterBeforeTopN: true), CancellationToken.None);

        Assert.Equal(0, Hit(asIs.Value!, "m17").TextScore);
        Assert.True(Hit(fixedVariant.Value!, "m17").TextScore > 0);
    }

    [Fact]
    public async Task BlankQueriesReturnNothingLikeProduction()
    {
        await using var store = await StoreAsync();
        Assert.Empty((await store.HybridSearchAsync(MiniCorpus.Query(" ", MiniCorpus.ScopeP1), CancellationToken.None)).Value!);
    }
}
```

Run: `dotnet test --filter "FullyQualifiedName~PostgresStoreTests"`
Expected: build FAILS (`PostgresSearchStore` not found).

- [ ] **Step 6: Write the store**

`src/Parity.Postgres/PostgresSearchStore.cs`:
```csharp
using Npgsql;
using NpgsqlTypes;
using Parity.Core;
using Parity.Core.Contracts;
using Parity.Core.Model;
using Pgvector;

namespace Parity.Postgres;

public sealed class PostgresSearchStore : ISearchStore, IAsyncDisposable
{
    private readonly string _profile;
    private readonly string _connectionString;
    private readonly string _config;
    private readonly string _host;
    private NpgsqlDataSource _dataSource;

    public PostgresSearchStore(string profile, string connectionString, string textSearchConfig)
    {
        PostgresSchema.ValidateConfigName(textSearchConfig);
        _profile = profile;
        _connectionString = connectionString;
        _config = textSearchConfig;
        _host = PostgresErrors.Host(connectionString);
        _dataSource = PostgresDataSources.Build(connectionString);
    }

    public async Task PingServerAsync(CancellationToken ct)
    {
        var admin = new NpgsqlConnectionStringBuilder(_connectionString) { Database = "postgres", Timeout = 5, Pooling = false };
        try
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(ct);
        }
        catch (Exception ex) when (PostgresErrors.Translate(ex, _profile, _host) is { } fatal)
        {
            throw fatal;
        }
    }

    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        var database = new NpgsqlConnectionStringBuilder(_connectionString).Database
            ?? throw new ParityConfigurationException($"Profile '{_profile}' connection string has no Database.");
        var admin = new NpgsqlConnectionStringBuilder(_connectionString) { Database = "postgres", Pooling = false };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {Quote(database)} WITH (FORCE)", ct);
            await ExecuteAsync(connection, $"CREATE DATABASE {Quote(database)}", ct);
        }

        // Pooled connections point at the dropped database; start from a fresh data source.
        NpgsqlConnection.ClearAllPools();
        await _dataSource.DisposeAsync();
        _dataSource = PostgresDataSources.Build(_connectionString);

        await using (var context = PostgresParityDbContext.Create(_dataSource))
        {
            await context.Database.EnsureCreatedAsync(ct); // creates the vector extension and the table
        }

        await using var schema = await _dataSource.OpenConnectionAsync(ct);
        await schema.ReloadTypesAsync(); // the vector type didn't exist when the data source loaded types
        if (_config == PostgresSchema.CustomConfigName)
        {
            foreach (var statement in PostgresSchema.CreateCustomTextSearchConfig)
            {
                await ExecuteAsync(schema, statement, ct);
            }
        }

        await ExecuteAsync(schema, PostgresSchema.AddTsvColumn(_config), ct);
        await ExecuteAsync(schema, PostgresSchema.CreateTsvIndex, ct);
    }

    public async Task LoadAsync(IReadOnlyList<ChunkRecord> chunks, DateTime createdUtc, CancellationToken ct)
    {
        foreach (var batch in chunks.Chunk(200))
        {
            await using var context = PostgresParityDbContext.Create(_dataSource);
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            context.DocumentChunks.AddRange(batch.Select(c => DocumentChunkEntity.From(c, createdUtc)));
            await context.SaveChangesAsync(ct);
        }
    }

    // A generated tsvector column is filled as each row is written; there's nothing to wait for.
    public Task WaitUntilSearchableAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<EngineResult<IReadOnlyList<SearchHit>>> HybridSearchAsync(HybridQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Text))
        {
            return EngineResult<IReadOnlyList<SearchHit>>.Ok(Array.Empty<SearchHit>()); // production returns empty for blank queries
        }

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(ct);
            await using var command = new NpgsqlCommand(PostgresSql.Hybrid(query.FilterBeforeTopN), connection);
            command.Parameters.Add(new NpgsqlParameter("cfg", NpgsqlDbType.Text) { Value = _config });
            command.Parameters.Add(new NpgsqlParameter("query", NpgsqlDbType.Text) { Value = query.Text });
            command.Parameters.Add(new NpgsqlParameter("vec", new Vector(new ReadOnlyMemory<float>(query.Vector))));
            command.Parameters.Add(new NpgsqlParameter("indexName", NpgsqlDbType.Text) { Value = query.Scope.IndexName });
            command.Parameters.Add(new NpgsqlParameter("notebookId", NpgsqlDbType.Uuid) { Value = (object?)query.Scope.NotebookId ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("candidateLimit", NpgsqlDbType.Integer) { Value = query.CandidateLimit });
            command.Parameters.Add(new NpgsqlParameter("maxResults", NpgsqlDbType.Integer) { Value = query.MaxResults });
            command.Parameters.Add(new NpgsqlParameter("vectorWeight", NpgsqlDbType.Double) { Value = query.VectorWeight });
            command.Parameters.Add(new NpgsqlParameter("textWeight", NpgsqlDbType.Double) { Value = query.TextWeight });

            var hits = new List<SearchHit>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                hits.Add(new SearchHit(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    reader.GetDouble(6),
                    reader.GetDouble(7),
                    reader.GetDouble(8)));
            }

            return EngineResult<IReadOnlyList<SearchHit>>.Ok(hits);
        }
        catch (Exception ex) when (ex is NpgsqlException)
        {
            if (PostgresErrors.Translate(ex, _profile, _host) is { } fatal)
            {
                throw fatal;
            }

            return EngineResult<IReadOnlyList<SearchHit>>.Fail(PostgresErrors.ToEngineError((PostgresException)ex));
        }
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
```

> **Note for the implementer:** `PostgresErrors.Translate` returns `null` only for `PostgresException` query errors, so the cast in the catch block is safe. (`UseVector()` and `ReloadTypesAsync()` were confirmed by reflection against Pgvector 0.3.2 / Pgvector.EntityFrameworkCore 0.3.0 / Npgsql 9.)

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~PostgresStoreTests|FullyQualifiedName~PostgresSchemaTests"`
Expected: PASS (all cases).

- [ ] **Step 8: Commit**

```bash
git add HybridSearchParity.sln src/Parity.Postgres src/Parity.Cli/Parity.Cli.csproj tests/Parity.Tests
git commit -m "feat(parity): add Postgres store porting the hybrid query to pgvector and tsvector"
```

---
### Task 13: Postgres diagnostics and a cross-engine check on the mini corpus

**Files:**
- Create: `src/Parity.Postgres/PostgresSearchDiagnostics.cs`
- Test: `tests/Parity.Tests/Integration/PostgresDiagnosticsTests.cs`, `tests/Parity.Tests/Integration/MiniParityTests.cs`

**Interfaces:**
- Consumes: `PostgresDataSources`, `PostgresErrors`, `PostgresSchema` (Task 12); `VectorParity`, `TextParity` (Task 8); `SqlServerSearchDiagnostics`, `SqlServerSearchStore` (Tasks 10–11).
- Produces:
  - `PostgresSearchDiagnostics(string profile, string connectionString, string textSearchConfig) : ISearchDiagnostics, IAsyncDisposable`
  - `EngineInfo.Details` keys: `serverVersion`, `pgvector`, `textSearchConfig`, `rankFunction`

- [ ] **Step 1: Write the failing tests**

`tests/Parity.Tests/Integration/PostgresDiagnosticsTests.cs`:
```csharp
using Parity.Core;
using Parity.Postgres;
using Parity.Tests.Support;

namespace Parity.Tests.Integration;

[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresDiagnosticsTests(IntegrationFixture fixture)
{
    private async Task<PostgresSearchDiagnostics> DiagnosticsAsync() =>
        new("mini-postgres", await fixture.MiniPostgresAsync(), PostgresSchema.CustomConfigName);

    private static float[] VectorOf(string key) => MiniCorpus.Chunks.Single(c => c.Id == MiniCorpus.Id(key)).Embedding;

    [Fact]
    public async Task VectorSearchRanksAChunksOwnVectorFirst()
    {
        await using var diagnostics = await DiagnosticsAsync();
        var hits = (await diagnostics.VectorSearchAsync(VectorOf("m01"), MiniCorpus.ScopeP1, 10, CancellationToken.None)).Value!;
        Assert.Equal(5, hits.Count);
        Assert.Equal(MiniCorpus.Id("m01"), hits[0].ChunkId);
        Assert.True(hits[0].Score >= 1 - 1e-6);
    }

    [Fact]
    public async Task TextMatchReturnsTheFullSetWithoutTopN()
    {
        await using var diagnostics = await DiagnosticsAsync();
        Assert.Equal(12, (await diagnostics.TextMatchAsync("alpha beta", MiniCorpus.ScopeP2, CancellationToken.None)).Value!.Count);
        Assert.Equal(new[] { MiniCorpus.Id("m17") }, (await diagnostics.TextMatchAsync("alpha beta", MiniCorpus.ScopeP1, CancellationToken.None)).Value!.Select(m => m.ChunkId));
    }

    [Fact]
    public async Task ScoreChunksReportsTheTopNCutAndTheVariantKeepsTheChunk()
    {
        await using var diagnostics = await DiagnosticsAsync();
        var ids = new[] { MiniCorpus.Id("m17") };

        var asIs = Assert.Single((await diagnostics.ScoreChunksAsync(MiniCorpus.Query("alpha beta", MiniCorpus.ScopeP1), ids, CancellationToken.None)).Value!);
        Assert.True(asIs.Matched);
        Assert.False(asIs.InCandidates);
        Assert.Equal(0, asIs.TextScore);
        Assert.True(asIs.RawRank < asIs.NthCandidateRank);

        var scoped = Assert.Single((await diagnostics.ScoreChunksAsync(MiniCorpus.Query("alpha beta", MiniCorpus.ScopeP1, filterBeforeTopN: true), ids, CancellationToken.None)).Value!);
        Assert.True(scoped.InCandidates);
        Assert.Equal(scoped.RawRank!.Value, scoped.TextScore, 12);
    }

    [Fact]
    public async Task ScoreChunksAgreesWithTheHybridQuery()
    {
        var cs = await fixture.MiniPostgresAsync();
        var query = MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1);
        await using var store = new PostgresSearchStore("mini-postgres", cs, PostgresSchema.CustomConfigName);
        await using var diagnostics = new PostgresSearchDiagnostics("mini-postgres", cs, PostgresSchema.CustomConfigName);
        var hits = (await store.HybridSearchAsync(query, CancellationToken.None)).Value!;
        var scores = (await diagnostics.ScoreChunksAsync(query, hits.Select(h => h.ChunkId).ToList(), CancellationToken.None)).Value!.ToDictionary(s => s.ChunkId);

        Assert.All(hits, h =>
        {
            Assert.Equal(h.VectorScore, scores[h.ChunkId].VectorScore, 9);
            Assert.Equal(h.TextScore, scores[h.ChunkId].TextScore, 9);
        });
    }

    [Fact]
    public async Task TokenizeMarksStopWords()
    {
        await using var diagnostics = await DiagnosticsAsync();
        var tokens = (await diagnostics.TokenizeAsync("the end of the line", CancellationToken.None)).Value!;
        Assert.Contains(tokens, t => t.Text == "the" && t.IsStopWord);
        Assert.Contains(tokens, t => t.Text == "end" && !t.IsStopWord && t.Normalized == "end");
    }

    [Fact]
    public async Task DescribeReportsVersionsAndConfig()
    {
        await using var diagnostics = await DiagnosticsAsync();
        var info = await diagnostics.DescribeAsync(CancellationToken.None);
        Assert.Equal("Postgres", info.Provider);
        Assert.Contains("PostgreSQL 18", info.Version);
        Assert.Equal("0.8.7", info.Details["pgvector"]);
        Assert.Equal(PostgresSchema.CustomConfigName, info.Details["textSearchConfig"]);
    }

    [Fact]
    public async Task CoverageCountsEveryRow()
    {
        await using var diagnostics = await DiagnosticsAsync();
        var coverage = await diagnostics.GetIndexCoverageAsync(CancellationToken.None);
        Assert.Equal((18, 18), (coverage.Rows, coverage.IndexedRows));
    }

    [Fact]
    public async Task MissingDatabaseIsReportedAsNotSeeded()
    {
        await using var diagnostics = new PostgresSearchDiagnostics("absent", fixture.PostgresConnectionString("parity_absent"), PostgresSchema.CustomConfigName);
        var error = await Assert.ThrowsAsync<NotSeededException>(() => diagnostics.GetIndexCoverageAsync(CancellationToken.None));
        Assert.Contains("seed --profile absent", error.Message);
    }
}
```

`tests/Parity.Tests/Integration/MiniParityTests.cs`:
```csharp
using Parity.Core.Comparison;
using Parity.Core.Model;
using Parity.Postgres;
using Parity.SqlServer;
using Parity.Tests.Support;

namespace Parity.Tests.Integration;

/// <summary>Both engines, the same mini corpus: rule 1 must hold, and plain-word phrases must match identically.</summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MiniParityTests(IntegrationFixture fixture)
{
    public static TheoryData<string> Vectors => new() { "chunk:m01", "chunk:m05", "query:conversation lock" };

    [Theory]
    [MemberData(nameof(Vectors))]
    public async Task VectorParityHoldsInEveryScope(string source)
    {
        var vector = source.StartsWith("chunk:", StringComparison.Ordinal)
            ? MiniCorpus.Chunks.Single(c => c.Id == MiniCorpus.Id(source[6..])).Embedding
            : HashEmbedding.Create(source[6..]);
        var sql = new SqlServerSearchDiagnostics("mini-sqlserver", await fixture.MiniSqlServerAsync());
        await using var pg = new PostgresSearchDiagnostics("mini-postgres", await fixture.MiniPostgresAsync(), PostgresSchema.CustomConfigName);

        foreach (var scope in new[] { MiniCorpus.ScopeP1, MiniCorpus.ScopeP2, MiniCorpus.ScopeN1 })
        {
            var a = (await sql.VectorSearchAsync(vector, scope, 10, CancellationToken.None)).Value!;
            var b = (await pg.VectorSearchAsync(vector, scope, 10, CancellationToken.None)).Value!;
            var result = VectorParity.Compare(a, b, 1e-5, 1e-6);
            Assert.True(result.Pass, result.Failure);
        }
    }

    [Theory]
    [InlineData("alpha beta")]
    [InlineData("conversation lock")]
    public async Task PlainWordPhrasesMatchTheSameChunks(string phrase)
    {
        var sql = new SqlServerSearchDiagnostics("mini-sqlserver", await fixture.MiniSqlServerAsync());
        await using var pg = new PostgresSearchDiagnostics("mini-postgres", await fixture.MiniPostgresAsync(), PostgresSchema.CustomConfigName);

        foreach (var scope in new[] { MiniCorpus.ScopeP1, MiniCorpus.ScopeP2 })
        {
            var diff = TextParity.Diff(
                (await sql.TextMatchAsync(phrase, scope, CancellationToken.None)).Value!,
                (await pg.TextMatchAsync(phrase, scope, CancellationToken.None)).Value!);
            Assert.True(diff.IsEqual, $"baseline-only {diff.BaselineOnly.Count}, candidate-only {diff.CandidateOnly.Count}");
        }
    }

    [Fact]
    public async Task HybridVectorScoresAgree()
    {
        var query = MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1);
        var sql = await new SqlServerSearchStore("mini-sqlserver", await fixture.MiniSqlServerAsync(), IntegrationFixture.PopulationTimeout).HybridSearchAsync(query, CancellationToken.None);
        await using var pgStore = new PostgresSearchStore("mini-postgres", await fixture.MiniPostgresAsync(), PostgresSchema.CustomConfigName);
        var pg = await pgStore.HybridSearchAsync(query, CancellationToken.None);

        var result = VectorParity.CompareHybridVectorScores(sql.Value!, pg.Value!, 1e-5);
        Assert.True(result.Pass, result.Failure);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~PostgresDiagnosticsTests|FullyQualifiedName~MiniParityTests"`
Expected: build FAILS (`PostgresSearchDiagnostics` not found).

- [ ] **Step 3: Write the implementation**

`src/Parity.Postgres/PostgresSearchDiagnostics.cs`:
```csharp
using Npgsql;
using NpgsqlTypes;
using Parity.Core.Contracts;
using Parity.Core.Model;
using Pgvector;

namespace Parity.Postgres;

public sealed class PostgresSearchDiagnostics : ISearchDiagnostics, IAsyncDisposable
{
    private const string VectorSql = """
        SELECT "Id", 1.0 - CAST("Embedding" <=> @vec AS float8)
        FROM "DocumentChunks"
        WHERE "IndexName" = @indexName AND (@notebookId IS NULL OR "NotebookId" = @notebookId)
        ORDER BY "Embedding" <=> @vec, "Id"
        LIMIT @k
        """;

    private const string TextMatchSql = """
        WITH q AS (SELECT phraseto_tsquery(@cfg::regconfig, @query) AS tsq)
        SELECT dc."Id", CAST(ts_rank_cd(dc."ContentTsv", q.tsq, 32) AS float8)
        FROM "DocumentChunks" dc, q
        WHERE dc."ContentTsv" @@ q.tsq
          AND dc."IndexName" = @indexName AND (@notebookId IS NULL OR dc."NotebookId" = @notebookId)
        """;

    private const string ScoreSql = """
        WITH q AS (SELECT phraseto_tsquery(@cfg::regconfig, @query) AS tsq),
        all_matches AS (
            SELECT dc."Id", CAST(ts_rank_cd(dc."ContentTsv", q.tsq, 32) AS float8) AS r
            FROM "DocumentChunks" dc, q
            WHERE dc."ContentTsv" @@ q.tsq
        ),
        cand AS (
            SELECT m."Id", m.r
            FROM all_matches m
            JOIN "DocumentChunks" d ON d."Id" = m."Id"
            WHERE NOT @filterBeforeTopN
               OR (d."IndexName" = @indexName AND (@notebookId IS NULL OR d."NotebookId" = @notebookId))
            ORDER BY m.r DESC
            LIMIT @candidateLimit
        ),
        stats AS (SELECT CASE WHEN COUNT(*) = @candidateLimit THEN MIN(r) END AS nth FROM cand)
        SELECT dc."Id",
               1.0 - CAST(dc."Embedding" <=> @vec AS float8),
               COALESCE(c.r, 0),
               m."Id" IS NOT NULL,
               m.r,
               c."Id" IS NOT NULL,
               s.nth
        FROM "DocumentChunks" dc
        CROSS JOIN stats s
        LEFT JOIN all_matches m ON m."Id" = dc."Id"
        LEFT JOIN cand c ON c."Id" = dc."Id"
        WHERE dc."Id" = ANY(@ids)
        """;

    private const string TokenizeSql = """
        SELECT token, array_to_string(lexemes, ' '), alias, (lexemes IS NOT NULL AND cardinality(lexemes) = 0)
        FROM ts_debug(@cfg::regconfig, @text)
        WHERE alias <> 'blank'
        """;

    private const string DescribeSql = """
        SELECT version(), current_setting('server_version'), (SELECT extversion FROM pg_extension WHERE extname = 'vector')
        """;

    private const string CoverageSql = """
        SELECT CAST(COUNT(*) AS int), CAST(COUNT(*) FILTER (WHERE length("ContentTsv") > 0) AS int) FROM "DocumentChunks"
        """;

    private readonly string _profile;
    private readonly string _config;
    private readonly string _host;
    private readonly NpgsqlDataSource _dataSource;

    public PostgresSearchDiagnostics(string profile, string connectionString, string textSearchConfig)
    {
        PostgresSchema.ValidateConfigName(textSearchConfig);
        _profile = profile;
        _config = textSearchConfig;
        _host = PostgresErrors.Host(connectionString);
        _dataSource = PostgresDataSources.Build(connectionString);
    }

    public Task<EngineResult<IReadOnlyList<VectorHit>>> VectorSearchAsync(float[] vector, Scope scope, int k, CancellationToken ct) =>
        QueryAsync(VectorSql, p =>
        {
            p.Add(Vec(vector));
            p.Add(new NpgsqlParameter("k", NpgsqlDbType.Integer) { Value = k });
            AddScope(p, scope);
        }, r => new VectorHit(r.GetGuid(0), r.GetDouble(1)), ct);

    public Task<EngineResult<IReadOnlyList<TextMatch>>> TextMatchAsync(string text, Scope scope, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(text)
            ? Task.FromResult(EngineResult<IReadOnlyList<TextMatch>>.Ok(Array.Empty<TextMatch>()))
            : QueryAsync(TextMatchSql, p =>
            {
                AddPhrase(p, text);
                AddScope(p, scope);
            }, r => new TextMatch(r.GetGuid(0), r.GetDouble(1)), ct);

    public Task<EngineResult<IReadOnlyList<ChunkScore>>> ScoreChunksAsync(HybridQuery query, IReadOnlyCollection<Guid> chunkIds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Text) || chunkIds.Count == 0)
        {
            return Task.FromResult(EngineResult<IReadOnlyList<ChunkScore>>.Ok(Array.Empty<ChunkScore>()));
        }

        return QueryAsync(ScoreSql, p =>
        {
            AddPhrase(p, query.Text);
            AddScope(p, query.Scope);
            p.Add(Vec(query.Vector));
            p.Add(new NpgsqlParameter("filterBeforeTopN", NpgsqlDbType.Boolean) { Value = query.FilterBeforeTopN });
            p.Add(new NpgsqlParameter("candidateLimit", NpgsqlDbType.Integer) { Value = query.CandidateLimit });
            p.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = chunkIds.ToArray() });
        }, r => new ChunkScore(
            r.GetGuid(0),
            r.GetDouble(1),
            r.GetDouble(2),
            r.GetBoolean(3),
            r.IsDBNull(4) ? null : r.GetDouble(4),
            r.GetBoolean(5),
            r.IsDBNull(6) ? null : r.GetDouble(6)), ct);
    }

    public Task<EngineResult<IReadOnlyList<Token>>> TokenizeAsync(string text, CancellationToken ct) =>
        QueryAsync(TokenizeSql, p =>
        {
            p.Add(new NpgsqlParameter("cfg", NpgsqlDbType.Text) { Value = _config });
            p.Add(new NpgsqlParameter("text", NpgsqlDbType.Text) { Value = text });
        }, r => new Token(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetBoolean(3)), ct);

    public async Task<EngineInfo> DescribeAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(DescribeSql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var details = new Dictionary<string, string>
        {
            ["serverVersion"] = reader.GetString(1),
            ["pgvector"] = reader.IsDBNull(2) ? "not installed" : reader.GetString(2),
            ["textSearchConfig"] = _config,
            ["rankFunction"] = "ts_rank_cd(…, 32)",
        };
        return new EngineInfo("Postgres", reader.GetString(0), details);
    }

    public async Task<IndexCoverage> GetIndexCoverageAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = await OpenAsync(ct);
            await using var command = new NpgsqlCommand(CoverageSql, connection);
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return new IndexCoverage(reader.GetInt32(0), reader.GetInt32(1));
        }
        catch (NpgsqlException ex) when (PostgresErrors.Translate(ex, _profile, _host) is { } fatal)
        {
            throw fatal;
        }
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        try
        {
            return await _dataSource.OpenConnectionAsync(ct);
        }
        catch (Exception ex) when (PostgresErrors.Translate(ex, _profile, _host) is { } fatal)
        {
            throw fatal;
        }
    }

    private async Task<EngineResult<IReadOnlyList<T>>> QueryAsync<T>(string sql, Action<NpgsqlParameterCollection> bind, Func<NpgsqlDataReader, T> map, CancellationToken ct)
    {
        try
        {
            await using var connection = await OpenAsync(ct);
            await using var command = new NpgsqlCommand(sql, connection);
            bind(command.Parameters);
            var rows = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(map(reader));
            }

            return EngineResult<IReadOnlyList<T>>.Ok(rows);
        }
        catch (PostgresException ex) when (PostgresErrors.Translate(ex, _profile, _host) is null)
        {
            return EngineResult<IReadOnlyList<T>>.Fail(PostgresErrors.ToEngineError(ex));
        }
        catch (NpgsqlException ex) when (PostgresErrors.Translate(ex, _profile, _host) is { } fatal)
        {
            throw fatal;
        }
    }

    private void AddPhrase(NpgsqlParameterCollection p, string text)
    {
        p.Add(new NpgsqlParameter("cfg", NpgsqlDbType.Text) { Value = _config });
        p.Add(new NpgsqlParameter("query", NpgsqlDbType.Text) { Value = text });
    }

    private static void AddScope(NpgsqlParameterCollection p, Scope scope)
    {
        p.Add(new NpgsqlParameter("indexName", NpgsqlDbType.Text) { Value = scope.IndexName });
        p.Add(new NpgsqlParameter("notebookId", NpgsqlDbType.Uuid) { Value = (object?)scope.NotebookId ?? DBNull.Value });
    }

    private static NpgsqlParameter Vec(float[] vector) => new("vec", new Vector(new ReadOnlyMemory<float>(vector)));
}
```

- [ ] **Step 4: Run all the integration tests so far**

Run: `dotnet test --filter "Category=Integration"`
Expected: PASS (SQL Server store and diagnostics, Postgres store and diagnostics, and mini parity). If `PlainWordPhrasesMatchTheSameChunks` fails, that's a real engine difference on plain words, which the spec didn't expect. Stop and report it with the two match sets before going further.

- [ ] **Step 5: Commit**

```bash
git add src/Parity.Postgres/PostgresSearchDiagnostics.cs tests/Parity.Tests/Integration
git commit -m "feat(parity): add Postgres diagnostics and cross-engine checks on the mini corpus"
```

---
### Task 14: Comparison runner

**Files:**
- Create: `src/Parity.Core/Comparison/ComparisonRunner.cs`, `src/Parity.Core/Comparison/Snippets.cs`
- Create: `tests/Parity.Tests/Support/InMemoryEngine.cs`, `tests/Parity.Tests/Support/TestData.cs`
- Test: `tests/Parity.Tests/Unit/ComparisonRunnerTests.cs`

**Interfaces:**
- Consumes: Tasks 2, 4, 8 and 9 (`SearchEngine`, `DataSet`, `SavedQuery`, `ParitySettings`, `VectorParity`, `TextParity`, `RankingMetrics`, `DifferenceClassifier`, `KnownDifferences`, `PairResult`); `GroundTruth.PhraseRegex` (Task 5).
- Produces:
  - `ComparisonRunner(SearchEngine baseline, SearchEngine candidate, DataSet data, ParitySettings settings, IReadOnlyList<KnownDifference> known)`, with:
    - `Task<IReadOnlyList<PairResult>> RunAsync(IReadOnlyList<NamedScope> scopes, CancellationToken ct, bool filterBeforeTopN = false, IProgress<string>? progress = null)`
    - `Task<PairResult> RunPairAsync(SavedQuery query, NamedScope scope, CancellationToken ct, bool filterBeforeTopN = false)`
  - `InformationalSummary(string Label, bool Available, string? UnavailableReason, int PairsCounted, double MeanOverlap, double SameTopOneRate, int ExtraMatches, int LostMatches)`, with `Unavailable(label, reason)` and `From(label, pairs)`
  - `Snippets.Around(string content, string query, int radius = 80) → string`
  - Tests: `InMemoryEngine` (implements both contracts; settable `TextRank` and `FailOn`; records `HybridQueries`), `TestData.DataSet(IReadOnlyList<ChunkRecord> chunks, params (string Id, string Group, string Text)[] queries)` and `TestData.Engine(string profile, InMemoryEngine engine, IReadOnlyList<ChunkRecord> chunks)`

- [ ] **Step 1: Add the in-memory engine and test data helpers**

`tests/Parity.Tests/Support/InMemoryEngine.cs`:
```csharp
using Parity.Core.Contracts;
using Parity.Core.Corpus;
using Parity.Core.Model;

namespace Parity.Tests.Support;

/// <summary>
/// A database-free engine with production's algorithm: cosine vectors, a text rank, top-N
/// candidates across every project (unless FilterBeforeTopN), and the same WHERE and ORDER BY.
/// </summary>
public sealed class InMemoryEngine : ISearchStore, ISearchDiagnostics
{
    private readonly List<ChunkRecord> _chunks = [];

    /// <summary>Text rank for (content, query), or null when it doesn't match. Default: n/(n+1) phrase occurrences.</summary>
    public Func<string, string, double?> TextRank { get; init; } = DefaultTextRank;

    /// <summary>Return an error to simulate a database rejecting a query.</summary>
    public Func<string, EngineError?> FailOn { get; init; } = _ => null;

    public List<HybridQuery> HybridQueries { get; } = [];

    public static double? DefaultTextRank(string content, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var count = GroundTruth.PhraseRegex(query).Matches(content).Count;
        return count == 0 ? null : count / (count + 1.0);
    }

    public Task PingServerAsync(CancellationToken ct) => Task.CompletedTask;

    public Task EnsureSchemaAsync(CancellationToken ct)
    {
        _chunks.Clear();
        return Task.CompletedTask;
    }

    public Task LoadAsync(IReadOnlyList<ChunkRecord> chunks, DateTime createdUtc, CancellationToken ct)
    {
        _chunks.AddRange(chunks);
        return Task.CompletedTask;
    }

    public Task WaitUntilSearchableAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<EngineResult<IReadOnlyList<SearchHit>>> HybridSearchAsync(HybridQuery q, CancellationToken ct)
    {
        HybridQueries.Add(q);
        if (string.IsNullOrWhiteSpace(q.Text))
        {
            return Ok<IReadOnlyList<SearchHit>>([]);
        }

        if (FailOn(q.Text) is { } error)
        {
            return Task.FromResult(EngineResult<IReadOnlyList<SearchHit>>.Fail(error));
        }

        var candidates = Candidates(q);
        IReadOnlyList<SearchHit> hits = InScope(q.Scope)
            .Select(c =>
            {
                var vs = Cosine(c.Embedding, q.Vector);
                var ts = candidates.GetValueOrDefault(c.Id);
                return new SearchHit(c.Id, c.Content, c.ContentFileId, null, c.ChunkIndex, c.IndexName, vs, ts, (q.VectorWeight * vs) + (q.TextWeight * ts));
            })
            .Where(h => h.TextScore > 0 || (1 - h.VectorScore) < 1.0)
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.ChunkId)
            .Take(q.MaxResults)
            .ToList();
        return Ok(hits);
    }

    public Task<EngineResult<IReadOnlyList<VectorHit>>> VectorSearchAsync(float[] vector, Scope scope, int k, CancellationToken ct) =>
        Ok<IReadOnlyList<VectorHit>>(InScope(scope)
            .Select(c => new VectorHit(c.Id, Cosine(c.Embedding, vector)))
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.ChunkId)
            .Take(k)
            .ToList());

    public Task<EngineResult<IReadOnlyList<TextMatch>>> TextMatchAsync(string text, Scope scope, CancellationToken ct)
    {
        if (FailOn(text) is { } error)
        {
            return Task.FromResult(EngineResult<IReadOnlyList<TextMatch>>.Fail(error));
        }

        return Ok<IReadOnlyList<TextMatch>>(InScope(scope)
            .Select(c => (c.Id, Rank: TextRank(c.Content, text)))
            .Where(x => x.Rank is not null)
            .Select(x => new TextMatch(x.Id, x.Rank!.Value))
            .ToList());
    }

    public Task<EngineResult<IReadOnlyList<ChunkScore>>> ScoreChunksAsync(HybridQuery q, IReadOnlyCollection<Guid> chunkIds, CancellationToken ct)
    {
        var all = _chunks.Select(c => (c.Id, Rank: TextRank(c.Content, q.Text))).Where(x => x.Rank is not null).ToDictionary(x => x.Id, x => x.Rank!.Value);
        var candidates = Candidates(q);
        double? nth = candidates.Count == q.CandidateLimit ? candidates.Values.Min() : null;
        return Ok<IReadOnlyList<ChunkScore>>(chunkIds
            .Select(id => _chunks.Single(c => c.Id == id))
            .Select(c => new ChunkScore(
                c.Id,
                Cosine(c.Embedding, q.Vector),
                candidates.GetValueOrDefault(c.Id),
                all.ContainsKey(c.Id),
                all.TryGetValue(c.Id, out var raw) ? raw : null,
                candidates.ContainsKey(c.Id),
                nth))
            .ToList());
    }

    public Task<EngineResult<IReadOnlyList<Token>>> TokenizeAsync(string text, CancellationToken ct) =>
        Ok<IReadOnlyList<Token>>(text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => new Token(w, w.ToLowerInvariant(), "word", false)).ToList());

    public Task<EngineInfo> DescribeAsync(CancellationToken ct) =>
        Task.FromResult(new EngineInfo("InMemory", "test", new Dictionary<string, string>()));

    public Task<IndexCoverage> GetIndexCoverageAsync(CancellationToken ct) =>
        Task.FromResult(new IndexCoverage(_chunks.Count, _chunks.Count));

    private Dictionary<Guid, double> Candidates(HybridQuery q) =>
        (q.FilterBeforeTopN ? InScope(q.Scope) : _chunks)
            .Select(c => (c.Id, Rank: TextRank(c.Content, q.Text)))
            .Where(x => x.Rank is not null)
            .OrderByDescending(x => x.Rank)
            .ThenBy(x => x.Id)
            .Take(q.CandidateLimit)
            .ToDictionary(x => x.Id, x => x.Rank!.Value);

    private IEnumerable<ChunkRecord> InScope(Scope scope) =>
        _chunks.Where(c => c.IndexName == scope.IndexName && (scope.NotebookId is null || c.NotebookId == scope.NotebookId));

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    private static Task<EngineResult<T>> Ok<T>(T value) => Task.FromResult(EngineResult<T>.Ok(value));
}
```

`tests/Parity.Tests/Support/TestData.cs`:
```csharp
using Parity.Core.Contracts;
using Parity.Core.Data;
using Parity.Core.Model;

namespace Parity.Tests.Support;

public static class TestData
{
    public static DataSet DataSet(IReadOnlyList<ChunkRecord> chunks, params (string Id, string Group, string Text)[] queries) =>
        new(
            new Manifest("test0000", DateTimeOffset.Parse("2026-10-20T00:00:00Z"), "fake-hash", VectorFile.Dimensions, chunks.Count, queries.Length,
                new Dictionary<string, string>(), new Dictionary<string, string>()),
            chunks,
            queries.Select(q => new SavedQuery(q.Id, q.Group, q.Text, HashEmbedding.Create(q.Text))).ToList(),
            new Dictionary<string, IReadOnlyList<Guid>>());

    /// <summary>An in-memory engine already loaded with <paramref name="chunks"/>, wrapped as a profile.</summary>
    public static SearchEngine Engine(string profile, InMemoryEngine engine, IReadOnlyList<ChunkRecord> chunks)
    {
        engine.LoadAsync(chunks, DateTime.UtcNow, CancellationToken.None).GetAwaiter().GetResult();
        return new SearchEngine(profile, "InMemory", engine, engine);
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/Parity.Tests/Unit/ComparisonRunnerTests.cs`:
```csharp
using System.Text.RegularExpressions;
using Parity.Core.Comparison;
using Parity.Core.Model;
using Parity.Core.Settings;
using Parity.Tests.Support;

namespace Parity.Tests.Unit;

public class ComparisonRunnerTests
{
    private static readonly NamedScope P1 = new("P1", MiniCorpus.ScopeP1);
    private static readonly NamedScope N1 = new("P1/N1", MiniCorpus.ScopeN1);

    private static ComparisonRunner Runner(InMemoryEngine baseline, InMemoryEngine candidate, params (string, string, string)[] queries) =>
        new(TestData.Engine("a", baseline, MiniCorpus.Chunks), TestData.Engine("b", candidate, MiniCorpus.Chunks),
            TestData.DataSet(MiniCorpus.Chunks, queries), new ParitySettings(), []);

    [Fact]
    public async Task IdenticalEnginesAgreeOnEverything()
    {
        var pairs = await Runner(new(), new(), ("Q01", "exact", "conversation lock"), ("Q37", "stress", "alpha beta"))
            .RunAsync([P1, N1], CancellationToken.None);

        Assert.Equal(4, pairs.Count);
        Assert.All(pairs, p =>
        {
            Assert.True(p.Vector.Pass, p.Vector.Failure);
            Assert.True(p.Text!.IsEqual);
            Assert.Equal(new HybridPairMetrics(1.0, true), p.Hybrid);
            Assert.Empty(p.Differences);
        });
        Assert.True(Verdict.Evaluate(pairs, new ThresholdSettings()).Switchable);
    }

    [Fact]
    public async Task AnExtraMatchIsReportedWithEvidenceAndExplained()
    {
        // The candidate also matches the plural "locks", like a stemming configuration would.
        var stemming = new InMemoryEngine
        {
            TextRank = (content, query) => query == "conversation lock" && Regex.IsMatch(content, "conversation locks?", RegexOptions.IgnoreCase)
                ? 0.5
                : InMemoryEngine.DefaultTextRank(content, query),
        };
        var pair = Assert.Single(await Runner(new(), stemming, ("Q01", "exact", "conversation lock")).RunAsync([P1], CancellationToken.None));

        Assert.Equal(new[] { MiniCorpus.Id("m02") }, pair.Text!.CandidateOnly);
        Assert.Equal(ReviewOutcome.Unreviewed, pair.TextReview!.Outcome);
        var snippet = Assert.Single(pair.Evidence!.Snippets);
        Assert.Equal("candidate-only", snippet.Side);
        Assert.Contains("locks", snippet.Snippet);
        Assert.DoesNotContain(pair.Differences, d => d.Category == DifferenceCategory.Unexplained);
        // Whether m02 changes rank depends on the random vectors; if it does, the cause must be its one-sided match.
        Assert.All(pair.Differences.Where(d => d.ChunkId == MiniCorpus.Id("m02")), d => Assert.Equal(DifferenceCategory.MatchedOneSide, d.Category));
    }

    [Fact]
    public async Task AOneSidedErrorIsLeftOutOfTheMetricsAndNeedsReview()
    {
        var failing = new InMemoryEngine { FailOn = text => text == "!!! ???" ? new EngineError("PG42601", "syntax error") : null };
        var pair = Assert.Single(await Runner(new(), failing, ("Q36", "error", "!!! ???")).RunAsync([P1], CancellationToken.None));

        Assert.Equal("candidate", pair.Error!.Side);
        Assert.Null(pair.Hybrid);
        Assert.Equal(ReviewOutcome.Unreviewed, pair.ErrorReview!.Outcome);
        Assert.True(pair.Vector.Pass);
    }

    [Fact]
    public async Task TheQuirkFixedRunPassesTheFlagToBothEngines()
    {
        InMemoryEngine a = new(), b = new();
        await Runner(a, b, ("Q37", "stress", "alpha beta")).RunAsync([P1], CancellationToken.None, filterBeforeTopN: true);
        Assert.All(a.HybridQueries.Concat(b.HybridQueries), q => Assert.True(q.FilterBeforeTopN));
    }

    [Fact]
    public void InformationalSummaryCountsExtraAndLostMatches()
    {
        var pair = new PairResult(new QueryRef("Q01", "exact", "x"), "P1", VectorParityResult.Passed,
            new TextDiff([MiniCorpus.Id("m01")], [MiniCorpus.Id("m02"), MiniCorpus.Id("m03")]), null, null, null, null,
            new HybridPairMetrics(0.8, false), [], [], []);
        var summary = InformationalSummary.From("postgres-english", [pair]);
        Assert.Equal((2, 1, 0.8, 0.0), (summary.ExtraMatches, summary.LostMatches, summary.MeanOverlap, summary.SameTopOneRate));
    }

    [Theory]
    [InlineData("aaa conversation locks expire bbb", "conversation lock", "conversation locks expire")]
    [InlineData("no match at all", "zzz", "no match at all")]
    public void SnippetsCentreOnTheLongestQueryWord(string content, string query, string expectedFragment) =>
        Assert.Contains(expectedFragment, Snippets.Around(content, query));
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ComparisonRunnerTests"`
Expected: build FAILS (`ComparisonRunner` not found).

- [ ] **Step 4: Write the implementation**

`src/Parity.Core/Comparison/Snippets.cs`:
```csharp
using System.Text.RegularExpressions;

namespace Parity.Core.Comparison;

public static class Snippets
{
    /// <summary>About 2 × <paramref name="radius"/> characters around the first occurrence of the query's longest word.</summary>
    public static string Around(string content, string query, int radius = 80)
    {
        var index = -1;
        foreach (var word in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).OrderByDescending(w => w.Length))
        {
            index = content.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                break;
            }
        }

        var start = Math.Max(0, (index < 0 ? 0 : index) - radius);
        var length = Math.Min(content.Length - start, radius * 2);
        return Regex.Replace(content.Substring(start, length), @"\s+", " ").Trim();
    }
}
```

`src/Parity.Core/Comparison/ComparisonRunner.cs`:
```csharp
using Parity.Core.Contracts;
using Parity.Core.Data;
using Parity.Core.Model;
using Parity.Core.Settings;

namespace Parity.Core.Comparison;

public sealed record InformationalSummary(
    string Label,
    bool Available,
    string? UnavailableReason,
    int PairsCounted,
    double MeanOverlap,
    double SameTopOneRate,
    int ExtraMatches,
    int LostMatches)
{
    public static InformationalSummary Unavailable(string label, string reason) => new(label, false, reason, 0, 0, 0, 0, 0);

    public static InformationalSummary From(string label, IReadOnlyList<PairResult> pairs)
    {
        var counted = pairs.Where(p => p.Hybrid is not null).Select(p => p.Hybrid!).ToList();
        return new InformationalSummary(
            label,
            true,
            null,
            counted.Count,
            counted.Count == 0 ? 0 : counted.Average(m => m.OverlapAt5),
            counted.Count == 0 ? 0 : counted.Count(m => m.SameTopOne) / (double)counted.Count,
            pairs.Sum(p => p.Text?.CandidateOnly.Count ?? 0),
            pairs.Sum(p => p.Text?.BaselineOnly.Count ?? 0));
    }
}

/// <summary>Runs every saved query in every scope against both engines and applies rules 1–3.</summary>
public sealed class ComparisonRunner(SearchEngine baseline, SearchEngine candidate, DataSet data, ParitySettings settings, IReadOnlyList<KnownDifference> known)
{
    private const int MaxSnippetsPerPair = 5;

    public async Task<IReadOnlyList<PairResult>> RunAsync(IReadOnlyList<NamedScope> scopes, CancellationToken ct, bool filterBeforeTopN = false, IProgress<string>? progress = null)
    {
        var results = new List<PairResult>();
        var total = data.Queries.Count * scopes.Count;
        foreach (var query in data.Queries)
        {
            foreach (var scope in scopes)
            {
                results.Add(await RunPairAsync(query, scope, ct, filterBeforeTopN));
                if (results.Count % 20 == 0 || results.Count == total)
                {
                    progress?.Report($"{results.Count}/{total} query/scope pairs");
                }
            }
        }

        return results;
    }

    public async Task<PairResult> RunPairAsync(SavedQuery query, NamedScope scope, CancellationToken ct, bool filterBeforeTopN = false)
    {
        var p = settings.Parity;
        var h = settings.Hybrid;
        var queryRef = new QueryRef(query.Id, query.Group, query.Text);

        // Rule 1: vector-only top k. A failure here is infrastructure, not a finding, so it throws.
        var vectorA = (await baseline.Diagnostics.VectorSearchAsync(query.Vector, scope.Scope, p.VectorTopK, ct)).Unwrap($"{baseline.Profile} vector search");
        var vectorB = (await candidate.Diagnostics.VectorSearchAsync(query.Vector, scope.Scope, p.VectorTopK, ct)).Unwrap($"{candidate.Profile} vector search");
        var vector = VectorParity.Compare(vectorA, vectorB, p.VectorTolerance, p.TieTolerance);

        // Rule 2 inputs and the hybrid run.
        var textA = await baseline.Diagnostics.TextMatchAsync(query.Text, scope.Scope, ct);
        var textB = await candidate.Diagnostics.TextMatchAsync(query.Text, scope.Scope, ct);
        var hybridQuery = new HybridQuery(query.Text, query.Vector, scope.Scope, h.MaxResults, h.VectorWeight, h.TextWeight, h.CandidateMultiplier, filterBeforeTopN);
        var hybridA = await baseline.Store.HybridSearchAsync(hybridQuery, ct);
        var hybridB = await candidate.Store.HybridSearchAsync(hybridQuery, ct);

        var error = new ErrorOutcome(textA.Error ?? hybridA.Error, textB.Error ?? hybridB.Error);
        if (error.Side != "none")
        {
            return new PairResult(queryRef, scope.Label, vector, null, null, null, error,
                KnownDifferences.ReviewError(known, query.Id, scope.Label, error), null, [],
                hybridA.IsSuccess ? hybridA.Value : [], hybridB.IsSuccess ? hybridB.Value : []);
        }

        var textDiff = TextParity.Diff(textA.Value!, textB.Value!);
        var textReview = textDiff.IsEqual ? null : KnownDifferences.ReviewText(known, query.Id, scope.Label, textDiff);
        var evidence = textDiff.IsEqual ? null : await GatherEvidenceAsync(query.Text, textDiff, ct);

        var hitsA = hybridA.Value!;
        var hitsB = hybridB.Value!;
        if (vector.Pass)
        {
            vector = VectorParity.CompareHybridVectorScores(hitsA, hitsB, p.VectorTolerance);
        }

        // Rule 3: score every chunk in either top 5 on both engines, then measure and explain.
        var union = hitsA.Take(RankingMetrics.TopN).Concat(hitsB.Take(RankingMetrics.TopN)).Select(x => x.ChunkId).Distinct().ToList();
        var scoresA = (await baseline.Diagnostics.ScoreChunksAsync(hybridQuery, union, ct)).Unwrap($"{baseline.Profile} score chunks").ToDictionary(s => s.ChunkId);
        var scoresB = (await candidate.Diagnostics.ScoreChunksAsync(hybridQuery, union, ct)).Unwrap($"{candidate.Profile} score chunks").ToDictionary(s => s.ChunkId);
        var evaluation = RankingMetrics.EvaluatePair(hitsA, hitsB, SideScores(scoresA), SideScores(scoresB), p.TieTolerance);
        var differences = DifferenceClassifier.Explain(evaluation.DifferingChunks, scoresA, scoresB, hitsA, hitsB, p.VectorTolerance, p.TieTolerance);

        return new PairResult(queryRef, scope.Label, vector, textDiff, textReview, evidence, null, null, evaluation.Metrics, differences, hitsA, hitsB);
    }

    private IReadOnlyDictionary<Guid, double> SideScores(IReadOnlyDictionary<Guid, ChunkScore> scores) =>
        scores.ToDictionary(kv => kv.Key, kv => RankingMetrics.HybridScore(kv.Value, settings.Hybrid.VectorWeight, settings.Hybrid.TextWeight));

    private async Task<TextEvidence> GatherEvidenceAsync(string queryText, TextDiff diff, CancellationToken ct)
    {
        var snippets = new List<SnippetEvidence>();
        var targets = diff.BaselineOnly.Select(id => (Id: id, Side: "baseline-only"))
            .Concat(diff.CandidateOnly.Select(id => (Id: id, Side: "candidate-only")))
            .Take(MaxSnippetsPerPair);
        foreach (var (id, side) in targets)
        {
            var snippet = Snippets.Around(data.FindChunk(id)?.Content ?? string.Empty, queryText);
            snippets.Add(new SnippetEvidence(id, side, snippet, await TokensAsync(baseline, snippet, ct), await TokensAsync(candidate, snippet, ct)));
        }

        return new TextEvidence(await TokensAsync(baseline, queryText, ct), await TokensAsync(candidate, queryText, ct), snippets);
    }

    private static async Task<IReadOnlyList<Token>> TokensAsync(SearchEngine engine, string text, CancellationToken ct)
    {
        var result = await engine.Diagnostics.TokenizeAsync(text, ct);
        return result.IsSuccess ? result.Value : [new Token($"(tokenizer error {result.Error.Code})", null, "error", false)];
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ComparisonRunnerTests"`
Expected: PASS (7 test cases).

- [ ] **Step 6: Commit**

```bash
git add src/Parity.Core/Comparison tests/Parity.Tests/Support tests/Parity.Tests/Unit/ComparisonRunnerTests.cs
git commit -m "feat(parity): add the comparison runner that applies rules 1-3 per query and scope"
```

---
### Task 15: `RESULTS.md` and the console summary

**Files:**
- Create: `src/Parity.Core/Reporting/ComparisonReport.cs`, `Reporting/MarkdownReport.cs`, `Reporting/ConsoleSummary.cs`
- Test: `tests/Parity.Tests/Unit/ReportTests.cs`

**Interfaces:**
- Consumes: `PairResult`, `VerdictResult`, `InformationalSummary`, `KnownDifference`, `ReviewStatus`, `DifferenceCategory` (Tasks 9 and 14); `Manifest` (Task 4); `EngineInfo`, `Token` (Task 2); `ThresholdSettings` (Task 2).
- Produces:
  - `RunContext(DateTimeOffset RunAtUtc, string SampleCommit, bool AppSettingsModified, string BaselineProfile, string CandidateProfile, EngineInfo BaselineInfo, EngineInfo CandidateInfo, Manifest Manifest, ThresholdSettings Thresholds, double VectorTolerance, double TieTolerance, string SqlServerImage, string SqlServerImageDigest, string PostgresImage, string PostgresImageDigest)`
  - `ComparisonReport(RunContext Context, IReadOnlyList<PairResult> Pairs, VerdictResult Verdict, InformationalSummary QuirkFixed, IReadOnlyList<InformationalSummary> Profiles, IReadOnlyList<KnownDifference> StaleEntries, IReadOnlyDictionary<Guid, string> ChunkLabels)`
  - `MarkdownReport.Render(ComparisonReport) → string` and `ConsoleSummary.Render(ComparisonReport) → string`

- [ ] **Step 1: Write the failing tests**

`tests/Parity.Tests/Unit/ReportTests.cs`:
```csharp
using Parity.Core.Comparison;
using Parity.Core.Data;
using Parity.Core.Model;
using Parity.Core.Reporting;
using Parity.Core.Settings;
using static Parity.Tests.Support.Hits;

namespace Parity.Tests.Unit;

public class ReportTests
{
    private static readonly ReviewStatus Unreviewed = new(ReviewOutcome.Unreviewed, null);

    private static ComparisonReport Report(bool appSettingsModified = false, params PairResult[] pairs)
    {
        var context = new RunContext(
            DateTimeOffset.Parse("2026-10-21T09:30:00Z"), "abcd1234", appSettingsModified, "sqlserver", "postgres",
            new EngineInfo("SqlServer", "Microsoft SQL Server 2025 (RTM)", new Dictionary<string, string> { ["fullTextLanguage"] = "1033" }),
            new EngineInfo("Postgres", "PostgreSQL 18.1", new Dictionary<string, string> { ["pgvector"] = "0.8.7" }),
            new Manifest("9f8cd102aaaa", DateTimeOffset.Parse("2026-10-20T00:00:00Z"), "text-embedding-3-small", 1536, 1190, 38,
                new Dictionary<string, string>(), new Dictionary<string, string>()),
            new ThresholdSettings(), 1e-5, 1e-6,
            "ghcr.io/elumenotion/mssql2025-express-fts:main", "[\"ghcr.io/x@sha256:abc\"]", "pgvector/pgvector:0.8.7-pg18", "unknown (docker image inspect failed)");
        return new ComparisonReport(
            context,
            pairs,
            Verdict.Evaluate(pairs, new ThresholdSettings()),
            new InformationalSummary("Quirk fixed on both engines (not production)", true, null, 1, 0.97, 0.95, 0, 0),
            [InformationalSummary.Unavailable("postgres-english", "Profile 'postgres-english' isn't seeded.")],
            [],
            new Dictionary<Guid, string> { [Id("a")] = "docs/a.md#0", [Id("b")] = "inputs/edge-cases.json#E01" });
    }

    private static PairResult Clean(string query = "Q01") =>
        new(new QueryRef(query, "exact", "conversation lock"), "P1", VectorParityResult.Passed, new TextDiff([], []), null, null, null, null,
            new HybridPairMetrics(1.0, true), [], [], []);

    private static PairResult WithTextDiff() =>
        new(new QueryRef("Q19", "edge", "text-embedding-3-small"), "P2", VectorParityResult.Passed, new TextDiff([Id("a")], [Id("b")]), Unreviewed,
            new TextEvidence([new Token("text", "text", "word", false)], [new Token("text-embedding-3-small", "text-embedding-3-small", "hword", false)],
                [new SnippetEvidence(Id("b"), "candidate-only", "default to text-embedding-3-small with", [new Token("the", "the", "Noise Word", true)], [])]),
            null, null, new HybridPairMetrics(0.8, true),
            [new RankDifference(Id("b"), null, 4, DifferenceCategory.MatchedOneSide, "text 0.0000 vs 0.3000")], [], []);

    private static PairResult WithError() =>
        new(new QueryRef("Q36", "error", "!!! ???"), "P1", VectorParityResult.Passed, null, null, null,
            new ErrorOutcome(null, new EngineError("PG42601", "syntax error at or near \"|\"")), Unreviewed, null, [], [], []);

    [Fact]
    public void ASwitchableRunSaysSoWithEveryCondition()
    {
        var markdown = MarkdownReport.Render(Report(false, Clean()));
        Assert.Contains("# Hybrid search parity results", markdown);
        Assert.Contains("**Verdict: SWITCHABLE**", markdown);
        Assert.Contains("| Vector parity | PASS | 1/1 pairs |", markdown);
        Assert.Contains("| Zero unexplained differences | PASS | 0 |", markdown);
        Assert.DoesNotContain("Warning", markdown);
    }

    [Fact]
    public void TheEnvironmentRecordsVersionsDigestsDataAndThresholds()
    {
        var markdown = MarkdownReport.Render(Report(false, Clean()));
        Assert.Contains("Sample commit: `abcd1234`", markdown);
        Assert.Contains("Microsoft SQL Server 2025 (RTM)", markdown);
        Assert.Contains("pgvector: `0.8.7`", markdown);
        Assert.Contains("ghcr.io/x@sha256:abc", markdown);
        Assert.Contains("corpus `9f8cd102`", markdown);
        Assert.Contains("mean top-5 overlap ≥ 0.9", markdown);
    }

    [Fact]
    public void ModifiedThresholdsAreFlagged() =>
        Assert.Contains("`appsettings.json` has uncommitted changes", MarkdownReport.Render(Report(true, Clean())));

    [Fact]
    public void TextDifferencesShowStatusChunksTokensAndSnippets()
    {
        var markdown = MarkdownReport.Render(Report(false, WithTextDiff()));
        Assert.Contains("**Verdict: NOT SWITCHABLE**", markdown);
        Assert.Contains("### `Q19` · P2", markdown);
        Assert.Contains("**UNREVIEWED**", markdown);
        Assert.Contains("inputs/edge-cases.json#E01", markdown);
        Assert.Contains("text-embedding-3-small", markdown);
        Assert.Contains("~~the~~", markdown);
        Assert.Contains("matched-one-side", markdown);
    }

    [Fact]
    public void ErrorsAndUnavailableInformationalRunsAreListed()
    {
        var markdown = MarkdownReport.Render(Report(false, Clean(), WithError()));
        Assert.Contains("## Errors", markdown);
        Assert.Contains("PG42601", markdown);
        Assert.Contains("syntax error at or near \"\\|\"", markdown); // table cells escape pipes
        Assert.Contains("not available: Profile 'postgres-english' isn't seeded.", markdown);
        Assert.Contains("Quirk fixed on both engines (not production)", markdown);
    }

    [Fact]
    public void ConsoleSummaryListsConditionsAndOpenItems()
    {
        var text = ConsoleSummary.Render(Report(false, Clean(), WithTextDiff(), WithError()));
        Assert.Contains("Hybrid search parity: NOT SWITCHABLE (sqlserver vs postgres, 3 pairs)", text);
        Assert.Contains("PASS  Vector parity", text);
        Assert.Contains("FAIL  Full-text and error differences reviewed and accepted", text);
        Assert.Contains("Q19 · P2  full-text  UNREVIEWED", text);
        Assert.Contains("Q36 · P1  error  UNREVIEWED", text);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ReportTests"`
Expected: build FAILS (`Parity.Core.Reporting` not found).

- [ ] **Step 3: Write the implementation**

`src/Parity.Core/Reporting/ComparisonReport.cs`:
```csharp
using Parity.Core.Comparison;
using Parity.Core.Data;
using Parity.Core.Model;
using Parity.Core.Settings;

namespace Parity.Core.Reporting;

public sealed record RunContext(
    DateTimeOffset RunAtUtc,
    string SampleCommit,
    bool AppSettingsModified,
    string BaselineProfile,
    string CandidateProfile,
    EngineInfo BaselineInfo,
    EngineInfo CandidateInfo,
    Manifest Manifest,
    ThresholdSettings Thresholds,
    double VectorTolerance,
    double TieTolerance,
    string SqlServerImage,
    string SqlServerImageDigest,
    string PostgresImage,
    string PostgresImageDigest);

public sealed record ComparisonReport(
    RunContext Context,
    IReadOnlyList<PairResult> Pairs,
    VerdictResult Verdict,
    InformationalSummary QuirkFixed,
    IReadOnlyList<InformationalSummary> Profiles,
    IReadOnlyList<KnownDifference> StaleEntries,
    IReadOnlyDictionary<Guid, string> ChunkLabels)
{
    public string Label(Guid id) => ChunkLabels.TryGetValue(id, out var label) ? label : id.ToString()[..8];

    public static string Status(ReviewStatus? status) => status?.Outcome switch
    {
        ReviewOutcome.Acceptable => "acceptable",
        ReviewOutcome.Blocker => "BLOCKER",
        ReviewOutcome.SnapshotMismatch => "SNAPSHOT CHANGED",
        _ => "UNREVIEWED",
    };

    public static string Kebab(DifferenceCategory category) => category switch
    {
        DifferenceCategory.ScoredDifferently => "scored-differently",
        DifferenceCategory.MatchedOneSide => "matched-one-side",
        DifferenceCategory.TopNCut => "top-n-cut",
        DifferenceCategory.TopNBoundaryTie => "top-n-boundary-tie",
        DifferenceCategory.Displaced => "displaced",
        _ => "unexplained",
    };
}
```

`src/Parity.Core/Reporting/MarkdownReport.cs`:
```csharp
using System.Globalization;
using System.Text;
using Parity.Core.Comparison;
using Parity.Core.Model;

namespace Parity.Core.Reporting;

/// <summary>Spec section 7.5: the RESULTS.md layout.</summary>
public static class MarkdownReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Render(ComparisonReport report)
    {
        var sb = new StringBuilder();
        var c = report.Context;

        sb.AppendLine("# Hybrid search parity results").AppendLine();
        sb.AppendLine(string.Create(Inv,
            $"**Verdict: {(report.Verdict.Switchable ? "SWITCHABLE" : "NOT SWITCHABLE")}**. `{c.BaselineProfile}` (baseline) vs `{c.CandidateProfile}` (candidate), {report.Pairs.Count} query/scope pairs, run {c.RunAtUtc:yyyy-MM-dd HH:mm} UTC."));
        sb.AppendLine();
        if (c.AppSettingsModified)
        {
            sb.AppendLine("> **Warning:** `appsettings.json` has uncommitted changes, so the thresholds below may differ from the committed ones.").AppendLine();
        }

        sb.AppendLine("| Condition | Result | Value |").AppendLine("|---|---|---|");
        foreach (var condition in report.Verdict.Conditions)
        {
            sb.AppendLine($"| {Cell(condition.Name)} | {(condition.Pass ? "PASS" : "FAIL")} | {Cell(condition.Value)} |");
        }

        sb.AppendLine();
        WriteEnvironment(sb, c);
        RuleOne(sb, report);
        RuleTwo(sb, report);
        RuleThree(sb, report);
        Informational(sb, report);
        Errors(sb, report);
        Stale(sb, report);
        return sb.ToString();
    }

    private static void WriteEnvironment(StringBuilder sb, RunContext c)
    {
        var m = c.Manifest;
        sb.AppendLine("## Environment").AppendLine();
        sb.AppendLine($"- Sample commit: `{c.SampleCommit}`{(c.AppSettingsModified ? " (appsettings.json modified)" : "")}");
        sb.AppendLine(string.Create(Inv, $"- Data: corpus `{m.CorpusCommit[..Math.Min(8, m.CorpusCommit.Length)]}`, model `{m.Model}`, {m.ChunkCount} chunks, {m.QueryCount} queries, generated {m.GeneratedUtc:yyyy-MM-dd}"));
        sb.AppendLine(string.Create(Inv,
            $"- Thresholds: mean top-5 overlap ≥ {c.Thresholds.MinMeanTop5Overlap}, same #1 ≥ {c.Thresholds.MinSameTopOneRate * 100:0}%; vector tolerance {c.VectorTolerance:0.#####E0}, tie tolerance {c.TieTolerance:0.#####E0}"));
        foreach (var (role, info) in new[] { ("Baseline", c.BaselineInfo), ("Candidate", c.CandidateInfo) })
        {
            sb.AppendLine($"- {role} ({info.Provider}): {info.Version}");
            foreach (var (key, value) in info.Details.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                sb.AppendLine($"  - {key}: `{value}`");
            }
        }

        sb.AppendLine($"- Images: SQL Server `{c.SqlServerImage}` → {c.SqlServerImageDigest}; Postgres `{c.PostgresImage}` → {c.PostgresImageDigest}");
        sb.AppendLine();
    }

    private static void RuleOne(StringBuilder sb, ComparisonReport report)
    {
        var failures = report.Pairs.Where(p => !p.Vector.Pass).ToList();
        sb.AppendLine("## Rule 1: vector parity").AppendLine();
        sb.AppendLine($"{report.Pairs.Count - failures.Count}/{report.Pairs.Count} pairs pass.");
        foreach (var p in failures)
        {
            sb.AppendLine($"- `{p.Query.Id}` · {p.Scope}: {p.Vector.Failure}");
        }

        sb.AppendLine();
    }

    private static void RuleTwo(StringBuilder sb, ComparisonReport report)
    {
        var differing = report.Pairs.Where(p => p.Text is { IsEqual: false }).ToList();
        sb.AppendLine("## Rule 2: full-text match parity").AppendLine();
        sb.AppendLine($"{differing.Count} pairs differ; {differing.Count(p => p.TextReview?.IsOpen ?? true)} are open. Stop words are shown ~~struck through~~.");
        sb.AppendLine();
        foreach (var p in differing)
        {
            var review = p.TextReview;
            sb.AppendLine($"### `{p.Query.Id}` · {p.Scope}: \"{p.Query.Text}\"").AppendLine();
            sb.AppendLine($"Status: **{ComparisonReport.Status(review)}**{(review?.Entry is { } e ? $" ({e.Category}: {e.Reason})" : "")}").AppendLine();
            sb.AppendLine($"- Baseline only: {Labels(report, p.Text!.BaselineOnly)}");
            sb.AppendLine($"- Candidate only: {Labels(report, p.Text.CandidateOnly)}");
            if (p.Evidence is { } evidence)
            {
                sb.AppendLine($"- Query tokens: baseline {Tokens(evidence.BaselineQueryTokens)}; candidate {Tokens(evidence.CandidateQueryTokens)}");
                foreach (var s in evidence.Snippets)
                {
                    sb.AppendLine($"- {s.Side} `{report.Label(s.ChunkId)}`: \"{s.Snippet}\"");
                    sb.AppendLine($"  - baseline tokens {Tokens(s.BaselineTokens)}; candidate tokens {Tokens(s.CandidateTokens)}");
                }
            }

            sb.AppendLine();
        }
    }

    private static void RuleThree(StringBuilder sb, ComparisonReport report)
    {
        var counted = report.Pairs.Where(p => p.Hybrid is not null).ToList();
        var differences = report.Pairs.SelectMany(p => p.Differences).ToList();
        sb.AppendLine("## Rule 3: hybrid ranking").AppendLine();
        if (counted.Count > 0)
        {
            sb.AppendLine(string.Create(Inv,
                $"Mean top-5 overlap {counted.Average(p => p.Hybrid!.OverlapAt5):F3} over {counted.Count} pairs; same #1 in {counted.Count(p => p.Hybrid!.SameTopOne) * 100.0 / counted.Count:0.0}%."));
        }

        sb.AppendLine($"{differences.Count} differences: {string.Join(", ", differences.GroupBy(d => d.Category).OrderBy(g => g.Key).Select(g => $"{g.Count()} {ComparisonReport.Kebab(g.Key)}"))}.");
        sb.AppendLine();
        sb.AppendLine("| Query | Scope | Overlap@5 | Same #1 | Differences |").AppendLine("|---|---|---|---|---|");
        foreach (var p in counted)
        {
            sb.AppendLine(string.Create(Inv, $"| `{p.Query.Id}` | {p.Scope} | {p.Hybrid!.OverlapAt5:F2} | {(p.Hybrid.SameTopOne ? "yes" : "no")} | {p.Differences.Count} |"));
        }

        sb.AppendLine();
        foreach (var p in report.Pairs.Where(p => p.Differences.Count > 0))
        {
            foreach (var d in p.Differences)
            {
                sb.AppendLine($"- `{p.Query.Id}` · {p.Scope}: `{report.Label(d.ChunkId)}` rank {d.BaselineRank?.ToString(Inv) ?? "–"} → {d.CandidateRank?.ToString(Inv) ?? "–"}: **{ComparisonReport.Kebab(d.Category)}** ({d.Detail})");
            }
        }

        sb.AppendLine();
    }

    private static void Informational(StringBuilder sb, ComparisonReport report)
    {
        sb.AppendLine("## Informational (not part of the verdict)").AppendLine();
        sb.AppendLine("| Run | Pairs | Mean top-5 overlap | Same #1 | Extra matches | Lost matches |").AppendLine("|---|---|---|---|---|---|");
        foreach (var s in report.Profiles.Prepend(report.QuirkFixed))
        {
            sb.AppendLine(s.Available
                ? string.Create(Inv, $"| {Cell(s.Label)} | {s.PairsCounted} | {s.MeanOverlap:F3} | {s.SameTopOneRate * 100:0.0}% | {s.ExtraMatches} | {s.LostMatches} |")
                : $"| {Cell(s.Label)} | not available: {Cell(s.UnavailableReason ?? "")} | | | | |");
        }

        sb.AppendLine();
    }

    private static void Errors(StringBuilder sb, ComparisonReport report)
    {
        var errors = report.Pairs.Where(p => p.Error is not null).ToList();
        if (errors.Count == 0)
        {
            return;
        }

        sb.AppendLine("## Errors").AppendLine();
        sb.AppendLine("| Query | Scope | Side | Baseline | Candidate | Review |").AppendLine("|---|---|---|---|---|---|");
        foreach (var p in errors)
        {
            var e = p.Error!;
            sb.AppendLine($"| `{p.Query.Id}` | {p.Scope} | {e.Side} | {Cell(Describe(e.Baseline))} | {Cell(Describe(e.Candidate))} | {(e.IsOneSided ? ComparisonReport.Status(p.ErrorReview) : "n/a (both failed)")} |");
        }

        sb.AppendLine();
    }

    private static void Stale(StringBuilder sb, ComparisonReport report)
    {
        if (report.StaleEntries.Count == 0)
        {
            return;
        }

        sb.AppendLine("## Stale known-differences entries").AppendLine();
        sb.AppendLine("These entries no longer match any observed difference. They don't affect the verdict; remove them when convenient.");
        foreach (var e in report.StaleEntries)
        {
            sb.AppendLine($"- `{e.QueryId}` · {e.Scope} ({e.Kind}, {e.Decision}): {e.Reason}");
        }

        sb.AppendLine();
    }

    private static string Describe(EngineError? error) => error is null ? "ok" : $"{error.Code}: {error.Message}";

    private static string Labels(ComparisonReport report, IReadOnlyList<Guid> ids) =>
        ids.Count == 0 ? "none" : string.Join(", ", ids.Select(id => $"`{report.Label(id)}`"));

    private static string Tokens(IReadOnlyList<Token> tokens) =>
        tokens.Count == 0 ? "(none)" : "`" + string.Join(" | ", tokens.Select(t => t.IsStopWord ? $"~~{t.Text}~~" : t.Normalized ?? t.Text)) + "`";

    private static string Cell(string value) => value.Replace("|", "\\|").ReplaceLineEndings(" ");
}
```

`src/Parity.Core/Reporting/ConsoleSummary.cs`:
```csharp
using System.Globalization;
using System.Text;
using Parity.Core.Comparison;

namespace Parity.Core.Reporting;

public static class ConsoleSummary
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Render(ComparisonReport report)
    {
        var sb = new StringBuilder();
        var c = report.Context;
        sb.AppendLine($"Hybrid search parity: {(report.Verdict.Switchable ? "SWITCHABLE" : "NOT SWITCHABLE")} ({c.BaselineProfile} vs {c.CandidateProfile}, {report.Pairs.Count} pairs)");
        foreach (var condition in report.Verdict.Conditions)
        {
            sb.AppendLine($"  {(condition.Pass ? "PASS" : "FAIL")}  {condition.Name,-55} {condition.Value}");
        }

        sb.AppendLine("Informational:");
        foreach (var s in report.Profiles.Prepend(report.QuirkFixed))
        {
            sb.AppendLine(s.Available
                ? string.Create(Inv, $"  {s.Label}: overlap {s.MeanOverlap:F3}, same #1 {s.SameTopOneRate * 100:0.0}%, +{s.ExtraMatches}/-{s.LostMatches} matches")
                : $"  {s.Label}: not available ({s.UnavailableReason})");
        }

        var open = new List<string>();
        foreach (var p in report.Pairs)
        {
            if (p.TextReview is { IsOpen: true })
            {
                open.Add($"  {p.Query.Id} · {p.Scope}  full-text  {ComparisonReport.Status(p.TextReview)}  (baseline-only {p.Text!.BaselineOnly.Count}, candidate-only {p.Text.CandidateOnly.Count})");
            }

            if (p.ErrorReview is { IsOpen: true })
            {
                open.Add($"  {p.Query.Id} · {p.Scope}  error  {ComparisonReport.Status(p.ErrorReview)}  ({p.Error!.Side} {p.Error.Code})");
            }

            if (!p.Vector.Pass)
            {
                open.Add($"  {p.Query.Id} · {p.Scope}  vector  FAIL  {p.Vector.Failure}");
            }

            foreach (var d in p.Differences.Where(d => d.Category == DifferenceCategory.Unexplained))
            {
                open.Add($"  {p.Query.Id} · {p.Scope}  hybrid  unexplained  {report.Label(d.ChunkId)} ({d.Detail})");
            }
        }

        if (open.Count > 0)
        {
            sb.AppendLine("Open items:");
            open.ForEach(line => sb.AppendLine(line));
        }

        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ReportTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Parity.Core/Reporting tests/Parity.Tests/Unit/ReportTests.cs
git commit -m "feat(parity): render RESULTS.md and the console summary"
```

---
### Task 16: `seed`, `search` and `compare`

**Files:**
- Create: `src/Parity.Cli/SearchStoreFactory.cs`, `src/Parity.Cli/QueryVectors.cs`, `src/Parity.Cli/EnvironmentProbe.cs`
- Create: `src/Parity.Cli/Commands/SeedCommand.cs`, `Commands/SearchCommand.cs`, `Commands/CompareCommand.cs`
- Replace: `src/Parity.Cli/CliApp.cs`
- Test: `tests/Parity.Tests/Unit/CliCommandTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 2–15.
- Produces:
  - `SearchStoreFactory.Create(ParitySettings settings, string profileName) → SearchEngine`
  - `QueryVectors.ResolveAsync(string text, DataSet data, IEmbeddingClient? embedder, CancellationToken) → Task<(float[] Vector, string Source)>`
  - `GitState(string Commit, bool AppSettingsModified)`, `IEnvironmentProbe` (`ImageDigestAsync`, `GitStateAsync`), and `ProcessEnvironmentProbe(string dockerExecutable = "docker", string gitExecutable = "git")`
  - `CliApp(TextWriter output, TextWriter error, string? sampleRoot = null, Func<ParitySettings, IEmbeddingClient?>? embedderFactory = null, Func<string, IDocsSource>? docsSourceFactory = null, IDictionary<string,string?>? configOverrides = null, Func<ParitySettings, string, SearchEngine>? engineFactory = null, IEnvironmentProbe? probe = null)`
  - `CompareCommand.KnownDifferencesFile` (`"known-differences.json"`)

- [ ] **Step 1: Write the failing tests**

`tests/Parity.Tests/Unit/CliCommandTests.cs`:
```csharp
using System.Text.RegularExpressions;
using Parity.Cli;
using Parity.Core;
using Parity.Core.Contracts;
using Parity.Core.Data;
using Parity.Core.Settings;
using Parity.Tests.Support;

namespace Parity.Tests.Unit;

public sealed class CliCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"parity-cmd-{Guid.NewGuid():N}");
    private readonly Dictionary<string, InMemoryEngine> _engines = new();

    public CliCommandTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "inputs"));
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), """
            { "Profiles": { "a": { "Provider": "InMemory", "ConnectionString": "x" }, "b": { "Provider": "InMemory", "ConnectionString": "x" } },
              "Parity": { "Baseline": "a", "Candidate": "b" } }
            """);
        File.WriteAllText(Path.Combine(_root, DataStore.EdgeCasesFile), "[]");
        File.WriteAllText(Path.Combine(_root, DataStore.QueriesFile), """
            [ { "id": "Q01", "group": "exact", "text": "conversation lock" }, { "id": "Q37", "group": "stress", "text": "alpha beta" } ]
            """);
        var queries = DataStore.ReadQueries(_root);
        DataStore.WriteAtomically(_root, new GeneratedData("test0000", DateTimeOffset.UtcNow, "fake-hash", MiniCorpus.Chunks, queries,
            queries.Select(q => HashEmbedding.Create(q.Text)).ToList(), new Dictionary<string, IReadOnlyList<Guid>>(), new Dictionary<string, string>()), CancellationToken.None);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private (CliApp App, StringWriter Out, StringWriter Err) App(Func<string, InMemoryEngine>? create = null, IDictionary<string, string?>? overrides = null, bool realEngines = false)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Func<ParitySettings, string, SearchEngine>? factory = realEngines ? null : (_, name) =>
        {
            if (!_engines.TryGetValue(name, out var engine))
            {
                engine = _engines[name] = create?.Invoke(name) ?? new InMemoryEngine();
            }

            return new SearchEngine(name, "InMemory", engine, engine);
        };
        var app = new CliApp(output, error, _root, embedderFactory: _ => null, configOverrides: overrides, engineFactory: factory, probe: new FixedProbe());
        return (app, output, error);
    }

    private sealed class FixedProbe : IEnvironmentProbe
    {
        public Task<string> ImageDigestAsync(string image, CancellationToken ct) => Task.FromResult("sha256:test");

        public Task<GitState> GitStateAsync(string sampleRoot, CancellationToken ct) => Task.FromResult(new GitState("abcd1234", false));
    }

    [Fact]
    public async Task SeedThenSearchPrintsRankedHitsForASavedQuery()
    {
        var (app, output, _) = App();
        Assert.Equal(ExitCodes.Success, await app.RunAsync(["seed", "--all"], CancellationToken.None));
        Assert.Equal(ExitCodes.Success, await app.RunAsync(["search", "conversation", "lock", "--profile", "a"], CancellationToken.None));
        Assert.Contains("Seeding a", output.ToString());
        Assert.Contains("saved Q01", output.ToString());
        Assert.Contains("mini/m01", output.ToString());
    }

    [Fact]
    public async Task CompareWritesResultsAndExitsZeroWhenTheEnginesAgree()
    {
        var (app, output, _) = App();
        await app.RunAsync(["seed", "--all"], CancellationToken.None);
        var outPath = Path.Combine(_root, "RESULTS.md");

        Assert.Equal(ExitCodes.Success, await app.RunAsync(["compare", "--out", outPath], CancellationToken.None));
        Assert.Contains("**Verdict: SWITCHABLE**", File.ReadAllText(outPath));
        Assert.Contains("Hybrid search parity: SWITCHABLE", output.ToString());
    }

    [Fact]
    public async Task CompareExitsOneWhenNotSwitchable()
    {
        var (app, output, _) = App(name => name == "b"
            ? new InMemoryEngine { TextRank = (c, q) => Regex.IsMatch(c, "conversation locks?", RegexOptions.IgnoreCase) && q == "conversation lock" ? 0.5 : InMemoryEngine.DefaultTextRank(c, q) }
            : new InMemoryEngine());
        await app.RunAsync(["seed", "--all"], CancellationToken.None);

        Assert.Equal(ExitCodes.NotSwitchable, await app.RunAsync(["compare"], CancellationToken.None));
        Assert.Contains("NOT SWITCHABLE", output.ToString());
        Assert.Contains("full-text  UNREVIEWED", output.ToString());
    }

    [Fact]
    public async Task CompareRefusesAnUnseededProfile()
    {
        var (app, _, error) = App();
        Assert.Equal(ExitCodes.ConfigurationOrData, await app.RunAsync(["compare"], CancellationToken.None));
        Assert.Contains("seed --profile a", error.ToString());
    }

    [Fact]
    public async Task UnreachableDatabaseExitsTwoNamingProfileAndHost()
    {
        var (app, _, error) = App(overrides: new Dictionary<string, string?>
        {
            ["Profiles:sql:Provider"] = "SqlServer",
            ["Profiles:sql:ConnectionString"] = "Server=127.0.0.1,1;Database=parity;User ID=sa;Password=x;TrustServerCertificate=True",
        }, realEngines: true);

        Assert.Equal(ExitCodes.ConfigurationOrData, await app.RunAsync(["search", "conversation lock", "--profile", "sql"], CancellationToken.None));
        Assert.Contains("Can't reach profile 'sql' (SqlServer) at 127.0.0.1,1", error.ToString());
    }

    [Fact]
    public async Task UnsavedTextNeedsAKey()
    {
        var (app, _, error) = App();
        await app.RunAsync(["seed", "--all"], CancellationToken.None);
        Assert.Equal(ExitCodes.ConfigurationOrData, await app.RunAsync(["compare", "--query", "something new"], CancellationToken.None));
        Assert.Contains("OPENAI_API_KEY", error.ToString());
    }

    [Fact]
    public async Task SavedQueriesMatchAfterTrimmingButNotAcrossCase()
    {
        var data = DataStore.Load(_root);
        var embedder = new FakeEmbeddingClient();
        Assert.Equal("saved Q01", (await QueryVectors.ResolveAsync("  conversation lock ", data, embedder, CancellationToken.None)).Source);
        Assert.Equal(0, embedder.Calls);
        Assert.Equal("live embedding", (await QueryVectors.ResolveAsync("Conversation Lock", data, embedder, CancellationToken.None)).Source);
        Assert.Equal(1, embedder.Calls);
    }

    [Fact]
    public void FactoryRejectsUnknownProfilesAndProviders()
    {
        var settings = new ParitySettings();
        settings.Profiles["sqlserver"] = new ProfileSettings { Provider = "SqlServer", ConnectionString = "Server=x" };
        settings.Profiles["odd"] = new ProfileSettings { Provider = "Oracle", ConnectionString = "x" };

        Assert.Contains("Configured profiles: odd, sqlserver", Assert.Throws<ParityConfigurationException>(() => SearchStoreFactory.Create(settings, "nope")).Message);
        Assert.Contains("Valid providers: SqlServer, Postgres", Assert.Throws<ParityConfigurationException>(() => SearchStoreFactory.Create(settings, "odd")).Message);
    }

    [Fact]
    public async Task FactoryBuildsEachProviderWithoutConnecting()
    {
        var settings = new ParitySettings();
        settings.Profiles["s"] = new ProfileSettings { Provider = "SqlServer", ConnectionString = "Server=localhost,14333;Database=parity" };
        settings.Profiles["p"] = new ProfileSettings { Provider = "Postgres", ConnectionString = "Host=localhost;Port=54329;Database=parity", TextSearchConfig = "english" };

        await using var s = SearchStoreFactory.Create(settings, "s");
        await using var p = SearchStoreFactory.Create(settings, "p");
        Assert.Equal(("SqlServer", "Postgres"), (s.Provider, p.Provider));
    }

    [Fact]
    public async Task MissingExecutablesReportUnknown()
    {
        var probe = new ProcessEnvironmentProbe("definitely-not-docker-xyz", "definitely-not-git-xyz");
        Assert.StartsWith("unknown", await probe.ImageDigestAsync("pgvector/pgvector:0.8.7-pg18", CancellationToken.None));
        Assert.Equal(new GitState("unknown", false), await probe.GitStateAsync(_root, CancellationToken.None));
    }

    [Fact]
    public async Task GitStateReadsTheRealRepository()
    {
        var state = await new ProcessEnvironmentProbe().GitStateAsync(RepoPaths.FindSampleRoot(), CancellationToken.None);
        Assert.Matches("^[0-9a-f]{8}$", state.Commit);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~CliCommandTests"`
Expected: build FAILS (`SearchStoreFactory`, `QueryVectors`, `IEnvironmentProbe` and the new `CliApp` parameters not found).

- [ ] **Step 3: Write the factory, query vectors and environment probe**

`src/Parity.Cli/SearchStoreFactory.cs`:
```csharp
using Parity.Core;
using Parity.Core.Contracts;
using Parity.Core.Settings;
using Parity.Postgres;
using Parity.SqlServer;

namespace Parity.Cli;

/// <summary>The one place a profile's Provider becomes a concrete implementation.</summary>
public static class SearchStoreFactory
{
    public static SearchEngine Create(ParitySettings settings, string profileName)
    {
        if (!settings.Profiles.TryGetValue(profileName, out var profile))
        {
            throw new ParityConfigurationException(
                $"Unknown profile '{profileName}'. Configured profiles: {string.Join(", ", settings.Profiles.Keys.Order(StringComparer.Ordinal))}.");
        }

        if (string.IsNullOrWhiteSpace(profile.ConnectionString))
        {
            throw new ParityConfigurationException($"Profile '{profileName}' has no ConnectionString.");
        }

        switch (profile.Provider)
        {
            case "SqlServer":
                return new SearchEngine(
                    profileName,
                    "SqlServer",
                    new SqlServerSearchStore(profileName, profile.ConnectionString, TimeSpan.FromSeconds(settings.Parity.FullTextPopulationTimeoutSeconds)),
                    new SqlServerSearchDiagnostics(profileName, profile.ConnectionString));
            case "Postgres":
                var config = profile.TextSearchConfig ?? PostgresSchema.CustomConfigName;
                var store = new PostgresSearchStore(profileName, profile.ConnectionString, config);
                var diagnostics = new PostgresSearchDiagnostics(profileName, profile.ConnectionString, config);
                return new SearchEngine(profileName, "Postgres", store, diagnostics, new Disposables(store, diagnostics));
            default:
                throw new ParityConfigurationException(
                    $"Profile '{profileName}' has unknown Provider '{profile.Provider}'. Valid providers: SqlServer, Postgres.");
        }
    }

    private sealed class Disposables(params IAsyncDisposable[] items) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            foreach (var item in items)
            {
                await item.DisposeAsync();
            }
        }
    }
}
```

`src/Parity.Cli/QueryVectors.cs`:
```csharp
using Parity.Core;
using Parity.Core.Data;
using Parity.Core.Embeddings;

namespace Parity.Cli;

public static class QueryVectors
{
    /// <summary>A saved query's vector when the text equals one after trimming; otherwise one live OpenAI call.</summary>
    public static async Task<(float[] Vector, string Source)> ResolveAsync(string text, DataSet data, IEmbeddingClient? embedder, CancellationToken ct)
    {
        if (data.FindQueryByText(text) is { } saved)
        {
            return (saved.Vector, $"saved {saved.Id}");
        }

        if (embedder is null)
        {
            throw new ParityConfigurationException(
                $"\"{text.Trim()}\" isn't a saved query, so it needs a live OpenAI embedding. Set OPENAI_API_KEY, or use a query from inputs/queries.json.");
        }

        var vectors = await embedder.EmbedAsync([text.Trim()], ct);
        return (vectors[0], "live embedding");
    }
}
```

`src/Parity.Cli/EnvironmentProbe.cs`:
```csharp
using System.ComponentModel;
using System.Diagnostics;

namespace Parity.Cli;

public sealed record GitState(string Commit, bool AppSettingsModified);

public interface IEnvironmentProbe
{
    Task<string> ImageDigestAsync(string image, CancellationToken ct);

    Task<GitState> GitStateAsync(string sampleRoot, CancellationToken ct);
}

/// <summary>Asks docker and git for the facts RESULTS.md records. A missing tool gives "unknown", never a failure.</summary>
public sealed class ProcessEnvironmentProbe(string dockerExecutable = "docker", string gitExecutable = "git") : IEnvironmentProbe
{
    public async Task<string> ImageDigestAsync(string image, CancellationToken ct)
    {
        var (ok, output) = await RunAsync(dockerExecutable, ["image", "inspect", "--format", "{{json .RepoDigests}}", image], null, ct);
        return ok && !string.IsNullOrWhiteSpace(output) ? output.Trim() : "unknown (docker image inspect failed)";
    }

    public async Task<GitState> GitStateAsync(string sampleRoot, CancellationToken ct)
    {
        var (commitOk, commit) = await RunAsync(gitExecutable, ["rev-parse", "--short=8", "HEAD"], sampleRoot, ct);
        var (statusOk, status) = await RunAsync(gitExecutable, ["status", "--porcelain", "--", "appsettings.json"], sampleRoot, ct);
        return new GitState(commitOk ? commit.Trim() : "unknown", statusOk && !string.IsNullOrWhiteSpace(status));
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
```

- [ ] **Step 4: Write the commands**

`src/Parity.Cli/Commands/SeedCommand.cs`:
```csharp
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
```

`src/Parity.Cli/Commands/SearchCommand.cs`:
```csharp
using System.Globalization;
using Parity.Core;
using Parity.Core.Contracts;
using Parity.Core.Corpus;
using Parity.Core.Data;
using Parity.Core.Embeddings;
using Parity.Core.Model;
using Parity.Core.Settings;

namespace Parity.Cli.Commands;

/// <summary>The "top-level app": it knows only ISearchStore and which profile the configuration names.</summary>
public sealed class SearchCommand(TextWriter output, string sampleRoot, Func<ParitySettings, string, SearchEngine> engineFactory, IEmbeddingClient? embedder)
{
    public async Task<int> RunAsync(CliArgs args, ParitySettings settings, CancellationToken ct)
    {
        var text = string.Join(' ', args.Positionals).Trim();
        if (text.Length == 0)
        {
            throw new ParityConfigurationException("search needs query text, for example: search \"conversation lock\".");
        }

        var profileName = args.Get("--profile") ?? settings.Search.Profile;
        var scope = ParityScopes.Find(args.Get("--scope") ?? "P1");
        var data = DataStore.Load(sampleRoot);
        await using var engine = engineFactory(settings, profileName);
        await engine.Store.PingServerAsync(ct);
        var (vector, source) = await QueryVectors.ResolveAsync(text, data, embedder, ct);

        var h = settings.Hybrid;
        var query = new HybridQuery(text, vector, scope.Scope, h.MaxResults, h.VectorWeight, h.TextWeight, h.CandidateMultiplier, args.Has("--quirk-fixed"));
        var result = await engine.Store.HybridSearchAsync(query, ct);

        output.WriteLine($"Profile {profileName} ({engine.Provider}) · scope {scope.Label} · \"{text}\" ({source}){(query.FilterBeforeTopN ? " · quirk fixed (not production)" : "")}");
        if (!result.IsSuccess)
        {
            output.WriteLine($"Database error [{result.Error.Code}]: {result.Error.Message}");
            output.WriteLine("Production catches this and returns 0 results.");
            return ExitCodes.Success;
        }

        output.WriteLine("  #  Score   Vector  Text    Chunk");
        for (var i = 0; i < result.Value.Count; i++)
        {
            var hit = result.Value[i];
            var chunk = data.FindChunk(hit.ChunkId);
            var label = chunk is null ? hit.ChunkId.ToString() : $"{chunk.SourcePath}{(chunk.Kind == ChunkKind.Doc ? $"#{chunk.ChunkIndex}" : "")}";
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {i + 1}  {hit.Score:F4}  {hit.VectorScore:F4}  {hit.TextScore:F4}  {label}"));
        }

        if (result.Value.Count == 0)
        {
            output.WriteLine("  (no results)");
        }

        return ExitCodes.Success;
    }
}
```

`src/Parity.Cli/Commands/CompareCommand.cs`:
```csharp
using System.Globalization;
using Parity.Core;
using Parity.Core.Comparison;
using Parity.Core.Contracts;
using Parity.Core.Corpus;
using Parity.Core.Data;
using Parity.Core.Embeddings;
using Parity.Core.Model;
using Parity.Core.Reporting;
using Parity.Core.Settings;

namespace Parity.Cli.Commands;

public sealed class CompareCommand(
    TextWriter output,
    string sampleRoot,
    Func<ParitySettings, string, SearchEngine> engineFactory,
    IEmbeddingClient? embedder,
    IEnvironmentProbe probe)
{
    public const string KnownDifferencesFile = "known-differences.json";

    public async Task<int> RunAsync(CliArgs args, ParitySettings settings, CancellationToken ct)
    {
        var data = DataStore.Load(sampleRoot);
        if (args.Get("--query") is { } live)
        {
            return await RunLiveAsync(live, settings, data, ct);
        }

        await using var baseline = engineFactory(settings, settings.Parity.Baseline);
        await using var candidate = engineFactory(settings, settings.Parity.Candidate);
        var baselineInfo = await EnsureSeededAsync(baseline, data, ct);
        var candidateInfo = await EnsureSeededAsync(candidate, data, ct);
        var known = KnownDifferences.Load(Path.Combine(sampleRoot, KnownDifferencesFile));
        var progress = new InlineProgress(message => output.WriteLine($"  {message}"));

        output.WriteLine($"Comparing {baseline.Profile} (baseline) with {candidate.Profile} (candidate)...");
        var pairs = await new ComparisonRunner(baseline, candidate, data, settings, known).RunAsync(ParityScopes.All, ct, progress: progress);

        output.WriteLine("Informational: quirk fixed on both engines...");
        var quirkPairs = await new ComparisonRunner(baseline, candidate, data, settings, []).RunAsync(ParityScopes.All, ct, filterBeforeTopN: true);
        var quirk = InformationalSummary.From("Quirk fixed on both engines (not production)", quirkPairs);

        var profiles = new List<InformationalSummary>();
        foreach (var name in settings.Parity.Informational)
        {
            output.WriteLine($"Informational: {name}...");
            profiles.Add(await InformationalProfileAsync(name, baseline, data, settings, ct));
        }

        var verdict = Verdict.Evaluate(pairs, settings.Parity.Thresholds);
        var git = await probe.GitStateAsync(sampleRoot, ct);
        var context = new RunContext(
            DateTimeOffset.UtcNow, git.Commit, git.AppSettingsModified, baseline.Profile, candidate.Profile, baselineInfo, candidateInfo,
            data.Manifest, settings.Parity.Thresholds, settings.Parity.VectorTolerance, settings.Parity.TieTolerance,
            settings.Images.SqlServer, await probe.ImageDigestAsync(settings.Images.SqlServer, ct),
            settings.Images.Postgres, await probe.ImageDigestAsync(settings.Images.Postgres, ct));
        var labels = data.Chunks.ToDictionary(c => c.Id, c => c.Kind == ChunkKind.Edge ? c.SourcePath : $"{c.SourcePath}#{c.ChunkIndex}");
        var report = new ComparisonReport(context, pairs, verdict, quirk, profiles, KnownDifferences.FindStale(known, pairs), labels);

        output.Write(ConsoleSummary.Render(report));
        if (args.Get("--out") is { } outPath)
        {
            File.WriteAllText(outPath, MarkdownReport.Render(report));
            output.WriteLine($"Wrote {outPath}");
        }

        return verdict.Switchable ? ExitCodes.Success : ExitCodes.NotSwitchable;
    }

    private async Task<InformationalSummary> InformationalProfileAsync(string name, SearchEngine baseline, DataSet data, ParitySettings settings, CancellationToken ct)
    {
        try
        {
            await using var engine = engineFactory(settings, name);
            await EnsureSeededAsync(engine, data, ct);
            var pairs = await new ComparisonRunner(baseline, engine, data, settings, []).RunAsync(ParityScopes.All, ct);
            return InformationalSummary.From(name, pairs);
        }
        catch (Exception ex) when (ex is NotSeededException or EngineUnavailableException or ParityConfigurationException)
        {
            return InformationalSummary.Unavailable(name, ex.Message);
        }
    }

    private async Task<int> RunLiveAsync(string text, ParitySettings settings, DataSet data, CancellationToken ct)
    {
        await using var baseline = engineFactory(settings, settings.Parity.Baseline);
        await using var candidate = engineFactory(settings, settings.Parity.Candidate);
        await EnsureSeededAsync(baseline, data, ct);
        await EnsureSeededAsync(candidate, data, ct);
        var (vector, source) = await QueryVectors.ResolveAsync(text, data, embedder, ct);
        var h = settings.Hybrid;

        output.WriteLine($"Live comparison of \"{text.Trim()}\" ({source}). Not part of any verdict.");
        output.WriteLine($"  {baseline.Profile} | {candidate.Profile}");
        foreach (var scope in ParityScopes.All)
        {
            var query = new HybridQuery(text.Trim(), vector, scope.Scope, h.MaxResults, h.VectorWeight, h.TextWeight, h.CandidateMultiplier);
            var a = await baseline.Store.HybridSearchAsync(query, ct);
            var b = await candidate.Store.HybridSearchAsync(query, ct);
            output.WriteLine($"Scope {scope.Label}");
            for (var i = 0; i < h.MaxResults; i++)
            {
                output.WriteLine($"  {i + 1}. {Describe(a, i, data)} | {Describe(b, i, data)}");
            }
        }

        return ExitCodes.Success;
    }

    private static async Task<EngineInfo> EnsureSeededAsync(SearchEngine engine, DataSet data, CancellationToken ct)
    {
        await engine.Store.PingServerAsync(ct);
        var coverage = await engine.Diagnostics.GetIndexCoverageAsync(ct); // throws NotSeededException when the database or table is missing
        if (coverage.Rows != data.Chunks.Count)
        {
            throw new NotSeededException(
                $"Profile '{engine.Profile}' has {coverage.Rows} chunks but data/ has {data.Chunks.Count}. Run `seed --profile {engine.Profile}`.");
        }

        return await engine.Diagnostics.DescribeAsync(ct);
    }

    private static string Describe(EngineResult<IReadOnlyList<SearchHit>> result, int index, DataSet data)
    {
        if (!result.IsSuccess)
        {
            return $"error {result.Error.Code}";
        }

        if (index >= result.Value.Count)
        {
            return "-";
        }

        var hit = result.Value[index];
        var chunk = data.FindChunk(hit.ChunkId);
        var label = chunk is null ? hit.ChunkId.ToString()[..8] : $"{chunk.SourcePath}{(chunk.Kind == ChunkKind.Doc ? $"#{chunk.ChunkIndex}" : "")}";
        return string.Create(CultureInfo.InvariantCulture, $"{label} {hit.Score:F4}");
    }

    /// <summary>Reports synchronously, so progress lines stay in order with the rest of the output.</summary>
    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
```

- [ ] **Step 5: Replace `CliApp.cs`**

`src/Parity.Cli/CliApp.cs` (full replacement):
```csharp
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
```

- [ ] **Step 6: Run the whole fast suite**

Run: `dotnet test --filter "Category!=Integration"`
Expected: PASS (every unit test, including the earlier `CliTests`, which still pass against the new `CliApp`). `UnreachableDatabaseExitsTwoNamingProfileAndHost` takes about 5 seconds, because the ping's connect timeout is 5.

- [ ] **Step 7: Commit**

```bash
git add src/Parity.Cli tests/Parity.Tests/Unit/CliCommandTests.cs
git commit -m "feat(parity): add seed, search and compare commands chosen by configuration"
```

---
### Task 17: Generate and commit the real data (needs the user's OpenAI key)

**Files:**
- Create (generated): `data/chunks.jsonl`, `data/embeddings.f32`, `data/query-embeddings.f32`, `data/ground-truth.json`, `data/manifest.json`
- Test: `tests/Parity.Tests/Unit/CommittedDataTests.cs`

**Interfaces:**
- Consumes: `generate-data` (Task 7), `DataStore`, `CorpusBuilder`, `GitDocsSource`, `GroundTruth`, `ProductionSources` (Tasks 3–5).
- Produces: the committed data set that Tasks 18 and 19 load.

- [ ] **Step 1: Check for the key without printing it**

Run: `test -n "$OPENAI_API_KEY" && echo "key present" || dotnet user-secrets list --project src/Parity.Cli | sed 's/=.*/= (set)/'`
Expected: `key present`, or `OpenAI:ApiKey = (set)`. If neither shows, **stop and ask the user** to run `export OPENAI_API_KEY=...` in their shell (or `dotnet user-secrets set OpenAI:ApiKey <key> --project src/Parity.Cli`). Never ask for the key to be pasted into the conversation.

- [ ] **Step 2: Generate the data**

Run: `dotnet run --project src/Parity.Cli -- generate-data`
Expected output, about a minute:
```
Corpus 9f8cd102: 79 files, ~1190 chunks (20 edge cases), 38 queries.
Embedded ~1190 chunks with text-embedding-3-small.
Wrote data/ (~1190 chunk vectors, 38 query vectors).
```
Then run `ls -l data/`. Expected sizes: `embeddings.f32` = chunk count × 6,144 bytes (about 7.3 MB), `query-embeddings.f32` = 233,472 bytes, `chunks.jsonl` about 2.5 MB.

- [ ] **Step 3: Write the tests that pin the committed data**

`tests/Parity.Tests/Unit/CommittedDataTests.cs`:
```csharp
using Parity.Core;
using Parity.Core.Corpus;
using Parity.Core.Data;
using Parity.Core.Model;
using Parity.Core.Production;

namespace Parity.Tests.Unit;

/// <summary>The committed data/ must be exactly what generate-data builds from the pinned corpus.</summary>
public class CommittedDataTests
{
    private static readonly string Root = RepoPaths.FindSampleRoot();
    private static readonly Lazy<DataSet> Data = new(() => DataStore.Load(Root));

    private static IReadOnlyList<ChunkDraft> FreshDrafts() =>
        CorpusBuilder.Build(new GitDocsSource(RepoPaths.FindRepoRoot(), ProductionSources.Commit).ReadDocs(), DataStore.ReadEdgeCases(Root));

    [Fact]
    public void ManifestRecordsThePinnedCorpusModelAndSources()
    {
        var m = Data.Value.Manifest;
        Assert.StartsWith(ProductionSources.Commit, m.CorpusCommit);
        Assert.Equal(("text-embedding-3-small", 1536, 38), (m.Model, m.Dimensions, m.QueryCount));
        Assert.Equal(ProductionSources.Sha256AtCommit.OrderBy(kv => kv.Key), m.ProductionSources.OrderBy(kv => kv.Key));
    }

    [Fact]
    public void ChunksAreExactlyTheCorpusBuiltFromGit()
    {
        var fresh = FreshDrafts();
        Assert.Equal(fresh.Select(d => (d.Id, d.Content, d.IndexName, d.NotebookId)), Data.Value.Chunks.Select(c => (c.Id, c.Content, c.IndexName, c.NotebookId)));
        Assert.Equal(20, Data.Value.Chunks.Count(c => c.Kind == ChunkKind.Edge));
        Assert.InRange(Data.Value.Chunks.Count, 1100, 1300);
    }

    [Fact]
    public void VectorsAreRealUnitLengthEmbeddings()
    {
        foreach (var vector in Data.Value.Chunks.Select(c => c.Embedding).Concat(Data.Value.Queries.Select(q => q.Vector)))
        {
            var norm = Math.Sqrt(vector.Sum(v => (double)v * v));
            Assert.InRange(norm, 0.999, 1.001); // OpenAI embeddings are unit length; the fake test embeddings are too, but are all positive
            Assert.Contains(vector, v => v < 0);
        }
    }

    [Fact]
    public void GroundTruthMatchesAFreshComputation()
    {
        var fresh = GroundTruth.Compute(DataStore.ReadQueries(Root), FreshDrafts());
        Assert.Equal(fresh.Keys.Order(), Data.Value.GroundTruth.Keys.Order());
        foreach (var (id, matches) in fresh)
        {
            Assert.Equal(matches, Data.Value.GroundTruth[id]);
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~CommittedDataTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit the data**

```bash
git add data tests/Parity.Tests/Unit/CommittedDataTests.cs
git commit -m "feat(parity): add generated corpus chunks, embeddings and ground truth at 9f8cd102"
```

---

### Task 18: Full parity integration suite on the real data

**Files:**
- Modify: `tests/Parity.Tests/Integration/IntegrationFixture.cs` (add `FullEnginesAsync` and `FullComparisonAsync`)
- Test: `tests/Parity.Tests/Integration/FullParityTests.cs`

**Interfaces:**
- Consumes: `IntegrationFixture.SeedOnceAsync` (Task 12), `SearchStoreFactory` (Task 16), `ComparisonRunner` (Task 14), `KnownDifferences` (Task 9), `DataStore` (Task 4), `CompareCommand.KnownDifferencesFile` (Task 16).
- Produces: `IntegrationFixture.FullData` (`Lazy<DataSet>`), `FullEnginesAsync() → Task<(SearchEngine Baseline, SearchEngine Candidate)>` and `FullComparisonAsync() → Task<IReadOnlyList<PairResult>>`

- [ ] **Step 1: Extend the fixture**

Add these usings to `tests/Parity.Tests/Integration/IntegrationFixture.cs`:
```csharp
using Parity.Cli;
using Parity.Cli.Commands;
using Parity.Core;
using Parity.Core.Comparison;
using Parity.Core.Corpus;
using Parity.Core.Data;
```

Add these members inside `IntegrationFixture`:
```csharp
    private readonly SemaphoreSlim _comparisonGate = new(1, 1);
    private IReadOnlyList<PairResult>? _fullPairs;

    public Lazy<DataSet> FullData { get; } = new(() => DataStore.Load(RepoPaths.FindSampleRoot()));

    /// <summary>Both engines seeded with the committed data, created through the same factory the CLI uses.</summary>
    public async Task<(SearchEngine Baseline, SearchEngine Candidate)> FullEnginesAsync()
    {
        var data = FullData.Value;
        var createdUtc = data.Manifest.GeneratedUtc.UtcDateTime;
        var sqlCs = await SeedOnceAsync("full-sqlserver", () => SqlServerConnectionString("parity"),
            cs => new SqlServerSearchStore("sqlserver", cs, PopulationTimeout), data.Chunks, createdUtc);
        var pgCs = await SeedOnceAsync("full-postgres", () => PostgresConnectionString("parity"),
            cs => new PostgresSearchStore("postgres", cs, PostgresSchema.CustomConfigName), data.Chunks, createdUtc);

        var settings = new ParitySettings();
        settings.Profiles["sqlserver"] = new ProfileSettings { Provider = "SqlServer", ConnectionString = sqlCs };
        settings.Profiles["postgres"] = new ProfileSettings { Provider = "Postgres", ConnectionString = pgCs, TextSearchConfig = PostgresSchema.CustomConfigName };
        return (SearchStoreFactory.Create(settings, "sqlserver"), SearchStoreFactory.Create(settings, "postgres"));
    }

    /// <summary>The verdict comparison (production behaviour, saved queries, all four scopes), computed once.</summary>
    public async Task<IReadOnlyList<PairResult>> FullComparisonAsync()
    {
        await _comparisonGate.WaitAsync();
        try
        {
            if (_fullPairs is not null)
            {
                return _fullPairs;
            }

            var (baseline, candidate) = await FullEnginesAsync();
            await using (baseline)
            await using (candidate)
            {
                var known = KnownDifferences.Load(Path.Combine(RepoPaths.FindSampleRoot(), CompareCommand.KnownDifferencesFile));
                _fullPairs = await new ComparisonRunner(baseline, candidate, FullData.Value, new ParitySettings(), known)
                    .RunAsync(ParityScopes.All, CancellationToken.None);
            }

            return _fullPairs;
        }
        finally
        {
            _comparisonGate.Release();
        }
    }
```

- [ ] **Step 2: Write the suite**

`tests/Parity.Tests/Integration/FullParityTests.cs`:
```csharp
using Parity.Core.Comparison;
using Parity.Core.Corpus;
using Parity.Core.Data;

namespace Parity.Tests.Integration;

/// <summary>
/// Rules 1 and 2 and "zero unexplained", plus the sanity checks that stop a false pass. The verdict
/// thresholds are deliberately not asserted: they're the experiment's result, not a code-correctness check.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class FullParityTests(IntegrationFixture fixture)
{
    [Fact]
    public async Task BothIndexesCoverEveryRow()
    {
        var (baseline, candidate) = await fixture.FullEnginesAsync();
        await using (baseline)
        await using (candidate)
        {
            var rows = fixture.FullData.Value.Chunks.Count;
            var sql = await baseline.Diagnostics.GetIndexCoverageAsync(CancellationToken.None);
            var pg = await candidate.Diagnostics.GetIndexCoverageAsync(CancellationToken.None);
            Assert.Equal((rows, rows), (sql.Rows, sql.IndexedRows));
            Assert.Equal((rows, rows), (pg.Rows, pg.IndexedRows));
        }
    }

    [Fact]
    public async Task Rule1VectorParityHoldsForEveryPair()
    {
        var failures = (await fixture.FullComparisonAsync()).Where(p => !p.Vector.Pass).Select(p => $"{p.Query.Id}·{p.Scope}: {p.Vector.Failure}").ToList();
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task Rule2EveryTextAndErrorDifferenceIsReviewedAndAccepted()
    {
        var open = (await fixture.FullComparisonAsync())
            .Where(p => (p.TextReview?.IsOpen ?? false) || (p.ErrorReview?.IsOpen ?? false))
            .Select(p => $"{p.Query.Id}·{p.Scope}: text {p.TextReview?.Outcome.ToString() ?? "-"}, error {p.ErrorReview?.Outcome.ToString() ?? "-"}")
            .ToList();
        Assert.True(open.Count == 0, "Open differences (review them into known-differences.json):\n" + string.Join("\n", open));
    }

    [Fact]
    public async Task ThereAreNoUnexplainedHybridDifferences()
    {
        var unexplained = (await fixture.FullComparisonAsync())
            .SelectMany(p => p.Differences.Where(d => d.Category == DifferenceCategory.Unexplained).Select(d => $"{p.Query.Id}·{p.Scope}: {d.ChunkId} ({d.Detail})"))
            .ToList();
        Assert.True(unexplained.Count == 0, string.Join("\n", unexplained));
    }

    [Fact]
    public async Task EachEnginesMatchesOverlapTheGroundTruth()
    {
        var data = fixture.FullData.Value;
        var (baseline, candidate) = await fixture.FullEnginesAsync();
        await using (baseline)
        await using (candidate)
        {
            var problems = new List<string>();
            foreach (var query in data.Queries.Where(q => QueryGroups.HasGroundTruth(q.Group) && data.GroundTruth[q.Id].Count > 0))
            {
                var truth = data.GroundTruth[query.Id].ToHashSet();
                foreach (var engine in new[] { baseline, candidate })
                {
                    var matched = new HashSet<Guid>();
                    foreach (var scope in ParityScopes.All)
                    {
                        var result = await engine.Diagnostics.TextMatchAsync(query.Text, scope.Scope, CancellationToken.None);
                        if (result.IsSuccess)
                        {
                            matched.UnionWith(result.Value.Select(m => m.ChunkId));
                        }
                    }

                    if (matched.Count == 0 || !matched.Overlaps(truth))
                    {
                        problems.Add($"{engine.Profile} {query.Id} \"{query.Text}\": {matched.Count} matches, none in the ground truth");
                    }
                }
            }

            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }

    [Fact]
    public async Task ExactPhraseQueriesSurfaceAGroundTruthChunkInSomeScope()
    {
        var data = fixture.FullData.Value;
        var pairs = await fixture.FullComparisonAsync();
        var problems = new List<string>();
        foreach (var query in data.Queries.Where(q => q.Group == QueryGroups.Exact))
        {
            var truth = data.GroundTruth[query.Id].ToHashSet();
            var forQuery = pairs.Where(p => p.Query.Id == query.Id).ToList();
            if (!forQuery.Any(p => p.BaselineHits.Take(5).Any(h => truth.Contains(h.ChunkId))))
            {
                problems.Add($"baseline {query.Id}");
            }

            if (!forQuery.Any(p => p.CandidateHits.Take(5).Any(h => truth.Contains(h.ChunkId))))
            {
                problems.Add($"candidate {query.Id}");
            }
        }

        Assert.True(problems.Count == 0, "No ground-truth chunk in any scope's top 5: " + string.Join(", ", problems));
    }
}
```

- [ ] **Step 3: Run the suite and record what it finds**

Run: `dotnet test --filter "FullyQualifiedName~FullParityTests"` (expect 10+ minutes: seeding the full corpus into emulated SQL Server, then 152 pairs).

Expected on this first run:
- PASS: `BothIndexesCoverEveryRow`, `EachEnginesMatchesOverlapTheGroundTruth` and `ExactPhraseQueriesSurfaceAGroundTruthChunkInSomeScope`. These check the machinery, so a failure is a bug: stop and debug it.
- `Rule1VectorParityHoldsForEveryPair` and `ThereAreNoUnexplainedHybridDifferences` should PASS. If either fails, that's a **finding about the engines**. Don't change code to make it pass. Record the failure output and raise it with the user in Task 19.
- `Rule2EveryTextAndErrorDifferenceIsReviewedAndAccepted` is **expected to FAIL** until Task 19's review, and lists the open differences. Save that list.

- [ ] **Step 4: Commit**

```bash
git add tests/Parity.Tests/Integration
git commit -m "test(parity): add full-corpus parity suite with false-pass sanity checks"
```

---

### Task 19: First run, the user's review, `RESULTS.md` and the README

**Files:**
- Create: `docker-compose.yml`, `known-differences.json`, `RESULTS.md` (generated), `README.md`

**Interfaces:**
- Consumes: the CLI (Task 16), the data (Task 17) and the full suite (Task 18).
- Produces: the deliverable, `RESULTS.md`, with the verdict.

- [ ] **Step 1: Add the compose file and start the databases**

`docker-compose.yml`:
```yaml
name: hybrid-search-parity

services:
  sqlserver:
    image: ghcr.io/elumenotion/mssql2025-express-fts:main
    platform: linux/amd64 # the image is amd64-only; Docker Desktop emulates it on Apple Silicon
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "Parity_Passw0rd!"
      MSSQL_PID: "Developer"
    ports:
      - "14333:1433"

  postgres:
    image: pgvector/pgvector:0.8.7-pg18
    environment:
      POSTGRES_PASSWORD: "parity"
    ports:
      - "54329:5432"
```

Run:
```bash
docker compose up -d
until docker compose logs sqlserver 2>&1 | grep -q "SQL Server is now ready for client connections"; do sleep 5; done; echo ready
```
Expected: `ready` (this can take a few minutes under emulation).

- [ ] **Step 2: Seed all three profiles**

Run: `dotnet run --project src/Parity.Cli -- seed --all`
Expected: `ready: N/N rows full-text indexed.` for `postgres`, `postgres-english` and `sqlserver`, with N equal to the chunk count from Task 17.

- [ ] **Step 3: Run the comparison**

Run: `dotnet run --project src/Parity.Cli -- compare --out RESULTS.md; echo "exit $?"`
Expected: the console summary. The exit code is `1` if there are open differences, which is normal on the first run. Open `RESULTS.md` and read the Rule 2 and Errors sections.

- [ ] **Step 4: Draft review entries — then STOP for the user's decisions**

For every open item (`UNREVIEWED` or `SNAPSHOT CHANGED`), draft a `known-differences.json` entry. Each draft needs:
- `queryId` and `scope`;
- `kind`;
- the exact `baselineOnly` and `candidateOnly` chunk IDs, or `errorSide` and `errorCode`;
- a `category` taken from the tokenization evidence (`tokenization`, `stop-words`, `punctuation`, `accents`, `error-handling` or `other`);
- a one-sentence `reason` citing what the tokens show.

Show the user a table of the drafts: query, scope, what differs, the proposed category and reason. **Ask them to mark each one `acceptable` or `blocker`.** The decision is theirs. Don't fill in `decision` yourself, and don't continue until they answer.

Also report, separately and before anything else, any Rule 1 failures or unexplained hybrid differences from the console or from Task 18. Those can't be reviewed away, and they decide the verdict.

- [ ] **Step 5: Record the decisions**

Write `known-differences.json` as a JSON array of the entries with the user's decisions. Set `reviewedBy` to the name the user gives, and `reviewedOn` to today's date (`YYYY-MM-DD`). An example of the shape:
```json
[
  {
    "queryId": "Q19",
    "scope": "P1",
    "kind": "text-match",
    "baselineOnly": [],
    "candidateOnly": ["<chunk id from RESULTS.md>"],
    "category": "tokenization",
    "reason": "Postgres indexes 'text-embedding-3-small' as one hyphenated word plus its parts; SQL Server only as separate words.",
    "decision": "acceptable",
    "reviewedBy": "<user>",
    "reviewedOn": "<YYYY-MM-DD>"
  }
]
```

- [ ] **Step 6: Re-run and verify**

Run:
```bash
dotnet run --project src/Parity.Cli -- compare --out RESULTS.md; echo "exit $?"
dotnet test
```
Expected:
- `RESULTS.md` shows no `UNREVIEWED` or `SNAPSHOT CHANGED` entries.
- The exit code is `0` (switchable) or `1` (not switchable), and either is a valid result.
- `dotnet test` passes, including `Rule2EveryTextAndErrorDifferenceIsReviewedAndAccepted`, *unless* a `blocker` was recorded. In that case the test fails by design. Say so explicitly when reporting.

- [ ] **Step 7: Write the README**

`README.md`:
````markdown
# Hybrid search parity: SQL Server vs PostgreSQL

Can GuideAnts' hybrid search (full-text + vector) move from SQL Server to PostgreSQL and give the same results? This sample answers that with evidence. **The current answer is in [RESULTS.md](RESULTS.md).** The design is in [the spec](../../docs/superpowers/specs/2026-10-20-hybrid-search-parity-design.md).

- SQL Server runs production's `HybridSearcher` query, copied verbatim from commit `9f8cd102`. A test fails if production changes.
- Postgres runs the candidate port: pgvector cosine distance, a generated `tsvector`, `phraseto_tsquery`, and `ts_rank_cd(…, 32)`.
- Both databases hold byte-identical chunks and vectors from `data/`, built from `docs/*.md` at `9f8cd102`.
- The `search` command knows only `ISearchStore`; `appsettings.json` decides which database it gets.

## Prerequisites

- Docker Desktop. The SQL Server image is amd64-only, so it's emulated (and slow to start) on Apple Silicon.
- .NET 8 SDK (`global.json` pins 8.0.4xx).
- An OpenAI API key, **only** for `generate-data` and live queries: `export OPENAI_API_KEY=...`, or `dotnet user-secrets set OpenAI:ApiKey <key> --project src/Parity.Cli`.

## Run it

```bash
docker compose up -d                                         # SQL Server on 14333, Postgres on 54329
dotnet run --project src/Parity.Cli -- seed --all            # recreate and load all three profiles
dotnet run --project src/Parity.Cli -- search "conversation lock" --profile postgres --scope P1
dotnet run --project src/Parity.Cli -- compare --out RESULTS.md
dotnet test --filter "Category!=Integration"                 # fast unit tests
dotnet test                                                  # everything, including Testcontainers suites
```

`compare` exits `0` when the verdict is switchable, `1` when it isn't, and `2` on configuration, data or connection errors.

## How the verdict works

"Switchable" requires all five conditions, with thresholds fixed in `appsettings.json` before the first run:

1. Vector results match: same top 10, scores within 1e-5.
2. Every full-text match difference, and every error that happens on one side only, is reviewed in `known-differences.json` and marked `acceptable`.
3. Mean top-5 hybrid overlap is at least 0.9.
4. The same #1 result appears in at least 80% of query/scope pairs.
5. There are zero unexplained hybrid differences.

Two runs are informational and never affect the verdict: production's cross-project top-N quirk fixed on both engines, and the stock `english` Postgres configuration (with stemming).

## Reviewing differences

When `compare` reports an `UNREVIEWED` difference, read its tokenization evidence in `RESULTS.md`. Then add an entry to `known-differences.json`, copying the exact chunk IDs, with `decision` set to `acceptable` or `blocker`. If a later run's difference doesn't match the recorded IDs exactly, the entry shows as `SNAPSHOT CHANGED` and needs another look.

## Regenerating the data

```bash
dotnet run --project src/Parity.Cli -- generate-data   # costs a few cents; reads docs from git at 9f8cd102
```

Chunk IDs are deterministic, so the existing `known-differences.json` entries stay valid when the data is regenerated from the same commit.
````

- [ ] **Step 8: Commit**

```bash
git add docker-compose.yml known-differences.json RESULTS.md README.md
git commit -m "docs(parity): record the reviewed differences and the parity verdict"
```

- [ ] **Step 9: Report to the user**

Tell the user:
- the verdict and the five condition values from `RESULTS.md`;
- any blockers they recorded;
- the informational numbers (the quirk-fixed overlap, and what stemming changes);
- that the branch is pushed only if they approve pushing.

Don't open a PR. Per the spec, this branch stays on the fork.
