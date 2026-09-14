// Run with: node --test tests/ui/overlay-editor-interactions.test.cjs
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = fs.readFileSync(path.join(__dirname, '../../src/BdoGrindTracker.App/wwwroot/overlay-editor.js'), 'utf8');

function classList() {
    const values = new Set();
    return {
        add(...names) { names.forEach(name => values.add(name)); },
        remove(...names) { names.forEach(name => values.delete(name)); },
        toggle(name, enabled) { if (enabled) values.add(name); else values.delete(name); },
        contains(name) { return values.has(name); }
    };
}

function setup({ chrome = true, snap = false, corner = 'se', widgets: widgetLayouts = [] } = {}) {
    const width = 400, height = 200;
    const chromeX = chrome ? 4 : 0, chromeY = chrome ? 34 : 0;
    const chromeLeft = chrome ? 2 : 0, chromeTop = chrome ? 32 : 0;
    const listeners = new Map(), calls = [], observers = [];
    const stage = {
        dataset: { overlayId: 'overlay-1', width: String(width), height: String(height), snap: String(snap),
            ...(chrome ? { chromeX: String(chromeX), chromeY: String(chromeY) } : {}) },
        style: { width: `${width + chromeX}px`, height: `${height + chromeY}px` }, classList: classList(),
        querySelector(selector) { return selector === '.oe-stage-content' ? content : null; },
        querySelectorAll(selector) { return selector === '.oe-widget' ? widgets : []; },
        contains(element) { return widgets.includes(element); },
        getBoundingClientRect() {
            const scale = Number(this.style.transform?.match(/scale\(([^)]+)\)/)?.[1] ?? 1);
            const width = parseFloat(this.style.width) * scale, height = parseFloat(this.style.height) * scale;
            const { left, top } = wrap.getBoundingClientRect();
            return { left, top, right: left + width, bottom: top + height, width, height, scale };
        }
    };
    const content = {
        getBoundingClientRect() {
            const rect = stage.getBoundingClientRect();
            const left = rect.left + chromeLeft * rect.scale, top = rect.top + chromeTop * rect.scale;
            return { left, top, right: left + (parseFloat(stage.style.width) - chromeX) * rect.scale,
                bottom: top + (parseFloat(stage.style.height) - chromeY) * rect.scale };
        }
    };
    const wrap = {
        style: {},
        getBoundingClientRect() {
            const width = parseFloat(this.style.width), height = parseFloat(this.style.height);
            // CSS margin: 0 auto recenters the wrapper whenever its layout
            // width changes; relative left/top then apply the drag correction.
            const left = 96 + Math.max(0, (viewport.clientWidth - width) / 2) + (parseFloat(this.style.left) || 0);
            const top = 50 + (parseFloat(this.style.top) || 0);
            return { left, top, right: left + width, bottom: top + height, width, height };
        }
    };
    // Exactly half-size on screen: fit must include both side borders.
    const viewport = { clientWidth: (width + chromeX) / 2 + 8 };
    const capture = selector => ({
        dataset: {}, disabled: false, captured: new Set(),
        closest(current) { return current === selector ? this : null; },
        matches() { return false; }, focus() {}, querySelector() { return null; },
        setPointerCapture(id) { this.captured.add(id); },
        hasPointerCapture(id) { return this.captured.has(id); },
        releasePointerCapture(id) { this.captured.delete(id); }
    });
    const module = capture('[data-module-kind]');
    module.dataset.moduleKind = 'duration';
    module.querySelector = selector => selector === 'strong' ? { textContent: 'Zeit' } : null;
    const canvasHandles = Object.fromEntries(['nw', 'ne', 'sw', 'se'].map(corner => {
        const handle = capture('[data-canvas-resize]');
        handle.dataset.canvasResize = corner;
        return [corner, handle];
    }));
    const canvasHandle = canvasHandles[corner];
    const widgets = widgetLayouts.map((layout, index) => ({
        dataset: { widgetId: `widget-${index}`, ...Object.fromEntries(Object.entries(layout).map(([name, value]) => [name, String(value)])) },
        style: { left: `${layout.x}px`, top: `${layout.y}px`, width: `${layout.width}px`, height: `${layout.height}px` },
        querySelector() { return null; },
        getBoundingClientRect() {
            const origin = content.getBoundingClientRect(), scale = stage.getBoundingClientRect().scale;
            const left = origin.left + parseFloat(this.style.left) * scale, top = origin.top + parseFloat(this.style.top) * scale;
            const width = parseFloat(this.style.width) * scale, height = parseFloat(this.style.height) * scale;
            return { left, top, right: left + width, bottom: top + height, width, height };
        }
    }));
    const root = {
        querySelector(selector) { return { '.oe-stage-viewport': viewport, '.oe-stage-wrap': wrap, '.oe-stage': stage }[selector] ?? null; },
        contains(element) { return element === module || Object.values(canvasHandles).includes(element) || widgets.includes(element); },
        addEventListener(name, callback) { listeners.set(name, callback); },
        removeEventListener(name, callback) { if (listeners.get(name) === callback) listeners.delete(name); }
    };
    const ghosts = [];
    const document = {
        getElementById() { return root; },
        body: { classList: classList(), appendChild(element) { ghosts.push(element); } },
        createElement() { return { style: {}, remove() { this.removed = true; } }; },
        addEventListener() {}, removeEventListener() {}
    };
    class Observer {
        constructor(callback) { this.callback = callback; observers.push(this); }
        observe() {} disconnect() {}
    }
    const window = {};
    vm.runInNewContext(script, { window, document, console, ResizeObserver: Observer, MutationObserver: Observer,
        getComputedStyle() { return { paddingLeft: '0', paddingRight: '0' }; },
        requestAnimationFrame() { return 1; }, cancelAnimationFrame() {}, setTimeout() {} });
    window.grindcrestOverlayEditor.mount('editor', { invokeMethodAsync(...args) { calls.push(args); return Promise.resolve(); } });
    const dispatch = (type, values = {}) => {
        const event = { type, button: 0, pointerId: 1, clientX: 0, clientY: 0, target: canvasHandle,
            defaultPrevented: false, preventDefault() { this.defaultPrevented = true; }, ...values };
        listeners.get(type)?.(event);
        return event;
    };
    return { stage, content, wrap, module, canvasHandle, canvasHandles, widgets, dispatch, calls, ghosts, observers, document };
}

