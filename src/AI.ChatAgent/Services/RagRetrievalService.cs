using AI.ChatAgent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Memory;

namespace AI.ChatAgent.Services;

/// <summary>
/// Retrieves the most semantically relevant document chunks for a given query
/// from the vector memory store. Used by <see cref="ChatService"/> to inject
/// context into the prompt before the LLM generates its response (RAG pattern).
/// </summary>
public sealed class RagRetrievalService(
    ISemanticTextMemory memory,
    ILogger<RagRetrievalService> logger)
{
    private const double DefaultMinScore    = 0.70;
    private const int    DefaultResultLimit = 5;

    /// <summary>
    /// Search all collections and return the top relevant chunks.
    /// Results are deduplicated and ranked by relevance score.
    /// </summary>
    public async Task<IReadOnlyList<RagSearchResult>> SearchAsync(
        string query,
        int    limit    = DefaultResultLimit,
        double minScore = DefaultMinScore,
        CancellationToken ct = default)
    {
        logger.LogDebug("RAG:Search query={Query}", query);

        var allResults = new List<RagSearchResult>();

        // Search all collections in parallel
        var tasks = RagCollections.All.Select(col =>
            SearchCollectionAsync(col, query, limit, minScore, ct));

        var collectionResults = await Task.WhenAll(tasks);
        foreach (var results in collectionResults)
            allResults.AddRange(results);

        // Sort by score descending, take top N, deduplicate by text prefix
        var ranked = allResults
            .OrderByDescending(r => r.Score)
            .DistinctBy(r => r.Text[..Math.Min(80, r.Text.Length)])
            .Take(limit)
            .ToList();

        logger.LogInformation(
            "RAG:Search returned {Count} results for query='{Query}'",
            ranked.Count, query);

        return ranked;
    }

    /// <summary>Search a specific collection only.</summary>
    public async Task<IReadOnlyList<RagSearchResult>> SearchCollectionAsync(
        string collection,
        string query,
        int    limit    = DefaultResultLimit,
        double minScore = DefaultMinScore,
        CancellationToken ct = default)
    {
        var results = new List<RagSearchResult>();
        try
        {
            await foreach (var r in memory.SearchAsync(
                collection, query, limit,
                minRelevanceScore: minScore,
                withEmbeddings: false,
                cancellationToken: ct))
            {
                results.Add(new RagSearchResult
                {
                    Text       = r.Metadata.Text,
                    Source     = r.Metadata.Description ?? collection,
                    Score      = r.Relevance,
                    Collection = collection,
                });
            }
        }
        catch (Exception ex)
        {
            // Collection may not exist yet (no documents indexed) — log and continue
            logger.LogDebug(ex, "RAG:SearchCollection {Collection} — no results or not indexed", collection);
        }

        return results;
    }

    /// <summary>
    /// Format retrieved chunks as a system message context block
    /// ready to be injected into <see cref="Microsoft.SemanticKernel.ChatCompletion.ChatHistory"/>.
    /// </summary>
    public static string FormatAsContext(IReadOnlyList<RagSearchResult> results)
    {
        if (results.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== RETRIEVED CONTEXT (from indexed documents) ===");
        sb.AppendLine("Use the following passages to help answer the user's question.");
        sb.AppendLine("Cite the source filename when referencing specific content.");
        sb.AppendLine();

        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"[{i + 1}] Source: {r.Source}  |  Relevance: {r.Score:P0}");
            sb.AppendLine(r.Text.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("=== END OF RETRIEVED CONTEXT ===");
        return sb.ToString();
    }
}
