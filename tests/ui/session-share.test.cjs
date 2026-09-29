// Run with: node --test tests/ui/session-share.test.cjs
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = fs.readFileSync(path.join(__dirname, '../../src/BdoGrindTracker.App/wwwroot/session-share.js'), 'utf8');
const PNG = 'data:image/png;base64,iVBORw0KGgo=';

function setup(options = {}) {
    const texts = [], rectangles = [], draws = [], requested = [], anchors = [], timers = new Map(), clipboard = [];
    let nextTimer = 0;
    const context = {
        font: '20px sans-serif', textAlign: 'left', textBaseline: 'top', fillStyle: '',
        measureText(value) { return { width: Array.from(value).length * Number(this.font.match(/(\d+)px/u)?.[1] || 20) * .52 }; },
        fillText(value, x, y) {
            const width = this.measureText(value).width;
            texts.push({ value, x, y, width, align: this.textAlign,
                left: this.textAlign === 'right' ? x - width : x, size: Number(this.font.match(/(\d+)px/u)?.[1] || 20) });
        },
        fillRect(...args) { rectangles.push(args); },
        drawImage(...args) { draws.push(args); },
        createLinearGradient() { return { addColorStop() {} }; },
        beginPath() {}, closePath() {}, moveTo() {}, lineTo() {}, quadraticCurveTo() {}, fill() {}, clip() {}, save() {}, restore() {}
    };
    const canvas = { width: 300, height: 150, getContext: () => options.noContext ? null : context,
        toDataURL(type) { assert.equal(type, 'image/png'); if (options.exportError) throw options.exportError; return options.dataUrl ?? PNG; } };
    const document = {
        baseURI: 'https://grindcrest.local/', body: { appendChild(anchor) { anchor.appended = true; } },
        createElement(tag) {
            if (tag === 'canvas') return canvas;
            assert.equal(tag, 'a');
            const anchor = { style: {}, click() { this.clicked = true; }, remove() { this.removed = true; } };
            anchors.push(anchor); return anchor;
        }
    };
    const images = [];
    class Image {
        naturalWidth = 600;
        naturalHeight = 400;
        constructor() { images.push(this); }
        set src(value) {
            this.url = value;
            if (!value) return;
            requested.push(value);
            if (!options.pendingImages) Promise.resolve().then(() => options.failedImages ? this.onerror?.() : this.onload?.());
        }
    }
    const window = { location: { origin: 'https://grindcrest.local', href: 'https://grindcrest.local/' } };
    const navigator = options.noClipboard ? {} : { clipboard: { async write(items) {
        if (options.clipboardError) throw options.clipboardError;
        clipboard.push(...items);
    } } };
    class ClipboardItem { constructor(items) { this.items = items; } }
    vm.runInNewContext(script, { window, document, navigator, Image, URL, Blob, Uint8Array, ClipboardItem,
        atob: value => Buffer.from(value, 'base64').toString('binary'),
        setTimeout(callback, ms) { const id = ++nextTimer; timers.set(id, { callback, ms }); return id; },
        clearTimeout(id) { timers.delete(id); } });
    return { api: window.grindcrestSessionShare, canvas, texts, rectangles, draws, requested, anchors, clipboard, images, timers };
}

function session(overrides = {}) {
    return { title: 'Gyfin Rhasia Temple', subtitle: '25.09.2026 · 20:15 – 21:17', status: 'Abgeschlossen',
        metrics: [{ label: 'Aktive Zeit', value: '1:02:00' },
            { label: 'Trashloot', value: '31.987', note: '30.955 / h' },
            { label: 'Silber', value: '1,42 Mrd.' },
            { label: 'Silber / h', value: '1,37 Mrd.' }],
        details: [{ label: 'Klasse', value: 'Dark Knight · Awakening' }, { label: 'Ausrüstung', value: '330 AP / 420 DP' }],
        lootHeading: 'Loot', consumablesHeading: 'Verbrauchte Buffs', quantityLabel: 'Menge', hourlyLabel: '/ Stunde',
        loot: [{ name: 'Trash item', quantity: '31.987', hourly: '30.955' }, { name: 'Black Stone', quantity: '150', hourly: '145' }],
        consumables: [{ name: 'Simple Cron Meal', quantity: '1' }],
        footer: 'Session-Momentaufnahme · EU', ...overrides };
}

