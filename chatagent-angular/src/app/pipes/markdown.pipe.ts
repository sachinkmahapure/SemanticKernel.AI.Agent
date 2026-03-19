import { Pipe, PipeTransform } from '@angular/core';
import { marked } from 'marked';

// Import highlight.js CORE only — not the full 900-language bundle
import hljs from 'highlight.js/lib/core';

// Register only the languages the AI agent actually returns in code blocks
import csharp     from 'highlight.js/lib/languages/csharp';
import typescript from 'highlight.js/lib/languages/typescript';
import javascript from 'highlight.js/lib/languages/javascript';
import json       from 'highlight.js/lib/languages/json';
import sql        from 'highlight.js/lib/languages/sql';
import bash       from 'highlight.js/lib/languages/bash';
import xml        from 'highlight.js/lib/languages/xml';
import python     from 'highlight.js/lib/languages/python';
import plaintext  from 'highlight.js/lib/languages/plaintext';

hljs.registerLanguage('csharp',     csharp);
hljs.registerLanguage('typescript', typescript);
hljs.registerLanguage('javascript', javascript);
hljs.registerLanguage('json',       json);
hljs.registerLanguage('sql',        sql);
hljs.registerLanguage('bash',       bash);
hljs.registerLanguage('shell',      bash);
hljs.registerLanguage('xml',        xml);
hljs.registerLanguage('html',       xml);
hljs.registerLanguage('python',     python);
hljs.registerLanguage('plaintext',  plaintext);

// Configure marked with inline syntax highlighting (no extra package needed)
marked.use({
  renderer: {
    code({ text, lang }: { text: string; lang?: string; escaped?: boolean }): string {
      const language = hljs.getLanguage(lang ?? '') ? (lang ?? 'plaintext') : 'plaintext';
      const highlighted = hljs.highlight(text, { language }).value;
      return `<div class="code-block">
        <div class="code-header">
          <span class="code-lang">${language}</span>
          <button class="code-copy" onclick="navigator.clipboard.writeText(this.closest('.code-block').querySelector('code').innerText)">Copy</button>
        </div>
        <pre><code class="hljs language-${language}">${highlighted}</code></pre>
      </div>`;
    }
  } as any,   // ← cast needed: marked's RendererObject types lag behind the v12 runtime API
  gfm: true,
  breaks: true,
});

@Pipe({ name: 'markdown', standalone: true, pure: true })
export class MarkdownPipe implements PipeTransform {
  transform(value: string): string {
    if (!value) return '';
    return marked.parse(value) as string;
  }
}
