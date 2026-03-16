using AI.ChatAgent.Data;
using AI.ChatAgent.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AI.ChatAgent.Services;

/// <summary>
/// Manages conversation sessions: creates sessions, persists messages,
/// and builds a <see cref="ChatHistory"/> from stored messages for the kernel.
/// </summary>
public sealed class ConversationService(
    ChatAgentDbContext db,
    ILogger<ConversationService> logger)
{
    private const int MaxHistoryMessages = 40; // keep last N messages for context

    /// <summary>
    /// Get or create a conversation session. Returns the session ID.
    /// If <paramref name="sessionId"/> is empty a new session is created.
    /// </summary>
    public async Task<string> GetOrCreateSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var exists = await db.ChatSessions.AnyAsync(s => s.Id == sessionId, cancellationToken);
            if (exists)
            {
                await db.ChatSessions
                    .Where(s => s.Id == sessionId)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(x => x.LastActivityAt, DateTimeOffset.UtcNow), cancellationToken);
                return sessionId;
            }
        }

        // Create new
        var session = new ChatSession { Id = Guid.NewGuid().ToString("N") };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Conversation:NewSession id={Id}", session.Id);
        return session.Id;
    }

    /// <summary>
    /// Build a <see cref="ChatHistory"/> for the kernel from the stored messages.
    /// Includes a system prompt as the first message.
    /// </summary>
    public async Task<ChatHistory> BuildHistoryAsync(
        string sessionId,
        string? systemPromptOverride,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = systemPromptOverride ?? DefaultSystemPrompt();
        var history = new ChatHistory(systemPrompt);

        var messages = await db.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.Id)   // newest first in SQL
            .Take(MaxHistoryMessages)               // Take N from the top (translates to SQL LIMIT)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Reverse in memory so the final list is oldest-first (chronological order for the prompt)
        messages.Reverse();

        foreach (var msg in messages)
        {
            switch (msg.Role)
            {
                case AppConstants.Roles.User:
                    history.AddUserMessage(msg.Content);
                    break;
                case AppConstants.Roles.Assistant:
                    history.AddAssistantMessage(msg.Content);
                    break;
            }
        }

        logger.LogDebug("Conversation:BuildHistory sessionId={Id} messages={Count}",
            sessionId, messages.Count);

        return history;
    }

    /// <summary>Persist a user message to the database.</summary>
    public async Task AddUserMessageAsync(string sessionId, string content, CancellationToken cancellationToken = default)
    {
        await AddMessageAsync(sessionId, AppConstants.Roles.User, content, cancellationToken);
    }

    /// <summary>Persist an assistant message to the database.</summary>
    public async Task AddAssistantMessageAsync(string sessionId, string content, int? tokens = null, CancellationToken cancellationToken = default)
    {
        await AddMessageAsync(sessionId, AppConstants.Roles.Assistant, content, cancellationToken, tokens);
    }

    /// <summary>Retrieve conversation history for a session.</summary>
    public async Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(
        string sessionId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        return await db.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.Id)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task AddMessageAsync(
        string sessionId, string role, string content,
        CancellationToken cancellationToken, int? tokens = null)
    {
        var message = new ChatMessage
        {
            SessionId  = sessionId,
            Role       = role,
            Content    = content,
            CreatedAt  = DateTimeOffset.UtcNow,
            TokenCount = tokens
        };

        db.ChatMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string DefaultSystemPrompt() =>
        @"You are a helpful AI assistant with access to multiple data sources:
database records, external APIs, PDF documents, data files, and web search.

When answering:
- Be concise and accurate
- If you used data from a tool, mention the source
- Format numbers and dates clearly
- If you don't know something, say so rather than guessing
- Keep responses friendly and professional

Current date/time: " + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
}