function assertInsideCanvas(result) {
    for (const text of result.texts) {
        assert.ok(text.left >= 0, `${text.value} starts outside the canvas`);
        assert.ok(text.left + text.width <= result.canvas.width, `${text.value} ends outside the canvas`);
        assert.ok(text.y >= 0 && text.y + text.size <= result.canvas.height, `${text.value} is vertically clipped`);
    }
}

test('renders a compact 1200px PNG with the four metrics, details, loot and buffs but no status badge', async () => {
    const result = setup();
    assert.equal(await result.api.render(session()), PNG);
    assert.equal(result.canvas.width, 1200);
    assert.ok(result.canvas.height > 700 && result.canvas.height < 1200);
    const values = result.texts.map(text => text.value);
    for (const value of ['Gyfin Rhasia Temple', '1:02:00', '31.987', '1,42 Mrd.', '1,37 Mrd.',
        'Dark Knight · Awakening', '330 AP / 420 DP', 'Black Stone', 'Simple Cron Meal', 'Session-Momentaufnahme · EU'])
        assert.ok(values.includes(value), `Missing ${value}`);
    assert.ok(!values.includes('Abgeschlossen'));
    const metricLabels = ['Aktive Zeit', 'Trashloot', 'Silber', 'Silber / h'].map(value => result.texts.find(text => text.value === value));
    assert.equal(new Set(metricLabels.map(text => text.y)).size, 1);
    assert.equal(new Set(metricLabels.map(text => text.x)).size, 4);
    assertInsideCanvas(result);
});

test('all rows survive the compact two-column layout and the final footer follows every row', async () => {
    const result = setup();
    const loot = Array.from({ length: 81 }, (_, index) => ({ name: `Loot-item-${index}`, quantity: String(index + 10), hourly: String(index + 20) }));
    await result.api.render(session({ loot, consumables: [] }));
    const rows = result.texts.filter(text => text.value.startsWith('Loot-item-'));
    assert.equal(rows.length, 81);
    assert.equal(new Set(rows.map(text => text.value)).size, 81);
    assert.equal(new Set(rows.map(text => text.x)).size, 2);
    assert.ok(result.canvas.height < 4000);
    const footer = result.texts.find(text => text.value === 'Session-Momentaufnahme · EU');
    assert.ok(footer.y > Math.max(...rows.map(text => text.y)));
    assertInsideCanvas(result);
});

test('long item names, titles and unbroken values wrap without clipping or overlapping the next row', async () => {
    const result = setup();
    const longName = 'LegendaryTreasures'.repeat(15) + ' 🗡️';
    const longQuantity = '9876543210'.repeat(5);
    await result.api.render(session({ title: 'Temple of the Great Ancient Forgotten Guardians of the Deep Darkness',
        loot: [{ name: longName, quantity: longQuantity, hourly: '54321' },
            { name: 'NEIGHBOR-CARD', quantity: '1' }, { name: 'NEXT-ROW', quantity: '1' }] }));
    const firstRowFragments = result.texts.filter(text => text.value.includes('Legend') || text.value.includes('Treas') || text.value.includes('ures'));
    assert.ok(firstRowFragments.length > 1);
    const nextRow = result.texts.find(text => text.value === 'NEXT-ROW');
    assert.ok(nextRow.y > Math.max(...firstRowFragments.map(text => text.y + text.size)));
    const quantities = result.texts.filter(text => /^[0-9]+$/u.test(text.value) && text.value.length > 5);
    assert.ok(quantities.length > 1);
    assert.ok(quantities.every(text => text.align === 'right'));
    assertInsideCanvas(result);
});

