import { POLL_MS, formatNumber, formatSilver, formatDuration, activeSeconds, hourlySilver,
  currentSessions, parseSessions, endpointFromConfig } from './model.js';

const byId = id => document.getElementById(id);
const list = byId('session-list');
let rows = [], endpoint, receivedServerTime = 0, receivedAt = 0, busy = false, loaded = false;
let lastRefresh = null;
const cardNodes = new Map();
const serverNow = () => receivedServerTime + (performance.now() - receivedAt);
const el = (tag, className, text) => {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
};

function createCard(row) {
  const s = row.session;
  const details = el('details', 'session-row');
  details.dataset.id = s.sessionId;
  const summary = el('summary', 'session-summary');
  const player = el('div');
  player.append(el('span', 'player-name', s.displayName), el('span', 'secondary', s.characterClass ?? 'Klasse noch unbekannt'));
  const spot = el('div');
  spot.append(el('span', 'spot-name', s.spotName ?? 'Spot wird erkannt'));
  const state = el('div', 'secondary');
  state.append(el('span', `status-pill${s.paused ? ' paused' : ''}`, s.paused ? 'Ⅱ Pausiert' : '● Aktiv'), el('span', 'region', s.region));
  spot.append(state);
  const duration = el('div', 'duration-cell');
  duration.append(el('span', 'mobile-label', 'AKTIVE ZEIT'), el('span', 'data-value duration', formatDuration(activeSeconds(row, serverNow()))));
  const silver = el('div');
  silver.append(el('span', 'mobile-label', 'SILBER / H'), el('span', 'data-value silver-value', formatSilver(hourlySilver(row), s.silverIsPartial)));
  const chevron = el('span', 'chevron', '›');
  chevron.setAttribute('aria-hidden', 'true');
  summary.append(player, spot, duration, silver, chevron);
  const detail = el('div', 'session-detail');
  const total = el('div', 'detail-summary');
  total.append(el('h3', '', 'Silber gesamt · nach Steuer'), el('strong', '', s.silverAfterTax === null ? '—' : `${s.silverIsPartial ? '≥ ' : ''}${formatNumber(s.silverAfterTax)}`));
  if (s.silverIsPartial) total.append(el('p', '', 'Teilsumme: Für einige Items fehlen Preise.'));
  if (s.pricesAreStale) total.append(el('p', '', 'Bewertung mit zwischengespeicherten Marktpreisen.'));
  total.append(el('p', '', `Gestartet ${new Date(s.startedAt).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' })}`));
  const loot = el('div');
  const items = Object.entries(s.loot).sort((a, b) => a[0].localeCompare(b[0]));
  if (items.length) {
    const table = el('table', 'loot-table');
    const caption = el('caption', 'sr-only', `Loot von ${s.displayName}`);
    table.append(caption);
    const head = el('thead'); const headRow = el('tr');
    for (const label of ['Session-Loot', 'Menge']) { const th = el('th', '', label); th.scope = 'col'; headRow.append(th); }
    head.append(headRow); table.append(head);
    const body = el('tbody');
    for (const [name, quantity] of items) { const tr = el('tr'); tr.append(el('td', '', name), el('td', '', formatNumber(quantity))); body.append(tr); }
    table.append(body); loot.append(table);
  } else loot.append(el('p', 'loot-empty', 'Noch kein Loot erfasst.'));
  detail.append(total, loot);
  details.dataset.paused = String(s.paused);
  details.append(summary, detail);
  return details;
}

