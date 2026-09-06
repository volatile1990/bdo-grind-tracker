export const POLL_MS = 15_000;
const numbers = new Intl.NumberFormat('de-DE', { maximumFractionDigits: 0 });
const compact = new Intl.NumberFormat('de-DE', { notation: 'compact', maximumFractionDigits: 2 });
export const formatNumber = value => numbers.format(value);
export const formatSilver = (value, partial = false) => value === null ? '—' : `${partial ? '≥ ' : ''}${compact.format(value)}`;
export function formatDuration(seconds) {
  const whole = Math.max(0, Math.floor(seconds));
  return [Math.floor(whole / 3600), Math.floor(whole / 60) % 60, whole % 60].map(n => String(n).padStart(2, '0')).join(':');
}
export function activeSeconds(row, now) {
  const delta = row.session.paused ? 0 : Math.max(0, (now - Date.parse(row.updatedAt)) / 1000);
  return row.session.activeSeconds + delta;
}
export function hourlySilver(row) {
  // Keep the rate based on the same snapshot as its loot; do not make it fall between updates.
  const { silverAfterTax, activeSeconds: seconds } = row.session;
  return silverAfterTax === null || seconds <= 0 ? null : silverAfterTax * 3600 / seconds;
}
export function currentSessions(rows, now) {
  return rows.filter(row => Date.parse(row.expiresAt) > now)
    .sort((a, b) => Number(a.session.paused) - Number(b.session.paused) || Date.parse(b.session.startedAt) - Date.parse(a.session.startedAt));
}
export function parseSessions(payload) {
  if (!payload || !Number.isFinite(Date.parse(payload.serverTime)) || !Array.isArray(payload.sessions)) throw new Error('Ungültige Antwort');
  const ids = new Set();
  for (const row of payload.sessions) {
    const s = row?.session;
    if (!s || typeof s.sessionId !== 'string' || ids.has(s.sessionId) ||
        typeof s.displayName !== 'string' || !s.displayName.trim() || s.displayName.length > 40 ||
        ![s.spotName, s.characterClass].every(v => v === null || typeof v === 'string') ||
        !['EU', 'NA'].includes(s.region) || typeof s.paused !== 'boolean' ||
        typeof s.silverIsPartial !== 'boolean' || typeof s.pricesAreStale !== 'boolean' ||
        !Number.isSafeInteger(s.activeSeconds) || s.activeSeconds < 0 ||
        !(s.silverAfterTax === null || (Number.isFinite(s.silverAfterTax) && s.silverAfterTax >= 0)) ||
        ![s.startedAt, row.updatedAt, row.expiresAt].every(v => Number.isFinite(Date.parse(v))) ||
        !s.loot || typeof s.loot !== 'object' || Array.isArray(s.loot) ||
        Object.entries(s.loot).some(([name, quantity]) => !name || !Number.isSafeInteger(quantity) || quantity <= 0))
      throw new Error('Ungültige Sessiondaten');
    ids.add(s.sessionId);
  }
  return payload;
}
export function endpointFromConfig(config) {
  if (!config || typeof config.apiBaseUrl !== 'string' || !config.apiBaseUrl.trim()) throw new Error('API nicht eingerichtet');
  const url = new URL(config.apiBaseUrl);
  if (url.username || url.password || url.search || url.hash ||
      (url.protocol !== 'https:' && !(url.protocol === 'http:' && ['localhost', '127.0.0.1'].includes(url.hostname))))
    throw new Error('Ungültige API-Adresse');
  return new URL('api/v1/sessions', `${url.href.replace(/\/$/, '')}/`).href;
}
