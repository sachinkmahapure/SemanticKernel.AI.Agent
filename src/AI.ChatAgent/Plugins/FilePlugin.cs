using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AI.ChatAgent.Configuration;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace AI.ChatAgent.Plugins;

/// <summary>
/// Semantic Kernel plugin for reading and querying CSV, JSON, and plain-text files.
/// </summary>
public sealed class FilePlugin(
    IOptions<StorageOptions> storageOptions,
    ILogger<FilePlugin> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    /// <summary>List all files in the file data directory.</summary>
    [KernelFunction(nameof(ListFiles))]
    [Description("List all available data files (CSV, JSON, TXT) in the data directory.")]
    public Task<string> ListFiles(CancellationToken ct = default)
    {
        var dir = storageOptions.Value.FileDirectory;
        logger.LogInformation("FILE:ListFiles dir={Dir}", dir);

        if (!Directory.Exists(dir))
            return Task.FromResult("{\"files\": [], \"message\": \"File directory not found\"}");

        var extensions = new[] { "*.csv", "*.json", "*.txt", "*.md" };
        var files = extensions
            .SelectMany(ext => Directory.GetFiles(dir, ext, SearchOption.AllDirectories))
            .Select(f =>
            {
                var info = new FileInfo(f);
                return new
                {
                    name = info.Name,
                    extension = info.Extension.TrimStart('.').ToUpperInvariant(),
                    relativePath = Path.GetRelativePath(dir, f),
                    sizeKb = info.Length / 1024
                };
            })
            .OrderBy(f => f.name)
            .ToList();

        return Task.FromResult(JsonSerializer.Serialize(new { files, count = files.Count }));
    }

    /// <summary>Read a CSV file and return structured data.</summary>
    [KernelFunction(nameof(ReadCsvFile))]
    [Description("Read a CSV file and return it as a JSON array. Supports filtering and limiting rows.")]
    public Task<string> ReadCsvFile(
        [Description("CSV filename (e.g. 'sales.csv')")] string filename,
        [Description("Optional filter: column=value (e.g. 'Region=North')")] string? filter = null,
        [Description("Max rows to return (default 100)")] int maxRows = 100,
        CancellationToken ct = default)
    {
        var path = ResolvePath(filename);
        logger.LogInformation("FILE:ReadCsvFile file={File} filter={Filter}", filename, filter);

        if (!File.Exists(path))
            return Task.FromResult($"{{\"error\": \"File not found: {filename}\"}}");

        try
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                BadDataFound = null
            };

            using var reader = new StreamReader(path, Encoding.UTF8);
            using var csv    = new CsvReader(reader, config);

            var records = new List<Dictionary<string, string>>();
            csv.Read();
            csv.ReadHeader();
            var headers = csv.HeaderRecord ?? [];

            while (csv.Read() && records.Count < maxRows)
            {
                ct.ThrowIfCancellationRequested();
                var row = new Dictionary<string, string>();

                foreach (var h in headers)
                    row[h] = csv.GetField(h) ?? "";

                // Apply filter if provided
                if (filter is not null)
                {
                    var parts = filter.Split('=', 2);
                    if (parts.Length == 2 && row.TryGetValue(parts[0].Trim(), out var val))
                    {
                        if (!val.Equals(parts[1].Trim(), StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                }

                records.Add(row);
            }

            logger.LogInformation("FILE:ReadCsvFile returned {Count} rows from {File}", records.Count, filename);
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                filename,
                columns = headers,
                rowCount = records.Count,
                data = records
            }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FILE:ReadCsvFile failed for {File}", filename);
            return Task.FromResult($"{{\"error\": \"{ex.Message}\"}}");
        }
    }

    /// <summary>Read a JSON file and return its contents.</summary>
    [KernelFunction(nameof(ReadJsonFile))]
    [Description("Read a JSON file and return its parsed content. Optionally specify a JSON path to extract a subset.")]
    public Task<string> ReadJsonFile(
        [Description("JSON filename (e.g. 'config.json')")] string filename,
        [Description("Optional JSON property path (e.g. 'users' or 'meta.version')")] string? propertyPath = null,
        CancellationToken ct = default)
    {
        var path = ResolvePath(filename);
        logger.LogInformation("FILE:ReadJsonFile file={File} path={Path}", filename, propertyPath);

        if (!File.Exists(path))
            return Task.FromResult($"{{\"error\": \"File not found: {filename}\"}}");

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);

            if (propertyPath is null)
                return Task.FromResult(json);

            // Navigate property path (dot-separated)
            JsonElement current = doc.RootElement;
            foreach (var segment in propertyPath.Split('.'))
            {
                if (current.ValueKind == JsonValueKind.Object &&
                    current.TryGetProperty(segment, out var child))
                {
                    current = child;
                }
                else
                {
                    return Task.FromResult($"{{\"error\": \"Property path '{propertyPath}' not found\"}}");
                }
            }

            return Task.FromResult(current.GetRawText());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FILE:ReadJsonFile failed for {File}", filename);
            return Task.FromResult($"{{\"error\": \"{ex.Message}\"}}");
        }
    }

    /// <summary>Read a plain text or markdown file.</summary>
    [KernelFunction(nameof(ReadTextFile))]
    [Description("Read a plain text or markdown file. Returns the file content as a string.")]
    public Task<string> ReadTextFile(
        [Description("Text filename (e.g. 'notes.txt')")] string filename,
        [Description("Max characters to return (default 10000)")] int maxChars = 10_000,
        CancellationToken ct = default)
    {
        var path = ResolvePath(filename);
        logger.LogInformation("FILE:ReadTextFile file={File}", filename);

        if (!File.Exists(path))
            return Task.FromResult($"{{\"error\": \"File not found: {filename}\"}}");

        try
        {
            using var sr = new StreamReader(path, Encoding.UTF8);
            var buffer = new char[maxChars];
            var read   = sr.ReadBlock(buffer, 0, maxChars);
            var text   = new string(buffer, 0, read);

            var info = new FileInfo(path);
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                filename,
                fileSizeKb = info.Length / 1024,
                truncated  = info.Length > maxChars,
                content    = text
            }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FILE:ReadTextFile failed for {File}", filename);
            return Task.FromResult($"{{\"error\": \"{ex.Message}\"}}");
        }
    }

    /// <summary>Analyse a CSV file: row count, column names, basic stats.</summary>
    [KernelFunction(nameof(AnalyzeCsvFile))]
    [Description("Analyse a CSV file: count rows, list columns, show sample rows, compute numeric column stats.")]
    public Task<string> AnalyzeCsvFile(
        [Description("CSV filename")] string filename,
        CancellationToken ct = default)
    {
        var path = ResolvePath(filename);
        logger.LogInformation("FILE:AnalyzeCsvFile file={File}", filename);

        if (!File.Exists(path))
            return Task.FromResult($"{{\"error\": \"File not found: {filename}\"}}");

        try
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true, MissingFieldFound = null, BadDataFound = null
            };

            using var reader = new StreamReader(path, Encoding.UTF8);
            using var csv    = new CsvReader(reader, config);

            var allRows = new List<Dictionary<string, string>>();
            csv.Read();
            csv.ReadHeader();
            var headers = csv.HeaderRecord ?? [];

            while (csv.Read())
            {
                ct.ThrowIfCancellationRequested();
                var row = new Dictionary<string, string>();
                foreach (var h in headers)
                    row[h] = csv.GetField(h) ?? "";
                allRows.Add(row);
            }

            // Numeric stats for numeric columns
            var numericStats = new Dictionary<string, object>();
            foreach (var col in headers)
            {
                var vals = allRows
                    .Select(r => r.TryGetValue(col, out var v) && double.TryParse(v, out var d) ? d : (double?)null)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();

                if (vals.Count > 0)
                    numericStats[col] = new { min = vals.Min(), max = vals.Max(), avg = vals.Average(), count = vals.Count };
            }

            return Task.FromResult(JsonSerializer.Serialize(new
            {
                filename,
                totalRows  = allRows.Count,
                columns    = headers,
                sampleRows = allRows.Take(3),
                numericStats
            }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FILE:AnalyzeCsvFile failed for {File}", filename);
            return Task.FromResult($"{{\"error\": \"{ex.Message}\"}}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private string ResolvePath(string filename)
    {
        var safe = Path.GetFileName(filename); // prevent traversal
        return Path.Combine(storageOptions.Value.FileDirectory, safe);
    }
}
