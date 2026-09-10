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
            const inspector = root.querySelector(".oe-inspector");
            let drag = null, ghost = null, suppressClick = false, disposed = false, contentFrame = null;
            const invoke = (name, ...args) => {
                if (disposed) return;
                dotnet.invokeMethodAsync(name, ...args).catch(() => {});
            };
            const fit = () => {
                if (drag && drag.moved) {
                    // Live snapshots may replace text/styles while the pointer
                    // rests mid-drag. Reapply only the pending content geometry;
                    // the stage and unsaved widget rectangles must stay put.
                    stage.querySelectorAll(".oe-widget").forEach(element => {
                        const original = drag.type === "canvas" ? drag.widgets.find(widget => widget.element === element) : null;
                        if (original) resizeContent(element,
                            original.width * (drag.newWidth ?? drag.canvas.width) / drag.canvas.width,
                            original.height * (drag.newHeight ?? drag.canvas.height) / drag.canvas.height);
                        else if (element === drag.element && drag.type === "resize")
                            resizeContent(element, drag.newWidth ?? drag.width, drag.newHeight ?? drag.height);
                        else resizeContent(element, number(element, "width"), number(element, "height"));
                    });
                    scheduleContentFit();
                    return;
                }
                const style = getComputedStyle(viewport);
                const available = viewport.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                const width = number(stage, "width"), height = number(stage, "height");
                const scale = Math.max(.2, Math.min(1, (available - 8) / Math.max(1, width)));
                stage.style.transform = `scale(${scale})`;
                wrap.style.width = `${width * scale}px`;
                wrap.style.height = `${height * scale}px`;
                stage.querySelectorAll(".oe-widget").forEach(element => resizeContent(element, number(element, "width"), number(element, "height")));
            };
            const fitText = line => {
                line.style.setProperty("--line-fit", "1");
                const style = getComputedStyle(line);
                if (!line.getClientRects().length || style.display === "none" || style.clipPath !== "none") return;
                const bounds = line.getBoundingClientRect();
                if (bounds.width <= 0 || bounds.height <= 0) return;
                const range = document.createRange();
                range.selectNodeContents(line);
                const rectangles = Array.from(range.getClientRects()).filter(rect => rect.width > 0 && rect.height > 0);
                line.querySelectorAll(".icon,.overlay-status-dot").forEach(icon => rectangles.push(icon.getBoundingClientRect()));
                if (!rectangles.length) return;
                const left = Math.min(...rectangles.map(rect => rect.left)), right = Math.max(...rectangles.map(rect => rect.right));
                const top = Math.min(...rectangles.map(rect => rect.top)), bottom = Math.max(...rectangles.map(rect => rect.bottom));
                const visualScale = bounds.width / (parseFloat(style.width) || line.offsetWidth || 1);
                const horizontalInset = parseFloat(style.paddingLeft) + parseFloat(style.paddingRight) +
                    parseFloat(style.borderLeftWidth) + parseFloat(style.borderRightWidth);
                const verticalInset = parseFloat(style.paddingTop) + parseFloat(style.paddingBottom) +
                    parseFloat(style.borderTopWidth) + parseFloat(style.borderBottomWidth);
                const width = Math.max(.01, bounds.width - horizontalInset * visualScale);
                const height = Math.max(.01, bounds.height - verticalInset * visualScale);
                const factor = Math.max(.001, Math.min(1, width / Math.max(.01, right - left), height / Math.max(.01, bottom - top)));
                if (factor < .999) line.style.setProperty("--line-fit", String(factor * .995));
            };
            const fitContentText = () => {
                contentFrame = null;
                if (disposed) return;
                // During an unequal drag the server's item grid still has its
                // previous arrangement. Fit that complete grid until the new
                // shared layout arrives, retaining every rendered item.
                stage.querySelectorAll(".overlay-loot-viewport").forEach(viewport => {
                    const items = viewport.querySelector(".overlay-widget-items");
                    if (!items) return;
                    const style = getComputedStyle(items);
                    const width = parseFloat(style.getPropertyValue("--loot-layout-width"));
                    const height = parseFloat(style.getPropertyValue("--loot-layout-height"));
                    const scale = width > 0 && height > 0
                        ? Math.min(1, viewport.clientWidth / width, viewport.clientHeight / height) : 1;
                    items.style.transform = `scale(${Math.max(.001, scale)})`;
                });
                stage.querySelectorAll("[data-overlay-fit]").forEach(fitText);
            };
            const scheduleContentFit = () => {
                if (contentFrame === null && !disposed) contentFrame = requestAnimationFrame(fitContentText);
            };
            const resizeContent = (element, width, height) => {
                const viewport = element.querySelector(".overlay-widget-viewport");
                const content = viewport?.querySelector(".overlay-widget-preview");
                if (!content || width <= 0 || height <= 0) return;
                const referenceWidth = number(viewport, "contentWidth") || width;
                const referenceHeight = number(viewport, "contentHeight") || height;
                const minimumWidth = number(viewport, "minContentWidth") || 80;
                const minimumHeight = number(viewport, "minContentHeight") || 48;
                const scale = Math.min(width / referenceWidth, height / referenceHeight,
                    width / minimumWidth, height / minimumHeight);
                content.style.width = `${width / scale}px`;
                content.style.height = `${height / scale}px`;
                content.style.transform = `scale(${scale})`;
                scheduleContentFit();
            };
            const snapshot = () => ({ width: number(stage, "width"), height: number(stage, "height"), rect: stage.getBoundingClientRect() });
            const grid = value => stage.dataset.snap === "true" ? Math.round(value / 8) * 8 : Math.round(value);
            const widgetGeometry = element => ({ element, x: number(element, "x"), y: number(element, "y"), width: number(element, "width"), height: number(element, "height") });
            const scaleWidget = (widget, scaleX = 1, scaleY = 1) => {
                widget.element.style.left = `${widget.x * scaleX}px`;
                widget.element.style.top = `${widget.y * scaleY}px`;
                widget.element.style.width = `${widget.width * scaleX}px`;
                widget.element.style.height = `${widget.height * scaleY}px`;
                resizeContent(widget.element, widget.width * scaleX, widget.height * scaleY);
            };
            const restore = current => {
                if (current.element) {
                    scaleWidget(current);
                } else if (current.type === "canvas") {
                    stage.style.width = `${current.canvas.width}px`;
                    stage.style.height = `${current.canvas.height}px`;
                    current.widgets.forEach(widget => scaleWidget(widget));
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
                    } else if (type === "canvas") {
                        drag.widgets = Array.from(stage.querySelectorAll(".oe-widget"), widgetGeometry);
                        capture.focus({ preventScroll: true });
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
                    drag.widgets.forEach(widget => scaleWidget(widget, drag.newWidth / drag.canvas.width, drag.newHeight / drag.canvas.height));
                    return;
                }
                if (drag.type === "move") {
                    drag.newX = clamp(grid(drag.x + dx), 0, drag.canvas.width - drag.width);
                    drag.newY = clamp(grid(drag.y + dy), 0, drag.canvas.height - drag.height);
                    drag.element.style.left = `${drag.newX}px`; drag.element.style.top = `${drag.newY}px`;
                } else {
                    drag.newWidth = clamp(grid(drag.width + dx), Math.min(80, drag.width), drag.canvas.width - drag.x);
                    drag.newHeight = clamp(grid(drag.height + dy), Math.min(40, drag.height), drag.canvas.height - drag.y);
                    drag.element.style.width = `${drag.newWidth}px`; drag.element.style.height = `${drag.newHeight}px`;
                    resizeContent(drag.element, drag.newWidth, drag.newHeight);
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
            const outsideClick = e => {
                if (drag || suppressClick || !stage.querySelector(".oe-widget.is-selected")) return;
                // Keep the selection while editing its properties or adding a module.
                // Clicks inside the canvas already use the Blazor selection handlers.
                if (stage.contains(e.target) || inspector?.contains(e.target)
                    || (root.contains(e.target) && e.target.closest("[data-module-kind]"))) return;
                invoke("ClearSelection");
            };
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
            const wheel = e => {
                if (e.ctrlKey) return;
                // Focused native controls otherwise consume the wheel and can
                // silently resize the overlay. Leave scrolling to the browser.
                const field = e.target.closest("input[type=number], input[type=range], select");
                if (field && field === document.activeElement) field.blur();
            };
            root.addEventListener("pointerdown", down);
            root.addEventListener("pointermove", move);
            root.addEventListener("pointerup", up);
            root.addEventListener("pointercancel", up);
            root.addEventListener("click", click, true);
            root.addEventListener("keydown", key);
            root.addEventListener("wheel", wheel, { capture: true, passive: true });
            document.addEventListener("click", outsideClick);
            const resizeObserver = new ResizeObserver(fit); resizeObserver.observe(viewport);
            const mutationObserver = new MutationObserver(fit);
            mutationObserver.observe(stage, { attributes: true, subtree: true, childList: true, characterData: true,
                attributeFilter: ["data-width", "data-height", "data-content-width", "data-content-height", "data-min-content-width", "data-min-content-height", "data-content-layout"] });
            fit();
            editors.set(id, () => {
                disposed = true;
                if (contentFrame !== null) cancelAnimationFrame(contentFrame);
                cleanupDrag(); resizeObserver.disconnect(); mutationObserver.disconnect();
                root.removeEventListener("pointerdown", down); root.removeEventListener("pointermove", move);
                root.removeEventListener("pointerup", up); root.removeEventListener("pointercancel", up);
                root.removeEventListener("click", click, true); root.removeEventListener("keydown", key);
                root.removeEventListener("wheel", wheel, true);
                document.removeEventListener("click", outsideClick);
            });
        },
        unmount(id) { editors.get(id)?.(); editors.delete(id); }
    };
})();
