using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AI.ChatAgent.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace AI.ChatAgent.Plugins;

/// <summary>
/// Generic REST API plugin. The AI can call registered external APIs
/// by name, or make ad-hoc GET/POST requests.
/// </summary>
public sealed class ApiPlugin(
    IHttpClientFactory httpClientFactory,
    IOptions<ExternalApiOptions> options,
    ILogger<ApiPlugin> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Call a registered API by its registry name.</summary>
    [KernelFunction(nameof(CallRegisteredApi))]
    [Description("Call a registered external API. Available: Weather, GitHub.")]
    public async Task<string> CallRegisteredApi(
        [Description("API name from registry (e.g. 'Weather', 'GitHub')")] string apiName,
        [Description("URL path appended to base URL (e.g. '/weather?q=London')")] string path,
        [Description("HTTP method: GET or POST")] string method = "GET",
        [Description("JSON request body for POST (optional)")] string? body = null,
        CancellationToken ct = default)
    {
        if (!options.Value.ApiRegistry.TryGetValue(apiName, out var entry))
        {
            var available = string.Join(", ", options.Value.ApiRegistry.Keys);
            return $"{{\"error\": \"API '{apiName}' not found. Available: {available}\"}}";
        }

        logger.LogInformation("API:CallRegisteredApi name={Name} path={Path} method={Method}",
            apiName, path, method);

        var client = httpClientFactory.CreateClient("ResilienceClient");
        var url = entry.BaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');

        using var request = BuildRequest(method, url, entry.ApiKey, entry.AuthHeader, body);

        try
        {
            using var response = await client.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            logger.LogInformation("API:CallRegisteredApi status={Status}", (int)response.StatusCode);
            return content;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "API:CallRegisteredApi failed for {ApiName}", apiName);
            return $"{{\"error\": \"{ex.Message}\"}}";
        }
    }

    /// <summary>Make a generic HTTP GET request.</summary>
    [KernelFunction(nameof(HttpGet))]
    [Description("Make a generic HTTP GET request to any public URL. Returns response body as string.")]
    public async Task<string> HttpGet(
        [Description("Full URL to call")] string url,
        [Description("Optional Bearer token or API key")] string? authToken = null,
        CancellationToken ct = default)
    {
        logger.LogInformation("API:HttpGet url={Url}", url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            return $"{{\"error\": \"Invalid URL: {url}\"}}";

        var client = httpClientFactory.CreateClient("ResilienceClient");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (!string.IsNullOrWhiteSpace(authToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        try
        {
            using var response = await client.SendAsync(request, ct);
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "API:HttpGet failed for {Url}", url);
            return $"{{\"error\": \"{ex.Message}\"}}";
        }
    }

    /// <summary>Make a generic HTTP POST request.</summary>
    [KernelFunction(nameof(HttpPost))]
    [Description("Make a generic HTTP POST request with a JSON body.")]
    public async Task<string> HttpPost(
        [Description("Full URL to call")] string url,
        [Description("JSON body to send")] string jsonBody,
        [Description("Optional Bearer token")] string? authToken = null,
        CancellationToken ct = default)
    {
        logger.LogInformation("API:HttpPost url={Url}", url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            return $"{{\"error\": \"Invalid URL: {url}\"}}";

        var client = httpClientFactory.CreateClient("ResilienceClient");
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(authToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        try
        {
            using var response = await client.SendAsync(request, ct);
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "API:HttpPost failed for {Url}", url);
            return $"{{\"error\": \"{ex.Message}\"}}";
        }
    }

    /// <summary>Fetch current weather for a city using OpenWeatherMap.</summary>
    [KernelFunction(nameof(GetWeather))]
    [Description("Get current weather for a city. Returns temperature, conditions, humidity.")]
    public async Task<string> GetWeather(
        [Description("City name (e.g. 'London', 'New York')")] string city,
        [Description("Units: metric (Celsius) or imperial (Fahrenheit)")] string units = "metric",
        CancellationToken ct = default)
    {
        logger.LogInformation("API:GetWeather city={City} units={Units}", city, units);

        if (!options.Value.ApiRegistry.TryGetValue("Weather", out var entry) ||
            string.IsNullOrWhiteSpace(entry.ApiKey))
        {
            return "{\"error\": \"Weather API not configured. Add ApiRegistry.Weather in appsettings.\"}";
        }

        var url = $"{entry.BaseUrl}/weather?q={Uri.EscapeDataString(city)}&units={units}&appid={entry.ApiKey}";
        var client = httpClientFactory.CreateClient("ResilienceClient");

        try
        {
            var json = await client.GetStringAsync(url, ct);
            // Parse and simplify the response
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new
            {
                city = root.TryGetProperty("name", out var n) ? n.GetString() : city,
                country = root.TryGetProperty("sys", out var sys) && sys.TryGetProperty("country", out var c) ? c.GetString() : "",
                temperature = root.TryGetProperty("main", out var main) && main.TryGetProperty("temp", out var t) ? t.GetDouble() : 0,
                feels_like = main.TryGetProperty("feels_like", out var fl) ? fl.GetDouble() : 0,
                humidity = main.TryGetProperty("humidity", out var h) ? h.GetInt32() : 0,
                description = root.TryGetProperty("weather", out var w) && w.GetArrayLength() > 0
                    ? w[0].TryGetProperty("description", out var d) ? d.GetString() : "" : "",
                units
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "API:GetWeather failed for {City}", city);
            return $"{{\"error\": \"{ex.Message}\"}}";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static HttpRequestMessage BuildRequest(
        string method, string url, string apiKey,
        string authHeader, string? body)
    {
        var httpMethod = method.ToUpperInvariant() switch
        {
            "POST"   => HttpMethod.Post,
            "PUT"    => HttpMethod.Put,
            "PATCH"  => HttpMethod.Patch,
            "DELETE" => HttpMethod.Delete,
            _        => HttpMethod.Get
        };

        var request = new HttpRequestMessage(httpMethod, url);

        if (!string.IsNullOrWhiteSpace(apiKey) &&
            !string.Equals(authHeader, "none", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(authHeader, apiKey);
        }

        if (body is not null && httpMethod != HttpMethod.Get)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        return request;
    }
}
