namespace AI.ChatAgent.Models;

/// <summary>A document loaded into the RAG index.</summary>
public sealed record RagDocument
{
    public required string Id          { get; init; }
    public required string Source      { get; init; }   // filename or URL
    public required string Collection  { get; init; }   // "pdfs" | "files" | "web"
    public required string Content     { get; init; }
    public DateTimeOffset  IndexedAt   { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>A single chunk of a document ready to be embedded.</summary>
public sealed record RagChunk
{
    public required string DocumentId  { get; init; }
    public required string ChunkId     { get; init; }
    public required string Text        { get; init; }
    public required int    ChunkIndex  { get; init; }
    public required string Source      { get; init; }
}

/// <summary>A search result returned from the vector store.</summary>
public sealed record RagSearchResult
{
    public required string Text        { get; init; }
    public required string Source      { get; init; }
    public required double Score       { get; init; }
    public required string Collection  { get; init; }
}

/// <summary>Collections (namespaces) in the memory store.</summary>
public static class RagCollections
{
    public const string Pdfs  = "pdfs";
    public const string Files = "files";
    public const string Web   = "web";

    public static readonly string[] All = [Pdfs, Files, Web];
}