function render() {
  if (!loaded) return;
  const visible = currentSessions(rows, serverNow());
  const ids = new Set(visible.map(r => r.session.sessionId));
  for (const [id, entry] of cardNodes) if (!ids.has(id)) { entry.node.remove(); cardNodes.delete(id); }
  for (const row of visible) {
    const id = row.session.sessionId;
    let entry = cardNodes.get(id);
    if (!entry || entry.row !== row) {
      const next = createCard(row);
      if (entry) {
        next.open = entry.node.open;
        const hadFocus = document.activeElement === entry.node.querySelector('summary');
        entry.node.replaceWith(next);
        if (hadFocus) next.querySelector('summary').focus({ preventScroll: true });
      }
      entry = { row, node: next }; cardNodes.set(id, entry);
    }
    entry.node.querySelector('.duration').textContent = formatDuration(activeSeconds(row, serverNow()));
  }
  // Move only rows whose position changed, preserving focus and open loot details.
  visible.forEach((row, index) => {
    const node = cardNodes.get(row.session.sessionId).node;
    if (list.children[index] !== node) list.insertBefore(node, list.children[index] ?? null);
  });
  byId('active-count').textContent = visible.filter(r => !r.session.paused).length;
  byId('paused-count').textContent = visible.filter(r => r.session.paused).length;
  byId('spot-count').textContent = new Set(visible.map(r => r.session.spotName).filter(Boolean)).size;
  byId('empty-state').hidden = visible.length > 0;
  byId('empty-title').textContent = 'Gerade keine geteilten Sessions';
  byId('empty-copy').textContent = 'Sobald jemand eine Session freigibt, erscheint sie hier automatisch.';
}

async function refresh() {
  if (busy || !endpoint) return;
  busy = true;
  byId('refresh').disabled = true;
  list.setAttribute('aria-busy', 'true');
  try {
    const response = await fetch(endpoint, { cache: 'no-store', credentials: 'omit', signal: AbortSignal.timeout(8000) });
    if (!response.ok) throw new Error('API nicht erreichbar');
    const result = parseSessions(await response.json());
    rows = result.sessions;
    receivedServerTime = Date.parse(result.serverTime);
    receivedAt = performance.now();
    loaded = true;
    lastRefresh = new Date();
    byId('connection-status').textContent = '● Mit der Community verbunden';
    byId('connection-status').dataset.state = 'connected';
    byId('last-refresh').textContent = `Stand ${lastRefresh.toLocaleTimeString('de-DE')} · alle 15 Sekunden`;
    byId('notice').hidden = true;
    render();
  } catch {
    byId('connection-status').textContent = 'Verbindung unterbrochen';
    byId('connection-status').dataset.state = 'offline';
    byId('notice').textContent = loaded
      ? 'Neue Daten sind gerade nicht erreichbar. Der letzte Stand bleibt bis zum Ablauf der Sessions sichtbar. Wir versuchen es automatisch erneut.'
      : 'Die Sessions konnten nicht geladen werden. Wir versuchen es automatisch erneut.';
    byId('notice').hidden = false;
    if (!loaded) {
      byId('empty-title').textContent = 'Sessions gerade nicht erreichbar';
      byId('empty-copy').textContent = 'Bitte versuche es in einem Moment erneut.';
    }
  } finally {
    busy = false;
    list.setAttribute('aria-busy', 'false');
    byId('refresh').disabled = false;
  }
}

byId('refresh').addEventListener('click', () => endpoint ? refresh() : initialize());
document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
async function initialize() {
  try {
    const response = await fetch('./config.json', { cache: 'no-store', signal: AbortSignal.timeout(8000) });
    if (!response.ok) throw new Error('Konfiguration nicht erreichbar');
    endpoint = endpointFromConfig(await response.json());
    await refresh();
  } catch {
    byId('connection-status').textContent = 'Live-Verbindung noch nicht eingerichtet';
    byId('empty-title').textContent = 'Die Live-Übersicht wird vorbereitet';
    byId('empty-copy').textContent = 'Sobald die Verbindung bereit ist, findest du hier die geteilten Sessions.';
    list.setAttribute('aria-busy', 'false');
  }
}
setInterval(() => { if (!document.hidden) endpoint ? refresh() : initialize(); }, POLL_MS);
setInterval(render, 1000);
initialize();
