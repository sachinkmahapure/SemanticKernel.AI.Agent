using AI.ChatAgent.Configuration;
using AI.ChatAgent.Data;
using AI.ChatAgent.Middleware;
using AI.ChatAgent.Models;
using AI.ChatAgent.Plugins;
using AI.ChatAgent.Services;
using AI.ChatAgent.Validation;
using AspNetCoreRateLimit;
using FluentValidation;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.OpenApi.Models;
using Microsoft.SemanticKernel;
using Polly;
using Serilog;
using System.Net;
using System.Text.Json;

// ── Bootstrap Serilog ────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
	.WriteTo.Console()
	.CreateBootstrapLogger();

try
{
	Log.Information("Starting AI.ChatAgent on .NET 10...");

	var builder = WebApplication.CreateBuilder(args);

	// ── Serilog ───────────────────────────────────────────────────────────────
	builder.Host.UseSerilog((ctx, services, cfg) =>
		cfg.ReadFrom.Configuration(ctx.Configuration)
		   .ReadFrom.Services(services)
		   .Enrich.FromLogContext());

	// ── Configuration binding ─────────────────────────────────────────────────
	builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
	builder.Services.Configure<WebSearchOptions>(builder.Configuration.GetSection(WebSearchOptions.SectionName));
	builder.Services.Configure<ExternalApiOptions>(builder.Configuration.GetSection(ExternalApiOptions.SectionName));
	builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
	builder.Services.Configure<HumanApprovalOptions>(builder.Configuration.GetSection(HumanApprovalOptions.SectionName));

	var aiOpts = builder.Configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
	var extApiOpts = builder.Configuration.GetSection(ExternalApiOptions.SectionName).Get<ExternalApiOptions>() ?? new ExternalApiOptions();

	// ── Database — SQLite (lightweight, zero-config, works everywhere) ────────
	var sqliteConn = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=chatagent.db";

	builder.Services.AddDbContext<ChatAgentDbContext>(opt => opt.UseSqlite(sqliteConn));

	// ── Redis / Distributed Cache ──────────────────────────────────────────────
	builder.Services.AddDistributedMemoryCache();

	// ── HTTP Client with Polly v8 resilience ──────────────────────────────────
	builder.Services.AddHttpClient("ResilienceClient", client =>
	{
		client.Timeout = TimeSpan.FromSeconds(extApiOpts.DefaultTimeoutSeconds);
		client.DefaultRequestHeaders.Add("User-Agent", "AI.ChatAgent/1.0");
	})
	.AddResilienceHandler("default", pipeline =>
	{
		pipeline.AddRetry(new HttpRetryStrategyOptions
		{
			MaxRetryAttempts = extApiOpts.RetryCount,
			Delay = TimeSpan.FromSeconds(extApiOpts.RetryDelaySeconds),
			UseJitter = true,
			BackoffType = DelayBackoffType.Exponential,
			ShouldHandle = args => ValueTask.FromResult(
				args.Outcome.Exception is HttpRequestException ||
				args.Outcome.Result?.StatusCode is HttpStatusCode.TooManyRequests
					or HttpStatusCode.InternalServerError
					or HttpStatusCode.BadGateway
					or HttpStatusCode.ServiceUnavailable
					or HttpStatusCode.GatewayTimeout),
			OnRetry = args =>
			{
				Log.Warning("HTTP retry {Attempt} after {Delay}s",
					args.AttemptNumber + 1, args.RetryDelay.TotalSeconds);
				return ValueTask.CompletedTask;
			}
		});

		pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
		{
			SamplingDuration = TimeSpan.FromSeconds(extApiOpts.CircuitBreakerDurationSeconds),
			MinimumThroughput = extApiOpts.CircuitBreakerThreshold,
			FailureRatio = 0.5,
			BreakDuration = TimeSpan.FromSeconds(extApiOpts.CircuitBreakerDurationSeconds),
			OnOpened = _ => { Log.Warning("Circuit breaker OPEN"); return ValueTask.CompletedTask; },
			OnClosed = _ => { Log.Information("Circuit breaker RESET"); return ValueTask.CompletedTask; }
		});

		pipeline.AddTimeout(TimeSpan.FromSeconds(extApiOpts.DefaultTimeoutSeconds));
	});

	// ── Semantic Kernel ───────────────────────────────────────────────────────
	var kernelBuilder = builder.Services.AddKernel();

	if (string.Equals(aiOpts.Provider, "AzureOpenAI", StringComparison.OrdinalIgnoreCase)
		&& !string.IsNullOrWhiteSpace(aiOpts.AzureOpenAI.ApiKey))
	{
		kernelBuilder.AddAzureOpenAIChatCompletion(
			deploymentName: aiOpts.AzureOpenAI.ChatDeployment,
			endpoint: aiOpts.AzureOpenAI.Endpoint,
			apiKey: aiOpts.AzureOpenAI.ApiKey);
		kernelBuilder.AddAzureOpenAITextEmbeddingGeneration(
			deploymentName: aiOpts.AzureOpenAI.EmbeddingDeployment,
			endpoint: aiOpts.AzureOpenAI.Endpoint,
			apiKey: aiOpts.AzureOpenAI.ApiKey);
		Log.Information("Using Azure OpenAI: {Endpoint}", aiOpts.AzureOpenAI.Endpoint);
	}
	else
	{
		kernelBuilder.AddOpenAIChatCompletion(
			modelId: aiOpts.OpenAI.ChatModelId,
			apiKey: aiOpts.OpenAI.ApiKey,
			orgId: string.IsNullOrWhiteSpace(aiOpts.OpenAI.OrgId) ? null : aiOpts.OpenAI.OrgId);
		// text-embedding-3-small: fast, cheap, 1536 dimensions
		kernelBuilder.AddOpenAITextEmbeddingGeneration(
			modelId: aiOpts.OpenAI.EmbeddingModelId,
			apiKey: aiOpts.OpenAI.ApiKey,
			orgId: string.IsNullOrWhiteSpace(aiOpts.OpenAI.OrgId) ? null : aiOpts.OpenAI.OrgId);
		Log.Information("Using OpenAI: {ModelId}, Embedding: {EmbedModel}",
			aiOpts.OpenAI.ChatModelId, aiOpts.OpenAI.EmbeddingModelId);
	}

	// ── RAG: Vector memory store + embedding services ─────────────────────────
	// VolatileMemoryStore = in-memory vector store (fast, reindexed on each restart).
	// For persistence swap with: Redis, Qdrant, Azure AI Search, etc.
	builder.Services.AddSingleton<Microsoft.SemanticKernel.Memory.IMemoryStore,
		Microsoft.SemanticKernel.Memory.VolatileMemoryStore>();

	builder.Services.AddSingleton<Microsoft.SemanticKernel.Memory.ISemanticTextMemory>(sp =>
	{
		var embeddingService = sp.GetRequiredService<
			Microsoft.SemanticKernel.Embeddings.ITextEmbeddingGenerationService>();
		var store = sp.GetRequiredService<
			Microsoft.SemanticKernel.Memory.IMemoryStore>();
		return new Microsoft.SemanticKernel.Memory.SemanticTextMemory(store, embeddingService);
	});

	builder.Services.AddSingleton<RagIndexService>();
	builder.Services.AddSingleton<RagRetrievalService>();

	// Plugins are NOT registered here via AddFromType<T>() — that would make them
	// singletons inside SK's container, which conflicts with scoped DbContext.
	// Instead, KernelFactory (registered as scoped below) attaches plugins per-request
	// using instances resolved from the current DI scope.

	// ── Application Services ──────────────────────────────────────────────────
	builder.Services.AddScoped<DatabasePlugin>();
	builder.Services.AddScoped<ApiPlugin>();
	builder.Services.AddScoped<PdfPlugin>();
	builder.Services.AddScoped<FilePlugin>();
	builder.Services.AddScoped<WebSearchPlugin>();
	builder.Services.AddScoped<KernelFactory>();
	builder.Services.AddScoped<IKernelFactory>(sp => sp.GetRequiredService<KernelFactory>());
	builder.Services.AddScoped<RouterService>();
	builder.Services.AddScoped<ToolExecutorService>();
	builder.Services.AddScoped<ConversationService>();
	builder.Services.AddScoped<ChatService>();
	builder.Services.AddSingleton<HumanApprovalService>();

	// ── Validation ────────────────────────────────────────────────────────────
	builder.Services.AddValidatorsFromAssemblyContaining<ChatRequestValidator>();

	// ── Rate Limiting ─────────────────────────────────────────────────────────
	builder.Services.AddMemoryCache();
	builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("RateLimit"));
	builder.Services.AddInMemoryRateLimiting();
	builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

	// ── Health Checks ─────────────────────────────────────────────────────────
	// AspNetCore.HealthChecks.EntityFrameworkCore (NOT .EntityFramework) provides AddDbContextCheck<T>
	builder.Services.AddHealthChecks()
		.AddDbContextCheck<ChatAgentDbContext>("database")
		.AddCheck("self", () => HealthCheckResult.Healthy("Running"));

	// ── Swagger / OpenAPI ─────────────────────────────────────────────────────
	builder.Services.AddEndpointsApiExplorer();
	builder.Services.AddSwaggerGen(c =>
	{
		c.SwaggerDoc("v1", new OpenApiInfo
		{
			Title = "AI ChatAgent API",
			Version = "v1",
			Description = "Conversational AI agent — Semantic Kernel + .NET 10",
			Contact = new OpenApiContact { Name = "AI ChatAgent" }
		});
		// EnableAnnotations() requires Swashbuckle.AspNetCore.Annotations package
		c.EnableAnnotations();

		var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
		var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
		if (File.Exists(xmlPath)) c.IncludeXmlComments(xmlPath);
	});

	// ── CORS ──────────────────────────────────────────────────────────────────
	builder.Services.AddCors(opt =>
		opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

	// ── JSON serialization ────────────────────────────────────────────────────
	builder.Services.ConfigureHttpJsonOptions(opt =>
	{
		opt.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
		opt.SerializerOptions.WriteIndented = false;
	});

	// ─────────────────────────────────────────────────────────────────────────
	var app = builder.Build();
	// ─────────────────────────────────────────────────────────────────────────

	// ── Auto-migrate DB ───────────────────────────────────────────────────────
	using (var scope = app.Services.CreateScope())
	{
		var db = scope.ServiceProvider.GetRequiredService<ChatAgentDbContext>();
		var log = scope.ServiceProvider.GetRequiredService<ILogger<ChatAgentDbContext>>();

		await EnsureDatabaseReadyAsync(db, log);
	}

	EnsureSampleDirectories(app.Configuration);

	// ── RAG: Index documents into vector memory on startup ────────────────────
	// Runs in background so it doesn't delay the first request.
	// VolatileMemoryStore is in-memory so this re-runs on every restart.
	_ = Task.Run(async () =>
	{
		try
		{
			using var scope = app.Services.CreateScope();
			var indexService = scope.ServiceProvider.GetRequiredService<RagIndexService>();
			await indexService.IndexAllAsync();
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "RAG startup indexing failed — semantic search will be unavailable");
		}
	});

	// ── Middleware pipeline ───────────────────────────────────────────────────
	app.UseMiddleware<GlobalExceptionMiddleware>();
	app.UseMiddleware<RequestLoggingMiddleware>();
	app.UseMiddleware<ContentTypeValidationMiddleware>();
	app.UseSerilogRequestLogging();
	app.UseCors();
	app.UseIpRateLimiting();

	if (!app.Environment.IsProduction())
	{
		app.UseSwagger();
		app.UseSwaggerUI(c =>
		{
			c.SwaggerEndpoint("/swagger/v1/swagger.json", "AI ChatAgent v1");
			c.RoutePrefix = "swagger";
		});
	}

	// ── Endpoints ─────────────────────────────────────────────────────────────

	// POST /chat/stream — SSE streaming
	app.MapPost(AppConstants.ChatStreamEndpoint, async (
		ChatRequest request,
		IValidator<ChatRequest> validator,
		ChatService chatService,
		HttpContext ctx,
		CancellationToken ct) =>
	{
		var validation = await validator.ValidateAsync(request, ct);
		if (!validation.IsValid)
			return Results.ValidationProblem(validation.ToDictionary());

		ctx.Response.ContentType = AppConstants.Headers.SseContentType;
		ctx.Response.Headers[AppConstants.Headers.CacheControl] = AppConstants.Headers.NoCacheValue;
		ctx.Response.Headers["X-Accel-Buffering"] = "no";

		await foreach (var chunk in chatService.ProcessStreamingAsync(request, ct))
		{
			var json = JsonSerializer.Serialize(chunk);
			await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
			await ctx.Response.Body.FlushAsync(ct);
		}

		return Results.Empty;
	})
	.WithName("StreamChat")
	.WithSummary("Streaming chat via Server-Sent Events")
	.WithOpenApi();

	// POST /chat — non-streaming
	app.MapPost(AppConstants.ChatEndpoint, async (
		ChatRequest request,
		IValidator<ChatRequest> validator,
		ChatService chatService,
		CancellationToken ct) =>
	{
		var validation = await validator.ValidateAsync(request, ct);
		if (!validation.IsValid)
			return Results.ValidationProblem(validation.ToDictionary());

		var response = await chatService.ProcessAsync(request, ct);
		return Results.Ok(response);
	})
	.WithName("Chat")
	.WithSummary("Non-streaming chat")
	.WithOpenApi();

	// GET /chat/{sessionId}/history
	app.MapGet("/chat/{sessionId}/history", async (
		string sessionId,
		ConversationService conversationService,
		int limit = 50,
		CancellationToken ct = default) =>
	{
		var history = await conversationService.GetHistoryAsync(sessionId, limit, ct);
		return Results.Ok(new { sessionId, count = history.Count, messages = history });
	})
	.WithName("GetHistory")
	.WithSummary("Get conversation history")
	.WithOpenApi();

	// GET /approvals
	app.MapGet("/approvals", (HumanApprovalService approvalService) =>
		Results.Ok(approvalService.GetPendingRequests()))
	.WithName("GetApprovals")
	.WithSummary("List pending human-approval requests")
	.WithOpenApi();

	// POST /approvals/{id}/resolve
	app.MapPost("/approvals/{id}/resolve", (
		string id, bool approved,
		string? reviewer, string? notes,
		HumanApprovalService approvalService) =>
	{
		var success = approvalService.Resolve(id, approved, reviewer, notes);
		return success
			? Results.Ok(new { id, approved, reviewer })
			: Results.NotFound(new { error = $"Approval '{id}' not found or already resolved" });
	})
	.WithName("ResolveApproval")
	.WithSummary("Approve or reject a pending action")
	.WithOpenApi();

	// GET /rag/search?query=...  — test semantic search directly
	app.MapGet("/rag/search", async (
		string query,
		RagRetrievalService ragRetrieval,
		int limit = 5,
		CancellationToken ct = default) =>
	{
		var results = await ragRetrieval.SearchAsync(query, limit, ct: ct);
		return Results.Ok(new { query, count = results.Count, results });
	})
	.WithName("RagSearch")
	.WithSummary("Test semantic search over indexed documents")
	.WithOpenApi();

	// POST /rag/reindex — re-index all documents
	app.MapPost("/rag/reindex", async (
		RagIndexService ragIndex,
		CancellationToken ct) =>
	{
		await ragIndex.IndexAllAsync(ct);
		return Results.Ok(new { message = "Re-indexing complete" });
	})
	.WithName("RagReindex")
	.WithSummary("Re-index all PDFs and files into the vector store")
	.WithOpenApi();


	// GET /health
	app.MapHealthChecks(AppConstants.HealthEndpoint, new HealthCheckOptions
	{
		ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
	})
	.WithName("Health")
	.WithOpenApi();

	await app.RunAsync();
}
catch (Exception ex)
{
	Log.Fatal(ex, "Startup failed");
	throw;
}
finally
{
	Log.CloseAndFlush();
}

