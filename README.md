# AI ChatAgent — Production-Ready Conversational AI with Semantic Kernel

[![CI/CD](https://github.com/your-org/ai-chatagent/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/your-org/ai-chatagent/actions)

A **production-ready ChatGPT-like AI agent** built with .NET 9 and Microsoft Semantic Kernel. Features intelligent multi-source routing, streaming responses, stateful conversation memory, and human-in-the-loop approval workflows.

---

## Architecture

```
User → POST /chat/stream
         │
         ▼
  ConversationService  ─── Loads chat history from SQLite/SQL Server
         │
         ▼
   RouterService       ─── LLM decides which plugins to invoke
         │
    ┌────┴────────────────────────────────────┐
    ▼         ▼          ▼         ▼          ▼
DatabasePlugin  ApiPlugin  PdfPlugin  FilePlugin  WebSearchPlugin
(EF Core)    (HttpClient)  (PdfPig)  (CSV/JSON)   (Serper/Bing)
    └────┬────────────────────────────────────┘
         │  (parallel execution by ToolExecutorService)
         ▼
  ChatService (synthesises final response)
         │
         ▼
  SSE stream → Client
```

---

## ⚡ 5-Minute Deployment Guide

### Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- An [OpenAI API key](https://platform.openai.com/api-keys)

### Option A — Docker Compose (Recommended)

```bash
# 1. Clone
git clone https://github.com/your-org/ai-chatagent
cd ai-chatagent

# 2. Set your API key
export OPENAI_API_KEY=sk-your-key-here
export SERPER_API_KEY=your-serper-key   # optional, for web search

# 3. Start everything (app + SQL Server + Redis + Seq)
docker-compose up -d

# 4. Check health
curl http://localhost:8080/health

# 5. Open Swagger UI
open http://localhost:8080/swagger

# 6. Send a chat message
curl -X POST http://localhost:8080/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "What products do you have under $100?"}'
```

### Option B — Local Development

```bash
# 1. Restore packages
dotnet restore

# 2. Configure User Secrets (keeps keys out of source control)
cd src/AI.ChatAgent
dotnet user-secrets set "AI:OpenAI:ApiKey" "sk-your-key-here"
dotnet user-secrets set "WebSearch:Serper:ApiKey" "your-serper-key"

# 3. Apply database migrations
dotnet ef database update

# 4. Run
dotnet run

# Open: http://localhost:5000/swagger
```

### Option C — Test with SQLite (Zero Config)

```bash
dotnet restore
dotnet run --project src/AI.ChatAgent
# App auto-creates chatagent.db with seed data
```

---

## API Reference

### POST /chat — Non-Streaming Chat

```json
// Request
{
  "message": "Show me all electronics products",
  "sessionId": "",           // empty = new session
  "systemPrompt": null,      // optional override
  "preferredPlugins": [],    // hint: ["WebSearchPlugin"]
  "structuredOutput": false
}

// Response
{
  "sessionId": "abc123",
  "message": "Here are our electronics products...",
  "toolsUsed": [
    {
      "pluginName": "DatabasePlugin",
      "functionName": "SearchProducts",
      "success": true,
      "durationMs": 45
    }
  ],
  "totalTokens": 312,
  "latencyMs": 1240
}
```

### POST /chat/stream — Streaming SSE

```bash
curl -X POST http://localhost:8080/chat/stream \
  -H "Content-Type: application/json" \
  -d '{"message": "What is our total revenue?"}' \
  --no-buffer

# Response chunks:
# data: {"type":"tool_start","tool":"DatabasePlugin.GetBusinessStats"}
# data: {"type":"tool_end","tool":"DatabasePlugin.GetBusinessStats"}
# data: {"type":"content","content":"Our total delivered revenue is "}
# data: {"type":"content","content":"$1,349.98..."}
# data: {"type":"done","sessionId":"abc123"}
```

### GET /chat/{sessionId}/history

```bash
curl http://localhost:8080/chat/abc123/history?limit=20
```

### Human Approval Workflow

```bash
# 1. List pending approvals
curl http://localhost:8080/approvals

# 2. Approve
curl -X POST "http://localhost:8080/approvals/{id}/resolve?approved=true&reviewer=admin"

# 3. Reject
curl -X POST "http://localhost:8080/approvals/{id}/resolve?approved=false&reviewer=admin"
```

---

## Configuration Reference

### appsettings.json key sections

| Section | Key | Description |
|---------|-----|-------------|
| `AI.Provider` | `"OpenAI"` or `"AzureOpenAI"` | Which AI provider to use |
| `AI.OpenAI.ApiKey` | `sk-...` | OpenAI API key |
| `AI.OpenAI.ChatModelId` | `gpt-4o` | Model to use |
| `WebSearch.Provider` | `"Serper"` or `"Bing"` | Web search provider |
| `WebSearch.Serper.ApiKey` | `...` | Serper API key |
| `ConnectionStrings.DefaultConnection` | SQLite path | Dev database |
| `ConnectionStrings.SqlServer` | SQL Server connstr | Prod database |
| `HumanApproval.RequiredForActions` | `["DeleteData"]` | Actions needing approval |

### User Secrets (Development)

```bash
dotnet user-secrets set "AI:OpenAI:ApiKey"         "sk-..."
dotnet user-secrets set "AI:AzureOpenAI:ApiKey"    "..."
dotnet user-secrets set "AI:AzureOpenAI:Endpoint"  "https://..."
dotnet user-secrets set "WebSearch:Serper:ApiKey"  "..."
dotnet user-secrets set "WebSearch:Bing:ApiKey"    "..."
dotnet user-secrets set "ExternalApis:ApiRegistry:Weather:ApiKey" "..."
```

---

## Project Structure

```
AI.ChatAgent/
├── src/AI.ChatAgent/
│   ├── Plugins/
│   │   ├── DatabasePlugin.cs     # EF Core queries (products/orders/customers)
│   │   ├── ApiPlugin.cs          # Generic REST API caller with Polly
│   │   ├── PdfPlugin.cs          # PdfPig text extraction & search
│   │   ├── FilePlugin.cs         # CSV/JSON/TXT processing with CsvHelper
│   │   └── WebSearchPlugin.cs    # Serper + Bing Search APIs
│   ├── Services/
│   │   ├── ChatService.cs        # Main orchestrator (stream + non-stream)
│   │   ├── RouterService.cs      # LLM-powered plugin router
│   │   ├── ToolExecutorService.cs# Parallel plugin execution
│   │   ├── ConversationService.cs# Chat history & session management
│   │   └── HumanApprovalService.cs # HITL approval workflow
│   ├── Data/
│   │   └── ChatAgentDbContext.cs # EF Core DbContext + seed data
│   ├── Models/
│   │   └── Models.cs             # All DTOs, entities, constants
│   ├── Configuration/
│   │   └── Options.cs            # Strongly-typed config classes
│   ├── Middleware/
│   │   └── Middleware.cs         # Logging, error handling, content-type
│   ├── Migrations/               # EF Core migrations
│   ├── SampleData/
│   │   ├── Files/                # CSV, JSON, TXT sample files
│   │   └── PDFs/                 # Drop PDFs here for PdfPlugin
│   ├── Program.cs                # Minimal API + DI composition root
│   ├── appsettings.json
│   └── appsettings.Development.json
├── tests/AI.ChatAgent.Tests/
│   ├── Unit/UnitTests.cs         # xUnit + Moq + FluentAssertions
│   └── Integration/IntegrationTests.cs
├── Dockerfile                    # Multi-stage build
├── docker-compose.yml            # App + SQL Server + Redis + Seq
├── AI.ChatAgent.postman_collection.json
└── README.md
```

---

## Running Tests

```bash
# All tests
dotnet test

# Unit tests only (fast, no external deps)
dotnet test --filter "Category!=Integration"

# With coverage
dotnet test --collect:"XPlat Code Coverage"

# Generate HTML coverage report
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report -reporttypes:Html
open coverage-report/index.html
```

---

## Adding New Plugins

1. Create `src/AI.ChatAgent/Plugins/MyPlugin.cs`:

```csharp
public sealed class MyPlugin(ILogger<MyPlugin> logger)
{
    [KernelFunction(nameof(DoSomething))]
    [Description("Describe what this function does for the AI router.")]
    public async Task<string> DoSomething(
        [Description("Parameter description")] string param,
        CancellationToken ct = default)
    {
        // Implementation
        return JsonSerializer.Serialize(result);
    }
}
```

2. Register in `Program.cs`:
```csharp
builder.Services.AddScoped<MyPlugin>();
kernelBuilder.Plugins.AddFromType<MyPlugin>("MyPlugin");
```

3. The router will automatically discover and use it.

---

## Production Checklist

- [ ] Set all API keys in environment variables / Azure Key Vault
- [ ] Switch `ConnectionStrings:DefaultConnection` to SQL Server
- [ ] Enable HTTPS / TLS termination (reverse proxy)
- [ ] Configure Redis for distributed caching
- [ ] Set up Seq or Application Insights for log aggregation
- [ ] Review `HumanApproval:RequiredForActions` list
- [ ] Configure rate limiting values for your traffic
- [ ] Set up database backups
- [ ] Enable Azure AD / JWT auth for production endpoints

---

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Framework | ASP.NET Core 9 Minimal API |
| AI Orchestration | Microsoft Semantic Kernel 1.21 |
| AI Provider | OpenAI GPT-4o / Azure OpenAI |
| Database | EF Core 9 + SQLite / SQL Server |
| Caching | Redis / In-Memory |
| HTTP Resilience | Polly (retry + circuit breaker) |
| PDF Processing | PdfPig |
| CSV Processing | CsvHelper |
| Logging | Serilog → Console / File / Seq |
| Testing | xUnit + Moq + FluentAssertions |
| Documentation | Swagger / OpenAPI |
| Containerisation | Docker multi-stage + Compose |
| CI/CD | GitHub Actions |

---

## License

MIT License — see [LICENSE](LICENSE)
