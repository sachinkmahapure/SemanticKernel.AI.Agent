import {
  Component, inject, output, ChangeDetectionStrategy
} from '@angular/core';
import { NgClass } from '@angular/common';
import { formatDistanceToNow } from 'date-fns';
import { ChatStateService } from '../../services/chat-state.service';
import { ChatSession } from '../../models/chat.models';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgClass],
  template: `
    <aside class="sidebar" [ngClass]="{ 'sidebar--open': state.sidebarOpen(), 'sidebar--closed': !state.sidebarOpen() }">

      <!-- Header -->
      <div class="sidebar__header">
        <div class="brand">
          <div class="brand__icon">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
              <rect x="2" y="14" width="20" height="8" rx="2"/>
              <path d="M12 8V4H8"/><path d="M6 14v-4"/>
              <path d="M10 14v-7"/><path d="M14 14V8"/><path d="M18 14v-2"/>
            </svg>
          </div>
          @if (state.sidebarOpen()) {
            <span class="brand__name">AI ChatAgent</span>
          }
        </div>
        <button class="icon-btn" (click)="state.toggleSidebar()" [title]="state.sidebarOpen() ? 'Collapse' : 'Expand'">
          @if (state.sidebarOpen()) {
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>
          } @else {
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>
          }
        </button>
      </div>

      <!-- New chat -->
      <button class="new-chat-btn" (click)="state.newChat()" title="New chat">
        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round">
          <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
        </svg>
        @if (state.sidebarOpen()) { <span>New chat</span> }
      </button>

      <!-- Sessions -->
      @if (state.sidebarOpen() && state.sessions().length > 0) {
        <div class="sessions">
          <div class="sessions__label">Recent</div>
          <div class="sessions__list">
            @for (session of state.sessions(); track session.id) {
              <div
                class="session-item"
                [ngClass]="{ 'session-item--active': session.id === state.currentId() }"
                (click)="state.loadSession(session.id)"
              >
                <div class="session-item__content">
                  <div class="session-item__title">{{ session.title }}</div>
                  <div class="session-item__meta">{{ timeAgo(session.lastActivityAt) }}</div>
                </div>
                <button
                  class="session-item__delete"
                  (click)="onDelete($event, session.id)"
                  title="Delete"
                >
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
                    <polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/>
                    <path d="M10 11v6"/><path d="M14 11v6"/><path d="M9 6V4h6v2"/>
                  </svg>
                </button>
              </div>
            }
          </div>
        </div>
      }

      <!-- Footer -->
      @if (state.sidebarOpen()) {
        <div class="sidebar__footer">
          <button class="icon-btn sidebar__footer-btn" (click)="state.toggleTheme()">
            @if (state.theme() === 'dark') {
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
                <circle cx="12" cy="12" r="5"/><line x1="12" y1="1" x2="12" y2="3"/><line x1="12" y1="21" x2="12" y2="23"/>
                <line x1="4.22" y1="4.22" x2="5.64" y2="5.64"/><line x1="18.36" y1="18.36" x2="19.78" y2="19.78"/>
                <line x1="1" y1="12" x2="3" y2="12"/><line x1="21" y1="12" x2="23" y2="12"/>
                <line x1="4.22" y1="19.78" x2="5.64" y2="18.36"/><line x1="18.36" y1="5.64" x2="19.78" y2="4.22"/>
              </svg>
              <span>Light mode</span>
            } @else {
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
                <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/>
              </svg>
              <span>Dark mode</span>
            }
          </button>
        </div>
      }
    </aside>
  `,
})
export class SidebarComponent {
  readonly state = inject(ChatStateService);

  timeAgo(date: Date): string {
    return formatDistanceToNow(date, { addSuffix: true });
  }

  onDelete(e: Event, id: string): void {
    e.stopPropagation();
    this.state.deleteSession(id);
  }
}
