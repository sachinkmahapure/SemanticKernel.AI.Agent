using System.Diagnostics;
using AI.ChatAgent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace AI.ChatAgent.Services;

/// <summary>
/// Executes plugin invocations decided by the router.
/// Runs plugins with the same priority in parallel; higher priority runs first.
/// </summary>
public sealed class ToolExecutorService(
    Kernel kernel,
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

        var allResults = new List<ToolExecutionResult>();

        // Group by priority (ascending = runs first)
        var groups = invocations
            .GroupBy(i => i.Priority)
            .OrderByDescending(g => g.Key); // higher number = higher priority = first

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            logger.LogDebug("ToolExecutor:Priority={Priority} parallel_count={Count}",
                group.Key, group.Count());

            // Execute all invocations in this priority group in parallel
            var tasks = group.Select(inv => ExecuteSingleAsync(inv, ct)).ToList();
            var results = await Task.WhenAll(tasks);
            allResults.AddRange(results);
        }

        return allResults;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task<ToolExecutionResult> ExecuteSingleAsync(
        PluginInvocation inv,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ToolExecutor:Invoke {Plugin}.{Function}",
            inv.PluginName, inv.FunctionName);

        try
        {
            // Locate the plugin function in the kernel
            if (!kernel.Plugins.TryGetPlugin(inv.PluginName, out var plugin))
            {
                return Fail(inv, sw, $"Plugin '{inv.PluginName}' not found");
            }

            if (!plugin.TryGetFunction(inv.FunctionName, out var function))
            {
                return Fail(inv, sw, $"Function '{inv.FunctionName}' not found in '{inv.PluginName}'");
            }

            // Build kernel arguments from the string dictionary
            var args = new KernelArguments();
            foreach (var (key, value) in inv.Arguments)
                args[key] = value;

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
            logger.LogWarning("ToolExecutor:{Plugin}.{Function} was cancelled", inv.PluginName, inv.FunctionName);
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
