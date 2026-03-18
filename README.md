# AI ChatAgent — Production-Ready Conversational AI with Semantic Kernel + RAG

[![CI](https://github.com/your-org/ai-chatagent/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/your-org/ai-chatagent/actions)
[![Deploy UI](https://github.com/your-org/ai-chatagent/actions/workflows/deploy-ui.yml/badge.svg)](https://github.com/your-org/ai-chatagent/actions)
[![Deploy API](https://github.com/your-org/ai-chatagent/actions/workflows/deploy-api.yml/badge.svg)](https://github.com/your-org/ai-chatagent/actions)

A **production-ready ChatGPT-like AI agent** built with **.NET 10** and **Microsoft Semantic Kernel 1.30**.  
Features intelligent multi-source routing, **RAG (Retrieval-Augmented Generation)**, streaming responses via SSE,
stateful conversation memory, parallel tool execution, and human-in-the-loop approval workflows.

The **Angular 19** frontend delivers a polished chat UI with dark/light theme, streaming tool badges, and session history.

---

## Architecture

```
User (Angular 19 UI)
        │
        ▼  POST /chat/stream
┌─────────────────────────────────────────────────────────────┐
│                    ASP.NET Core 10 API                      │
│  Middleware: RateLimiting → Logging → ContentType → CORS    │
│  FluentValidation → ChatService                             │
└─────────────┬───────────────────────────────────────────────┘
              │
    ┌─────────┴──────────┐
    ▼                    ▼
ConversationService   RagRetrievalService
(session + history)   (semantic search)
                           │
                    text-embedding-3-small
                    → VolatileMemoryStore
                    → top-5 relevant chunks
                           │
              ┌────────────┘
              ▼
        RouterService
        (LLM decides which plugins to call)
              │
    ┌─────────┴───────────────────────────────┐
    ▼         ▼          ▼         ▼          ▼
DatabasePlugin  ApiPlugin  PdfPlugin  FilePlugin  WebSearchPlugin
 (EF Core)   (Polly HTTP)  (PdfPig)  (CsvHelper)  (Serper/Bing)
    └─────────┬───────────────────────────────┘
              │  Task.WhenAll (parallel)
              ▼
        ToolExecutorService
              │
              ▼
     GPT-4o / Azure OpenAI
     (RAG context + tool results → final answer)
              │
              ▼
     SSE stream → Angular UI
```

---

## ⚡ Quick Start — 5 Minutes

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [OpenAI API key](https://platform.openai.com/api-keys)

### Option A — Docker Compose (full stack)

```bash
# 1. Clone the repo
git clone https://github.com/your-org/ai-chatagent
cd ai-chatagent

# 2. Set secrets
export OPENAI_API_KEY=sk-your-key-here
export SERPER_API_KEY=your-serper-key        # optional — for web search

# 3. Start everything
#    API :8080 | Angular UI :3000 | SQL Server :1433 | Redis :6379 | Seq :8081
docker-compose up -d

# 4. Verify
curl http://localhost:8080/health

# 5. Open the UI
open http://localhost:3000

# 6. Or call the API directly
curl -X POST http://localhost:8080/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "What products do you have under $100?"}'
```

### Option B — Local Development (no Docker)

```bash
# 1. Restore packages
dotnet restore

# 2. Set secrets (never commit keys to source control)
cd src/AI.ChatAgent
dotnet user-secrets set "AI:OpenAI:ApiKey"            "sk-your-key-here"
dotnet user-secrets set "AI:OpenAI:EmbeddingModelId"  "text-embedding-3-small"
dotnet user-secrets set "WebSearch:Serper:ApiKey"     "your-serper-key"

# 3. Run — database is auto-created and seeded on first start
dotnet run

# API → http://localhost:5000
# Swagger → http://localhost:5000/swagger
```

### Option C — Angular UI (local dev)

```bash
cd chatagent-angular
npm install

# Update proxy.conf.json target to match your .NET port:
# "target": "https://localhost:54576"

npm start
# UI → http://localhost:4200
```

---

## RAG — Retrieval-Augmented Generation

The agent automatically indexes all documents in `SampleData/` using **text-embedding-3-small**
and retrieves the most relevant chunks before every LLM call.

### How it works

```
1. User asks: "What is our return policy?"
        │
        ▼
2. RagRetrievalService embeds the question
   → cosine similarity search across "files", "pdfs", "web" collections
   → returns top-5 chunks with score ≥ 0.70
        │
        ▼
3. Relevant chunks injected as SystemMessage into ChatHistory:
   [1] Source: product_kb.txt | 94% relevance
       "All products can be returned within 30 days..."
        │
        ▼
4. GPT-4o answers grounded in retrieved context — no hallucination
```

### Add your own documents

| Drop files here | Collection | Supported formats |
|---|---|---|
| `SampleData/PDFs/` | `pdfs` | `.pdf` |
| `SampleData/Files/` | `files` | `.txt` `.md` `.csv` `.json` |

Documents are indexed automatically on startup. To index at runtime without restarting:

```bash
POST http://localhost:8080/rag/reindex
```

Test what the vector store knows:

```bash
GET http://localhost:8080/rag/search?query=return+policy&limit=5
```

### Swap the vector store (production)

`VolatileMemoryStore` re-indexes on every restart. For persistence:

```csharp
// Redis (recommended for production)
services.AddSingleton<IMemoryStore>(
    new RedisMemoryStore("localhost:6379", vectorSize: 1536));

// Qdrant
services.AddSingleton<IMemoryStore>(
    new QdrantMemoryStore("http://localhost:6333", vectorSize: 1536));

// Azure AI Search
services.AddSingleton<IMemoryStore>(
    new AzureAISearchMemoryStore(endpoint, apiKey));
```

---

## API Reference

### POST `/chat` — Non-streaming

```json
// Request
{
  "message": "Show me electronics under $200",
  "sessionId": "",              // empty = new session; reuse to continue conversation
  "systemPrompt": null,         // optional system prompt override
  "preferredPlugins": [],       // hint router: ["WebSearchPlugin"]
  "structuredOutput": false
}

// Response
{
  "sessionId": "abc123def456",
  "message": "Here are our electronics products under $200...",
  "toolsUsed": [
    { "pluginName": "DatabasePlugin", "functionName": "SearchProducts",
      "success": true, "durationMs": 38 }
  ],
  "totalTokens": 412,
  "latencyMs": 1340
}
```

### POST `/chat/stream` — Streaming SSE

```bash
curl -X POST http://localhost:8080/chat/stream \
  -H "Content-Type: application/json" \
  -d '{"message": "Summarise our business performance"}' \
  --no-buffer

# SSE chunks:
# data: {"type":"tool_start","tool":"DatabasePlugin.GetBusinessStats"}
# data: {"type":"tool_end","tool":"DatabasePlugin.GetBusinessStats"}
# data: {"type":"content","content":"📚 Found 2 relevant document(s)...\n"}
# data: {"type":"content","content":"Our total revenue is "}
# data: {"type":"content","content":"$142,500 this month..."}
# data: {"type":"done","sessionId":"abc123"}
```

### GET `/chat/{sessionId}/history`

```bash
curl http://localhost:8080/chat/abc123def456/history?limit=20
```

### GET `/rag/search` — Test semantic search

```bash
curl "http://localhost:8080/rag/search?query=warranty+policy&limit=5"
```

### POST `/rag/reindex` — Re-index documents

```bash
curl -X POST http://localhost:8080/rag/reindex
```

### GET `/approvals` + POST `/approvals/{id}/resolve` — Human-in-the-loop

```bash
# List pending approvals
curl http://localhost:8080/approvals

# Approve an action
curl -X POST "http://localhost:8080/approvals/{id}/resolve?approved=true&reviewer=admin"

# Reject an action
curl -X POST "http://localhost:8080/approvals/{id}/resolve?approved=false&reviewer=admin"
```

---

## Configuration

### User Secrets (recommended for local dev)

```bash
cd src/AI.ChatAgent

dotnet user-secrets set "AI:OpenAI:ApiKey"                      "sk-..."
dotnet user-secrets set "AI:OpenAI:ChatModelId"                 "gpt-4o"
dotnet user-secrets set "AI:OpenAI:EmbeddingModelId"            "text-embedding-3-small"

# Azure OpenAI (alternative provider)
dotnet user-secrets set "AI:AzureOpenAI:ApiKey"                 "..."
dotnet user-secrets set "AI:AzureOpenAI:Endpoint"               "https://your-resource.openai.azure.com/"
dotnet user-secrets set "AI:AzureOpenAI:ChatDeployment"         "gpt-4o"
dotnet user-secrets set "AI:AzureOpenAI:EmbeddingDeployment"    "text-embedding-3-small"

# Web search
dotnet user-secrets set "WebSearch:Serper:ApiKey"               "..."
dotnet user-secrets set "WebSearch:Bing:ApiKey"                 "..."

# External APIs
dotnet user-secrets set "ExternalApis:ApiRegistry:Weather:ApiKey" "..."
```

### Key `appsettings.json` settings

| Section | Key | Default | Description |
|---|---|---|---|
| `AI` | `Provider` | `OpenAI` | `OpenAI` or `AzureOpenAI` |
| `AI.OpenAI` | `ChatModelId` | `gpt-4o` | Chat completion model |
| `AI.OpenAI` | `EmbeddingModelId` | `text-embedding-3-small` | Embedding model for RAG |
| `WebSearch` | `Provider` | `Serper` | `Serper` or `Bing` |
| `ConnectionStrings` | `DefaultConnection` | SQLite path | Dev database |
| `ConnectionStrings` | `SqlServer` | — | Production SQL Server |
| `HumanApproval` | `RequiredForActions` | `["DeleteData","SendEmail"]` | Actions that need approval |
| `Storage` | `PdfDirectory` | `SampleData/PDFs` | PDF files for RAG indexing |
| `Storage` | `FileDirectory` | `SampleData/Files` | Text/CSV/JSON for RAG indexing |

### Angular UI — set the API URL

**Local dev** — edit `chatagent-angular/proxy.conf.json`:
```json
{ "/chat": { "target": "https://localhost:54576", "secure": false } }
```

**Production** — edit `chatagent-angular/src/environments/environment.prod.ts`:
```typescript
export const environment = { production: true, apiBaseUrl: 'https://your-api.railway.app' };
```

---

## Project Structure

```
AI.ChatAgent/
├── src/AI.ChatAgent/
│   ├── Plugins/
│   │   ├── DatabasePlugin.cs       # EF Core — products, customers, orders
│   │   ├── ApiPlugin.cs            # Generic REST + OpenWeatherMap
│   │   ├── PdfPlugin.cs            # PdfPig text extraction
│   │   ├── FilePlugin.cs           # CsvHelper — CSV/JSON/TXT
│   │   └── WebSearchPlugin.cs      # Serper / Bing Search
│   ├── Services/
│   │   ├── ChatService.cs          # Main orchestrator (RAG + plugins + LLM)
│   │   ├── RouterService.cs        # LLM-powered plugin router
│   │   ├── ToolExecutorService.cs  # Parallel plugin execution
│   │   ├── ConversationService.cs  # Session & chat history
│   │   ├── HumanApprovalService.cs # HITL approval workflow
│   │   ├── KernelFactory.cs        # Per-request Kernel with scoped plugins
│   │   ├── RagIndexService.cs      # Chunks + embeds documents at startup
│   │   ├── RagRetrievalService.cs  # Semantic search over vector store
│   │   └── TextChunker.cs          # Splits docs into overlapping chunks
│   ├── Data/
│   │   └── ChatAgentDbContext.cs   # EF Core DbContext + seed data
│   ├── Models/
│   │   ├── Models.cs               # DTOs, entities, AppConstants
│   │   └── RagModels.cs            # RagDocument, RagChunk, RagSearchResult
│   ├── Configuration/
│   │   └── Options.cs              # Strongly-typed config classes
│   ├── Middleware/
│   │   └── Middleware.cs           # Exception, logging, content-type
│   ├── Validation/
│   │   └── ChatRequestValidator.cs # FluentValidation rules
│   ├── Migrations/                 # EF Core migrations (auto-applied)
│   ├── SampleData/
│   │   ├── Files/                  # Drop .txt .csv .json here → auto-indexed
│   │   └── PDFs/                   # Drop .pdf files here → auto-indexed
│   ├── Program.cs                  # Minimal API + full DI composition root
│   ├── appsettings.json
│   └── appsettings.Development.json
├── chatagent-angular/              # Angular 19 chat UI
│   ├── src/app/
│   │   ├── components/             # sidebar, chat-window, message-bubble, etc.
│   │   ├── services/               # chat-api.service, chat-state.service (signals)
│   │   ├── models/                 # TypeScript interfaces
│   │   └── pipes/                  # markdown.pipe
│   ├── proxy.conf.json             # Dev proxy → .NET API
│   └── nginx.conf                  # Prod: SSE buffering disabled
├── tests/AI.ChatAgent.Tests/
│   ├── Unit/UnitTests.cs           # 28 unit tests — xUnit + Moq + FluentAssertions
│   └── Integration/IntegrationTests.cs  # WebApplicationFactory integration tests
├── .github/workflows/
│   ├── ci-cd.yml                   # Build + test on PR / develop branch
│   ├── deploy-ui.yml               # Deploy Angular → GitHub Pages
│   └── deploy-api.yml              # Build Docker → deploy to Railway / Render
├── Dockerfile                      # Multi-stage .NET build → aspnet:10 runtime
├── docker-compose.yml              # API + Angular UI + SQL Server + Redis + Seq
├── railway.json                    # Railway deployment config
├── render.yaml                     # Render.com deployment config
├── DEPLOYMENT.md                   # Step-by-step GitHub + Railway/Render guide
└── AI.ChatAgent.postman_collection.json
```

---

## Running Tests

```bash
# Unit tests (fast — no network, no DB, no AI keys needed)
dotnet test tests/AI.ChatAgent.Tests/AI.ChatAgent.Tests.csproj \
  --filter "Category!=Integration" \
  --logger "console;verbosity=normal"

# All tests including integration
dotnet test

# With coverage report
dotnet test --collect:"XPlat Code Coverage"
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report -reporttypes:Html
```

---

## Adding New Plugins

**1.** Create `src/AI.ChatAgent/Plugins/MyPlugin.cs`:

```csharp
using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace AI.ChatAgent.Plugins;

public sealed class MyPlugin(ILogger<MyPlugin> logger)
{
    [KernelFunction(nameof(DoSomething))]
    [Description("What this function does — the router LLM reads this description.")]
    public async Task<string> DoSomething(
        [Description("The input parameter")] string input,
        CancellationToken ct = default)
    {
        logger.LogInformation("MyPlugin:DoSomething input={Input}", input);
        var result = new { input, processed = true };
        return System.Text.Json.JsonSerializer.Serialize(result);
    }
}
```

**2.** Register in `Program.cs` (two lines):

```csharp
builder.Services.AddScoped<MyPlugin>();
```

**3.** Add to `KernelFactory.cs`:

```csharp
public sealed class KernelFactory(
    Kernel rootKernel,
    DatabasePlugin databasePlugin,
    // ... existing plugins ...
    MyPlugin myPlugin) : IKernelFactory
{
    public virtual Kernel CreateForRequest()
    {
        var kernel = rootKernel.Clone();
        // ... existing AddFromObject calls ...
        kernel.Plugins.AddFromObject(myPlugin, "MyPlugin");
        return kernel;
    }
}
```

**4.** The router automatically discovers it — no other changes needed.

---

## Adding Documents to RAG

| What to do | Where |
|---|---|
| Add a text knowledge base | Drop `.txt` or `.md` into `SampleData/Files/` |
| Add product specs / manuals | Drop `.pdf` into `SampleData/PDFs/` |
| Add structured data | Drop `.csv` or `.json` into `SampleData/Files/` |
| Index at runtime | `POST /rag/reindex` |
| Test retrieval | `GET /rag/search?query=your+question` |

---

## Deployment — GitHub Pages + Railway

See **[DEPLOYMENT.md](DEPLOYMENT.md)** for the full step-by-step guide.

**Summary (6 steps, ~10 minutes, $0/month):**

1. Push to GitHub
2. Deploy API to [Railway](https://railway.app) (auto-detects `Dockerfile`) — set env vars
3. Add `API_URL` + `RAILWAY_TOKEN` to GitHub Secrets
4. Enable GitHub Pages (Settings → Pages → Source: GitHub Actions)
5. Add CORS origin for your Pages URL in `Program.cs`
6. Push to `main` — both workflows fire automatically

```
Angular UI  → https://your-username.github.io/your-repo/
API         → https://your-app.railway.app
Swagger     → https://your-app.railway.app/swagger
```

---

## Production Checklist

- [ ] Set `AI:OpenAI:ApiKey` and `AI:OpenAI:EmbeddingModelId` in environment variables
- [ ] Set `WebSearch:Serper:ApiKey` for web search
- [ ] Switch `ConnectionStrings:DefaultConnection` to SQL Server
- [ ] Replace `VolatileMemoryStore` with Redis/Qdrant for persistent RAG
- [ ] Enable HTTPS / TLS at reverse proxy (nginx, Cloudflare, Railway)
- [ ] Configure Redis for distributed cache
- [ ] Set up Seq or Application Insights for log aggregation
- [ ] Review `HumanApproval:RequiredForActions` list
- [ ] Add custom documents to `SampleData/` for RAG
- [ ] Enable authentication (Azure AD / API keys) for production endpoints
- [ ] Set up database backups (SQL Server scheduled backup or Railway volume snapshot)

---

## Tech Stack

| Layer | Technology | Version |
|---|---|---|
| Framework | ASP.NET Core Minimal API | .NET 10 |
| AI Orchestration | Microsoft Semantic Kernel | 1.30.0 |
| Chat Model | OpenAI GPT-4o / Azure OpenAI | — |
| Embedding Model | text-embedding-3-small | 1536 dims |
| Vector Store | VolatileMemoryStore (→ Redis/Qdrant) | — |
| Database | EF Core + SQLite / SQL Server | 9.0.4 |
| Caching | Redis / In-Memory | — |
| HTTP Resilience | Polly v8 (retry + circuit breaker) | 8.4.2 |
| PDF Processing | PdfPig | 0.1.8 |
| CSV Processing | CsvHelper | 33.0.1 |
| Frontend | Angular + TypeScript | 19.0 |
| Logging | Serilog → Console / File / Seq | — |
| Testing | xUnit + Moq + FluentAssertions | — |
| Documentation | Swagger / OpenAPI | — |
| Containerisation | Docker multi-stage + Compose | — |
| CI/CD | GitHub Actions | — |
| Hosting | GitHub Pages (UI) + Railway (API) | — |

---

## License

MIT — see [LICENSE](LICENSE)
