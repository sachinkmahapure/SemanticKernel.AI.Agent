// ── API contracts ─────────────────────────────────────────────────────────────

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

// ── RAG models ────────────────────────────────────────────────────────────────

export interface RagSearchResult {
  text: string;
  source: string;
  score: number;
  collection: string;
}

export interface RagSearchResponse {
  query: string;
  count: number;
  results: RagSearchResult[];
}

export interface ReindexResponse {
  message: string;
}

// ── Approval models ───────────────────────────────────────────────────────────

export interface ApprovalRequest {
  id: string;
  actionName: string;
  description: string;
  parameters: Record<string, string>;
  requestedAt: string;
  expiresAt: string;
  status: 'Pending' | 'Approved' | 'Rejected' | 'Expired';
  reviewedBy?: string;
  reviewNotes?: string;
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

export type ActivePanel = 'chat' | 'rag' | 'approvals';
