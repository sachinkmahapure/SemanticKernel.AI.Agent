namespace AI.ChatAgent.Configuration;

/// <summary>Root AI configuration bound from appsettings.json → "AI".</summary>
public sealed class AiOptions
{
    public const string SectionName = "AI";

    public string Provider { get; set; } = "OpenAI";
    public OpenAiOptions OpenAI { get; set; } = new();
    public AzureOpenAiOptions AzureOpenAI { get; set; } = new();
    public int MaxTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.7;
    public double TopP { get; set; } = 0.95;
    public bool StreamingEnabled { get; set; } = true;
}

public sealed class OpenAiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string ChatModelId { get; set; } = "gpt-4o";
    public string EmbeddingModelId { get; set; } = "text-embedding-3-small";
    public string OrgId { get; set; } = string.Empty;
}

public sealed class AzureOpenAiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string ChatDeployment { get; set; } = "gpt-4o";
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";
    public string ApiVersion { get; set; } = "2024-10-21";
}

/// <summary>Web search configuration bound from appsettings.json → "WebSearch".</summary>
public sealed class WebSearchOptions
{
    public const string SectionName = "WebSearch";

    public string Provider { get; set; } = "Serper";
    public SerperOptions Serper { get; set; } = new();
    public BingOptions Bing { get; set; } = new();
}

public sealed class SerperOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://google.serper.dev";
    public int MaxResults { get; set; } = 10;
}

public sealed class BingOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.bing.microsoft.com/v7.0/search";
    public int MaxResults { get; set; } = 10;
}

/// <summary>External APIs configuration bound from appsettings.json → "ExternalApis".</summary>
public sealed class ExternalApiOptions
{
    public const string SectionName = "ExternalApis";

    public int DefaultTimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 2;
    public int CircuitBreakerThreshold { get; set; } = 5;
    public int CircuitBreakerDurationSeconds { get; set; } = 30;
    public Dictionary<string, ApiRegistryEntry> ApiRegistry { get; set; } = [];
}

public sealed class ApiRegistryEntry
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string AuthHeader { get; set; } = "Bearer";
}

/// <summary>Storage options bound from appsettings.json → "Storage".</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string PdfDirectory { get; set; } = "SampleData/PDFs";
    public string FileDirectory { get; set; } = "SampleData/Files";
    public int MaxFileSizeMb { get; set; } = 50;
}

/// <summary>Human approval workflow options.</summary>
public sealed class HumanApprovalOptions
{
    public const string SectionName = "HumanApproval";

    public bool Enabled { get; set; } = true;
    public IReadOnlyList<string> RequiredForActions { get; set; } = [];
    public int TimeoutSeconds { get; set; } = 60;
}
