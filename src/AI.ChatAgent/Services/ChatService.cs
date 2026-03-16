using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using AI.ChatAgent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AI.ChatAgent.Services;

/// <summary>
/// Main chat orchestrator.
/// Coordinates: conversation history → router → tool execution → response synthesis.
/// Supports both streaming and non-streaming response modes.
/// </summary>
public sealed class ChatService(
    IKernelFactory kernelFactory,
    RouterService router,
    ToolExecutorService toolExecutor,
    ConversationService conversation,
    HumanApprovalService approvalService,
    ILogger<ChatService> logger)
{
    private static readonly OpenAIPromptExecutionSettings DefaultSettings = new()
    {
        MaxTokens   = 4096,
        Temperature = 0.7,
        TopP        = 0.95
    };

    // ── Non-streaming ─────────────────────────────────────────────────────────

    /// <summary>Process a chat request and return a complete response.</summary>
    public async Task<ChatResponse> ProcessAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        // 1. Session management
        var sessionId = await conversation.GetOrCreateSessionAsync(request.SessionId, cancellationToken);

        // 2. Persist user message
        await conversation.AddUserMessageAsync(sessionId, request.Message, cancellationToken);

        // 3. Route to plugins
        var decision = await router.DecideAsync(request.Message, request.PreferredPlugins, cancellationToken);
        logger.LogInformation("Chat:Route reasoning={Reasoning}", decision.Reasoning);

        // 4. Human approval if required
        if (decision.RequiresHumanApproval)
        {
            var approved = await approvalService.RequestApprovalAsync(
                "PluginExecution",
                decision.ApprovalReason ?? "AI wants to execute plugins",
                new Dictionary<string, string> { ["reasoning"] = decision.Reasoning },
                cancellationToken);

            if (!approved)
            {
                var rejectedMsg = "I need human approval to perform this action, but the request was rejected or timed out.";
                await conversation.AddAssistantMessageAsync(sessionId, rejectedMsg, cancellationToken: cancellationToken);
                return new ChatResponse
                {
                    SessionId   = sessionId,
                    Message     = rejectedMsg,
                    ToolsUsed   = [],
                    TotalTokens = 0,
                    LatencyMs   = sw.ElapsedMilliseconds
                };
            }
        }

        // 5. Execute plugins
        var toolResults = await toolExecutor.ExecuteAsync(decision.Invocations, cancellationToken);

        // 6. Build chat history + tool context
        var history = await conversation.BuildHistoryAsync(sessionId, request.SystemPrompt, cancellationToken);
        AppendToolContext(history, toolResults, decision.Reasoning);

        // 7. Get final LLM response
        // GetChatMessageContentAsync correct named parameter is 'cancellationToken'
        var kernel      = kernelFactory.CreateForRequest();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var llmResponse = await chatService.GetChatMessageContentAsync(
            history,
            DefaultSettings,
            kernel,
            cancellationToken: cancellationToken);

        var responseText = llmResponse.Content ?? "(no response)";
        var tokens       = llmResponse.Metadata?.TryGetValue("Usage", out var usage) == true
            ? ExtractTokens(usage)
            : 0;

        // 8. Persist assistant response
        await conversation.AddAssistantMessageAsync(sessionId, responseText, tokens, cancellationToken);

        sw.Stop();
        logger.LogInformation(
            "Chat:Complete sessionId={Id} tokens={Tokens} ms={Ms}",
            sessionId, tokens, sw.ElapsedMilliseconds);

        return new ChatResponse
        {
            SessionId   = sessionId,
            Message     = responseText,
            ToolsUsed   = toolResults,
            TotalTokens = tokens,
            LatencyMs   = sw.ElapsedMilliseconds
        };
    }

    // ── Streaming ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Process a chat request with streaming output.
    /// Yields <see cref="StreamChunk"/> items via an async enumerable.
    /// </summary>
    public async IAsyncEnumerable<StreamChunk> ProcessStreamingAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 1. Session management
        var sessionId = await conversation.GetOrCreateSessionAsync(request.SessionId, cancellationToken);
        yield return new StreamChunk
        {
            Type      = AppConstants.StreamTypes.Content,
            Content   = "",
            SessionId = sessionId
        };

        // 2. Persist user message
        await conversation.AddUserMessageAsync(sessionId, request.Message, cancellationToken);

        // 3. Route
        var decision = await router.DecideAsync(request.Message, request.PreferredPlugins, cancellationToken);

        // 4. Emit tool-start events
        foreach (var inv in decision.Invocations)
        {
            yield return new StreamChunk
            {
                Type = AppConstants.StreamTypes.ToolStart,
                Tool = $"{inv.PluginName}.{inv.FunctionName}"
            };
        }

        // 5. Human approval
        if (decision.RequiresHumanApproval)
        {
            yield return new StreamChunk
            {
                Type    = AppConstants.StreamTypes.Content,
                Content = "⏳ Waiting for human approval..."
            };

            var approved = await approvalService.RequestApprovalAsync(
                "PluginExecution",
                decision.ApprovalReason ?? "AI wants to execute plugins",
                new Dictionary<string, string> { ["reasoning"] = decision.Reasoning },
                cancellationToken);

            if (!approved)
            {
                const string rejected = "I need human approval to perform this action, but it was not granted.";
                yield return new StreamChunk { Type = AppConstants.StreamTypes.Content, Content = rejected };
                yield return new StreamChunk { Type = AppConstants.StreamTypes.Done };
                await conversation.AddAssistantMessageAsync(sessionId, rejected, cancellationToken: cancellationToken);
                yield break;
            }
        }

        // 6. Execute plugins
        var toolResults = await toolExecutor.ExecuteAsync(decision.Invocations, cancellationToken);

        // Emit tool-end events
        foreach (var result in toolResults)
        {
            yield return new StreamChunk
            {
                Type = AppConstants.StreamTypes.ToolEnd,
                Tool = $"{result.PluginName}.{result.FunctionName}"
            };
        }

        // 7. Build history + tool context
        var history = await conversation.BuildHistoryAsync(sessionId, request.SystemPrompt, cancellationToken);
        AppendToolContext(history, toolResults, decision.Reasoning);

        // 8. Stream LLM response
        var kernel      = kernelFactory.CreateForRequest();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var sb          = new StringBuilder();

        await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(
                           history,
                           DefaultSettings,
                           kernel,
                           cancellationToken))
        {
            var text = chunk.Content;
            if (string.IsNullOrEmpty(text)) continue;

            sb.Append(text);
            yield return new StreamChunk
            {
                Type    = AppConstants.StreamTypes.Content,
                Content = text
            };
        }

        // 9. Persist complete response
        var fullResponse = sb.ToString();
        await conversation.AddAssistantMessageAsync(sessionId, fullResponse, cancellationToken: cancellationToken);

        yield return new StreamChunk
        {
            Type      = AppConstants.StreamTypes.Done,
            SessionId = sessionId
        };
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static void AppendToolContext(
        ChatHistory history,
        IReadOnlyList<ToolExecutionResult> results,
        string reasoning)
    {
        if (results.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine($"[Router reasoning: {reasoning}]");
        sb.AppendLine();
        sb.AppendLine("Tool results:");

        foreach (var r in results)
        {
            sb.AppendLine($"- {r.PluginName}.{r.FunctionName} ({r.DurationMs}ms):");
            if (r.Success)
                sb.AppendLine($"  {r.Output}");
            else
                sb.AppendLine($"  ERROR: {r.Error}");
        }

        history.AddSystemMessage(sb.ToString());
    }

    private static int ExtractTokens(object? usage)
    {
        if (usage is null) return 0;
        try
        {
            var prop = usage.GetType().GetProperty("TotalTokenCount")
                    ?? usage.GetType().GetProperty("TotalTokens");
            return (int?)prop?.GetValue(usage) ?? 0;
        }
        catch { return 0; }
    }
}