test('window fit includes external chrome while saved canvas dimensions remain content only', () => {
    const { stage, wrap } = setup();
    assert.equal(stage.style.transform, 'scale(0.5)');
    assert.equal(wrap.style.width, '202px');
    assert.equal(wrap.style.height, '117px');
    assert.equal(stage.dataset.width, '400');
    assert.equal(stage.dataset.height, '200');
});

test('dropping a module uses the content origin below the title bar at the current scale', () => {
    const { module, dispatch, calls, content, ghosts } = setup();
    const rect = content.getBoundingClientRect();
    dispatch('pointerdown', { target: module, clientX: 0, clientY: 0 });
    dispatch('pointermove', { clientX: rect.left + 21, clientY: rect.top + 36 });
    dispatch('pointerup', { clientX: rect.left + 21, clientY: rect.top + 36 });
    assert.deepEqual(calls, [['AddModuleAt', 'duration', 42, 72]]);
    assert.equal(module.captured.size, 0);
    assert.equal(ghosts[0].removed, true);
});

test('window title bar and borders cannot receive modules as if they were content', () => {
    for (const point of [{ clientX: 150, clientY: 58 }, { clientX: 100.5, clientY: 100 }]) {
        const { module, stage, dispatch, calls } = setup();
        dispatch('pointerdown', { target: module });
        dispatch('pointermove', point);
        assert.equal(stage.classList.contains('is-drop-target'), false);
        dispatch('pointerup', point);
        assert.deepEqual(calls, []);
    }
});

