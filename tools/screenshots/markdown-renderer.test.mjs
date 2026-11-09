import assert from 'node:assert/strict';
import test from 'node:test';
import { renderAssistantMarkdown } from '../../src/Derai.RagAssistant.Api/wwwroot/markdown-renderer.mjs';

test('renders paragraphs, lists, bold, and clickable citation chips', () => {
  const html = renderAssistantMarkdown('Direct answer [1].\n\n- **First step** [2]\n- Second step');
  assert.match(html, /<p>Direct answer <a href="#source-1" class="citation"/);
  assert.match(html, /<ul><li><strong>First step<\/strong>/);
  assert.match(html, /href="#source-2"/);
});

test('escapes malicious HTML before applying Markdown formatting', () => {
  const html = renderAssistantMarkdown('<img src=x onerror="alert(1)"> **safe** [1]');
  assert.match(html, /&lt;img src=x onerror=&quot;alert\(1\)&quot;&gt;/);
  assert.match(html, /<strong>safe<\/strong>/);
  assert.doesNotMatch(html, /<img\b[^>]*onerror=/i);
});

