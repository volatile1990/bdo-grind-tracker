// Run with: node --test tests/ui/history-interactions.test.cjs
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = fs.readFileSync(path.join(__dirname, '../../src/BdoGrindTracker.App/wwwroot/app.js'), 'utf8');

function setup() {
    const listeners = new Map();
    const document = { body: {}, documentElement: {}, activeElement: null };
    let scrollLeft = 0;
    const viewport = {
        dataset: {}, isConnected: true, clientWidth: 300, scrollWidth: 900,
        get scrollLeft() { return scrollLeft; },
        set scrollLeft(value) { scrollLeft = Math.min(this.scrollWidth - this.clientWidth, Math.max(0, value)); },
        addEventListener(name, callback) { if (!listeners.has(name)) listeners.set(name, []); listeners.get(name).push(callback); },
        removeEventListener(name, callback) { listeners.set(name, (listeners.get(name) ?? []).filter(item => item !== callback)); },
        classList: { add() {}, remove() {} }, setPointerCapture() {}, hasPointerCapture() { return false; },
        querySelectorAll() { return []; }
    };
    document.getElementById = () => viewport;
    const window = {};
    vm.runInNewContext(script, { document, window, console });
    const event = values => ({ deltaX: 0, deltaY: 0, deltaMode: 0, shiftKey: false, ctrlKey: false,
        defaultPrevented: false, preventDefault() { this.defaultPrevented = true; }, ...values });
    const dispatch = async (name, values) => {
        const current = event(values);
        for (const listener of [...listeners.get(name) ?? []]) await listener(current);
        return current;
    };
    return { viewport, window, document, dispatch };
}

test('ordinary vertical wheel remains native over both loot and fixed columns', async () => {
    const { viewport, window, dispatch } = setup();
    window.grindcrest.enableHorizontalWheel('matrix');
    viewport.scrollLeft = 250;
    for (const deltaY of [-120, 120]) {
        const event = await dispatch('wheel', { deltaY });
        assert.equal(event.defaultPrevented, false);
        assert.equal(viewport.scrollLeft, 250);
    }
});

test('shift wheel scrolls horizontally only when space remains, including line units', async () => {
    const { viewport, window, dispatch } = setup();
    window.grindcrest.enableHorizontalWheel('matrix');
    let event = await dispatch('wheel', { shiftKey: true, deltaY: 3, deltaMode: 1 });
    assert.equal(event.defaultPrevented, true);
    assert.equal(viewport.scrollLeft, 48);
    viewport.scrollLeft = 600;
    event = await dispatch('wheel', { shiftKey: true, deltaY: 120 });
    assert.equal(event.defaultPrevented, false);
    assert.equal(viewport.scrollLeft, 600);
    viewport.scrollLeft = 0;
    event = await dispatch('wheel', { shiftKey: true, deltaY: -120 });
    assert.equal(event.defaultPrevented, false);
    assert.equal(viewport.scrollLeft, 0);
});

test('native trackpad and browser zoom gestures are not redirected', async () => {
    const { viewport, window, dispatch } = setup();
    window.grindcrest.enableHorizontalWheel('matrix');
    for (const gesture of [{ deltaX: 100, deltaY: 4 }, { deltaY: 100, ctrlKey: true, shiftKey: true },
        { deltaX: 100, deltaY: 0, shiftKey: true }]) {
        const event = await dispatch('wheel', gesture);
        assert.equal(event.defaultPrevented, false);
        assert.equal(viewport.scrollLeft, 0);
    }
});

function columns(context) {
    const { viewport, document } = context;
    const headers = ['Trash', 'Black Stone', 'Dust'].map(name => {
        const header = { dataset: { lootName: name }, classList: { add() {}, remove() {} },
            getBoundingClientRect() { return { width: 88 }; } };
        const button = { disabled: false, isConnected: true, focused: false,
            closest(selector) { return selector === '.session-favorite-button' ? button : header; },
            focus() { button.focused = true; document.activeElement = button; }, scrollIntoView() {} };
        header.button = button;
        header.querySelector = selector => selector === 'button:disabled' ? null : button;
        return header;
    });
    viewport.querySelectorAll = selector => selector === '.session-loot-column' ? headers : [];
    const calls = [];
    const receiver = { async invokeMethodAsync(...args) { calls.push(args); } };
    return { headers, receiver, calls };
}

test('Alt plus arrows moves the selected item one column without changing its favorite', async () => {
    const context = setup();
    const { headers, receiver, calls } = columns(context);
    const button = headers[1].button;
    context.document.activeElement = button;
    context.window.grindcrest.enableColumnDrag('matrix', receiver, 'spot');
    const event = await context.dispatch('keydown', { target: button, key: 'ArrowLeft', altKey: true });
    assert.equal(event.defaultPrevented, true);
    assert.equal(calls.length, 1);
    assert.deepEqual(JSON.parse(JSON.stringify(calls[0])), ['SaveColumnOrder', 'spot', ['Black Stone', 'Trash', 'Dust']]);
    assert.equal(button.focused, true);
});

test('keyboard reorder respects edges, disabled controls and plain arrow keys', async () => {
    const context = setup();
    const { headers, receiver, calls } = columns(context);
    context.window.grindcrest.enableColumnDrag('matrix', receiver, 'spot');
    await context.dispatch('keydown', { target: headers[0].button, key: 'ArrowLeft', altKey: true });
    await context.dispatch('keydown', { target: headers[2].button, key: 'ArrowRight', altKey: true });
    await context.dispatch('keydown', { target: headers[0].button, key: 'ArrowRight', altKey: false });
    headers[1].button.disabled = true;
    await context.dispatch('keydown', { target: headers[1].button, key: 'ArrowLeft', altKey: true });
    assert.equal(calls.length, 0);
});

test('pointer click still toggles a favorite exactly once and drag saves column order', async () => {
    const context = setup();
    const { headers, receiver, calls } = columns(context);
    context.window.grindcrest.enableColumnDrag('matrix', receiver, 'spot');
    const start = { target: headers[1].button, button: 0, pointerId: 1, clientX: 150, clientY: 10 };
    await context.dispatch('pointerdown', start);
    await context.dispatch('pointerup', { type: 'pointerup' });
    assert.deepEqual(JSON.parse(JSON.stringify(calls)), [['ToggleSessionFavorite', 'spot', 'Black Stone']]);
    await context.dispatch('pointerdown', start);
    await context.dispatch('pointermove', { clientX: 60, clientY: 10 });
    await context.dispatch('pointerup', { type: 'pointerup' });
    assert.deepEqual(JSON.parse(JSON.stringify(calls[1])), ['SaveColumnOrder', 'spot', ['Black Stone', 'Trash', 'Dust']]);
    assert.equal(context.viewport.columnDragging, false);
});

test('inline correction restores unclaimed focus without stealing focus from navigation', () => {
    const { document, window } = setup();
    let focused = 0;
    const trigger = { isConnected: true, focus() { focused++; } };
    document.activeElement = document.body;
    window.grindcrest.focusIfUnclaimed(trigger);
    assert.equal(focused, 1);
    document.activeElement = { isConnected: true };
    window.grindcrest.focusIfUnclaimed(trigger);
    assert.equal(focused, 1);
    document.activeElement = { isConnected: false };
    window.grindcrest.focusIfUnclaimed(trigger);
    assert.equal(focused, 2);
});
