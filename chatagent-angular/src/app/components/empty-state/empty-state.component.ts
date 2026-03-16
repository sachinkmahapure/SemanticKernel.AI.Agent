import { Component, output, ChangeDetectionStrategy } from '@angular/core';

interface Suggestion {
  icon: string;
  label: string;
}

@Component({
  selector: 'app-empty-state',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="empty-state">
      <div class="empty-logo">
        <svg width="36" height="36" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round">
          <rect x="2" y="14" width="20" height="8" rx="2"/>
          <path d="M12 8V4H8"/><path d="M6 14v-4"/>
          <path d="M10 14v-7"/><path d="M14 14V8"/><path d="M18 14v-2"/>
        </svg>
      </div>
      <h2 class="empty-title">How can I help you today?</h2>
      <p class="empty-sub">I have access to your database, APIs, PDFs, files, and the web.</p>
      <div class="suggestions-grid">
        @for (s of suggestions; track s.label) {
          <button class="suggestion-btn" (click)="select.emit(s.label)">
            <span class="suggestion-icon" [innerHTML]="s.icon"></span>
            <span>{{ s.label }}</span>
          </button>
        }
      </div>
    </div>
  `,
})
export class EmptyStateComponent {
  readonly select = output<string>();

  readonly suggestions: Suggestion[] = [
    {
      icon: `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><ellipse cx="12" cy="5" rx="9" ry="3"/><path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"/><path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"/></svg>`,
      label: 'Show me low stock products',
    },
    {
      icon: `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="2" y1="12" x2="22" y2="12"/><path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/></svg>`,
      label: "What's the weather in New York?",
    },
    {
      icon: `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>`,
      label: 'List available PDF documents',
    },
    {
      icon: `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M13 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"/><polyline points="13 2 13 9 20 9"/></svg>`,
      label: 'Analyse sales_data.csv file',
    },
    {
      icon: `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>`,
      label: 'Search for latest AI news',
    },
    {
      icon: `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/></svg>`,
      label: 'Give me a business summary',
    },
  ];
}
