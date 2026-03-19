import {
  Component, inject, signal, ChangeDetectionStrategy
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgClass, DecimalPipe } from '@angular/common';
import { ChatApiService } from '../../services/chat-api.service';
import { RagSearchResult } from '../../models/chat.models';

@Component({
  selector: 'app-rag-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, NgClass, DecimalPipe],
  template: `
    <div class="rag-panel">

      <div class="panel-header">
        <div class="panel-title">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
          </svg>
          Semantic Search
        </div>
        <button class="reindex-btn" [ngClass]="{ loading: reindexing() }" (click)="reindex()">
          @if (reindexing()) {
            <span class="spinner"></span> Indexing...
          } @else {
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
              <path d="M21 2v6h-6"/><path d="M3 12a9 9 0 0 1 15-6.7L21 8"/>
              <path d="M3 22v-6h6"/><path d="M21 12a9 9 0 0 1-15 6.7L3 16"/>
            </svg>
            Re-index
          }
        </button>
      </div>

      @if (reindexMessage()) {
        <div class="info-banner">{{ reindexMessage() }}</div>
      }

      <!-- Search box -->
      <div class="search-box" [ngClass]="{ focused: focused() }">
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
          <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
        </svg>
        <input
          type="text"
          class="search-input"
          placeholder="Search indexed documents..."
          [(ngModel)]="query"
          (focus)="focused.set(true)"
          (blur)="focused.set(false)"
          (keydown.enter)="search()"
        />
        <button class="search-go" [disabled]="!query().trim() || searching()" (click)="search()">
          @if (searching()) { <span class="spinner"></span> } @else { Go }
        </button>
      </div>

      <!-- Results -->
      @if (results().length > 0) {
        <div class="results-header">
          {{ results().length }} result{{ results().length !== 1 ? 's' : '' }}
          for <em>"{{ lastQuery() }}"</em>
        </div>
        <div class="results-list">
          @for (r of results(); track r.source + r.score) {
            <div class="result-card">
              <div class="result-meta">
                <span class="result-source">
                  <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                    <polyline points="14 2 14 8 20 8"/>
                  </svg>
                  {{ r.source }}
                </span>
                <span class="result-score" [ngClass]="scoreClass(r.score)">
                  {{ r.score | number:'1.0-0' }}% match
                </span>
                <span class="result-col">{{ r.collection }}</span>
              </div>
              <p class="result-text">{{ truncate(r.text) }}</p>
            </div>
          }
        </div>
      } @else if (searched() && !searching()) {
        <div class="empty-results">
          <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round">
            <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
          </svg>
          <p>No results found for <em>"{{ lastQuery() }}"</em></p>
          <p class="hint">Try re-indexing if you added new documents recently.</p>
        </div>
      } @else if (!searched()) {
        <div class="search-hint">
          <p>Search across all indexed documents — PDFs, text files, CSV, and JSON.</p>
          <div class="example-queries">
            <span class="hint-label">Try:</span>
            @for (ex of examples; track ex) {
              <button class="example-chip" (click)="useExample(ex)">{{ ex }}</button>
            }
          </div>
        </div>
      }
    </div>
  `,
})
export class RagPanelComponent {
  private readonly api = inject(ChatApiService);

  readonly query        = signal('');
  readonly results      = signal<RagSearchResult[]>([]);
  readonly searching    = signal(false);
  readonly searched     = signal(false);
  readonly lastQuery    = signal('');
  readonly reindexing   = signal(false);
  readonly reindexMessage = signal('');
  readonly focused      = signal(false);

  readonly examples = ['return policy', 'warranty', 'product categories', 'shipping'];

  search(): void {
    const q = this.query().trim();
    if (!q || this.searching()) return;

    this.searching.set(true);
    this.searched.set(false);
    this.lastQuery.set(q);

    this.api.ragSearch(q, 8).subscribe({
      next: res => {
        this.results.set(res.results ?? []);
        this.searched.set(true);
        this.searching.set(false);
      },
      error: () => {
        this.results.set([]);
        this.searched.set(true);
        this.searching.set(false);
      }
    });
  }

  reindex(): void {
    if (this.reindexing()) return;
    this.reindexing.set(true);
    this.reindexMessage.set('');

    this.api.ragReindex().subscribe({
      next: res => {
        this.reindexMessage.set(res.message ?? 'Re-indexing complete.');
        this.reindexing.set(false);
        setTimeout(() => this.reindexMessage.set(''), 4000);
      },
      error: () => {
        this.reindexMessage.set('Re-indexing failed. Check the API logs.');
        this.reindexing.set(false);
      }
    });
  }

  useExample(q: string): void {
    this.query.set(q);
    this.search();
  }

  truncate(text: string, max = 220): string {
    return text.length > max ? text.slice(0, max) + '…' : text;
  }

  scoreClass(score: number): string {
    if (score >= 0.9) return 'score-high';
    if (score >= 0.75) return 'score-mid';
    return 'score-low';
  }
}