test('canvas resize commits content dimensions and renders outer dimensions including chrome', () => {
    const { stage, wrap, dispatch, calls } = setup();
    dispatch('pointerdown', { clientX: 302, clientY: 167 });
    dispatch('pointermove', { clientX: 352, clientY: 187 });
    assert.equal(stage.style.width, '504px');
    assert.equal(stage.style.height, '274px');
    assert.equal(wrap.style.width, '252px');
    assert.equal(wrap.style.height, '137px');
    // The server remains the owner of committed data attributes.
    assert.equal(stage.dataset.width, '400');
    assert.equal(stage.dataset.height, '200');
    dispatch('pointerup', { clientX: 352, clientY: 187 });
    assert.deepEqual(calls, [['CommitCanvasCorner', 500, 240, 'se']]);
});

for (const cancellation of ['pointercancel', 'Escape']) {
    test(`${cancellation} restores original outer geometry without committing a resized canvas`, () => {
        const { stage, wrap, dispatch, calls, canvasHandle, document } = setup();
        dispatch('pointerdown', { clientX: 302, clientY: 167 });
        dispatch('pointermove', { clientX: 352, clientY: 187 });
        if (cancellation === 'Escape') dispatch('keydown', { key: 'Escape' });
        else dispatch('pointercancel');
        assert.equal(stage.style.width, '404px');
        assert.equal(stage.style.height, '234px');
        assert.equal(wrap.style.width, '202px');
        assert.equal(wrap.style.height, '117px');
        assert.equal(canvasHandle.captured.size, 0);
        assert.equal(document.body.classList.contains('oe-resizing'), false);
        assert.deepEqual(calls, []);
    });
}

test('the default theme without chrome keeps its original drop and resize coordinate system', () => {
    const { stage, wrap, module, dispatch, calls } = setup({ chrome: false });
    assert.equal(stage.style.transform, 'scale(0.5)');
    assert.equal(wrap.style.width, '200px');
    assert.equal(wrap.style.height, '100px');
    dispatch('pointerdown', { target: module });
    dispatch('pointermove', { clientX: 121, clientY: 86 });
    dispatch('pointerup', { clientX: 121, clientY: 86 });
    assert.deepEqual(calls, [['AddModuleAt', 'duration', 42, 72]]);
    dispatch('pointerdown', { clientX: 300, clientY: 150 });
    dispatch('pointermove', { clientX: 350, clientY: 170 });
    assert.equal(stage.style.width, '500px');
    assert.equal(stage.style.height, '240px');
    dispatch('pointerup', { clientX: 350, clientY: 170 });
    assert.deepEqual(calls[1], ['CommitCanvasCorner', 500, 240, 'se']);
});

test('canvas resize applies snapping and limits to content rather than title bar dimensions', () => {
    const { stage, dispatch, calls } = setup({ snap: true });
    dispatch('pointerdown', { clientX: 302, clientY: 167 });
    dispatch('pointermove', { clientX: 309, clientY: 174 });
    assert.equal(stage.style.width, '420px');
    assert.equal(stage.style.height, '250px');
    dispatch('pointermove', { clientX: -1000, clientY: -1000 });
    assert.equal(stage.style.width, '164px');
    assert.equal(stage.style.height, '98px');
    dispatch('pointerup', { clientX: -1000, clientY: -1000 });
    assert.deepEqual(calls, [['CommitCanvasCorner', 160, 64, 'se']]);
});

const spacedWidgets = [
    { x: 20, y: 24, width: 120, height: 64 },
    { x: 180, y: 112, width: 180, height: 72 }
];

