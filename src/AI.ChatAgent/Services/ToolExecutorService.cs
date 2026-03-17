using System.Diagnostics;
using AI.ChatAgent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace AI.ChatAgent.Services;

/// <summary>
/// Executes plugin invocations decided by the router.
/// Runs plugins with the same priority level in parallel (Task.WhenAll).
/// Uses <see cref="KernelFactory"/> to obtain a per-request Kernel whose
/// plugins are resolved from the current DI scope — avoiding the
/// "Cannot resolve scoped service from root provider" error.
/// </summary>
public sealed class ToolExecutorService(
    IKernelFactory kernelFactory,
    ILogger<ToolExecutorService> logger)
{
    /// <summary>
    /// Execute a list of plugin invocations respecting priority ordering.
    /// All invocations at the same priority level run in parallel.
    /// </summary>
    public async Task<IReadOnlyList<ToolExecutionResult>> ExecuteAsync(
        IReadOnlyList<PluginInvocation> invocations,
        CancellationToken ct = default)
    {
        if (invocations.Count == 0)
            return [];

        logger.LogInformation("ToolExecutor:Execute count={Count}", invocations.Count);

        // Build ONE scoped kernel for this execution batch.
        // All parallel tasks within the same request share this instance safely
        // because Kernel.InvokeAsync is thread-safe for concurrent reads.
        var kernel = kernelFactory.CreateForRequest();

        var allResults = new List<ToolExecutionResult>();

        // Group by priority; higher number = higher priority = runs first
        var groups = invocations
            .GroupBy(i => i.Priority)
            .OrderByDescending(g => g.Key);

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            logger.LogDebug("ToolExecutor:Priority={Priority} parallel_count={Count}",
                group.Key, group.Count());

            var tasks   = group.Select(inv => ExecuteSingleAsync(kernel, inv, ct)).ToList();
            var results = await Task.WhenAll(tasks);
            allResults.AddRange(results);
        }

        return allResults;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task<ToolExecutionResult> ExecuteSingleAsync(
        Kernel kernel,
        PluginInvocation inv,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ToolExecutor:Invoke {Plugin}.{Function}",
            inv.PluginName, inv.FunctionName);

        try
        {
            if (!kernel.Plugins.TryGetPlugin(inv.PluginName, out var plugin))
                return Fail(inv, sw, $"Plugin '{inv.PluginName}' not found");

            if (!plugin.TryGetFunction(inv.FunctionName, out var function))
                return Fail(inv, sw, $"Function '{inv.FunctionName}' not found in '{inv.PluginName}'");

            var args = new KernelArguments();

            // The router LLM may capitalise argument names differently from the C# parameter
            // declarations (e.g. "City" vs "city", "Query" vs "query").
            // SK's KernelArguments lookup is case-sensitive, so we must match exactly.
            // Strategy: for each LLM-supplied key, find the declared parameter whose name
            // matches case-insensitively, and use the declared name as the key.
            var declaredParams = function.Metadata.Parameters
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, value) in inv.Arguments)
            {
                // Find the exact declared parameter name (case-insensitive match)
                var canonical = declaredParams.FirstOrDefault(
                    p => string.Equals(p, key, StringComparison.OrdinalIgnoreCase));

                args[canonical ?? key] = value;
            }

            var result = await kernel.InvokeAsync(function, args, ct);
            var output = result.GetValue<string>() ?? result.ToString();

            sw.Stop();
            logger.LogInformation("ToolExecutor:{Plugin}.{Function} completed in {Ms}ms",
                inv.PluginName, inv.FunctionName, sw.ElapsedMilliseconds);

            return new ToolExecutionResult
            {
                PluginName   = inv.PluginName,
                FunctionName = inv.FunctionName,
                Success      = true,
                Output       = output,
                DurationMs   = sw.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            logger.LogWarning("ToolExecutor:{Plugin}.{Function} cancelled",
                inv.PluginName, inv.FunctionName);
            return Fail(inv, sw, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "ToolExecutor:{Plugin}.{Function} threw exception",
                inv.PluginName, inv.FunctionName);
            return Fail(inv, sw, ex.Message);
        }
    }

    private static ToolExecutionResult Fail(PluginInvocation inv, Stopwatch sw, string error) =>
        new()
        {
            PluginName   = inv.PluginName,
            FunctionName = inv.FunctionName,
            Success      = false,
            Error        = error,
            DurationMs   = sw.ElapsedMilliseconds
        };
}
