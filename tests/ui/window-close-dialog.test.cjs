// Run with: node --test tests/ui/window-close-dialog.test.cjs
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const script = fs.readFileSync(path.join(__dirname, '../../src/BdoGrindTracker.App/wwwroot/app.js'), 'utf8');

function setup(managedCancel = true) {
    const listeners = [];
    let closeClicks = 0;
    const closeButton = {
        disabled: false,
        focus() {},
        click() { if (!this.disabled) closeClicks++; },
    };
    const dialog = {
        open: false,
        dataset: {},
        hasAttribute(name) { return name === 'data-managed-cancel' && managedCancel; },
        addEventListener(name, handler) { listeners.push({ name, handler }); },
        showModal() { this.open = true; },
        close() { this.open = false; },
        querySelector(selector) {
            return selector === '[data-dialog-close]' || selector === 'button, input, select' ? closeButton : null;
        },
    };
    const window = {};
    const document = { getElementById(id) { return id === 'window-close-dialog' ? dialog : null; } };
    vm.runInNewContext(script, { window, document });
    return { api: window.grindcrest, dialog, closeButton, listeners, closeClicks: () => closeClicks };
}

test('managed Escape prevents native close and invokes the app dismissal action', () => {
    const { api, dialog, listeners, closeClicks } = setup();
    api.showDialog('window-close-dialog');
    assert.equal(listeners.length, 1);
    assert.equal(listeners[0].name, 'cancel');
    let prevented = 0;

    listeners[0].handler({ preventDefault() { prevented++; } });

    assert.equal(prevented, 1);
    assert.equal(closeClicks(), 1);
    assert.equal(dialog.open, true);
    api.closeDialog('window-close-dialog');
    assert.equal(dialog.open, false);
    api.showDialog('window-close-dialog');
    assert.equal(listeners.length, 1);
});

test('Escape cannot dismiss the managed dialog while its close control is disabled for saving', () => {
    const { api, dialog, closeButton, listeners, closeClicks } = setup();
    api.showDialog('window-close-dialog');
    closeButton.disabled = true;
    let prevented = false;

    listeners[0].handler({ preventDefault() { prevented = true; } });

    assert.equal(prevented, true);
    assert.equal(closeClicks(), 0);
    assert.equal(dialog.open, true);
});

test('dialogs without the managed marker keep their existing native cancel behavior', () => {
    const { api, listeners } = setup(false);

    api.showDialog('window-close-dialog');

    assert.equal(listeners.length, 0);
});
