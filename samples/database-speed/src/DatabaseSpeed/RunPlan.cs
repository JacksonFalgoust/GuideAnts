namespace DatabaseSpeed;

/// <summary>How much data the seed writes (the same on both databases).</summary>
/// <param name="LongConversationMessages">The conversation the load operation reads (in addition to the bulk conversations).</param>
/// <param name="NotebookFilesPerNotebook">Rows for the path-prefix operation (not in the spec's list; see README).</param>
public sealed record SeedVolumes(
    int Projects,
    int NotebooksPerProject,
    int ConversationsPerNotebook,
    int MessagesPerConversation,
    int UsageEvents,
    int Chunks,
    int LongConversationMessages,
    int NotebookFilesPerNotebook,
    int JobsPerDrain,
    int JobWorkers)
{
    public int Notebooks => Projects * NotebooksPerProject;

    public int Conversations => Notebooks * ConversationsPerNotebook;

    public int TotalMessages => Conversations * MessagesPerConversation;
}

/// <summary>
/// The full plan follows the spec (docs/superpowers/specs/2026-11-03-postgres-db-switch-design.md, "Speed comparison").
/// The smoke plan only proves every operation runs end to end, so it is tiny and writes a different file.
/// </summary>
public sealed record RunPlan(
    bool Smoke,
    SeedVolumes Volumes,
    int WarmupRuns,
    int MeasuredRuns,
    int ColdStartWarmupRuns,
    int ColdStartMeasuredRuns,
    string ResultsFileName)
{
    /// <summary>Both containers get the same limits.</summary>
    public const double ContainerCpus = 4.0;

    public const long ContainerMemoryBytes = 8L * 1024 * 1024 * 1024;

    public static RunPlan Full { get; } = new(
        Smoke: false,
        Volumes: new SeedVolumes(
            Projects: 50,
            NotebooksPerProject: 10,
            ConversationsPerNotebook: 20,
            MessagesPerConversation: 50,
            UsageEvents: 10_000,
            Chunks: 20_000,
            LongConversationMessages: 200,
            NotebookFilesPerNotebook: 20,
            JobsPerDrain: 1_000,
            JobWorkers: 8),
        WarmupRuns: 20,
        MeasuredRuns: 200,
        // One discarded run per provider so the harness's own first-use costs (JIT, EF model build) don't land on
        // whichever provider happens to go first.
        ColdStartWarmupRuns: 1,
        ColdStartMeasuredRuns: 10,
        ResultsFileName: "RESULTS.md");

    public static RunPlan SmokePlan { get; } = new(
        Smoke: true,
        Volumes: new SeedVolumes(
            Projects: 2,
            NotebooksPerProject: 2,
            ConversationsPerNotebook: 2,
            MessagesPerConversation: 10,
            UsageEvents: 200,
            Chunks: 500,
            LongConversationMessages: 200,
            NotebookFilesPerNotebook: 4,
            JobsPerDrain: 100,
            JobWorkers: 8),
        WarmupRuns: 3,
        MeasuredRuns: 10,
        ColdStartWarmupRuns: 0,
        ColdStartMeasuredRuns: 1,
        ResultsFileName: "RESULTS-smoke.md");

    public static RunPlan For(bool smoke) => smoke ? SmokePlan : Full;
}
