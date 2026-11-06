using System.Runtime.CompilerServices;

// Grant the HyperFi Windows desktop app access to internal Core types.
// The server project (HyperFi.Server) intentionally does NOT have access
// to audio-processing internals — it serves files via HTTP, not sample data.
[assembly: InternalsVisibleTo("HyperFi")]
[assembly: InternalsVisibleTo("HyperFi.Core.Tests")]
