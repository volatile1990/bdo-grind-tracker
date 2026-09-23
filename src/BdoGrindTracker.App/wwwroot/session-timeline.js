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
    width(element) {
        const measured = element ? element.getBoundingClientRect().width : 0;
        return measured > 0 ? measured : 0;
    }
};
