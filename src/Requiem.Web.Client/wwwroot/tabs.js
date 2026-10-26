// Tab strips scroll natively (app.css turns off MudBlazor's sliding, which fought the browser's own scrolling and
// could leave the strip blank). This keeps the active tab in view however it became active: a tap, the keyboard
// or code. It moves only the strip, never the page.
(() => {
    // A strip changes width after a tab change when the new panel's content brings or removes the page scrollbar.
    const resized = new ResizeObserver(entries => {
        for (const e of entries) {
            const tab = e.target.querySelector(".mud-tab-active");
            if (tab) show(tab);
        }
    });

    function show(tab) {
        const strip = tab.closest(".mud-tabs-tabbar-content");
        if (!strip) return;
        resized.observe(strip);
        const t = tab.getBoundingClientRect(), s = strip.getBoundingClientRect();
        if (t.left < s.left) strip.scrollLeft -= s.left - t.left;
        else if (t.right > s.right) strip.scrollLeft += t.right - s.right;
    }

    new MutationObserver(records => {
        for (const r of records) {
            if (r.type === "attributes") {
                if (r.target.classList.contains("mud-tab-active") && !r.oldValue?.includes("mud-tab-active")) show(r.target);
            } else {
                for (const node of r.addedNodes) {
                    const tab = node.nodeType === 1 && (node.matches(".mud-tab-active") ? node : node.querySelector(".mud-tab-active"));
                    if (tab) show(tab);
                }
            }
        }
    }).observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ["class"], attributeOldValue: true });
})();
