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
/// Pipeline: session → RAG retrieval → router → tool execution → LLM synthesis → stream.
/// </summary>
public sealed class ChatService(
	IKernelFactory kernelFactory,
	RouterService router,
	ToolExecutorService toolExecutor,
	ConversationService conversation,
	HumanApprovalService approvalService,
	RagRetrievalService ragRetrieval,
	ILogger<ChatService> logger)
{
	private static readonly OpenAIPromptExecutionSettings DefaultSettings = new()
	{
		MaxTokens = 4096,
		Temperature = 0.7,
		TopP = 0.95
	};

	// ── Non-streaming ─────────────────────────────────────────────────────────

	/// <summary>Process a chat request and return a complete response.</summary>
	public async Task<ChatResponse> ProcessAsync(
		ChatRequest request,
		CancellationToken cancellationToken = default)
	{
		var sw = Stopwatch.StartNew();

		var sessionId = await conversation.GetOrCreateSessionAsync(request.SessionId, cancellationToken);
		await conversation.AddUserMessageAsync(sessionId, request.Message, cancellationToken);

		var decision = await router.DecideAsync(request.Message, request.PreferredPlugins, cancellationToken);
		logger.LogInformation("Chat:Route reasoning={Reasoning}", decision.Reasoning);

		if (decision.RequiresHumanApproval)
		{
			var approved = await approvalService.RequestApprovalAsync(
				"PluginExecution",
				decision.ApprovalReason ?? "AI wants to execute plugins",
				new Dictionary<string, string> { ["reasoning"] = decision.Reasoning },
				cancellationToken);

			if (!approved)
			{
				const string rejected = "I need human approval to perform this action, but the request was rejected or timed out.";
				await conversation.AddAssistantMessageAsync(sessionId, rejected, cancellationToken: cancellationToken);
				return new ChatResponse
				{
					SessionId = sessionId,
					Message = rejected,
					ToolsUsed = [],
					TotalTokens = 0,
					LatencyMs = sw.ElapsedMilliseconds
				};
			}
		}

		var toolResults = await toolExecutor.ExecuteAsync(decision.Invocations, cancellationToken);
		var history = await conversation.BuildHistoryAsync(sessionId, request.SystemPrompt, cancellationToken);

		// ── RAG: retrieve relevant context and inject before tool results ──────
		await InjectRagContextAsync(history, request.Message, cancellationToken);

		AppendToolContext(history, toolResults, decision.Reasoning);

		var kernel = kernelFactory.CreateForRequest();
		var chatService = kernel.GetRequiredService<IChatCompletionService>();
		var llmResponse = await chatService.GetChatMessageContentAsync(
			history, DefaultSettings, kernel,
			cancellationToken: cancellationToken);

		var responseText = llmResponse.Content ?? "(no response)";
		var tokens = ExtractTokens(llmResponse.Metadata);

		await conversation.AddAssistantMessageAsync(sessionId, responseText, tokens, cancellationToken);

		sw.Stop();
		logger.LogInformation("Chat:Complete sessionId={Id} tokens={Tokens} ms={Ms}",
			sessionId, tokens, sw.ElapsedMilliseconds);

		return new ChatResponse
		{
			SessionId = sessionId,
			Message = responseText,
			ToolsUsed = toolResults,
			TotalTokens = tokens,
			LatencyMs = sw.ElapsedMilliseconds
		};
	}

	// ── Streaming ─────────────────────────────────────────────────────────────

	public async IAsyncEnumerable<StreamChunk> ProcessStreamingAsync(
		ChatRequest request,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var sessionId = await conversation.GetOrCreateSessionAsync(request.SessionId, cancellationToken);
		yield return new StreamChunk { Type = AppConstants.StreamTypes.Content, Content = "", SessionId = sessionId };

		await conversation.AddUserMessageAsync(sessionId, request.Message, cancellationToken);

		var decision = await router.DecideAsync(request.Message, request.PreferredPlugins, cancellationToken);

		foreach (var inv in decision.Invocations)
			yield return new StreamChunk { Type = AppConstants.StreamTypes.ToolStart, Tool = $"{inv.PluginName}.{inv.FunctionName}" };

		if (decision.RequiresHumanApproval)
		{
			yield return new StreamChunk { Type = AppConstants.StreamTypes.Content, Content = "⏳ Waiting for human approval..." };

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

		var toolResults = await toolExecutor.ExecuteAsync(decision.Invocations, cancellationToken);

		foreach (var result in toolResults)
			yield return new StreamChunk { Type = AppConstants.StreamTypes.ToolEnd, Tool = $"{result.PluginName}.{result.FunctionName}" };

		var history = await conversation.BuildHistoryAsync(sessionId, request.SystemPrompt, cancellationToken);

		// ── RAG: retrieve relevant context ─────────────────────────────────────
		var ragHits = await ragRetrieval.SearchAsync(request.Message, ct: cancellationToken);
		if (ragHits.Count > 0)
		{
			yield return new StreamChunk
			{
				Type = AppConstants.StreamTypes.Content,
				Content = $"📚 Found {ragHits.Count} relevant document(s)...\n"
			};
			history.AddSystemMessage(RagRetrievalService.FormatAsContext(ragHits));
		}

		AppendToolContext(history, toolResults, decision.Reasoning);

		var kernel = kernelFactory.CreateForRequest();
		var chatService = kernel.GetRequiredService<IChatCompletionService>();
		var sb = new StringBuilder();

		await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(
						   history, DefaultSettings, kernel, cancellationToken))
		{
			var text = chunk.Content;
			if (string.IsNullOrEmpty(text)) continue;
			sb.Append(text);
			yield return new StreamChunk { Type = AppConstants.StreamTypes.Content, Content = text };
		}

		await conversation.AddAssistantMessageAsync(sessionId, sb.ToString(), cancellationToken: cancellationToken);

		yield return new StreamChunk { Type = AppConstants.StreamTypes.Done, SessionId = sessionId };
	}

	// ─────────────────────────────────────────────────────────────────────────

	// ── Helpers ───────────────────────────────────────────────────────────────

	private async Task InjectRagContextAsync(
		Microsoft.SemanticKernel.ChatCompletion.ChatHistory history,
		string query,
		CancellationToken ct)
	{
		var results = await ragRetrieval.SearchAsync(query, ct: ct);
		if (results.Count > 0)
		{
			history.AddSystemMessage(RagRetrievalService.FormatAsContext(results));
			logger.LogInformation("RAG:Injected {Count} chunks into prompt", results.Count);
		}
	}

	private static void AppendToolContext(
		Microsoft.SemanticKernel.ChatCompletion.ChatHistory history,
		IReadOnlyList<ToolExecutionResult> results,
		string reasoning)
	{
		if (results.Count == 0) return;
		var sb = new StringBuilder();
		sb.AppendLine($"[Router reasoning: {reasoning}]");
		sb.AppendLine("Tool results:");
		foreach (var r in results)
		{
			sb.AppendLine($"- {r.PluginName}.{r.FunctionName} ({r.DurationMs}ms):");
			sb.AppendLine(r.Success ? $"  {r.Output}" : $"  ERROR: {r.Error}");
		}
		history.AddSystemMessage(sb.ToString());
	}

	private static int ExtractTokens(IReadOnlyDictionary<string, object?>? metadata)
	{
		if (metadata is null) return 0;
		try
		{
			if (metadata.TryGetValue("Usage", out var usage) && usage is not null)
			{
				var prop = usage.GetType().GetProperty("TotalTokenCount")
						?? usage.GetType().GetProperty("TotalTokens");
				return (int?)prop?.GetValue(usage) ?? 0;
			}
		}
		catch { /* ignore */ }
		return 0;
	}
}
