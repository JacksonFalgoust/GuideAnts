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
