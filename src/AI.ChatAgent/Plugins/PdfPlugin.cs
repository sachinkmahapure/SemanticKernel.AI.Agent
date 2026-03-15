using System.ComponentModel;
using System.Text;
using System.Text.Json;
using AI.ChatAgent.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace AI.ChatAgent.Plugins;

/// <summary>
/// Semantic Kernel plugin for extracting and searching text in PDF files.
/// Uses PdfPig for .NET for zero-dependency PDF parsing.
/// </summary>
public sealed class PdfPlugin(
    IOptions<StorageOptions> storageOptions,
    ILogger<PdfPlugin> logger)
{
    /// <summary>List all available PDF files.</summary>
    [KernelFunction(nameof(ListPdfFiles))]
    [Description("List all available PDF files in the PDF directory.")]
    public Task<string> ListPdfFiles(CancellationToken ct = default)
    {
        var dir = storageOptions.Value.PdfDirectory;
        logger.LogInformation("PDF:ListPdfFiles dir={Dir}", dir);

        if (!Directory.Exists(dir))
            return Task.FromResult("{\"files\": [], \"message\": \"PDF directory not found\"}");

        var files = Directory.GetFiles(dir, "*.pdf", SearchOption.AllDirectories)
            .Select(f => new
            {
                Name = Path.GetFileName(f),
                RelativePath = Path.GetRelativePath(dir, f),
                SizeKb = new FileInfo(f).Length / 1024
            })
            .ToList();

        return Task.FromResult(JsonSerializer.Serialize(new { files, count = files.Count }));
    }

    /// <summary>Extract full text from a PDF file.</summary>
    [KernelFunction(nameof(ExtractPdfText))]
    [Description("Extract all text content from a PDF file. Provide the filename (e.g. 'report.pdf').")]
    public Task<string> ExtractPdfText(
        [Description("PDF filename (e.g. 'report.pdf')")] string filename,
        [Description("Starting page (1-based, default 1)")] int startPage = 1,
        [Description("Ending page (0 = all pages)")] int endPage = 0,
        CancellationToken ct = default)
    {
        var path = ResolvePath(filename);
        logger.LogInformation("PDF:ExtractPdfText file={File} pages={Start}-{End}", filename, startPage, endPage);

        if (!File.Exists(path))
            return Task.FromResult($"{{\"error\": \"File not found: {filename}\"}}");

        try
        {
            using var doc = PdfDocument.Open(path);
            var totalPages = doc.NumberOfPages;
            var sb = new StringBuilder();

            var from = Math.Clamp(startPage, 1, totalPages);
            var to   = endPage == 0 ? totalPages : Math.Clamp(endPage, from, totalPages);

            for (int i = from; i <= to; i++)
            {
                ct.ThrowIfCancellationRequested();
                var page = doc.GetPage(i);
                sb.AppendLine($"--- Page {i} ---");
                sb.AppendLine(page.Text);
                sb.AppendLine();
            }

            var result = new
            {
                filename,
                totalPages,
                extractedPages = to - from + 1,
                text = sb.ToString().Trim()
            };

            logger.LogInformation("PDF:ExtractPdfText extracted {Pages} pages from {File}", result.extractedPages, filename);
            return Task.FromResult(JsonSerializer.Serialize(result));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PDF:ExtractPdfText failed for {File}", filename);
            return Task.FromResult($"{{\"error\": \"{ex.Message}\"}}");
        }
    }

    /// <summary>Search for a keyword across all PDF files.</summary>
    [KernelFunction(nameof(SearchAcrossPdfs))]
    [Description("Search for a keyword or phrase across all PDF files. Returns matching snippets.")]
    public Task<string> SearchAcrossPdfs(
        [Description("Keyword or phrase to search for")] string keyword,
        [Description("Max number of matches per file (default 5)")] int maxMatchesPerFile = 5,
        CancellationToken ct = default)
    {
        var dir = storageOptions.Value.PdfDirectory;
        logger.LogInformation("PDF:SearchAcrossPdfs keyword={Keyword}", keyword);

        if (!Directory.Exists(dir))
            return Task.FromResult("{\"matches\": [], \"message\": \"PDF directory not found\"}");

        var allMatches = new List<object>();

        foreach (var file in Directory.GetFiles(dir, "*.pdf", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var fileMatches = SearchInFile(file, keyword, maxMatchesPerFile);
                allMatches.AddRange(fileMatches);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "PDF:SearchAcrossPdfs could not search {File}", file);
            }
        }

        var result = new
        {
            keyword,
            totalMatches = allMatches.Count,
            matches = allMatches
        };

        return Task.FromResult(JsonSerializer.Serialize(result));
    }

    /// <summary>Get PDF metadata (title, author, page count, etc.).</summary>
    [KernelFunction(nameof(GetPdfMetadata))]
    [Description("Get metadata from a PDF: title, author, page count, creation date.")]
    public Task<string> GetPdfMetadata(
        [Description("PDF filename")] string filename,
        CancellationToken ct = default)
    {
        var path = ResolvePath(filename);
        logger.LogInformation("PDF:GetPdfMetadata file={File}", filename);

        if (!File.Exists(path))
            return Task.FromResult($"{{\"error\": \"File not found: {filename}\"}}");

        try
        {
            using var doc = PdfDocument.Open(path);
            var info = doc.Information;
            var metadata = new
            {
                filename,
                pageCount = doc.NumberOfPages,
                title = info.Title,
                author = info.Author,
                subject = info.Subject,
                creator = info.Creator,
                producer = info.Producer,
                creationDate = info.CreationDate,
                modifiedDate = info.ModifiedDate,
                fileSizeKb = new FileInfo(path).Length / 1024
            };

            return Task.FromResult(JsonSerializer.Serialize(metadata));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PDF:GetPdfMetadata failed for {File}", filename);
            return Task.FromResult($"{{\"error\": \"{ex.Message}\"}}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private string ResolvePath(string filename)
    {
        // Sanitize: prevent directory traversal
        var safe = Path.GetFileName(filename);
        return Path.Combine(storageOptions.Value.PdfDirectory, safe);
    }

    private static List<object> SearchInFile(string filePath, string keyword, int max)
    {
        using var doc = PdfDocument.Open(filePath);
        var matches = new List<object>();
        var lower = keyword.ToLowerInvariant();

        for (int i = 1; i <= doc.NumberOfPages && matches.Count < max; i++)
        {
            var page = doc.GetPage(i);
            var text = page.Text;
            var idx  = text.ToLowerInvariant().IndexOf(lower, StringComparison.OrdinalIgnoreCase);

            if (idx < 0) continue;

            // Grab a 200-char snippet around the match
            var start   = Math.Max(0, idx - 100);
            var length  = Math.Min(200, text.Length - start);
            var snippet = text.Substring(start, length).Replace('\n', ' ');

            matches.Add(new
            {
                file = Path.GetFileName(filePath),
                page = i,
                snippet = $"...{snippet}..."
            });
        }

        return matches;
    }
}
