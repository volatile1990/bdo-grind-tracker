(() => {
    "use strict";
    const editors = new Map();
    const number = (element, name) => Number(element.dataset[name]) || 0;
    const clamp = (value, min, max) => Math.min(Math.max(value, min), Math.max(min, max));

    window.grindcrestOverlayEditor = {
        mount(id, dotnet) {
            this.unmount(id);
            const root = document.getElementById(id);
            if (!root) return;
            const viewport = root.querySelector(".oe-stage-viewport");
            const wrap = root.querySelector(".oe-stage-wrap");
            const stage = root.querySelector(".oe-stage");
            let drag = null, ghost = null, suppressClick = false, disposed = false;
            const invoke = (name, ...args) => {
                if (disposed) return;
                dotnet.invokeMethodAsync(name, ...args).catch(() => {});
            };
            const fit = () => {
                if (drag && drag.moved) return;
                const style = getComputedStyle(viewport);
                const available = viewport.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                const width = number(stage, "width"), height = number(stage, "height");
                const scale = Math.max(.2, Math.min(1, (available - 8) / Math.max(1, width)));
                stage.style.transform = `scale(${scale})`;
                wrap.style.width = `${width * scale}px`;
                wrap.style.height = `${height * scale}px`;
            };
            const snapshot = () => ({ width: number(stage, "width"), height: number(stage, "height"), rect: stage.getBoundingClientRect() });
            const grid = value => stage.dataset.snap === "true" ? Math.round(value / 8) * 8 : Math.round(value);
            const restore = current => {
                if (current.element) {
                    current.element.style.left = `${current.x}px`;
                    current.element.style.top = `${current.y}px`;
                    current.element.style.width = `${current.width}px`;
                    current.element.style.height = `${current.height}px`;
                } else if (current.type === "canvas") {
                    stage.style.width = `${current.canvas.width}px`;
                    stage.style.height = `${current.canvas.height}px`;
                }
            };
            const cleanupDrag = () => {
                ghost?.remove(); ghost = null;
                stage.classList.remove("is-drop-target");
                document.body.classList.remove("oe-dragging", "oe-resizing");
                if (drag?.capture.hasPointerCapture?.(drag.pointerId)) drag.capture.releasePointerCapture(drag.pointerId);
                drag = null;
            };
            const down = e => {
                if (e.button !== 0 || drag || e.target.closest("[data-widget-delete]")) return;
                const module = e.target.closest("[data-module-kind]");
                const grip = e.target.closest("[data-widget-drag]");
                const resize = e.target.closest("[data-widget-resize]");
                const canvasResize = e.target.closest("[data-canvas-resize]");
                const capture = module || grip || resize || canvasResize;
                if (!capture || capture.disabled || !root.contains(capture)) return;
                const canvas = snapshot();
                const type = module ? "add" : grip ? "move" : resize ? "resize" : "canvas";
                drag = { type, canvas, capture, pointerId: e.pointerId, startX: e.clientX, startY: e.clientY, moved: false, scale: canvas.rect.width / canvas.width };
                if (module) {
                    drag.kind = module.dataset.moduleKind;
                    drag.label = module.querySelector("strong")?.textContent || "Modul";
                } else {
                    e.preventDefault();
                    const widget = capture.closest(".oe-widget");
                    if (widget) {
                        drag.element = widget; drag.id = widget.dataset.widgetId;
                        drag.x = number(widget, "x"); drag.y = number(widget, "y");
                        drag.width = number(widget, "width"); drag.height = number(widget, "height");
                        widget.focus({ preventScroll: true });
                        invoke("SelectWidget", drag.id);
                    }
                }
                capture.setPointerCapture(e.pointerId);
            };
            const move = e => {
                if (!drag || drag.pointerId !== e.pointerId) return;
                const dx = (e.clientX - drag.startX) / drag.scale, dy = (e.clientY - drag.startY) / drag.scale;
                if (!drag.moved && Math.abs(e.clientX - drag.startX) + Math.abs(e.clientY - drag.startY) < 5) return;
                drag.moved = true; e.preventDefault();
                document.body.classList.add(drag.type === "resize" || drag.type === "canvas" ? "oe-resizing" : "oe-dragging");
                if (drag.type === "add") {
                    if (!ghost) {
                        ghost = document.createElement("div"); ghost.className = "oe-drag-ghost";
                        ghost.textContent = drag.label; document.body.appendChild(ghost);
                    }
                    ghost.style.left = `${e.clientX + 12}px`; ghost.style.top = `${e.clientY + 12}px`;
                    const rect = stage.getBoundingClientRect();
                    stage.classList.toggle("is-drop-target", e.clientX >= rect.left && e.clientX <= rect.right && e.clientY >= rect.top && e.clientY <= rect.bottom);
                    return;
                }
                if (drag.type === "canvas") {
                    drag.newWidth = clamp(grid(drag.canvas.width + dx), 160, 1600);
                    drag.newHeight = clamp(grid(drag.canvas.height + dy), 64, 1200);
                    stage.style.width = `${drag.newWidth}px`; stage.style.height = `${drag.newHeight}px`;
                    wrap.style.width = `${drag.newWidth * drag.scale}px`; wrap.style.height = `${drag.newHeight * drag.scale}px`;
                    return;
                }
                if (drag.type === "move") {
                    drag.newX = clamp(grid(drag.x + dx), 0, drag.canvas.width - drag.width);
                    drag.newY = clamp(grid(drag.y + dy), 0, drag.canvas.height - drag.height);
                    drag.element.style.left = `${drag.newX}px`; drag.element.style.top = `${drag.newY}px`;
                } else {
                    drag.newWidth = clamp(grid(drag.width + dx), 80, drag.canvas.width - drag.x);
                    drag.newHeight = clamp(grid(drag.height + dy), 40, drag.canvas.height - drag.y);
                    drag.element.style.width = `${drag.newWidth}px`; drag.element.style.height = `${drag.newHeight}px`;
                }
            };
            const up = e => {
                if (!drag || drag.pointerId !== e.pointerId) return;
                const current = drag;
                const cancelled = e.type === "pointercancel";
                if (current.moved) {
                    suppressClick = true;
                    // Cancel only the synthetic click immediately following this
                    // drag; a later deliberate click on a module remains usable.
                    setTimeout(() => suppressClick = false, 0);
                    if (cancelled) restore(current);
                    else if (current.type === "add") {
                        const rect = stage.getBoundingClientRect();
                        if (e.clientX >= rect.left && e.clientX <= rect.right && e.clientY >= rect.top && e.clientY <= rect.bottom)
                            invoke("AddModuleAt", current.kind, grid((e.clientX - rect.left) / current.scale), grid((e.clientY - rect.top) / current.scale));
                    } else if (current.type === "canvas") invoke("CommitCanvasSize", current.newWidth, current.newHeight);
                    else invoke("CommitWidgetGeometry", current.id, current.newX ?? current.x, current.newY ?? current.y, current.newWidth ?? current.width, current.newHeight ?? current.height);
                }
                cleanupDrag();
                if (cancelled || !current.moved) fit();
            };
            const click = e => { if (suppressClick) { e.preventDefault(); e.stopImmediatePropagation(); suppressClick = false; } };
            const key = e => {
                if (e.key === "Escape" && drag) {
                    restore(drag); cleanupDrag(); fit(); e.preventDefault(); return;
                }
                if (e.altKey || e.ctrlKey || e.metaKey || e.target.matches("input,select,textarea") || e.target.closest("[data-widget-delete]")) return;
                const widget = e.target.closest(".oe-widget");
                if (!widget) return;
                const step = e.shiftKey ? 8 : 1;
                const deltas = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] };
                const delta = deltas[e.key];
                if (delta) { e.preventDefault(); invoke("NudgeWidget", widget.dataset.widgetId, ...delta); }
            };
            root.addEventListener("pointerdown", down);
            root.addEventListener("pointermove", move);
            root.addEventListener("pointerup", up);
            root.addEventListener("pointercancel", up);
            root.addEventListener("click", click, true);
            root.addEventListener("keydown", key);
            const resizeObserver = new ResizeObserver(fit); resizeObserver.observe(viewport);
            const mutationObserver = new MutationObserver(fit); mutationObserver.observe(stage, { attributes: true, attributeFilter: ["data-width", "data-height"] });
            fit();
            editors.set(id, () => {
                disposed = true;
                cleanupDrag(); resizeObserver.disconnect(); mutationObserver.disconnect();
                root.removeEventListener("pointerdown", down); root.removeEventListener("pointermove", move);
                root.removeEventListener("pointerup", up); root.removeEventListener("pointercancel", up);
                root.removeEventListener("click", click, true); root.removeEventListener("keydown", key);
            });
        },
        unmount(id) { editors.get(id)?.(); editors.delete(id); }
    };
})();
