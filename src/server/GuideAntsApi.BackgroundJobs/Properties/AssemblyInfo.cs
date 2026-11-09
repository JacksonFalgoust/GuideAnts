using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GuideAntsApi.Tests")]
// The hybrid search parity harness (samples/hybrid-search-parity) runs the production searchers directly.
[assembly: InternalsVisibleTo("Parity.Production")]
