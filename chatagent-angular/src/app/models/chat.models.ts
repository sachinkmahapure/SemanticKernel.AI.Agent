// ── API contracts (mirror .NET Models) ───────────────────────────────────────

export interface ChatRequest {
  message: string;
  sessionId: string;
  systemPrompt?: string;
  preferredPlugins?: string[];
  structuredOutput?: boolean;
}

export interface ToolExecutionResult {
  pluginName: string;
  functionName: string;
  success: boolean;
  output?: string;
  error?: string;
  durationMs: number;
}

export interface ChatResponse {
  sessionId: string;
  message: string;
  toolsUsed: ToolExecutionResult[];
  totalTokens: number;
  latencyMs: number;
  timestamp: string;
}

export interface StreamChunk {
  type: 'content' | 'tool_start' | 'tool_end' | 'done' | 'error';
  content?: string;
  tool?: string;
  sessionId?: string;
  tokens?: number;
}

export interface HistoryResponse {
  sessionId: string;
  count: number;
  messages: ApiMessage[];
}

export interface ApiMessage {
  id: number;
  sessionId: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  createdAt: string;
  tokenCount?: number;
}

// ── UI models ─────────────────────────────────────────────────────────────────

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  timestamp: Date;
  toolsUsed?: ToolExecutionResult[];
  activeTools?: string[];
  isStreaming?: boolean;
  tokens?: number;
  error?: boolean;
}

export interface ChatSession {
  id: string;
  title: string;
  lastMessage: string;
  lastActivityAt: Date;
  messageCount: number;
}

export interface AppTheme {
  mode: 'dark' | 'light';
}
