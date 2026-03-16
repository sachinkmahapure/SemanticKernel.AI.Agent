using System.Text.Json;
using AI.ChatAgent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AI.ChatAgent.Services;

/// <summary>
/// Analyses a user message and decides which plugins to call, with what arguments.
/// Uses the LLM itself to make an intelligent routing decision.
/// </summary>
public sealed class RouterService(
    IKernelFactory kernelFactory,
    ILogger<RouterService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    private const string RouterSystemPrompt =
        """
        You are a routing agent. Given a user message and conversation context, decide which
        tools/plugins should be called to answer the query. Respond ONLY with valid JSON.

        Available plugins:
        - DatabasePlugin: Search products, customers, orders, business stats (use for DB/data questions)
        - ApiPlugin: Call external REST APIs, get weather (use for real-time external data)
        - PdfPlugin: Extract text from PDFs, search PDFs (use when user asks about documents)
        - FilePlugin: Read CSV/JSON/text files (use for file analysis)
        - WebSearchPlugin: Search the web, fetch URLs (use for current events or unknown topics)

        You must respond with JSON matching this schema:
        {
          "reasoning": "Brief explanation of routing decision",
          "requiresHumanApproval": false,
          "approvalReason": null,
          "invocations": [
            {
              "pluginName": "DatabasePlugin",
              "functionName": "SearchProducts",
              "arguments": { "query": "laptop", "maxResults": "10" },
              "priority": 1
            }
          ]
        }

        Rules:
        - Return empty invocations array [] when no tools are needed (e.g. general chat)
        - Multiple tools CAN be used in parallel (same priority = parallel execution)
        - Higher priority number = runs first
        - requiresHumanApproval=true only for destructive/sensitive actions (DeleteData, SendEmail)
        - All argument values must be strings
        - Only use functions that actually exist in the plugin definitions above
        """;

    /// <summary>
    /// Calls the LLM to decide routing. Falls back to an empty (direct chat) decision on failure.
    /// </summary>
    public async Task<RouterDecision> DecideAsync(
        string userMessage,
        IReadOnlyList<string> preferredPlugins,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Router:Decide message_length={Length}", userMessage.Length);

        var hints = preferredPlugins.Count > 0
            ? $"\n\nUser hinted they want to use: {string.Join(", ", preferredPlugins)}"
            : string.Empty;

        var prompt = $"User message: {userMessage}{hints}";

        try
        {
            var kernel      = kernelFactory.CreateForRequest();
            var chatService = kernel.GetRequiredService<IChatCompletionService>();
            var history     = new ChatHistory(RouterSystemPrompt);
            history.AddUserMessage(prompt);

            // ResponseFormat is marked [Experimental] in SK 1.x — suppress the diagnostic.
#pragma warning disable SKEXP0010
            var settings = new OpenAIPromptExecutionSettings
            {
                MaxTokens      = 1024,
                Temperature    = 0,
                ResponseFormat = "json_object"
            };
#pragma warning restore SKEXP0010

            // GetChatMessageContentAsync: correct parameter is 'cancellationToken', not 'ct'
            var response = await chatService.GetChatMessageContentAsync(
                history,
                settings,
                cancellationToken: cancellationToken);

            var json = response.Content ?? "{}";
            logger.LogDebug("Router:LLM response: {Json}", json);

            return ParseDecision(json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Router:Decide failed, falling back to no-tool route");
            return new RouterDecision
            {
                Invocations           = [],
                Reasoning             = "Router failed; answering directly.",
                RequiresHumanApproval = false
            };
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static RouterDecision ParseDecision(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var reasoning     = root.TryGetProperty("reasoning",             out var r)  ? r.GetString()  ?? "" : "";
            var needsApproval = root.TryGetProperty("requiresHumanApproval", out var ha) && ha.GetBoolean();
            var approvalReason= root.TryGetProperty("approvalReason",        out var ar) ? ar.GetString() : null;

            var invocations = new List<PluginInvocation>();

            if (root.TryGetProperty("invocations", out var inv) &&
                inv.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in inv.EnumerateArray())
                {
                    var pluginName   = item.TryGetProperty("pluginName",   out var pn) ? pn.GetString() ?? "" : "";
                    var functionName = item.TryGetProperty("functionName", out var fn) ? fn.GetString() ?? "" : "";
                    var priority     = item.TryGetProperty("priority",     out var pr) ? pr.GetInt32()      : 0;

                    var args = new Dictionary<string, string>();
                    if (item.TryGetProperty("arguments", out var argsEl) &&
                        argsEl.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in argsEl.EnumerateObject())
                            args[prop.Name] = prop.Value.GetString() ?? prop.Value.ToString();
                    }

                    if (!string.IsNullOrWhiteSpace(pluginName) &&
                        !string.IsNullOrWhiteSpace(functionName))
                    {
                        invocations.Add(new PluginInvocation
                        {
                            PluginName   = pluginName,
                            FunctionName = functionName,
                            Arguments    = args,
                            Priority     = priority
                        });
                    }
                }
            }

            return new RouterDecision
            {
                Invocations           = invocations,
                Reasoning             = reasoning,
                RequiresHumanApproval = needsApproval,
                ApprovalReason        = approvalReason
            };
        }
        catch
        {
            return new RouterDecision
            {
                Invocations           = [],
                Reasoning             = "Failed to parse routing decision; answering directly.",
                RequiresHumanApproval = false
            };
        }
    }
}
