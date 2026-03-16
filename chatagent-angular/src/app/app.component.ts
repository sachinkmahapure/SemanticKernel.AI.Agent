import {
  Component, inject, ChangeDetectionStrategy, HostBinding
} from '@angular/core';
import { ChatStateService } from './services/chat-state.service';
import { SidebarComponent } from './components/sidebar/sidebar.component';
import { ChatWindowComponent } from './components/chat-window/chat-window.component';

@Component({
  selector: 'app-root',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SidebarComponent, ChatWindowComponent],
  template: `
    <app-sidebar />
    <app-chat-window />
  `,
  host: {
    '[class.dark]': 'state.theme() === "dark"',
    '[class.light]': 'state.theme() === "light"',
  },
})
export class AppComponent {
  readonly state = inject(ChatStateService);
}
