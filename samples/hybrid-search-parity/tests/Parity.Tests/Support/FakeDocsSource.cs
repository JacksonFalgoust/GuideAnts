using Parity.Core.Corpus;

namespace Parity.Tests.Support;

public sealed class FakeDocsSource(params CorpusFile[] files) : IDocsSource
{
    public string ResolvedCommit => "fakecommit0000000000000000000000000000000";

    public IReadOnlyList<CorpusFile> ReadDocs() => files;
}
