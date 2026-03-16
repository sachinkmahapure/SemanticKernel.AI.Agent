# Deployment Guide — GitHub Pages + Railway/Render

Deploy the Angular UI to **GitHub Pages** (free, permanent URL) and the .NET API to
**Railway** or **Render** (both have generous free tiers with GitHub integration).

---

## Architecture after deployment

```
User browser
    │
    ▼
https://<your-username>.github.io/<repo-name>/   ← Angular UI (GitHub Pages)
    │
    │  API calls  (CORS-enabled)
    ▼
https://<your-app>.railway.app                   ← .NET 10 API (Railway free tier)
    │
    ├── SQLite database (persisted volume)
    └── OpenAI / Azure OpenAI
```

---

## Step 1 — Push your code to GitHub

```bash
# Create a new repo on github.com, then:
git init
git remote add origin https://github.com/<YOUR-USERNAME>/<YOUR-REPO>.git
git add .
git commit -m "chore: initial commit"
git push -u origin main
```

---

## Step 2 — Deploy the .NET API

### Option A — Railway (recommended, easiest)

1. Go to [railway.app](https://railway.app) → **New Project** → **Deploy from GitHub repo**
2. Select your repository
3. Railway auto-detects the `Dockerfile` at the root
4. In the Railway dashboard, set these **Environment Variables**:

   | Variable | Value |
   |---|---|
   | `ASPNETCORE_ENVIRONMENT` | `Production` |
   | `ASPNETCORE_URLS` | `http://+:8080` |
   | `AI__Provider` | `OpenAI` |
   | `AI__OpenAI__ApiKey` | `sk-your-key-here` |
   | `AI__OpenAI__ChatModelId` | `gpt-4o-mini` |
   | `WebSearch__Provider` | `Serper` |
   | `WebSearch__Serper__ApiKey` | `your-serper-key` |
   | `ConnectionStrings__DefaultConnection` | `Data Source=/data/chatagent.db` |

5. Add a **Volume**: mount path `/data` (persists the SQLite database)
6. Copy the generated URL: `https://your-app.railway.app`
7. Go to **Settings → Tokens** → create a token → copy it (needed for Step 3)

### Option B — Render (free tier, no credit card)

1. Go to [render.com](https://render.com) → **New** → **Web Service** → **Connect GitHub**
2. Select your repository
3. Render detects `render.yaml` automatically and pre-fills settings
4. Set the secret env vars in the Render dashboard:
   - `AI__OpenAI__ApiKey` = `sk-your-key-here`
   - `WebSearch__Serper__ApiKey` = `your-serper-key`
5. Click **Deploy** — Render builds from `Dockerfile`
6. Copy your deploy hook URL (Settings → Deploy Hook) — needed for Step 3
7. Your URL: `https://ai-chatagent-api.onrender.com`

> ⚠️ **Render free tier note**: the service sleeps after 15 min inactivity.  
> First request after sleep takes ~30s to wake up. Upgrade to the $7/mo Starter plan to avoid this.

---

## Step 3 — Add GitHub Secrets

Go to your repo → **Settings** → **Secrets and variables** → **Actions** → **New repository secret**

| Secret Name | Value | Required for |
|---|---|---|
| `API_URL` | `https://your-app.railway.app` | Angular build — injects the API URL |
| `RAILWAY_TOKEN` | Your Railway token from Step 2A | Railway auto-deploy |
| `RENDER_DEPLOY_HOOK` | Your Render deploy hook URL | Render auto-deploy (if using Render) |

---

## Step 4 — Enable GitHub Pages

1. Go to your repo → **Settings** → **Pages**
2. Under **Source**, select **GitHub Actions**
3. Save

That's it. The `deploy-ui.yml` workflow handles everything else.

---

## Step 5 — Enable CORS on the API

The Angular UI on `*.github.io` needs the .NET API to accept cross-origin requests.

Add this to `Program.cs` in the CORS section (replace the existing `AddCors`):

```csharp
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(policy =>
        policy
            .WithOrigins(
                "http://localhost:4200",                                    // local dev
                $"https://{Environment.GetEnvironmentVariable("GITHUB_ACTOR")}.github.io" // Pages
            )
            .AllowAnyMethod()
            .AllowAnyHeader()));
```

Or more broadly (fine for non-sensitive public APIs):

```csharp
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
```

Set the `CORS_ORIGIN` environment variable in Railway/Render to your GitHub Pages URL:
```
CORS_ORIGIN = https://<your-username>.github.io
```

---

## Step 6 — Trigger deployment

Push to `main` — both workflows run automatically:

```bash
git add .
git commit -m "feat: production deployment"
git push origin main
```

Watch the progress in **Actions** tab. After ~3 minutes:
- **Angular UI**: `https://<username>.github.io/<repo-name>/`
- **API health**: `https://your-app.railway.app/health`
- **API Swagger**: `https://your-app.railway.app/swagger`

---

## Workflow overview

```
Push to main
    │
    ├── deploy-ui.yml
    │   ├── npm ci
    │   ├── Patch environment.prod.ts with API_URL secret
    │   ├── ng build --base-href /<repo-name>/
    │   └── Deploy dist/ → GitHub Pages
    │
    └── deploy-api.yml
        ├── dotnet test (unit tests must pass)
        ├── docker build + push → ghcr.io/<org>/<repo>/ai-chatagent-api:latest
        └── railway up  (or curl Render deploy hook)
```

---

## Custom domain (optional)

### For GitHub Pages
1. Repo → Settings → Pages → Custom domain → enter `chat.yourdomain.com`
2. Add a CNAME record: `chat.yourdomain.com → <username>.github.io`

### For Railway/Render
Both support custom domains in their dashboards under Settings → Domains.

---

## Environment Variables Reference

All config is passed via environment variables — no secrets in source code.

| Variable | Description | Example |
|---|---|---|
| `AI__Provider` | `OpenAI` or `AzureOpenAI` | `OpenAI` |
| `AI__OpenAI__ApiKey` | OpenAI secret key | `sk-...` |
| `AI__OpenAI__ChatModelId` | Model to use | `gpt-4o-mini` |
| `AI__AzureOpenAI__ApiKey` | Azure key (if using Azure) | `abc123...` |
| `AI__AzureOpenAI__Endpoint` | Azure endpoint | `https://...openai.azure.com/` |
| `AI__AzureOpenAI__ChatDeployment` | Azure deployment name | `gpt-4o` |
| `WebSearch__Provider` | `Serper` or `Bing` | `Serper` |
| `WebSearch__Serper__ApiKey` | Serper API key | `abc...` |
| `WebSearch__Bing__ApiKey` | Bing Search key | `abc...` |
| `ConnectionStrings__DefaultConnection` | SQLite path | `Data Source=/data/chatagent.db` |
| `ASPNETCORE_URLS` | Bind address | `http://+:8080` |

---

## Cost summary

| Service | Free tier | Limits |
|---|---|---|
| GitHub Pages | ✅ Free forever | 1GB storage, 100GB/month bandwidth |
| GitHub Actions | ✅ 2,000 min/month free | Resets monthly |
| Railway | ✅ $5 credit/month | ~500hrs of a 512MB container |
| Render | ✅ Free web service | Sleeps after 15min idle |
| GHCR (container registry) | ✅ Free for public repos | Unlimited public images |

**Estimated monthly cost for a hobby project: $0.**

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| Angular 404 on refresh | GitHub Pages only serves index.html at `/` — add `404.html` that redirects to `index.html` (see below) |
| API calls fail (CORS) | Add `CORS_ORIGIN=https://<username>.github.io` to Railway env vars |
| Railway container exits | Check logs — likely missing `AI__OpenAI__ApiKey` env var |
| Render cold start timeout | Upgrade to Starter plan or use Railway instead |
| GitHub Pages URL is wrong | Check `--base-href` matches your repo name exactly |

### Fix Angular 404 on deep links (GitHub Pages)

Add this to `deploy-ui.yml` after the build step:

```yaml
- name: Create 404 redirect for SPA
  working-directory: chatagent-angular/dist/chatagent-angular/browser
  run: cp index.html 404.html
```

GitHub Pages serves `404.html` for unknown paths — Angular's router then takes over client-side.
