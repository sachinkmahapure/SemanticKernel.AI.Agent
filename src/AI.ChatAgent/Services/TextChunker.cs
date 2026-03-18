using AI.ChatAgent.Models;

namespace AI.ChatAgent.Services;

/// <summary>
/// Splits a document into overlapping text chunks suitable for embedding.
///
/// Uses a sliding window with overlap so that sentences spanning chunk
/// boundaries are still captured in at least one chunk.
///
/// Default: 512-token chunks with 64-token overlap (roughly 2,000 / 250 chars).
/// </summary>
public static class TextChunker
{
    private const int DefaultChunkSize    = 1800;   // chars ≈ ~450 tokens
    private const int DefaultChunkOverlap = 200;    // chars overlap between chunks

    /// <summary>
    /// Split <paramref name="text"/> into chunks and return as <see cref="RagChunk"/> records.
    /// </summary>
    public static IReadOnlyList<RagChunk> Chunk(
        string documentId,
        string source,
        string text,
        int chunkSize    = DefaultChunkSize,
        int chunkOverlap = DefaultChunkOverlap)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        text = NormaliseWhitespace(text);

        var chunks = new List<RagChunk>();
        var start  = 0;
        var index  = 0;

        while (start < text.Length)
        {
            var end = Math.Min(start + chunkSize, text.Length);

            // Try to break at a paragraph or sentence boundary
            if (end < text.Length)
                end = FindBreakPoint(text, start, end);

            var chunkText = text[start..end].Trim();

            if (!string.IsNullOrWhiteSpace(chunkText))
            {
                chunks.Add(new RagChunk
                {
                    DocumentId = documentId,
                    ChunkId    = $"{documentId}_{index}",
                    Text       = chunkText,
                    ChunkIndex = index,
                    Source     = source,
                });
                index++;
            }

            // Advance with overlap so context isn't lost at boundaries
            start = end - chunkOverlap;
            if (start <= 0 || start >= text.Length) break;
        }

        return chunks;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Find the nearest paragraph/sentence break before <paramref name="end"/>.</summary>
    private static int FindBreakPoint(string text, int start, int end)
    {
        // Prefer paragraph break (\n\n)
        var idx = text.LastIndexOf("\n\n", end, end - start, StringComparison.Ordinal);
        if (idx > start) return idx + 2;

        // Then sentence-ending punctuation followed by space
        foreach (var sentinel in new[] { ". ", "! ", "? ", ".\n", "!\n", "?\n" })
        {
            idx = text.LastIndexOf(sentinel, end, end - start, StringComparison.Ordinal);
            if (idx > start) return idx + sentinel.Length;
        }

        // Fallback: newline
        idx = text.LastIndexOf('\n', end - 1, end - start - 1);
        if (idx > start) return idx + 1;

        return end;
    }

    private static string NormaliseWhitespace(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\r\n|\r", "\n");
}
