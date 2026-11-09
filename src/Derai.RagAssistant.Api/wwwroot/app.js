import { renderAssistantMarkdown } from './markdown-renderer.mjs';
const $ = selector => document.querySelector(selector);
let token = '', language = 'es', conversation = crypto.randomUUID();
const role = $('#role'), messages = $('#messages'), sources = $('#sources');
const suggestions = {
  es: ['\u00bfCu\u00e1nto vendi\u00f3 en marzo?', '\u00bfQu\u00e9 facturas est\u00e1n pendientes?', '\u00bfCu\u00e1l fue el resultado operativo?', '\u00bfC\u00f3mo tramito una reclamaci\u00f3n?'],
  en: ['How much did it sell in March?', 'Which invoices are unpaid?', 'What was the operating result?', 'How do I file a claim?']
};
function applyLanguage() {
  const en = language === 'en'; document.documentElement.lang = language;
  $('.brand').childNodes[0].textContent = en ? 'SAMPLE COMPANY' : 'EMPRESA DE MUESTRA';
  $('.brand span').textContent = en ? ' · ASSISTANT' : ' · ASISTENTE';
  role.options[0].text = en ? 'Support' : 'Soporte'; role.options[1].text = en ? 'Operations' : 'Operaciones'; role.options[2].text = en ? 'Management' : 'Dirección';
  $('.secure').innerHTML = en ? '&#9679; ACL enabled' : '&#9679; ACL activa';
  $('#hidden').textContent = en ? 'Some documents are restricted for this role.' : 'Algunos documentos están restringidos para este rol.';
  $('footer').textContent = en ? 'Demo · Nordia Logistics is fictional · All financial data is invented' : 'Demo · Nordia Logística es una empresa ficticia · Los datos financieros son inventados';
  $('.eyebrow').textContent = en ? 'FICTIONAL COMPANY ASSISTANT' : 'ASISTENTE DE EMPRESA FICTICIA';
  $('.hero h1').innerHTML = en ? 'Useful knowledge,<br><em>answers with sources.</em>' : 'Conocimiento \u00fatil,<br><em>respuestas con fuentes.</em>';
  $('.hero p').textContent = en ? 'Explore a fictional logistics company with sample documents and role-based access.' : 'Explora un ejemplo ficticio de log\u00edstica, con documentos de muestra y permisos por usuario.';
  $('.knowledge-label').innerHTML = en ? 'Knowledge base: <strong>Nordia Logistics (fictional company)</strong>' : 'Base de conocimiento: <strong>Nordia Log\u00edstica (empresa ficticia)</strong>';
  $('label[for="role"]').textContent = en ? 'Ask as' : 'Consultar como';
  $('#question').placeholder = en ? 'Ask about invoices, sales, or operations\u2026' : 'Pregunta sobre facturas, ventas o procesos\u2026';
  $('#form button').textContent = en ? 'Send \u2197' : 'Enviar \u2197';
  $('.welcome').textContent = en ? 'Hello. This is a fictional Nordia Logistics example. What would you like to look up?' : 'Hola. Este es un ejemplo ficticio de Nordia Log\u00edstica. \u00bfQu\u00e9 quieres consultar?';
  $('.asidehead span').textContent = en ? 'SOURCES' : 'FUENTES';
  $('.muted').textContent = en ? 'Sources will appear here with each answer.' : 'Las fuentes aparecer\u00e1n aqu\u00ed con cada respuesta.';
  $('.quality').textContent = en ? 'View retrieval quality \u2192' : 'Ver calidad de b\u00fasqueda \u2192';
  $('#quality h2').textContent = en ? 'Retrieval quality' : 'Calidad de recuperaci\u00f3n';
  $('#quality>p').textContent = en ? 'Run the deterministic evaluation.' : 'Ejecuta la evaluaci\u00f3n determinista.';
  $('#runEval').textContent = en ? 'Run evaluation' : 'Ejecutar evaluaci\u00f3n';
  $('#language').textContent = en ? 'ES' : 'EN';
  $('#suggestions').replaceChildren(...suggestions[language].map(question => { const button = document.createElement('button'); button.type = 'button'; button.textContent = question; button.onclick = () => { $('#question').value = question; $('#form').requestSubmit(); }; return button; }));
}
async function authenticate() { const response = await fetch('/api/v1/auth/demo', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ role: role.value }) }); token = (await response.json()).token; }
role.onchange = authenticate;
$('#language').onclick = () => { language = language === 'es' ? 'en' : 'es'; applyLanguage(); };
$('#theme').onclick = () => document.body.classList.toggle('dark');
applyLanguage(); authenticate();
$('#form').onsubmit = async event => {
  event.preventDefault(); const question = $('#question').value.trim(); if (!question) return;
  $('#question').value = ''; const user = document.createElement('div'); user.className = 'bubble user'; user.textContent = question; messages.append(user);
  const answer = document.createElement('div'); answer.className = 'bubble assistant'; messages.append(answer); sources.replaceChildren(); $('#sourcecount').textContent = '0';
  try {
    const response = await fetch('/api/v1/chat?corpus=nordia', { method: 'POST', headers: { 'content-type': 'application/json', authorization: `Bearer ${token}` }, body: JSON.stringify({ question, conversationId: conversation, language }) });
    if (!response.ok) throw new Error('Request failed'); const reader = response.body.getReader(), decoder = new TextDecoder(); let buffer = '';
    while (true) { const { value, done } = await reader.read(); if (done) break; buffer += decoder.decode(value, { stream: true }); const events = buffer.split('\n\n'); buffer = events.pop();
      for (const eventText of events) { const lines = eventText.split('\n'), name = lines.find(line => line.startsWith('event:'))?.slice(6).trim(), data = lines.find(line => line.startsWith('data:'))?.slice(5).trim();
        if (name === 'token') { answer.dataset.answer = (answer.dataset.answer || '') + JSON.parse(data); answer.innerHTML = renderAssistantMarkdown(answer.dataset.answer); }
        if (name === 'sources') { const items = JSON.parse(data); $('#sourcecount').textContent = items.length; for (const source of items) { const card = document.createElement('div'); card.className = 'source'; card.id = `source-${source.id}`; card.innerHTML = `<strong>[${source.id}] ${escapeHtml(source.file)}</strong><small>${escapeHtml(source.section)} ? ${source.language.toUpperCase()} \u00b7 ${source.score}<br>${escapeHtml(source.excerpt)}</small>`; sources.append(card); } }
      }
    }
  } catch { answer.textContent = language === 'en' ? 'The request could not be completed. Please try again.' : 'No se pudo completar la consulta. Int\u00e9ntalo de nuevo.'; }
};
messages.onclick = event => { const citation = event.target.closest('.citation'); if (!citation) return; event.preventDefault(); const target = $(citation.getAttribute('href')); if (target) { target.scrollIntoView({ behavior: 'smooth', block: 'center' }); target.classList.add('highlight'); setTimeout(() => target.classList.remove('highlight'), 1400); } };
$('#eval').onclick = () => $('#quality').hidden = !$('#quality').hidden;
$('#runEval').onclick = async () => { const result = await (await fetch('/api/v1/eval?corpus=nordia')).json(); $('#evalresult').textContent = `${language === 'en' ? 'Hit-rate@5' : 'Acierto@5'}: ${Math.round(result.score * 100)}% (${result.hits}/${result.total})`; $('#evalrows').replaceChildren(...result.cases.map(item => { const row = document.createElement('p'); row.textContent = `${item.hit ? '\u2713' : '\u00b7'} ${item.question} \u2014 ${item.expected}`; return row; })); };
function escapeHtml(value) { return value.replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]); }
