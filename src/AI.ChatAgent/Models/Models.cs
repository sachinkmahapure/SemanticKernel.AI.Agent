using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AI.ChatAgent.Models;

// ─── Chat Models ─────────────────────────────────────────────────────────────

/// <summary>Incoming chat request from the API consumer.</summary>
public sealed record ChatRequest
{
    /// <summary>The user's message text.</summary>
    [Required, MinLength(1), MaxLength(32_000)]
    public required string Message { get; init; }

    /// <summary>Conversation session identifier. Empty string = new conversation.</summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>Optional system prompt override.</summary>
    [MaxLength(8_000)]
    public string? SystemPrompt { get; init; }

    /// <summary>If true the caller wants the full response JSON; otherwise plain text.</summary>
    public bool StructuredOutput { get; init; } = false;

    /// <summary>Hint to the router about which plugins to prefer.</summary>
    public IReadOnlyList<string> PreferredPlugins { get; init; } = [];

    /// <summary>Additional metadata forwarded to plugins.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>Full chat response returned from POST /chat.</summary>
public sealed record ChatResponse
{
    public required string SessionId { get; init; }
    public required string Message { get; init; }
    public required IReadOnlyList<ToolExecutionResult> ToolsUsed { get; init; }
    public required int TotalTokens { get; init; }
    public required long LatencyMs { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Individual chunk sent during SSE streaming.</summary>
public sealed record StreamChunk
{
    [JsonPropertyName("type")]
    public required string Type { get; init; } // "content" | "tool_start" | "tool_end" | "done" | "error"

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("tool")]
    public string? Tool { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("tokens")]
    public int? Tokens { get; init; }
}

// ─── Router / Tool Models ─────────────────────────────────────────────────────

/// <summary>Decision produced by the RouterService.</summary>
public sealed record RouterDecision
{
    public required IReadOnlyList<PluginInvocation> Invocations { get; init; }
    public required string Reasoning { get; init; }
    public bool RequiresHumanApproval { get; init; }
    public string? ApprovalReason { get; init; }
}

/// <summary>A single plugin call the router decided to make.</summary>
public sealed record PluginInvocation
{
    public required string PluginName { get; init; }
    public required string FunctionName { get; init; }
    public required IReadOnlyDictionary<string, string> Arguments { get; init; }
    public int Priority { get; init; } = 0;
}

/// <summary>Result of executing a single plugin function.</summary>
public sealed record ToolExecutionResult
{
    public required string PluginName { get; init; }
    public required string FunctionName { get; init; }
    public required bool Success { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
    public long DurationMs { get; init; }
}

// ─── Database Entities ────────────────────────────────────────────────────────

/// <summary>EF Core entity: Product catalogue entry.</summary>
public sealed class Product
{
    public int Id { get; set; }
    [MaxLength(200)] public required string Name { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public decimal Price { get; set; }
    [MaxLength(100)] public string? Category { get; set; }
    public int StockQuantity { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>EF Core entity: Customer record.</summary>
public sealed class Customer
{
    public int Id { get; set; }
    [MaxLength(100)] public required string FirstName { get; set; }
    [MaxLength(100)] public required string LastName { get; set; }
    [MaxLength(254)] public required string Email { get; set; }
    [MaxLength(20)] public string? Phone { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Order> Orders { get; set; } = [];
}

/// <summary>EF Core entity: Order record.</summary>
public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public decimal TotalAmount { get; set; }
    [MaxLength(50)] public string Status { get; set; } = OrderStatus.Pending;
    public DateTimeOffset OrderDate { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<OrderItem> Items { get; set; } = [];
}

/// <summary>EF Core entity: Order line item.</summary>
public sealed class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>EF Core entity: Chat session.</summary>
public sealed class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [MaxLength(500)] public string? Title { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ChatMessage> Messages { get; set; } = [];
}

/// <summary>EF Core entity: Individual chat message.</summary>
public sealed class ChatMessage
{
    public int Id { get; set; }
    public required string SessionId { get; set; }
    public ChatSession? Session { get; set; }
    [MaxLength(10)] public required string Role { get; set; } // user | assistant | system
    public required string Content { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int? TokenCount { get; set; }
}

// ─── Human-in-the-loop ────────────────────────────────────────────────────────

/// <summary>Request sent to the human approval channel.</summary>
public sealed record ApprovalRequest
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string ActionName { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; init; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string? ReviewedBy { get; set; }
    public string? ReviewNotes { get; set; }
}

public enum ApprovalStatus { Pending, Approved, Rejected, Expired }

// ─── Query / Search ───────────────────────────────────────────────────────────

/// <summary>Database query parameters from the AI router.</summary>
public sealed record DbQueryRequest
{
    public required string Intent { get; init; }
    public IReadOnlyList<string> Tables { get; init; } = [];
    public IReadOnlyList<string> Filters { get; init; } = [];
    public int MaxRows { get; init; } = 50;
}

/// <summary>Web search result item.</summary>
public sealed record SearchResult
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public string? Snippet { get; init; }
    public string? Source { get; init; }
}

// ─── Constants ────────────────────────────────────────────────────────────────

/// <summary>Application-wide string constants. Zero magic strings.</summary>
public static class AppConstants
{
    public const string ChatStreamEndpoint  = "/chat/stream";
    public const string ChatEndpoint        = "/chat";
    public const string HealthEndpoint      = "/health";
    public const string SwaggerEndpoint     = "/swagger";

    public static class Plugins
    {
        public const string Database  = "DatabasePlugin";
        public const string Api       = "ApiPlugin";
        public const string Pdf       = "PdfPlugin";
        public const string File      = "FilePlugin";
        public const string WebSearch = "WebSearchPlugin";
        public const string Router    = "RouterPlugin";
    }

    public static class Roles
    {
        public const string User      = "user";
        public const string Assistant = "assistant";
        public const string System    = "system";
    }

    public static class StreamTypes
    {
        public const string Content   = "content";
        public const string ToolStart = "tool_start";
        public const string ToolEnd   = "tool_end";
        public const string Done      = "done";
        public const string Error     = "error";
    }

    public static class CacheKeys
    {
        public const string SessionPrefix = "session:";
        public const string RateLimitPrefix = "rl:";
    }

    public static class Headers
    {
        public const string ContentType    = "Content-Type";
        public const string CacheControl   = "Cache-Control";
        public const string Authorization  = "Authorization";
        public const string XRequestId     = "X-Request-Id";
        public const string SseContentType = "text/event-stream";
        public const string NoCacheValue   = "no-cache";
    }
}

/// <summary>Order status constants.</summary>
public static class OrderStatus
{
    public const string Pending   = "Pending";
    public const string Confirmed = "Confirmed";
    public const string Shipped   = "Shipped";
    public const string Delivered = "Delivered";
    public const string Cancelled = "Cancelled";
}
