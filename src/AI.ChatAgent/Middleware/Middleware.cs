using System.Diagnostics;
using System.Net;
using System.Text.Json;
using AI.ChatAgent.Models;
using Microsoft.Extensions.Logging;

namespace AI.ChatAgent.Middleware;

/// <summary>
/// Logs every incoming request with correlation ID, method, path, status, and duration.
/// </summary>
public sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = context.TraceIdentifier;
        var sw = Stopwatch.StartNew();

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[AppConstants.Headers.XRequestId] = requestId;
            return Task.CompletedTask;
        });

        try
        {
            await next(context);
        }
        finally
        {
            sw.Stop();
            logger.LogInformation(
                "{Method} {Path} → {Status} ({Ms}ms) [{RequestId}]",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                sw.ElapsedMilliseconds,
                requestId);
        }
    }
}

/// <summary>
/// Global exception handler: returns structured JSON error responses.
/// </summary>
public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected — not an error
            logger.LogDebug("Request cancelled by client: {Path}", context.Request.Path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Method} {Path}",
                context.Request.Method, context.Request.Path);

            context.Response.StatusCode  = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var error = new
            {
                error  = "An unexpected error occurred.",
                detail = ex.Message,
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsync(
                JsonSerializer.Serialize(error, JsonOpts));
        }
    }
}

/// <summary>
/// Validates the Content-Type for POST endpoints.
/// </summary>
public sealed class ContentTypeValidationMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> PostPaths = ["/chat", "/chat/stream"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Method == HttpMethod.Post.Method &&
            PostPaths.Contains(context.Request.Path.Value ?? ""))
        {
            var contentType = context.Request.ContentType ?? "";
            if (!contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode  = StatusCodes.Status415UnsupportedMediaType;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    "{\"error\":\"Content-Type must be application/json\"}");
                return;
            }
        }

        await next(context);
    }
}
