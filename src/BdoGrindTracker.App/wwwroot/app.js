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
    closeDialog: function (id) { const dialog = document.getElementById(id); if (dialog && dialog.open) dialog.close(); },
    scrollTop: function (smooth = true) { document.querySelector('main')?.scrollTo({ top: 0, behavior: smooth ? 'smooth' : 'auto' }); }
};
