import { Injectable, inject, signal, computed } from '@angular/core';
import { ChatApiService } from './chat-api.service';
import { ChatMessage, ChatSession, StreamChunk } from '../models/chat.models';

function uid(): string {
  return Math.random().toString(36).slice(2, 10);
}

function titleFrom(msg: string): string {
  return msg.length > 42 ? msg.slice(0, 42) + '…' : msg;
}

@Injectable({ providedIn: 'root' })
export class ChatStateService {
  private readonly api = inject(ChatApiService);

  // ── Signals ────────────────────────────────────────────────────────────────
  readonly sessions   = signal<ChatSession[]>([]);
  readonly currentId  = signal<string>('');
  readonly isLoading  = signal<boolean>(false);
  readonly error      = signal<string | null>(null);
  readonly theme      = signal<'dark' | 'light'>('dark');
  readonly sidebarOpen = signal<boolean>(true);

  private readonly messageMap = signal<Map<string, ChatMessage[]>>(new Map());

  // ── Computed ───────────────────────────────────────────────────────────────
  readonly currentMessages = computed<ChatMessage[]>(() => {
    const id = this.currentId();
    return this.messageMap().get(id) ?? [];
  });

  readonly currentSession = computed<ChatSession | undefined>(() =>
    this.sessions().find(s => s.id === this.currentId())
  );

  // ── Abort controller ───────────────────────────────────────────────────────
  private abortCtrl: AbortController | null = null;

  // ── Public API ─────────────────────────────────────────────────────────────

  newChat(): void {
    this.currentId.set('');
    this.error.set(null);
  }

  toggleSidebar(): void {
    this.sidebarOpen.update(v => !v);
  }

  toggleTheme(): void {
    this.theme.update(t => t === 'dark' ? 'light' : 'dark');
  }

  stopGeneration(): void {
    this.abortCtrl?.abort();
    this.isLoading.set(false);
  }

  deleteSession(id: string): void {
    this.sessions.update(list => list.filter(s => s.id !== id));
    this.messageMap.update(map => {
      const next = new Map(map);
      next.delete(id);
      return next;
    });
    if (this.currentId() === id) this.currentId.set('');
  }

  async loadSession(id: string): Promise<void> {
    this.currentId.set(id);
    this.error.set(null);
    if (this.messageMap().has(id)) return;

    this.isLoading.set(true);
    try {
      const data = await this.api.getHistory(id).toPromise();
      if (!data) return;
      const msgs: ChatMessage[] = data.messages
        .filter(m => m.role !== 'system')
        .map(m => ({
          id: String(m.id),
          role: m.role as 'user' | 'assistant',
          content: m.content,
          timestamp: new Date(m.createdAt),
          tokens: m.tokenCount,
        }));
      this.setMessages(id, msgs);
    } catch (e: any) {
      this.error.set(e.message ?? 'Failed to load history');
    } finally {
      this.isLoading.set(false);
    }
  }

  async sendMessage(text: string, systemPrompt?: string): Promise<void> {
    if (!text.trim() || this.isLoading()) return;

    this.error.set(null);
    this.isLoading.set(true);

    this.abortCtrl?.abort();
    const ctrl = new AbortController();
    this.abortCtrl = ctrl;

    const sid = this.currentId();

    // Optimistic user message
    const userMsg: ChatMessage = {
      id: uid(), role: 'user',
      content: text, timestamp: new Date(),
    };
    const assistantId = uid();
    const assistantMsg: ChatMessage = {
      id: assistantId, role: 'assistant',
      content: '', timestamp: new Date(),
      isStreaming: true, activeTools: [],
    };
    this.appendMessages(sid, [userMsg, assistantMsg]);

    let resolvedId = sid;
    let full = '';

    try {
      const stream = this.api.streamMessage(
        { message: text, sessionId: sid, systemPrompt },
        ctrl.signal
      );

      for await (const chunk of this.toAsyncIterable<StreamChunk>(stream)) {
        if (ctrl.signal.aborted) break;
        resolvedId = this.handleChunk(chunk, assistantId, sid, resolvedId, full);
        if (chunk.type === 'content') full += chunk.content ?? '';
      }

      // Finalise
      this.updateMessage(resolvedId || sid, assistantId, msg => ({
        ...msg, isStreaming: false, activeTools: [],
      }));

      this.upsertSession(resolvedId || sid, titleFrom(text), text);

    } catch (e: any) {
      if (e.name === 'AbortError') return;
      const msg = e.message ?? 'Request failed';
      this.error.set(msg);
      this.updateMessage(resolvedId || sid, assistantId, m => ({
        ...m, content: `Error: ${msg}`, isStreaming: false, error: true,
      }));
    } finally {
      this.isLoading.set(false);
    }
  }

