using System.Security.Cryptography;
using System.Text;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Models;
using Microsoft.EntityFrameworkCore;

namespace DatabaseSpeed;

/// <summary>
/// The data both databases get, generated deterministically (fixed seeds, name-based IDs, whole-second timestamps), so
/// the SQL Server and Postgres databases hold exactly the same rows:
/// <list type="bullet">
/// <item>One user; <see cref="SeedVolumes.Projects"/> projects x <see cref="SeedVolumes.NotebooksPerProject"/> notebooks
/// x <see cref="SeedVolumes.ConversationsPerNotebook"/> conversations x <see cref="SeedVolumes.MessagesPerConversation"/>
/// messages (one user and one assistant message per turn, so half as many turns).</item>
/// <item>A long conversation (<see cref="SeedVolumes.LongConversationMessages"/> messages) the load operation reads, and
/// an empty conversation the save operation appends turns to, both in the first notebook.</item>
/// <item><see cref="SeedVolumes.UsageEvents"/> usage events over the last 45 days.</item>
/// <item>The demo corpus replicated to <see cref="SeedVolumes.Chunks"/> chunks: copy k goes to project k, with its own
/// content files; notebook-scoped chunks go to that project's first notebook.</item>
/// <item><see cref="SeedVolumes.NotebookFilesPerNotebook"/> notebook files per notebook, in four folders.</item>
/// </list>
/// Everything is written through EF with the host's pooled context factory. The seed isn't timed.
/// </summary>
public sealed class SeedData
{
    public const string AssistantName = "Speed Guide";
    public const string ModelDeploymentId = "gpt-4.1-mini";
    public const string PathPrefixFolder = "docs";

    private static readonly string[] NotebookFolders = [PathPrefixFolder, "src", "Output", "Resources"];
    private static readonly string[] Instructions =
    [
        "You are a helpful assistant for this notebook. Answer from the notebook's files and cite them.",
    ];

    private readonly SeedVolumes _volumes;
    private readonly Corpus _corpus;
    private readonly IReadOnlyList<string> _texts;

    public SeedData(SeedVolumes volumes, Corpus corpus, DateTime nowUtc)
    {
        _volumes = volumes;
        _corpus = corpus;
        Now = WholeSecond(nowUtc);
        _texts = corpus.Chunks.Select(c => c.Content).Where(c => c.Length >= 200).ToList();
    }

    /// <summary>The seed's "now" (whole seconds, UTC); the usage window ends here.</summary>
    public DateTime Now { get; }

    public SeedVolumes Volumes => _volumes;

    public Guid UserId => Id("user");

    public Guid LongConversationId => Id("long-conversation");

    public Guid TurnSaveConversationId => Id("turn-save-conversation");

    /// <summary>The project the notebook listing reads (the second, so it isn't the one holding the special conversations).</summary>
    public Guid ListNotebooksProjectId => ProjectId(Math.Min(1, _volumes.Projects - 1));

    public Guid ListConversationsNotebookId => NotebookId(Math.Min(1, _volumes.Projects - 1), 0);

    /// <summary>The project index the searches use: copy 0 of the corpus.</summary>
    public string SearchIndexName => ProjectId(0).ToString();

    public Guid PathPrefixNotebookId => NotebookId(0, 0);

    public int CorpusCopies => (int)Math.Ceiling(_volumes.Chunks / (double)_corpus.Chunks.Count);

    /// <summary>The chunks in <see cref="SearchIndexName"/>: copy 0 of the corpus, whole or cut short.</summary>
    public int ChunksInSearchIndex => Math.Min(_corpus.Chunks.Count, _volumes.Chunks);

