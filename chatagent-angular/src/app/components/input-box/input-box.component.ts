import {
  Component, output, input, signal,
  ViewChild, ElementRef, AfterViewInit,
  ChangeDetectionStrategy, effect
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgClass } from '@angular/common';

@Component({
  selector: 'app-input-box',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, NgClass],
  template: `
    <div class="input-area">
      <div class="input-box" [ngClass]="{ focused: focused() }">
        <textarea
          #textarea
          class="message-input"
          [(ngModel)]="text"
          (ngModelChange)="onTextChange()"
          (keydown.enter)="onEnter($event)"
          (focus)="focused.set(true)"
          (blur)="focused.set(false)"
          placeholder="Message AI ChatAgent… (Shift+Enter for new line)"
          rows="1"
          [disabled]="isLoading() && text.length === 0"
        ></textarea>

        <div class="input-actions">
          @if (isLoading()) {
            <button class="send-btn send-btn--stop" (click)="stop.emit()" title="Stop generation">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
                <rect x="3" y="3" width="18" height="18" rx="2"/>
              </svg>
            </button>
          } @else {
            <button
              class="send-btn"
              [disabled]="!text.trim()"
              (click)="submit()"
              title="Send (Enter)"
            >
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round">
                <line x1="22" y1="2" x2="11" y2="13"/>
                <polygon points="22 2 15 22 11 13 2 9 22 2"/>
              </svg>
            </button>
          }
        </div>
      </div>
      <p class="input-hint">AI ChatAgent can make mistakes. Verify important information.</p>
    </div>
  `,
})
export class InputBoxComponent implements AfterViewInit {
  @ViewChild('textarea') textareaRef!: ElementRef<HTMLTextAreaElement>;

  readonly isLoading = input<boolean>(false);
  readonly send = output<string>();
  readonly stop = output<void>();

  text = '';
  readonly focused = signal(false);

  ngAfterViewInit(): void {
    this.textareaRef?.nativeElement?.focus();
  }

  onTextChange(): void {
    this.resize();
  }

  onEnter(e: Event): void {
    const ke = e as KeyboardEvent;
    if (ke.shiftKey) return;
    ke.preventDefault();
    this.submit();
  }

  submit(): void {
    const val = this.text.trim();
    if (!val || this.isLoading()) return;
    this.text = '';
    this.resize();
    this.send.emit(val);
  }

  /** Pre-fill text from suggestion chip */
  prefill(val: string): void {
    this.text = val;
    this.resize();
    this.textareaRef?.nativeElement?.focus();
  }

  private resize(): void {
    const el = this.textareaRef?.nativeElement;
    if (!el) return;
    el.style.height = 'auto';
    el.style.height = Math.min(el.scrollHeight, 200) + 'px';
  }
}
