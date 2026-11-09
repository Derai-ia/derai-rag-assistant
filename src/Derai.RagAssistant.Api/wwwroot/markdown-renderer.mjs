const escapeHtml = value => String(value).replace(/[&<>"']/g, character => ({
  '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
})[character]);

function renderInline(value) {
  let safe = escapeHtml(value).replace(/`([^`]+)`/g, '$1');
  safe = safe.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
  safe = safe.replace(/\[(\d+)\]/g, '<a href="#source-$1" class="citation" aria-label="Source $1">[$1]</a>');
  return safe;
}

export function renderAssistantMarkdown(value) {
  const normalized = String(value).replace(/\[([^\]]+)\]\([^)]*\)/g, '$1').replace(/\r\n?/g, '\n');
  const blocks = normalized.split(/\n\s*\n/).map(block => block.trim()).filter(Boolean);
  return blocks.map(block => {
    const lines = block.split('\n').map(line => line.trim()).filter(Boolean);
    if (lines.length && lines.every(line => /^[-*+]\s+/.test(line))) {
      return `<ul>${lines.map(line => `<li>${renderInline(line.replace(/^[-*+]\s+/, ''))}</li>`).join('')}</ul>`;
    }
    return `<p>${lines.map(renderInline).join('<br>')}</p>`;
  }).join('');
}
