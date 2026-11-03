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