for (const corner of ['nw', 'ne', 'sw', 'se']) {
    test(`${corner} expansion at 50% with chrome preserves widget size, spacing and screen positions`, () => {
        const { stage, dispatch, calls, widgets, observers, canvasHandle, document } = setup({ corner, widgets: spacedWidgets });
        const before = stage.getBoundingClientRect();
        const widgetRects = widgets.map(widget => widget.getBoundingClientRect());
        const west = corner.includes('w'), north = corner.includes('n');
        const start = { clientX: west ? before.left : before.right, clientY: north ? before.top : before.bottom };
        dispatch('pointerdown', start);
        for (const [dx, dy] of [[50, 20], [60, 25]]) {
            dispatch('pointermove', { clientX: start.clientX + (west ? -dx : dx), clientY: start.clientY + (north ? -dy : dy) });
            // Simulate the observer callback caused by a live snapshot mid-drag.
            observers[1].callback([]);
            const current = stage.getBoundingClientRect();
            assert.equal(current[west ? 'right' : 'left'], before[west ? 'right' : 'left']);
            assert.equal(current[north ? 'bottom' : 'top'], before[north ? 'bottom' : 'top']);
            assert.equal(current.width, before.width + dx);
            assert.equal(current.height, before.height + dy);
            assert.deepEqual(widgets.map(widget => widget.getBoundingClientRect()), widgetRects);
            assert.equal(parseFloat(widgets[1].style.left) - parseFloat(widgets[0].style.left), 160);
            assert.equal(parseFloat(widgets[1].style.top) - parseFloat(widgets[0].style.top), 88);
            assert.equal(document.body.classList.contains('oe-resizing-reverse'), west !== north);
        }
        assert.equal(stage.style.width, '524px');
        assert.equal(stage.style.height, '284px');
        assert.equal(stage.dataset.width, '400');
        assert.equal(stage.dataset.height, '200');
        dispatch('pointerup');
        assert.deepEqual(calls, [['CommitCanvasCorner', 520, 250, corner]]);
        assert.equal(canvasHandle.captured.size, 0);
        assert.equal(document.body.classList.contains('oe-resizing-reverse'), false);
    });
}

for (const corner of ['nw', 'ne', 'sw']) {
    test(`${corner} shrinking stops at the first module and cancellation restores the complete layout`, () => {
        const { stage, wrap, dispatch, calls, widgets } = setup({ corner, widgets: spacedWidgets });
        const original = widgets.map(widget => ({ ...widget.style }));
        const rect = stage.getBoundingClientRect();
        const west = corner.includes('w'), north = corner.includes('n');
        const start = { clientX: west ? rect.left : rect.right, clientY: north ? rect.top : rect.bottom };
        dispatch('pointerdown', start);
        dispatch('pointermove', { clientX: start.clientX + (west ? 500 : -500), clientY: start.clientY + (north ? 500 : -500) });
        assert.equal(stage.style.width, `${(west ? 380 : 160) + 4}px`);
        assert.equal(stage.style.height, `${(north ? 176 : 64) + 34}px`);
        assert.equal(parseFloat(widgets[1].style.left) - parseFloat(widgets[0].style.left), 160);
        assert.equal(parseFloat(widgets[1].style.top) - parseFloat(widgets[0].style.top), 88);
        assert.ok(widgets.every(widget => parseFloat(widget.style.left) >= 0 && parseFloat(widget.style.top) >= 0));
        dispatch('pointercancel');
        assert.deepEqual(widgets.map(widget => widget.style), original);
        assert.equal(stage.style.width, '404px');
        assert.equal(stage.style.height, '234px');
        assert.equal(wrap.style.left, '');
        assert.equal(wrap.style.top, '');
        assert.deepEqual(calls, []);
    });
}

test('the existing true-valued southeast handle remains compatible with the corner callback', () => {
    const { canvasHandle, dispatch, calls } = setup();
    canvasHandle.dataset.canvasResize = 'true';
    dispatch('pointerdown', { clientX: 302, clientY: 167 });
    dispatch('pointermove', { clientX: 352, clientY: 187 });
    dispatch('pointerup');
    assert.deepEqual(calls, [['CommitCanvasCorner', 500, 240, 'true']]);
});
