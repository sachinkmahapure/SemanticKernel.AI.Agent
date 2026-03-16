# AI ChatAgent — Angular 19 UI

Premium Angular 19 chat interface for the AI ChatAgent .NET backend.  
Uses **signals**, **standalone components**, and **control flow** (`@if`, `@for`) — no NgModules.

---

## Quick Start

### Development (proxies API calls to `localhost:8080`)
```bash
npm install
npm start
# → http://localhost:4200
```

### Production build
```bash
npm run build:prod
# Output: dist/chatagent-angular/browser/
```

### Docker (full stack)
```bash
# From the AI.ChatAgent root:
docker-compose up -d
# UI → http://localhost:3000
# API → http://localhost:8080
# Seq → http://localhost:8081
```

---

## Project Structure

```
src/
├── app/
│   ├── components/
│   │   ├── sidebar/           # Session list, new chat, theme toggle
│   │   ├── chat-window/       # Message list with auto-scroll
│   │   ├── message-bubble/    # Renders one message (user or assistant)
│   │   ├── input-box/         # Auto-resize textarea + send/stop
│   │   ├── empty-state/       # Welcome screen + suggestion chips
│   │   └── tool-badge/        # Animated active/done tool pill
│   ├── services/
│   │   ├── chat-api.service.ts    # HTTP + SSE fetch client
│   │   └── chat-state.service.ts  # Signals-based state management
│   ├── models/
│   │   └── chat.models.ts         # All TypeScript interfaces
│   ├── pipes/
│   │   └── markdown.pipe.ts       # marked + highlight.js renderer
│   ├── app.component.ts           # Root shell (theme host binding)
│   └── app.config.ts              # provideHttpClient, provideRouter
├── environments/
│   ├── environment.ts             # Dev (proxied)
│   └── environment.prod.ts        # Prod (nginx reverse-proxy)
├── styles.scss                    # Full design system (dark + light tokens)
└── index.html
```

---

## Angular 19 Features Used

| Feature | Where |
|---------|-------|
| `signal()` / `computed()` | `ChatStateService` — entire app state |
| `input()` / `output()` | All components — typed, signal-based I/O |
| `@if` / `@for` control flow | All templates — no `*ngIf` / `*ngFor` |
| Standalone components | Every component — no NgModules |
| `ChangeDetectionStrategy.OnPush` | Every component |
| `inject()` function | Services injected without constructor |
| `provideHttpClient(withFetch())` | Uses native Fetch API |
| `effect()` | Auto-scroll trigger in `ChatWindowComponent` |
| Host bindings | Theme class on `app-root` |

---

## API Integration

The UI connects to the .NET 10 backend:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/chat/stream` | `POST` | **SSE streaming** — `tool_start`, `content`, `tool_end`, `done` chunks |
| `/chat` | `POST` | Non-streaming (fallback) |
| `/chat/{id}/history` | `GET` | Load conversation messages |
| `/health` | `GET` | Backend health check |

---

## Design System

- **Dark theme** (default) — `#0e0f11` background, `#e8a84a` amber accent
- **Light theme** — warm parchment `#faf9f7`, `#c17d2a` amber accent
- **Typography** — Instrument Serif (headings), DM Sans (body), JetBrains Mono (code/tools)
- **Markdown** — Full GFM via `marked` + syntax highlighting via `highlight.js`
- **Responsive** — sidebar collapses to overlay on mobile