test('an ongoing session is snapshotted before artwork loading begins', async () => {
    const result = setup({ pendingImages: true });
    const input = session({ backgroundUrl: '/spots/gyfin.jpg', iconUrl: '/classes/dk.png' });
    const rendered = result.api.render(input);
    input.title = 'NEW SPOT'; input.metrics[0].value = 'UPDATED TIME'; input.loot[0].name = 'NEW LOOT';
    for (const image of result.images) image.onload();
    await rendered;
    const values = result.texts.map(text => text.value);
    assert.ok(values.includes('Gyfin Rhasia Temple'));
    assert.ok(values.includes('1:02:00'));
    assert.ok(values.includes('Trash item'));
    assert.ok(!values.some(value => /NEW SPOT|UPDATED TIME|NEW LOOT/u.test(value)));
    assert.equal(result.timers.size, 0);
});

test('only same-origin assets are requested, with deduplication and graceful image failures', async () => {
    const result = setup({ failedImages: true });
    await result.api.render(session({ backgroundUrl: 'https://external.example/track.jpg', iconUrl: 'data:image/png;base64,aaaa',
        loot: [{ name: 'A', quantity: '1', iconUrl: '/icons/a.png' }, { name: 'B', quantity: '2', iconUrl: '/icons/a.png' },
            { name: 'C', quantity: '3', iconUrl: '//external.example/c.png' }, { name: 'D', quantity: '4', iconUrl: 'javascript:alert(1)' }] }));
    assert.deepEqual(result.requested, ['https://grindcrest.local/icons/a.png']);
    assert.equal(result.draws.length, 0);
    assert.equal(result.timers.size, 0);
    assert.ok(result.texts.some(text => text.value === 'A'));
});

test('stalled image requests have a bounded timeout and still produce the complete card', async () => {
    const result = setup({ pendingImages: true });
    const rendered = result.api.render(session({ backgroundUrl: '/never-finishes.png' }));
    assert.equal(result.timers.size, 1);
    for (const timer of [...result.timers.values()]) { assert.ok(timer.ms <= 3000); timer.callback(); }
    assert.equal(await rendered, PNG);
    assert.equal(result.timers.size, 0);
    assert.ok(result.texts.some(text => text.value === 'Black Stone'));
});

test('oversized sessions fail explicitly before exporting or requesting artwork', async () => {
    const result = setup();
    const loot = Array.from({ length: 600 }, (_, index) => ({ name: `Item-${index}`, quantity: '1' }));
    await assert.rejects(result.api.render(session({ loot, backgroundUrl: '/spot.png' })), /too much text or too many items/u);
    assert.equal(result.requested.length, 0);
    assert.equal(result.texts.length, 0);
});

test('PNG export failures and canvas unavailability are surfaced to the caller', async () => {
    await assert.rejects(setup({ noContext: true }).api.render(session()), /cannot render/u);
    await assert.rejects(setup({ dataUrl: 'data:,' }).api.render(session()), /valid PNG/u);
    const denied = new Error('tainted canvas');
    await assert.rejects(setup({ exportError: denied }).api.render(session()), error => error === denied);
});

test('download exports the prepared PNG with a safe filename and cleans up its anchor', () => {
    const result = setup();
    result.api.download(PNG, 'session:gyfin/2026');
    assert.equal(result.anchors[0].download, 'session-gyfin-2026.png');
    assert.equal(result.anchors[0].href, PNG);
    assert.equal(result.anchors[0].appended, true);
    assert.equal(result.anchors[0].clicked, true);
    assert.equal(result.anchors[0].removed, true);
    assert.throws(() => result.api.download('https://external.example/image.png', 'bad'), /valid PNG/u);
});

test('copy writes PNG bytes and reports unsupported or denied clipboard access', async () => {
    const result = setup();
    await result.api.copy(PNG);
    assert.equal(result.clipboard.length, 1);
    const blob = result.clipboard[0].items['image/png'];
    assert.equal(blob.type, 'image/png');
    assert.deepEqual(Buffer.from(await blob.arrayBuffer()), Buffer.from('iVBORw0KGgo=', 'base64'));
    await assert.rejects(setup({ noClipboard: true }).api.copy(PNG), /unavailable/u);
    const denied = new Error('Clipboard permission denied');
    await assert.rejects(setup({ clipboardError: denied }).api.copy(PNG), error => error === denied);
});
