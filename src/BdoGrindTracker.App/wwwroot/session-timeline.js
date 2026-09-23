// The session timeline zooms with Shift + wheel. Blazor can only prevent a wheel's default statically, which would
// stop the page from scrolling whenever the pointer crosses the timeline, so the decision is made here.
window.sessionTimeline = {
    attach(element) {
        if (!element || element.dataset.wheelBound === "1") return;
        element.dataset.wheelBound = "1";
        element.addEventListener("wheel", event => {
            if (event.shiftKey) event.preventDefault();
        }, { passive: false });
    },
    // The pointer keeps belonging to the timeline while it is dragged, even when it leaves the element or the
    // window. Without that a drag ends silently as soon as the pointer crosses the edge.
    capture(element, pointerId) {
        try { element?.setPointerCapture(pointerId); } catch { /* the pointer is already gone */ }
    },
    release(element, pointerId) {
        try { element?.releasePointerCapture(pointerId); } catch { /* it was never captured */ }
    },
    width(element) {
        const measured = element ? element.getBoundingClientRect().width : 0;
        return measured > 0 ? measured : 0;
    }
};
