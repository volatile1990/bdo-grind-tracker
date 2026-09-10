// Dialog focus stays in the desktop surface; no external content or network calls.
window.grindcrest = {
    showDialog: function (id) {
        const dialog = document.getElementById(id);
        if (dialog && !dialog.open) {
            dialog.showModal();
            const focus = dialog.querySelector('[autofocus]') || dialog.querySelector('button, input, select');
            if (focus) focus.focus();
        }
    },
    focusIfUnclaimed: function (element) {
        const current = document.activeElement;
        if (element?.isConnected && (!current || !current.isConnected || current === document.body || current === document.documentElement))
            element.focus({ preventScroll: true });
    },
    closeDialog: function (id) { const dialog = document.getElementById(id); if (dialog && dialog.open) dialog.close(); },
    scrollTop: function (smooth = true) { document.querySelector('main')?.scrollTo({ top: 0, behavior: smooth ? 'smooth' : 'auto' }); },
    enableHorizontalWheel: function (id) {
        const viewport = document.getElementById(id);
        if (!viewport || viewport.dataset.horizontalWheel === 'true') return;
        viewport.dataset.horizontalWheel = 'true';
        viewport.addEventListener('wheel', function (event) {
            if (viewport.columnDragging) { event.preventDefault(); return; }
            if (event.ctrlKey) return;
            // Preserve normal vertical scrolling and native trackpad gestures.
            // Shift is the explicit request to turn a vertical wheel horizontally.
            if (!event.shiftKey || Math.abs(event.deltaX) >= Math.abs(event.deltaY)) return;
            const unit = event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? viewport.clientWidth : 1;
            const movement = event.deltaY * unit;
            if (!movement) return;
            const previous = viewport.scrollLeft;
            viewport.scrollLeft += movement;
            // At an edge, leave the event available for the outer scroll area.
            if (viewport.scrollLeft !== previous) event.preventDefault();
        }, { passive: false });
    }
};
// Pointer capture keeps column dragging horizontal, even outside the header.
window.grindcrest.enableColumnDrag = function (id, receiver, spotId) {
    const viewport = document.getElementById(id);
    if (!viewport) return;
    viewport.columnDragReceiver = receiver;
    viewport.columnDragSpot = spotId;
    if (viewport.dataset.columnDrag) return;
    viewport.dataset.columnDrag = 'true';
    viewport.addEventListener('keydown', async event => {
        const button = event.target.closest('.session-favorite-button');
        if (!button || button.disabled || !event.altKey || event.ctrlKey || event.metaKey ||
            (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight')) return;
        event.preventDefault();
        if (viewport.columnDragging || viewport.columnKeyboardReordering) return;
        const header = button.closest('.session-loot-column');
        const order = [...viewport.querySelectorAll('.session-loot-column')].map(item => item.dataset.lootName);
        const source = order.indexOf(header.dataset.lootName);
        const destination = source + (event.key === 'ArrowLeft' ? -1 : 1);
        if (source < 0 || destination < 0 || destination >= order.length) return;
        order.splice(source, 1);
        order.splice(destination, 0, header.dataset.lootName);
        const activeSpot = viewport.columnDragSpot;
        viewport.columnKeyboardReordering = true;
        try {
            await viewport.columnDragReceiver.invokeMethodAsync('SaveColumnOrder', activeSpot, order);
            // Razor keys preserve the moved control. Focus follows the item,
            // unless the user already moved on while the preference was saving.
            if (viewport.isConnected && viewport.columnDragSpot === activeSpot &&
                (document.activeElement === button || document.activeElement === document.body)) {
                const moved = [...viewport.querySelectorAll('.session-loot-column')]
                    .find(item => item.dataset.lootName === header.dataset.lootName)?.querySelector('.session-favorite-button');
                moved?.focus({ preventScroll: true });
                moved?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
            }
        } catch (error) { console.error('Spaltenreihenfolge konnte nicht gespeichert werden.', error); }
        finally { viewport.columnKeyboardReordering = false; }
    });
    viewport.addEventListener('dragstart', e => {
        if (e.target.closest('.session-loot-column')) e.preventDefault();
    });
    viewport.addEventListener('pointerdown', e => {
        const header = e.target.closest('.session-loot-column');
        if (!header || e.button !== 0 || viewport.columnDragging || header.querySelector("button:disabled")) return;
        e.preventDefault();
        const headers = [...viewport.querySelectorAll('.session-loot-column')];
        const original = headers.map(h => h.dataset.lootName);
        const order = [...original];
        const source = original.indexOf(header.dataset.lootName);
        const width = header.getBoundingClientRect().width;
        const cells = [...viewport.querySelectorAll('tr')].map(row => [...row.querySelectorAll('.session-loot-column, .session-loot-quantity')]).filter(row => row.length === headers.length);
        const startX = e.clientX, startScroll = viewport.scrollLeft;
        const activeSpot = viewport.columnDragSpot;
        let destination = source;
        let moved = false;
        viewport.columnDragging = true;
        viewport.classList.add('is-column-dragging');
        header.classList.add('is-dragging');
        viewport.setPointerCapture(e.pointerId);
        const move = event => {
            event.preventDefault();
            if (Math.abs(event.clientX - startX) > 5 || Math.abs(event.clientY - e.clientY) > 5) moved = true;
            const dx = Math.max(-source * width, Math.min((headers.length - source - 1) * width, event.clientX - startX + viewport.scrollLeft - startScroll));
            const next = Math.max(0, Math.min(headers.length - 1, Math.round(source + dx / width)));
            if (next !== destination) {
                order.splice(destination, 1);
                order.splice(next, 0, original[source]);
                destination = next;
            }
            cells.forEach(row => row.forEach((cell, index) => {
                const offset = index === source ? dx : (order.indexOf(original[index]) - index) * width;
                cell.style.transform = `translateX(${offset}px)`;
                cell.classList.toggle('drag-active-cell', index === source);
            }));
        };
        const finish = async event => {
            viewport.removeEventListener('pointermove', move);
            viewport.removeEventListener('pointerup', finish);
            viewport.removeEventListener('pointercancel', cancel);
            viewport.removeEventListener('lostpointercapture', cancel);
            if (viewport.hasPointerCapture(e.pointerId)) viewport.releasePointerCapture(e.pointerId);
            try {
                if (event.type === 'pointerup' && !moved && e.target.closest('.session-favorite-button'))
                    await viewport.columnDragReceiver.invokeMethodAsync('ToggleSessionFavorite', activeSpot, original[source]);
                else if (event.type === 'pointerup' && destination !== source)
                    await viewport.columnDragReceiver.invokeMethodAsync('SaveColumnOrder', activeSpot, order);
            } catch (error) { console.error('Spaltenreihenfolge konnte nicht gespeichert werden.', error); }
            finally {
                cells.flat().forEach(cell => { cell.style.removeProperty('transform'); cell.classList.remove('drag-active-cell'); });
                header.classList.remove('is-dragging');
                viewport.classList.remove('is-column-dragging');
                viewport.columnDragging = false;
            }
        };
        const cancel = event => finish(event);
        viewport.addEventListener('pointermove', move, { passive: false });
        viewport.addEventListener('pointerup', finish);
        viewport.addEventListener('pointercancel', cancel);
        viewport.addEventListener('lostpointercapture', cancel);
    });
    viewport.addEventListener('wheel', e => { if (viewport.columnDragging) e.preventDefault(); }, { passive: false });
};
