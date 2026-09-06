import test from 'node:test';
import assert from 'node:assert/strict';
import { activeSeconds, currentSessions, endpointFromConfig, formatDuration, formatSilver, hourlySilver, parseSessions } from '../model.js';
const now = Date.parse('2026-09-06T12:00:00Z');
const row = {
  updatedAt: new Date(now).toISOString(), expiresAt: new Date(now + 90_000).toISOString(),
  session: { sessionId: 'test-id', displayName: 'Test', spotName: null, characterClass: null, region: 'EU',
    activeSeconds: 120, startedAt: new Date(now - 120_000).toISOString(), paused: false, silverAfterTax: 1000,
    silverIsPartial: false, pricesAreStale: false, loot: { 'Black Stone': 8 } }
};
test('expiry removes a session at exactly 90 seconds even without a successful refresh', () => {
  assert.equal(currentSessions([row], now + 89_999).length, 1);
  assert.equal(currentSessions([row], now + 90_000).length, 0);
});
test('active clock advances; paused clock remains frozen', () => {
  assert.equal(activeSeconds(row, now + 5000), 125);
  assert.equal(activeSeconds({ ...row, session: { ...row.session, paused: true } }, now + 5000), 120);
});
test('silver rate uses the snapshot duration; zero time and unknown value stay unknown', () => {
  assert.equal(hourlySilver(row), 30000);
  assert.equal(hourlySilver({ ...row, session: { ...row.session, activeSeconds: 0 } }), null);
  assert.equal(hourlySilver({ ...row, session: { ...row.session, silverAfterTax: null } }), null);
  assert.equal(formatSilver(null), '—');
  assert.ok(formatSilver(1000, true).startsWith('≥ '));
});
test('duration preserves hours beyond a day', () => assert.equal(formatDuration(90061), '25:01:01'));
test('API paths work under a prefix and reject insecure and credential-bearing URLs', () => {
  assert.equal(endpointFromConfig({ apiBaseUrl: 'https://example.com/live/' }), 'https://example.com/live/api/v1/sessions');
  for (const apiBaseUrl of ['', 'http://example.com', 'https://user:secret@example.com', 'https://example.com/?token=secret'])
    assert.throws(() => endpointFromConfig({ apiBaseUrl }));
});
test('valid response accepted; malformed, duplicate and non-numeric session data rejected', () => {
  const payload = { serverTime: new Date(now).toISOString(), sessions: [row] };
  assert.equal(parseSessions(payload), payload);
  assert.throws(() => parseSessions({ ...payload, sessions: [row, row] }));
  assert.throws(() => parseSessions({ ...payload, sessions: [{ ...row, session: { ...row.session, loot: null } }] }));
  assert.throws(() => parseSessions({ ...payload, sessions: [{ ...row, session: { ...row.session, activeSeconds: 'NaN' } }] }));
});
test('active sessions appear before pauses', () => {
  const paused = { ...row, session: { ...row.session, sessionId: 'paused', paused: true } };
  assert.equal(currentSessions([paused, row], now)[0].session.sessionId, 'test-id');
});
