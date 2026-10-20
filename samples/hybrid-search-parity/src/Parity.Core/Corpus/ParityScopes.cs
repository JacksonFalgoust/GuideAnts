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
