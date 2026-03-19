import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, from } from 'rxjs';
import {
  ChatRequest, ChatResponse, HistoryResponse, StreamChunk,
  RagSearchResponse, ReindexResponse, ApprovalRequest
} from '../models/chat.models';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class ChatApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiBaseUrl;

  // ── Chat ───────────────────────────────────────────────────────────────────

  sendMessage(req: ChatRequest): Observable<ChatResponse> {
    return this.http.post<ChatResponse>(`${this.baseUrl}/chat`, req);
  }

  getHistory(sessionId: string, limit = 100): Observable<HistoryResponse> {
    return this.http.get<HistoryResponse>(
      `${this.baseUrl}/chat/${sessionId}/history?limit=${limit}`
    );
  }

  checkHealth(): Observable<unknown> {
    return this.http.get(`${this.baseUrl}/health`);
  }

  // ── RAG ────────────────────────────────────────────────────────────────────

  ragSearch(query: string, limit = 5): Observable<RagSearchResponse> {
    const params = `query=${encodeURIComponent(query)}&limit=${limit}`;
    return this.http.get<RagSearchResponse>(`${this.baseUrl}/rag/search?${params}`);
  }

  ragReindex(): Observable<ReindexResponse> {
    return this.http.post<ReindexResponse>(`${this.baseUrl}/rag/reindex`, {});
  }

  // ── Approvals ──────────────────────────────────────────────────────────────

  getApprovals(): Observable<ApprovalRequest[]> {
    return this.http.get<ApprovalRequest[]>(`${this.baseUrl}/approvals`);
  }

  resolveApproval(
    id: string, approved: boolean,
    reviewer = 'UI User', notes = ''
  ): Observable<unknown> {
    const params = `approved=${approved}&reviewer=${encodeURIComponent(reviewer)}&notes=${encodeURIComponent(notes)}`;
    return this.http.post(`${this.baseUrl}/approvals/${id}/resolve?${params}`, {});
  }

  // ── SSE Streaming ──────────────────────────────────────────────────────────

  streamMessage(req: ChatRequest, signal?: AbortSignal): Observable<StreamChunk> {
    return from(this.streamGenerator(req, signal));
  }

  private async *streamGenerator(
    req: ChatRequest,
    signal?: AbortSignal
  ): AsyncGenerator<StreamChunk> {
    const response = await fetch(`${this.baseUrl}/chat/stream`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
      body: JSON.stringify(req),
      signal,
    });

    if (!response.ok) {
      const err = await response.json().catch(() => ({ error: response.statusText }));
      throw new Error(err.error ?? `HTTP ${response.status}`);
    }

    const reader  = response.body!.getReader();
    const decoder = new TextDecoder();
    let buffer    = '';

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split('\n');
      buffer = lines.pop() ?? '';
      for (const line of lines) {
        if (line.startsWith('data: ')) {
          const data = line.slice(6).trim();
          if (!data) continue;
          try { yield JSON.parse(data) as StreamChunk; } catch { /* skip malformed */ }
        }
      }
    }
  }
}