// ── Helpers ───────────────────────────────────────────────────────────────────
static void EnsureSampleDirectories(IConfiguration config)
{
	Directory.CreateDirectory(config["Storage:PdfDirectory"] ?? "SampleData/PDFs");
	Directory.CreateDirectory(config["Storage:FileDirectory"] ?? "SampleData/Files");
	Directory.CreateDirectory("logs");
}

// ── Database bootstrap helpers ────────────────────────────────────────────────

/// <summary>
/// Robust database startup:
/// 1. Runs MigrateAsync() — applies pending migrations normally.
/// 2. Verifies that real application tables were actually created.
/// 3. If tables are missing (stale __EFMigrationsHistory from a prior failed run),
///    deletes and recreates the database from scratch via EnsureCreated().
///
/// This fixes the silent failure where only __EFMigrationsHistory and
/// __EFMigrationsLock exist but no application tables were created.
/// </summary>
static async Task EnsureDatabaseReadyAsync(
	AI.ChatAgent.Data.ChatAgentDbContext db,
	Microsoft.Extensions.Logging.ILogger logger)
{
	try
	{
		await db.Database.MigrateAsync();
		logger.LogInformation("Database migration complete");
	}
	catch (Exception ex)
	{
		logger.LogWarning(ex, "MigrateAsync failed — attempting EnsureCreated fallback");
	}

	// Verify tables actually exist by trying to query Products.
	// If missing, migration history is stale from a prior failed run — nuke and recreate.
	var tablesExist = await ProductsTableExistsAsync(db, logger);
	if (!tablesExist)
	{
		logger.LogWarning(
			"Application tables missing despite migration history. " +
			"Deleting and recreating the database from scratch...");

		await db.Database.EnsureDeletedAsync();
		await db.Database.EnsureCreatedAsync();

		logger.LogInformation("Database recreated via EnsureCreated — all tables and seed data applied");
	}
	else
	{
		logger.LogInformation("Database tables verified OK");
	}
}

static async Task<bool> ProductsTableExistsAsync(
	AI.ChatAgent.Data.ChatAgentDbContext db,
	Microsoft.Extensions.Logging.ILogger logger)
{
	try
	{
		// Cheapest check — COUNT(*) with no row fetch
		_ = await db.Products.CountAsync();
		return true;
	}
	catch (Exception ex)
	{
		logger.LogDebug(ex, "Products table check failed — tables do not exist yet");
		return false;
	}
}

