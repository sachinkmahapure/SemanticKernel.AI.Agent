import { Component, inject, ChangeDetectionStrategy } from '@angular/core';
import { NgClass } from '@angular/common';
import { ChatStateService } from './services/chat-state.service';
import { SidebarComponent } from './components/sidebar/sidebar.component';
import { ChatWindowComponent } from './components/chat-window/chat-window.component';
import { RagPanelComponent } from './components/rag-panel/rag-panel.component';
import { ApprovalsPanelComponent } from './components/approvals-panel/approvals-panel.component';

@Component({
  selector: 'app-root',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgClass, SidebarComponent, ChatWindowComponent, RagPanelComponent, ApprovalsPanelComponent],
  template: `
    <app-sidebar />
    <div class="main-content">
      @switch (state.activePanel()) {
        @case ('chat')      { <app-chat-window /> }
        @case ('rag')       { <app-rag-panel /> }
        @case ('approvals') { <app-approvals-panel /> }
      }
    </div>
  `,
  host: {
    '[class.dark]':  'state.theme() === "dark"',
    '[class.light]': 'state.theme() === "light"',
  },
})
export class AppComponent {
  readonly state = inject(ChatStateService);
}