  // ── Internal helpers ───────────────────────────────────────────────────────

  private handleChunk(
    chunk: StreamChunk,
    assistantId: string,
    originalSid: string,
    resolvedId: string,
    accumulated: string
  ): string {
    const key = resolvedId || originalSid;

    switch (chunk.type) {
      case 'content': {
        if (chunk.sessionId && !resolvedId) {
          resolvedId = chunk.sessionId;
          this.currentId.set(resolvedId);
          // Move messages from '' to real id
          const msgs = this.messageMap().get('') ?? this.messageMap().get(originalSid) ?? [];
          this.setMessages(resolvedId, msgs);
        }
        const newContent = accumulated + (chunk.content ?? '');
        this.updateMessage(resolvedId || originalSid, assistantId,
          m => ({ ...m, content: newContent }));
        break;
      }
      case 'tool_start':
        this.updateMessage(key, assistantId, m => ({
          ...m, activeTools: [...(m.activeTools ?? []), chunk.tool ?? ''],
        }));
        break;

      case 'tool_end':
        this.updateMessage(key, assistantId, m => ({
          ...m,
          activeTools: (m.activeTools ?? []).filter(t => t !== chunk.tool),
          toolsUsed: [...(m.toolsUsed ?? []),
            { pluginName: chunk.tool?.split('.')[0] ?? '',
              functionName: chunk.tool?.split('.')[1] ?? '',
              success: true, durationMs: 0 }],
        }));
        break;

      case 'done':
        if (chunk.sessionId) {
          resolvedId = chunk.sessionId;
          this.currentId.set(resolvedId);
        }
        break;

      case 'error':
        throw new Error(chunk.content ?? 'Stream error');
    }
    return resolvedId;
  }

  private appendMessages(sid: string, msgs: ChatMessage[]): void {
    this.messageMap.update(map => {
      const next = new Map(map);
      next.set(sid, [...(next.get(sid) ?? []), ...msgs]);
      return next;
    });
  }

  private setMessages(sid: string, msgs: ChatMessage[]): void {
    this.messageMap.update(map => new Map(map).set(sid, msgs));
  }

  private updateMessage(
    sid: string, msgId: string,
    updater: (m: ChatMessage) => ChatMessage
  ): void {
    this.messageMap.update(map => {
      const msgs = [...(map.get(sid) ?? [])];
      const idx = msgs.findIndex(m => m.id === msgId);
      if (idx !== -1) msgs[idx] = updater(msgs[idx]);
      return new Map(map).set(sid, msgs);
    });
  }

  private upsertSession(id: string, title: string, lastMsg: string): void {
    this.sessions.update(list => {
      const existing = list.find(s => s.id === id);
      const updated: ChatSession = {
        id, title: existing?.title ?? title,
        lastMessage: lastMsg,
        lastActivityAt: new Date(),
        messageCount: (existing?.messageCount ?? 0) + 1,
      };
      return [updated, ...list.filter(s => s.id !== id)].slice(0, 50);
    });
  }

  private toAsyncIterable<T>(obs: any): AsyncIterable<T> {
    return {
      [Symbol.asyncIterator]() {
        const queue: T[] = [];
        const resolvers: Array<(v: IteratorResult<T>) => void> = [];
        let done = false;
        let error: any = null;

        obs.subscribe({
          next: (v: T) => {
            if (resolvers.length) resolvers.shift()!({ value: v, done: false });
            else queue.push(v);
          },
          error: (e: any) => {
            error = e;
            resolvers.forEach(r => r({ value: undefined as any, done: true }));
          },
          complete: () => {
            done = true;
            resolvers.forEach(r => r({ value: undefined as any, done: true }));
          },
        });

        return {
          next(): Promise<IteratorResult<T>> {
            if (queue.length) return Promise.resolve({ value: queue.shift()!, done: false });
            if (done) return Promise.resolve({ value: undefined as any, done: true });
            if (error) return Promise.reject(error);
            return new Promise(resolve => resolvers.push(resolve));
          },
        };
      },
    };
  }
}
