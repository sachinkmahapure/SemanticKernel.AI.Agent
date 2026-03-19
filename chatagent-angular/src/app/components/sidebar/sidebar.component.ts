import {
  Component, inject, output, ChangeDetectionStrategy
} from '@angular/core';
import { NgClass } from '@angular/common';
import { formatDistanceToNow } from 'date-fns';
import { ChatStateService } from '../../services/chat-state.service';
import { ActivePanel } from '../../models/chat.models';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgClass],
  template: `
    <aside class="sidebar"
      [ngClass]="{ 'sidebar--open': state.sidebarOpen(), 'sidebar--closed': !state.sidebarOpen() }">

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
        <button class="icon-btn" (click)="state.toggleSidebar()">
          @if (state.sidebarOpen()) {
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>
          } @else {
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>
          }
        </button>
      </div>

      <!-- Panel nav -->
      <div class="panel-nav" [ngClass]="{ 'panel-nav--collapsed': !state.sidebarOpen() }">
        <button class="nav-btn" [ngClass]="{ active: state.activePanel() === 'chat' }"
          (click)="setPanel('chat')" title="Chat">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>
          </svg>
          @if (state.sidebarOpen()) { <span>Chat</span> }
        </button>

        <button class="nav-btn" [ngClass]="{ active: state.activePanel() === 'rag' }"
          (click)="setPanel('rag')" title="Document Search">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
          </svg>
          @if (state.sidebarOpen()) { <span>Doc Search</span> }
        </button>

        <button class="nav-btn" [ngClass]="{ active: state.activePanel() === 'approvals' }"
          (click)="setPanel('approvals')" title="Approvals">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
          </svg>
          @if (state.sidebarOpen()) { <span>Approvals</span> }
        </button>
      </div>

      <!-- Sessions (only in chat panel) -->
      @if (state.sidebarOpen() && state.activePanel() === 'chat') {
        <button class="new-chat-btn" (click)="state.newChat()">
          <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round">
            <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
          </svg>
          <span>New chat</span>
        </button>

        @if (state.sessions().length > 0) {
          <div class="sessions">
            <div class="sessions__label">Recent</div>
            <div class="sessions__list">
              @for (session of state.sessions(); track session.id) {
                <div class="session-item"
                  [ngClass]="{ 'session-item--active': session.id === state.currentId() }"
                  (click)="state.loadSession(session.id)">
                  <div class="session-item__content">
                    <div class="session-item__title">{{ session.title }}</div>
                    <div class="session-item__meta">{{ timeAgo(session.lastActivityAt) }}</div>
                  </div>
                  <button class="session-item__delete"
                    (click)="onDelete($event, session.id)" title="Delete">
                    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
                      <polyline points="3 6 5 6 21 6"/>
                      <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/>
                    </svg>
                  </button>
                </div>
              }
            </div>
          </div>
        }
      }

      <!-- Footer -->
      @if (state.sidebarOpen()) {
        <div class="sidebar__footer">
          <button class="icon-btn sidebar__footer-btn" (click)="state.toggleTheme()">
            @if (state.theme() === 'dark') {
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
                <circle cx="12" cy="12" r="5"/>
                <line x1="12" y1="1" x2="12" y2="3"/><line x1="12" y1="21" x2="12" y2="23"/>
                <line x1="4.22" y1="4.22" x2="5.64" y2="5.64"/><line x1="18.36" y1="18.36" x2="19.78" y2="19.78"/>
                <line x1="1" y1="12" x2="3" y2="12"/><line x1="21" y1="12" x2="23" y2="12"/>
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

  setPanel(panel: ActivePanel): void {
    this.state.setPanel(panel);
    if (!this.state.sidebarOpen() && panel === 'chat') {
      this.state.toggleSidebar();
    }
  }

  onDelete(e: Event, id: string): void {
    e.stopPropagation();
    this.state.deleteSession(id);
  }
}
