import {
  Component, inject, ViewChild, ElementRef,
  AfterViewChecked, ChangeDetectionStrategy, effect
} from '@angular/core';
import { ChatStateService } from '../../services/chat-state.service';
import { MessageBubbleComponent } from '../message-bubble/message-bubble.component';
import { EmptyStateComponent } from '../empty-state/empty-state.component';
import { InputBoxComponent } from '../input-box/input-box.component';

@Component({
  selector: 'app-chat-window',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MessageBubbleComponent, EmptyStateComponent, InputBoxComponent],
  template: `
    <div class="chat-window">

      <!-- Top bar -->
      <header class="topbar">
        <button class="icon-btn topbar__menu" (click)="state.toggleSidebar()">
          <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <line x1="3" y1="6" x2="21" y2="6"/><line x1="3" y1="12" x2="21" y2="12"/><line x1="3" y1="18" x2="21" y2="18"/>
          </svg>
        </button>

        <span class="topbar__title">
          {{ state.currentSession()?.title ?? 'New conversation' }}
        </span>

        <button class="icon-btn" (click)="state.toggleTheme()" [title]="state.theme() === 'dark' ? 'Light mode' : 'Dark mode'">
          @if (state.theme() === 'dark') {
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
              <circle cx="12" cy="12" r="5"/><line x1="12" y1="1" x2="12" y2="3"/><line x1="12" y1="21" x2="12" y2="23"/>
              <line x1="4.22" y1="4.22" x2="5.64" y2="5.64"/><line x1="18.36" y1="18.36" x2="19.78" y2="19.78"/>
              <line x1="1" y1="12" x2="3" y2="12"/><line x1="21" y1="12" x2="23" y2="12"/>
            </svg>
          } @else {
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
              <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/>
            </svg>
          }
        </button>
      </header>

      <!-- Messages -->
      <div class="messages-area" #scrollAnchor>
        @if (state.currentMessages().length === 0) {
          <app-empty-state (select)="onSuggest($event)" />
        } @else {
          <div class="messages-list">
            @for (msg of state.currentMessages(); track msg.id) {
              <app-message-bubble [msg]="msg" />
            }
            <div #bottom></div>
          </div>
        }
      </div>

      <!-- Error banner -->
      @if (state.error()) {
        <div class="error-banner">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>
          </svg>
          <span>{{ state.error() }}</span>
        </div>
      }

      <!-- Input -->
      <app-input-box
        #inputBox
        [isLoading]="state.isLoading()"
        (send)="onSend($event)"
        (stop)="state.stopGeneration()"
      />
    </div>
  `,
})
export class ChatWindowComponent implements AfterViewChecked {
  readonly state = inject(ChatStateService);

  @ViewChild('bottom') bottomRef?: ElementRef<HTMLDivElement>;
  @ViewChild('inputBox') inputBoxRef?: InputBoxComponent;

  private shouldScroll = false;

  constructor() {
    // Mark for scroll whenever messages change
    effect(() => {
      this.state.currentMessages(); // subscribe
      this.shouldScroll = true;
    });
  }

  ngAfterViewChecked(): void {
    if (this.shouldScroll) {
      this.bottomRef?.nativeElement?.scrollIntoView({ behavior: 'smooth' });
      this.shouldScroll = false;
    }
  }

  onSend(text: string): void {
    this.state.sendMessage(text);
  }

  onSuggest(text: string): void {
    this.inputBoxRef?.prefill(text);
  }
}