    public static Guid Id(string kind, params int[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"database-speed:{kind}:{string.Join('/', parts)}"));
        var guid = bytes[..16];
        guid[7] = (byte)((guid[7] & 0x0F) | 0x50); // version 5-style
        guid[8] = (byte)((guid[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(guid);
    }

    public Guid ProjectId(int p) => Id("project", p);

    public Guid NotebookId(int p, int n) => Id("notebook", p, n);

    public Guid ConversationId(int p, int n, int c) => Id("conversation", p, n, c);

    /// <summary>Catalog models for the model listing (the migrations seed none); every fifth has no DisplayOrder.</summary>
    public const int CatalogModels = 40;

    /// <summary>The demo corpus's chunk count (one copy).</summary>
    public int CorpusChunks => _corpus.Chunks.Count;

    /// <summary>
    /// The rows this seed writes, by table (what RESULTS.md calls the data volumes). <c>AddedToMigrations</c> marks a
    /// table that also holds the migrations' seed rows, so the count is a minimum.
    /// </summary>
    public IReadOnlyList<(string Table, long Rows, bool AddedToMigrations)> PlannedRows()
    {
        var bulkTurns = (long)_volumes.Conversations * (_volumes.MessagesPerConversation / 2);
        var longTurns = _volumes.LongConversationMessages / 2;
        return
        [
            ("Users", 1, false),
            ("Projects", _volumes.Projects, false),
            ("Notebooks", _volumes.Notebooks, false),
            ("NotebookConversations", _volumes.Conversations + 2, false),
            ("ConversationTurns", bulkTurns + longTurns, false),
            ("NotebookConversationMessages", _volumes.TotalMessages + (long)_volumes.LongConversationMessages, false),
            ("UsageEvents", _volumes.UsageEvents, false),
            ("ContentFiles", ChunkPlan().Select(c => c.ContentFileId).Distinct().LongCount(), false),
            ("DocumentChunks", _volumes.Chunks, false),
            ("NotebookFiles", (long)_volumes.Notebooks * _volumes.NotebookFilesPerNotebook, false),
            ("Models", CatalogModels, true),
            ("Tools", 0, true),
        ];
    }

    public async Task WriteAsync(IDbContextFactory<ApplicationDbContext> factory, Action<string> log, CancellationToken ct)
    {
        await WriteProjectsAsync(factory, ct);
        log("projects, notebooks, user");
        await WriteConversationsAsync(factory, log, ct);
        await WriteUsageEventsAsync(factory, ct);
        log($"{_volumes.UsageEvents:N0} usage events");
        await WriteChunksAsync(factory, ct);
        log($"{_volumes.Chunks:N0} chunks");
        await WriteNotebookFilesAsync(factory, ct);
        log($"{(long)_volumes.Notebooks * _volumes.NotebookFilesPerNotebook:N0} notebook files");
        await WriteModelsAsync(factory, ct);
        log($"{CatalogModels} catalog models");
    }

    private async Task WriteModelsAsync(IDbContextFactory<ApplicationDbContext> factory, CancellationToken ct)
    {
        string[] providers = ["openai", "anthropic", "google-gemini", "openrouter", "llama-cpp"];
        await using var db = await CreateAsync(factory, ct);
        for (var i = 0; i < CatalogModels; i++)
        {
            db.Models.Add(new Model
            {
                ModelId = $"speed-model-{i:D2}",
                DisplayName = $"Speed model {i:D2}",
                Provider = providers[i % providers.Length],
                Description = "Seeded by samples/database-speed.",
                ContextWindowTokens = 128_000,
                MaxOutputTokens = 16_000,
                IsActive = i % 8 != 7,
                DisplayOrder = i % 5 == 4 ? null : CatalogModels - i,
                Created = Now.AddDays(-60),
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task WriteProjectsAsync(IDbContextFactory<ApplicationDbContext> factory, CancellationToken ct)
    {
        await using var db = await CreateAsync(factory, ct);
        db.Users.Add(new User
        {
            Id = UserId,
            Name = "Speed Tester",
            Email = "speed.tester@example.invalid",
            SecurityStamp = Id("user-stamp"),
            Created = Now.AddDays(-60),
        });

        for (var p = 0; p < _volumes.Projects; p++)
        {
            db.Projects.Add(new Project
            {
                Id = ProjectId(p),
                Title = $"Speed project {p:D2}",
                Slug = $"speed-project-{p:D2}",
                Description = "Seeded by samples/database-speed.",
                Created = Now.AddDays(-50).AddMinutes(p),
            });

            for (var n = 0; n < _volumes.NotebooksPerProject; n++)
            {
                db.Notebooks.Add(new Notebook
                {
                    Id = NotebookId(p, n),
                    ProjectId = ProjectId(p),
                    Title = $"Notebook {p:D2}-{n:D2}",
                    Slug = $"notebook-{n:D2}",
                    Description = "Seeded notebook.",
                    // The legacy template id has no foreign key; the listings only need one of GuideId/NotebookTemplateId.
                    NotebookTemplateId = Id("template"),
                    Created = Now.AddDays(-49).AddMinutes((p * 100) + n),
                });
            }
        }

        db.NotebookConversations.Add(new NotebookConversation
        {
            Id = LongConversationId,
            NotebookId = NotebookId(0, 0),
            Title = "Long conversation",
            Created = Now.AddDays(-20),
        });
        db.NotebookConversations.Add(new NotebookConversation
        {
            Id = TurnSaveConversationId,
            NotebookId = NotebookId(0, 0),
            Title = "Turn save target",
            Created = Now.AddDays(-1),
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task WriteConversationsAsync(IDbContextFactory<ApplicationDbContext> factory, Action<string> log, CancellationToken ct)
    {
        var random = new Random(1701);
        const int batchConversations = 40;
        var conversations = new List<(Guid Id, Guid NotebookId, DateTime Created)>();
        for (var p = 0; p < _volumes.Projects; p++)
        {
            for (var n = 0; n < _volumes.NotebooksPerProject; n++)
            {
                for (var c = 0; c < _volumes.ConversationsPerNotebook; c++)
                {
                    conversations.Add((ConversationId(p, n, c), NotebookId(p, n), Now.AddDays(-45).AddSeconds(random.Next(0, 40 * 86_400))));
                }
            }
        }

        var written = 0;
        foreach (var batch in conversations.Chunk(batchConversations))
        {
            await using var db = await CreateAsync(factory, ct);
            foreach (var (id, notebookId, created) in batch)
            {
                db.NotebookConversations.Add(new NotebookConversation { Id = id, NotebookId = notebookId, Title = $"Conversation {written++:D5}", Created = created });
                AddTurns(db, id, created, _volumes.MessagesPerConversation / 2, random, userChars: 160, assistantChars: 600);
            }

            await db.SaveChangesAsync(ct);
            if (written % 2000 == 0)
            {
                log($"{written:N0} / {conversations.Count:N0} conversations");
            }
        }

        // The long conversation keeps whole chunks as assistant replies (about 1-2 KB each), like real answers.
        await using (var db = await CreateAsync(factory, ct))
        {
            AddTurns(db, LongConversationId, Now.AddDays(-20), _volumes.LongConversationMessages / 2, random, userChars: 240, assistantChars: int.MaxValue);
            await db.SaveChangesAsync(ct);
        }

        log($"{conversations.Count:N0} conversations ({_volumes.TotalMessages:N0} messages) + the {_volumes.LongConversationMessages}-message conversation");
    }

    private void AddTurns(ApplicationDbContext db, Guid conversationId, DateTime start, int turns, Random random, int userChars, int assistantChars)
    {
        for (var t = 1; t <= turns; t++)
        {
            var at = start.AddMinutes(t * 3);
            db.ConversationTurns.Add(new ConversationTurn
            {
                Id = DeterministicFrom(random),
                NotebookConversationId = conversationId,
                TurnIndex = t,
                AssistantName = AssistantName,
                ModelDeploymentId = ModelDeploymentId,
                Instructions = Instructions[0],
                Created = at,
                Status = "completed",
                TerminalizedAt = at.AddSeconds(20),
                LastUpdated = at.AddSeconds(20),
            });
            db.NotebookConversationMessages.Add(new NotebookConversationMessage
            {
                Id = DeterministicFrom(random),
                NotebookConversationId = conversationId,
                TurnIndex = t,
                MessageSequence = 1,
                Role = ChatRole.User,
                Content = Clip(_texts[random.Next(_texts.Count)], userChars),
                AssistantName = "user",
                ModelDeploymentId = ModelDeploymentId,
                UserId = UserId,
                Created = at,
            });
            db.NotebookConversationMessages.Add(new NotebookConversationMessage
            {
                Id = DeterministicFrom(random),
                NotebookConversationId = conversationId,
                TurnIndex = t,
                MessageSequence = 2,
                Role = ChatRole.Assistant,
                Content = Clip(_texts[random.Next(_texts.Count)], assistantChars),
                AssistantName = AssistantName,
                ModelDeploymentId = ModelDeploymentId,
                IsStreaming = false,
                Created = at.AddSeconds(15),
            });
        }
    }

    private async Task WriteUsageEventsAsync(IDbContextFactory<ApplicationDbContext> factory, CancellationToken ct)
    {
        var random = new Random(2001);
        var categories = new (UsageCategory Category, string Service, string Operation, int Weight)[]
        {
            (UsageCategory.ChatCompletion, "OpenAI", "chat", 70),
            (UsageCategory.ToolCall, "GuideAnts", "tool", 15),
            (UsageCategory.Embeddings, "OpenAI", "embeddings", 10),
            (UsageCategory.Search, "GuideAnts", "search", 5),
        };
        var events = new List<UsageEvent>(_volumes.UsageEvents);
        for (var i = 0; i < _volumes.UsageEvents; i++)
        {
            var p = random.Next(_volumes.Projects);
            var n = random.Next(_volumes.NotebooksPerProject);
            var c = random.Next(_volumes.ConversationsPerNotebook);
            var roll = random.Next(100);
            var kind = categories[0];
            foreach (var candidate in categories)
            {
                if (roll < candidate.Weight)
                {
                    kind = candidate;
                    break;
                }

                roll -= candidate.Weight;
            }

            var input = random.Next(200, 20_000);
            var output = random.Next(50, 2_000);
            var cost = Math.Round((input * 0.0000004m) + (output * 0.0000016m), 4);
            events.Add(new UsageEvent
            {
                Id = DeterministicFrom(random),
                Created = Now.AddSeconds(-random.Next(0, 45 * 86_400)),
                UserId = UserId.ToString(),
                ProjectId = ProjectId(p),
                NotebookId = NotebookId(p, n),
                ConversationId = ConversationId(p, n, c),
                Category = kind.Category,
                Service = kind.Service,
                Operation = kind.Operation,
                ModelDeploymentId = ModelDeploymentId,
                ValueInput = input,
                ValueOutput = output,
                CostUsd = cost,
                MarkupPercent = 1.20m,
                ChargeUsd = Math.Round(cost * 1.20m, 4),
            });
        }

        foreach (var batch in events.Chunk(1000))
        {
            await using var db = await CreateAsync(factory, ct);
            db.UsageEvents.AddRange(batch);
            await db.SaveChangesAsync(ct);
        }
    }

    private sealed record PlannedChunk(CorpusChunk Source, int SourceIndex, int Copy, Guid ProjectId, Guid? NotebookId, Guid ContentFileId, string Label);

    private IEnumerable<PlannedChunk> ChunkPlan()
    {
        var labels = _corpus.Chunks.Select(c => c.ProjectId).Distinct().Select((id, i) => (id, i)).ToDictionary(x => x.id, x => $"p{x.i}");
        var contentFiles = _corpus.Chunks.Select(c => c.ContentFileId).Distinct().Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var written = 0;
        for (var copy = 0; written < _volumes.Chunks; copy++)
        {
            var project = copy % _volumes.Projects;
            for (var index = 0; index < _corpus.Chunks.Count; index++)
            {
                if (written++ >= _volumes.Chunks)
                {
                    yield break;
                }

                var chunk = _corpus.Chunks[index];
                yield return new PlannedChunk(
                    chunk,
                    index,
                    copy,
                    ProjectId(project),
                    chunk.NotebookId is null ? null : NotebookId(project, 0),
                    Id("content-file", copy, contentFiles[chunk.ContentFileId]),
                    labels[chunk.ProjectId]);
            }
        }
    }

    private async Task WriteChunksAsync(IDbContextFactory<ApplicationDbContext> factory, CancellationToken ct)
    {
        var plan = ChunkPlan().ToList();
        await using (var db = await CreateAsync(factory, ct))
        {
            foreach (var file in plan.GroupBy(c => c.ContentFileId))
            {
                var first = file.First();
                var relativePath = $"{first.Label}/{first.Source.SourcePath}";
                var contentFile = new ContentFile
                {
                    Id = file.Key,
                    ProjectId = first.ProjectId,
                    FileName = Path.GetFileName(first.Source.SourcePath),
                    Path = relativePath,
                    RelativePath = relativePath,
                    FileSize = file.Sum(c => (long)Encoding.UTF8.GetByteCount(c.Source.Content)),
                    ContentType = "text/markdown",
                    Created = Now.AddDays(-30),
                };
                contentFile.OnSaveOrUpdate();
                db.ContentFiles.Add(contentFile);
            }

            await db.SaveChangesAsync(ct);
        }

        foreach (var batch in plan.Chunk(200))
        {
            await using var db = await CreateAsync(factory, ct);
            db.DocumentChunks.AddRange(batch.Select(c => new DocumentChunk
            {
                Id = Id("chunk", c.Copy, c.SourceIndex),
                IndexName = c.ProjectId.ToString(),
                ContentFileId = c.ContentFileId,
                ProjectId = c.ProjectId,
                NotebookId = c.NotebookId,
                DocumentId = Clip($"{c.Label}/{c.Source.DocumentId}", 255),
                ChunkIndex = c.Source.ChunkIndex,
                Content = c.Source.Content,
                Embedding = c.Source.Embedding,
                CreatedUtc = Now.AddDays(-30),
            }));
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task WriteNotebookFilesAsync(IDbContextFactory<ApplicationDbContext> factory, CancellationToken ct)
    {
        var random = new Random(3001);
        for (var p = 0; p < _volumes.Projects; p++)
        {
            await using var db = await CreateAsync(factory, ct);
            for (var n = 0; n < _volumes.NotebooksPerProject; n++)
            {
                var notebookId = NotebookId(p, n);
                for (var f = 0; f < _volumes.NotebookFilesPerNotebook; f++)
                {
                    var file = new NotebookFile
                    {
                        Id = Id("notebook-file", p, n, f),
                        NotebookId = notebookId,
                        RelativePath = $"{NotebookFolders[f % NotebookFolders.Length]}/file-{f:D3}.md",
                        FileSize = random.Next(500, 50_000),
                        LastModifiedUtc = Now.AddDays(-random.Next(1, 30)),
                        FileHash = Convert.ToHexString(SHA256.HashData(BitConverter.GetBytes(random.NextInt64()))).ToLowerInvariant(),
                        Created = Now.AddDays(-30),
                    };
                    file.GenerateDocumentId(notebookId);
                    db.NotebookFiles.Add(file);
                }
            }

            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task<ApplicationDbContext> CreateAsync(IDbContextFactory<ApplicationDbContext> factory, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct);
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        return db;
    }

    private static Guid DeterministicFrom(Random random)
    {
        Span<byte> bytes = stackalloc byte[16];
        random.NextBytes(bytes);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private static string Clip(string text, int maxChars) => text.Length <= maxChars ? text : text[..maxChars];

    private static DateTime WholeSecond(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
    }
}
