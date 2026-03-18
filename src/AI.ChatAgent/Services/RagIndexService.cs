using AI.ChatAgent.Configuration;
using AI.ChatAgent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Memory;
using UglyToad.PdfPig;

namespace AI.ChatAgent.Services;

/// <summary>
/// Indexes documents (PDFs, text files, CSV, JSON) into the vector memory store
/// so they can be retrieved semantically by <see cref="RagRetrievalService"/>.
///
/// Call <see cref="IndexAllAsync"/> once on startup, or call the individual
/// methods whenever new documents are added.
/// </summary>
public sealed class RagIndexService(
    ISemanticTextMemory memory,
    IOptions<StorageOptions> storageOptions,
    ILogger<RagIndexService> logger)
{
    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Index all PDFs and data files found in the configured directories.</summary>
    public async Task IndexAllAsync(CancellationToken ct = default)
    {
        logger.LogInformation("RAG:IndexAll starting...");

        var pdfCount  = await IndexPdfsAsync(ct);
        var fileCount = await IndexFilesAsync(ct);

        logger.LogInformation(
            "RAG:IndexAll complete — {Pdfs} PDF chunks, {Files} file chunks indexed",
            pdfCount, fileCount);
    }

    /// <summary>Index all PDF files in the configured PDF directory.</summary>
    public async Task<int> IndexPdfsAsync(CancellationToken ct = default)
    {
        var dir = storageOptions.Value.PdfDirectory;
        if (!Directory.Exists(dir))
        {
            logger.LogWarning("RAG:IndexPdfs directory not found: {Dir}", dir);
            return 0;
        }

        var total = 0;
        foreach (var file in Directory.GetFiles(dir, "*.pdf", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            total += await IndexPdfAsync(file, ct);
        }
        return total;
    }

    /// <summary>Index all text/CSV/JSON files in the configured file directory.</summary>
    public async Task<int> IndexFilesAsync(CancellationToken ct = default)
    {
        var dir = storageOptions.Value.FileDirectory;
        if (!Directory.Exists(dir))
        {
            logger.LogWarning("RAG:IndexFiles directory not found: {Dir}", dir);
            return 0;
        }

        var total = 0;
        var extensions = new[] { "*.txt", "*.md", "*.csv", "*.json" };

        foreach (var ext in extensions)
        foreach (var file in Directory.GetFiles(dir, ext, SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            total += await IndexTextFileAsync(file, ct);
        }
        return total;
    }

    /// <summary>Index a single piece of text directly (useful for web search results).</summary>
    public async Task IndexTextAsync(
        string collection, string id, string text,
        string source, CancellationToken ct = default)
    {
        var chunks = TextChunker.Chunk(id, source, text);
        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            await memory.SaveInformationAsync(
                collection,
                text:        chunk.Text,
                id:          chunk.ChunkId,
                description: source,
                cancellationToken: ct);
        }
        logger.LogDebug("RAG:IndexText source={Source} chunks={Count}", source, chunks.Count);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task<int> IndexPdfAsync(string filePath, CancellationToken ct)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        logger.LogInformation("RAG:IndexPdf {File}", fileName);

        try
        {
            var text = ExtractPdfText(filePath);
            var chunks = TextChunker.Chunk(fileName, Path.GetFileName(filePath), text);

            foreach (var chunk in chunks)
            {
                ct.ThrowIfCancellationRequested();
                await memory.SaveInformationAsync(
                    RagCollections.Pdfs,
                    text:        chunk.Text,
                    id:          chunk.ChunkId,
                    description: $"PDF: {Path.GetFileName(filePath)}",
                    cancellationToken: ct);
            }

            logger.LogInformation("RAG:IndexPdf {File} → {Count} chunks", fileName, chunks.Count);
            return chunks.Count;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RAG:IndexPdf failed for {File}", fileName);
            return 0;
        }
    }

    private async Task<int> IndexTextFileAsync(string filePath, CancellationToken ct)
    {
        var fileName = Path.GetFileName(filePath);
        logger.LogInformation("RAG:IndexFile {File}", fileName);

        try
        {
            var text   = await File.ReadAllTextAsync(filePath, ct);
            var docId  = Path.GetFileNameWithoutExtension(filePath);
            var chunks = TextChunker.Chunk(docId, fileName, text);
            var col    = RagCollections.Files;

            foreach (var chunk in chunks)
            {
                ct.ThrowIfCancellationRequested();
                await memory.SaveInformationAsync(
                    col,
                    text:        chunk.Text,
                    id:          chunk.ChunkId,
                    description: $"File: {fileName}",
                    cancellationToken: ct);
            }

            logger.LogInformation("RAG:IndexFile {File} → {Count} chunks", fileName, chunks.Count);
            return chunks.Count;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RAG:IndexFile failed for {File}", fileName);
            return 0;
        }
    }

    private static string ExtractPdfText(string path)
    {
        using var doc = PdfDocument.Open(path);
        var sb = new System.Text.StringBuilder();
        for (int i = 1; i <= doc.NumberOfPages; i++)
            sb.AppendLine(doc.GetPage(i).Text);
        return sb.ToString();
    }
}
