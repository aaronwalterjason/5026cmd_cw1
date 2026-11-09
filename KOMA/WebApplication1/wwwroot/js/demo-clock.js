(() => {
    "use strict";
    const host = document.getElementById("demo-clock");
    if (!host) return;
    if (window.self !== window.top) { host.hidden = true; return; }
    const toggle = document.getElementById("demo-clock-toggle");
    const dialog = document.getElementById("demo-clock-dialog");
    const form = document.getElementById("demo-clock-form");
    const input = document.getElementById("demo-clock-input");
    const error = document.getElementById("demo-clock-error");
    const reset = document.getElementById("demo-clock-reset");
    const version = host.dataset.version;
    let serverTime = Date.parse(host.dataset.now), receivedAt = performance.now();
    let simulated = host.dataset.simulated === "true", busy = false;
    const formatter = new Intl.DateTimeFormat("en-GB", {
        timeZone: "Asia/Kuala_Lumpur", day: "2-digit", month: "short", year: "numeric",
        hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23"
    });
    function current() { return serverTime + performance.now() - receivedAt; }
    function paint() {
        document.getElementById("demo-clock-now").textContent = formatter.format(current());
        document.getElementById("demo-clock-mode").textContent = simulated ? "Demo time is active" : "Real time";
        toggle.classList.toggle("is-simulated", simulated);
        toggle.querySelector(".demo-clock-badge").hidden = !simulated;
        toggle.title = `${simulated ? "Demo" : "Real"} time: ${formatter.format(current())} · drag to move`;
    }
    async function sync() {
        if (busy || document.hidden) return;
        try {
            const response = await fetch(host.dataset.url, { cache: "no-store" });
            if (!response.ok || response.redirected) throw new Error("Cannot connect to the clock. Try again.");
            const state = await response.json();
            if (state.version !== version) { location.reload(); return; }
            serverTime = Date.parse(state.now); receivedAt = performance.now(); simulated = state.simulated;
            paint();
        } catch (e) { if (dialog.open) error.textContent = e.message; }
    }
    toggle.addEventListener("click", event => {
        if (dragged && event.detail !== 0) { dragged = false; return; }
        // datetime-local has no timezone: explicitly format the Malaysia wall clock.
        input.value = new Date(current() + 8 * 3600000).toISOString().slice(0, 16);
        error.textContent = "";
        dialog.showModal();
        sync();
    });
    document.getElementById("demo-clock-close").addEventListener("click", () => dialog.close());
    async function change(handler) {
        if (busy) return;
        busy = true; error.textContent = "";
        const buttons = [...form.querySelectorAll("button")];
        buttons.forEach(b => b.disabled = true);
        try {
            const url = new URL(host.dataset.url, location.origin);
            url.searchParams.set("handler", handler);
            const response = await fetch(url, { method: "POST", body: new FormData(form) });
            if (!response.ok || response.redirected)
                throw new Error(response.status === 400 ? "Choose a valid date and time, or refresh the page and retry." : "Could not change the clock. Try again.");
            location.reload();
        } catch (e) { error.textContent = e.message; }
        finally { busy = false; buttons.forEach(b => b.disabled = false); }
    }
    form.addEventListener("submit", event => { event.preventDefault(); if (form.reportValidity()) change("Set"); });
    reset.addEventListener("click", () => change("Reset"));

    let drag = null, dragged = false;
    function position(x, y, save = false) {
        x = Math.max(8, Math.min(x, innerWidth - 64));
        y = Math.max(8, Math.min(y, innerHeight - 70));
        Object.assign(host.style, { left: x + "px", top: y + "px", right: "auto", bottom: "auto" });
        if (save) try { localStorage.setItem("koma-demo-clock-position", JSON.stringify({ x, y })); } catch { }
    }
    try {
        const stored = JSON.parse(localStorage.getItem("koma-demo-clock-position"));
        if (Number.isFinite(stored?.x) && Number.isFinite(stored?.y)) position(stored.x, stored.y);
    } catch { }
    toggle.addEventListener("pointerdown", event => {
        if (event.button !== 0) return;
        const rect = host.getBoundingClientRect();
        drag = { id: event.pointerId, x: event.clientX, y: event.clientY, left: rect.left, top: rect.top };
        dragged = false;
        toggle.setPointerCapture(event.pointerId);
    });
    toggle.addEventListener("pointermove", event => {
        if (!drag || drag.id !== event.pointerId) return;
        const dx = event.clientX - drag.x, dy = event.clientY - drag.y;
        if (Math.hypot(dx, dy) > 6) dragged = true;
        if (dragged) position(drag.left + dx, drag.top + dy);
    });
    function finishDrag(event) {
        if (!drag || drag.id !== event.pointerId) return;
        if (dragged) { const rect = host.getBoundingClientRect(); position(rect.left, rect.top, true); }
        drag = null;
        if (toggle.hasPointerCapture(event.pointerId)) toggle.releasePointerCapture(event.pointerId);
    }
    toggle.addEventListener("pointerup", finishDrag);
    toggle.addEventListener("pointercancel", finishDrag);
    window.addEventListener("resize", () => { const r = host.getBoundingClientRect(); position(r.left, r.top); });
    document.addEventListener("visibilitychange", () => { if (!document.hidden) sync(); });
    paint();
    setInterval(paint, 1000);
    setInterval(sync, 3000);
})();
