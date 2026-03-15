using System.Collections.Concurrent;
using AI.ChatAgent.Configuration;
using AI.ChatAgent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI.ChatAgent.Services;

/// <summary>
/// Human-in-the-loop approval workflow.
/// Requests are stored in memory and can be approved/rejected via an API endpoint.
/// In production, integrate with Slack, email, or a review dashboard.
/// </summary>
public sealed class HumanApprovalService(
    IOptions<HumanApprovalOptions> options,
    ILogger<HumanApprovalService> logger)
{
    private readonly ConcurrentDictionary<string, ApprovalRequest> _pending = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _completionSources = new();

    /// <summary>
    /// Check if a specific action requires human approval.
    /// </summary>
    public bool RequiresApproval(string actionName) =>
        options.Value.Enabled &&
        options.Value.RequiredForActions.Any(a =>
            string.Equals(a, actionName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Submit an action for human approval. Waits until approved/rejected or timeout.
    /// Returns <c>true</c> if approved, <c>false</c> if rejected or timed out.
    /// </summary>
    public async Task<bool> RequestApprovalAsync(
        string actionName,
        string description,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken ct = default)
    {
        var request = new ApprovalRequest
        {
            ActionName  = actionName,
            Description = description,
            Parameters  = parameters,
            RequestedAt = DateTimeOffset.UtcNow,
            ExpiresAt   = DateTimeOffset.UtcNow.AddSeconds(options.Value.TimeoutSeconds)
        };

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.Id]           = request;
        _completionSources[request.Id] = tcs;

        logger.LogWarning(
            "HumanApproval:PendingApproval id={Id} action={Action}",
            request.Id, actionName);

        // Simulate sending a notification (webhook/email/Slack in production)
        NotifyReviewers(request);

        // Wait for approval or timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));

        try
        {
            var approved = await tcs.Task.WaitAsync(cts.Token);
            logger.LogInformation(
                "HumanApproval:Resolved id={Id} approved={Approved}", request.Id, approved);
            return approved;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("HumanApproval:Timeout id={Id}", request.Id);
            request.Status = ApprovalStatus.Expired;
            _completionSources.TryRemove(request.Id, out _);
            return false;
        }
    }

    /// <summary>Get all pending approval requests.</summary>
    public IReadOnlyList<ApprovalRequest> GetPendingRequests() =>
        _pending.Values.Where(r => r.Status == ApprovalStatus.Pending).ToList();

    /// <summary>Get a specific approval request by ID.</summary>
    public ApprovalRequest? GetRequest(string id) =>
        _pending.TryGetValue(id, out var req) ? req : null;

    /// <summary>
    /// Approve or reject a pending request (called by a human reviewer via API).
    /// </summary>
    public bool Resolve(string id, bool approved, string? reviewer, string? notes)
    {
        if (!_pending.TryGetValue(id, out var request))
            return false;

        if (request.Status != ApprovalStatus.Pending)
            return false;

        request.Status     = approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        request.ReviewedBy = reviewer;
        request.ReviewNotes = notes;

        if (_completionSources.TryRemove(id, out var tcs))
            tcs.TrySetResult(approved);

        logger.LogInformation(
            "HumanApproval:Resolved id={Id} approved={Approved} by={Reviewer}",
            id, approved, reviewer);

        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private void NotifyReviewers(ApprovalRequest request)
    {
        // In production: send to Slack webhook, email, PagerDuty, etc.
        logger.LogWarning(
            "⚠️  HUMAN APPROVAL REQUIRED:\n" +
            "  ID:          {Id}\n" +
            "  Action:      {Action}\n" +
            "  Description: {Description}\n" +
            "  Expires:     {Expires}\n" +
            "  Approve via: POST /approvals/{Id}/resolve?approved=true",
            request.Id, request.ActionName, request.Description, request.ExpiresAt, request.Id);
    }
}
