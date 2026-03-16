import {
  Component, input, ChangeDetectionStrategy, ViewEncapsulation
} from '@angular/core';
import { NgClass, DatePipe } from '@angular/common';
import { ChatMessage } from '../../models/chat.models';
import { MarkdownPipe } from '../../pipes/markdown.pipe';
import { ToolBadgeComponent } from '../tool-badge/tool-badge.component';

@Component({
  selector: 'app-message-bubble',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  imports: [NgClass, MarkdownPipe, ToolBadgeComponent, DatePipe],
  template: `
    <div class="msg-row" [ngClass]="msg().role">
      <!-- Avatar -->
      <div class="msg-avatar" [ngClass]="msg().role">
        @if (msg().role === 'user') {
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>
          </svg>
        } @else {
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <rect x="2" y="14" width="20" height="8" rx="2"/>
            <path d="M12 8V4H8"/><path d="M6 14v-4"/><path d="M10 14v-7"/>
            <path d="M14 14V8"/><path d="M18 14v-2"/>
          </svg>
        }
      </div>

      <!-- Body -->
      <div class="msg-body" [ngClass]="msg().role">

        <!-- Active tools (streaming) -->
        @if ((msg().activeTools?.length ?? 0) > 0) {
          <div class="msg-tools">
            @for (tool of msg().activeTools!; track tool) {
              <app-tool-badge [name]="tool" [done]="false" />
            }
          </div>
        }

        <!-- Completed tools -->
        @if ((msg().toolsUsed?.length ?? 0) > 0) {
          <div class="msg-tools">
            @for (t of msg().toolsUsed!; track t.functionName) {
              <app-tool-badge
                [name]="t.pluginName + '.' + t.functionName"
                [done]="true"
              />
            }
          </div>
        }

        <!-- Content -->
        <div class="msg-content" [ngClass]="{ 'msg-content--error': msg().error }">
          @if (!msg().content && msg().isStreaming) {
            <span class="typing-dots">
              <span></span><span></span><span></span>
            </span>
          } @else if (msg().role === 'user') {
            <p class="msg-text">{{ msg().content }}</p>
          } @else {
            <div class="markdown-body"
                 [innerHTML]="msg().content | markdown">
            </div>
            @if (msg().isStreaming && msg().content) {
              <span class="cursor-blink">▋</span>
            }
          }
        </div>

        <!-- Meta -->
        @if (!msg().isStreaming) {
          <div class="msg-meta">
            <svg width="9" height="9" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
              <circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>
            </svg>
            <span>{{ msg().timestamp | date:'HH:mm' }}</span>
            @if (msg().tokens) {
              <svg width="9" height="9" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="4" y="4" width="16" height="16" rx="2"/></svg>
              <span>{{ msg().tokens }} tokens</span>
            }
          </div>
        }
      </div>
    </div>
  `,
})
export class MessageBubbleComponent {
  readonly msg = input.required<ChatMessage>();
}
