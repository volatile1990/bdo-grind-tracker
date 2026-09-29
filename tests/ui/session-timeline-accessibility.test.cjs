// Run with: node --test tests/ui/session-timeline-accessibility.test.cjs
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = fs.readFileSync(path.join(__dirname, '../../src/BdoGrindTracker.App/wwwroot/session-timeline.js'), 'utf8');

function timeline() {
    const listeners = new Map();
    const element = { dataset: {}, addEventListener(name, listener) { listeners.set(name, listener); } };
    const window = {};
    vm.runInNewContext(script, { window });
    window.sessionTimeline.attach(element);
    return { element, listeners, api: window.sessionTimeline,
        dispatch(name, values) {
            const event = { target: element, defaultPrevented: false,
                preventDefault() { this.defaultPrevented = true; }, ...values };
            listeners.get(name)?.(event);
            return event;
        } };
}

test('timeline consumes only its own zoom and pan keys while keeping page navigation available', () => {
    const { dispatch } = timeline();
    for (const key of ['+', '=', '-', 'ArrowLeft', 'ArrowRight', 'Home'])
        assert.equal(dispatch('keydown', { key }).defaultPrevented, true, key);
    for (const key of ['Tab', 'Escape', 'ArrowUp', 'ArrowDown', 'PageDown'])
        assert.equal(dispatch('keydown', { key }).defaultPrevented, false, key);
    assert.equal(dispatch('keydown', { key: '+', ctrlKey: true }).defaultPrevented, false);
    assert.equal(dispatch('keydown', { key: 'ArrowLeft', altKey: true }).defaultPrevented, false);
    assert.equal(dispatch('keydown', { key: '+', metaKey: true }).defaultPrevented, false);
    assert.equal(dispatch('keydown', { key: 'Home', target: {} }).defaultPrevented, false);
});

test('unmodified scrolling stays available and a repeated attach does not duplicate handlers', () => {
    const { dispatch, api, element, listeners } = timeline();
    assert.equal(dispatch('wheel', {}).defaultPrevented, false);
    assert.equal(dispatch('wheel', { shiftKey: true }).defaultPrevented, true);
    const original = listeners.get('keydown');
    api.attach(element);
    assert.equal(listeners.get('keydown'), original);
});
