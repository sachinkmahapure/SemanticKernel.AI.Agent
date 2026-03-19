import {
  Component, inject, signal, OnInit, ChangeDetectionStrategy
} from '@angular/core';
import { NgClass, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChatApiService } from '../../services/chat-api.service';
import { ApprovalRequest } from '../../models/chat.models';

@Component({
  selector: 'app-approvals-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgClass, DatePipe, FormsModule],
  template: `
    <div class="approvals-panel">

      <div class="panel-header">
        <div class="panel-title">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
          </svg>
          Human Approvals
          @if (pendingCount() > 0) {
            <span class="badge">{{ pendingCount() }}</span>
          }
        </div>
        <button class="icon-btn" (click)="load()" title="Refresh">
          <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <path d="M21 2v6h-6"/><path d="M3 12a9 9 0 0 1 15-6.7L21 8"/>
            <path d="M3 22v-6h6"/><path d="M21 12a9 9 0 0 1-15 6.7L3 16"/>
          </svg>
        </button>
      </div>

      @if (loading()) {
        <div class="panel-loading">
          <span class="spinner"></span> Loading...
        </div>
      } @else if (approvals().length === 0) {
        <div class="empty-approvals">
          <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round">
            <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
            <polyline points="9 12 11 14 15 10"/>
          </svg>
          <p>No pending approvals</p>
        </div>
      } @else {
        <div class="approvals-list">
          @for (a of approvals(); track a.id) {
            <div class="approval-card" [ngClass]="'status-' + a.status.toLowerCase()">
              <div class="approval-head">
                <span class="approval-action">{{ a.actionName }}</span>
                <span class="approval-status" [ngClass]="'status-' + a.status.toLowerCase()">
                  {{ a.status }}
                </span>
              </div>

              <p class="approval-desc">{{ a.description }}</p>

              @if (a.parameters && objectKeys(a.parameters).length > 0) {
                <div class="approval-params">
                  @for (key of objectKeys(a.parameters); track key) {
                    <div class="param-row">
                      <span class="param-key">{{ key }}</span>
                      <span class="param-val">{{ a.parameters[key] }}</span>
                    </div>
                  }
                </div>
              }

              <div class="approval-meta">
                <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
                  <circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>
                </svg>
                Expires {{ a.expiresAt | date:'HH:mm:ss' }}
              </div>

              @if (a.status === 'Pending') {
                <div class="approval-actions">
                  <input
                    type="text"
                    class="reviewer-input"
                    placeholder="Reviewer name (optional)"
                    [(ngModel)]="reviewerMap[a.id]"
                  />
                  <div class="approval-btns">
                    <button
                      class="btn-approve"
                      [disabled]="resolving()[a.id]"
                      (click)="resolve(a.id, true)"
                    >
                      @if (resolving()[a.id]) { <span class="spinner-sm"></span> }
                      Approve
                    </button>
                    <button
                      class="btn-reject"
                      [disabled]="resolving()[a.id]"
                      (click)="resolve(a.id, false)"
                    >
                      Reject
                    </button>
                  </div>
                </div>
              } @else if (a.reviewedBy) {
                <div class="approval-resolved">
                  Reviewed by <strong>{{ a.reviewedBy }}</strong>
                </div>
              }
            </div>
          }
        </div>
      }
    </div>
  `,
})
export class ApprovalsPanelComponent implements OnInit {
  private readonly api = inject(ChatApiService);

  readonly approvals  = signal<ApprovalRequest[]>([]);
  readonly loading    = signal(false);
  readonly resolving  = signal<Record<string, boolean>>({});
  readonly pendingCount = () => this.approvals().filter(a => a.status === 'Pending').length;

  reviewerMap: Record<string, string> = {};

  ngOnInit(): void {
    this.load();
    // Auto-refresh every 10s while panel is visible
    setInterval(() => this.load(), 10_000);
  }

  load(): void {
    this.loading.set(true);
    this.api.getApprovals().subscribe({
      next: data => { this.approvals.set(data ?? []); this.loading.set(false); },
      error: ()   => this.loading.set(false),
    });
  }

  resolve(id: string, approved: boolean): void {
    this.resolving.update(m => ({ ...m, [id]: true }));
    const reviewer = this.reviewerMap[id] || 'UI User';

    this.api.resolveApproval(id, approved, reviewer).subscribe({
      next: () => {
        this.resolving.update(m => ({ ...m, [id]: false }));
        this.load();
      },
      error: () => this.resolving.update(m => ({ ...m, [id]: false })),
    });
  }

  objectKeys(obj: Record<string, string>): string[] {
    return Object.keys(obj);
  }
}
