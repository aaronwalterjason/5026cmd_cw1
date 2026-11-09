(() => {
    "use strict";
    const bill = document.getElementById("visit-bill-dialog");
    const content = document.getElementById("visit-bill-content");
    const message = document.getElementById("bill-message");
    const warning = document.getElementById("last-call-dialog");
    const controls = document.getElementById("ordering-controls");
    const form = document.getElementById("preorder-form");
    const initialVisit = content.querySelector("#visit-bill-state").dataset.visit;
    let deadline, locked, busy = false, refreshing = false, warningKey;
    const url = handler => { const u = new URL(location.href); u.searchParams.set("handler", handler); return u; };
    function readHours() {
        const state = content.querySelector("#visit-bill-state").dataset;
        const remaining = new Date(state.cutoff) - new Date(state.now);
        deadline = performance.now() + remaining;
        locked = state.locked === "true";
        warningKey = `last-call:${initialVisit}:${state.cutoff}`;
        document.getElementById("last-call-text").textContent =
            `Last call is at ${state.cutoff.slice(11, 16)}. Any more food to add? Please confirm your order before then.`;
        tick();
    }
    function tick() {
        const remaining = deadline - performance.now();
        controls.disabled = locked || remaining <= 0;
        document.getElementById("ordering-hours-message").hidden = !controls.disabled;
        if (controls.disabled) {
            document.getElementById("review-dialog").close();
            warning.close();
        } else if (remaining <= 600000 && !bill.open && !warning.open && !document.getElementById("review-dialog").open) {
            let shown = false;
            try { shown = sessionStorage.getItem(warningKey) === "1"; } catch { }
            if (!shown && warning.dataset.shown !== warningKey) {
                warning.dataset.shown = warningKey;
                try { sessionStorage.setItem(warningKey, "1"); } catch { }
                warning.showModal();
            }
        }
    }
    async function refresh() {
        if (refreshing || busy) return;
        refreshing = true;
        try {
            const response = await fetch(url("Bill"), { cache: "no-store" });
            if (!response.ok || response.redirected) throw new Error("Unable to refresh this visit. Reload or ask staff for help.");
            const html = await response.text();
            const parsed = new DOMParser().parseFromString(html, "text/html");
            if (parsed.querySelector("#visit-bill-state")?.dataset.visit !== initialVisit)
                throw new Error("The table visit changed. Reload this tablet.");
            if (!busy) { content.innerHTML = html; readHours(); }
            message.textContent = "";
        } catch (error) { message.textContent = error.message; }
        finally { refreshing = false; }
    }
    document.getElementById("open-visit-bill").addEventListener("click", () => { bill.showModal(); refresh(); });
    document.getElementById("close-visit-bill").addEventListener("click", () => bill.close());
    document.getElementById("close-last-call").addEventListener("click", () => warning.close());
    content.addEventListener("change", async event => {
        const checkbox = event.target.closest("[data-serve-dish]");
        if (!checkbox) return;
        checkbox.checked = false;
        if (busy) return;
        const pin = window.prompt("Staff: enter your tablet PIN to mark this dish served.");
        if (pin === null) return;
        busy = true; checkbox.disabled = true;
        try {
            const data = new FormData();
            data.set("__RequestVerificationToken", form.querySelector('[name="__RequestVerificationToken"]').value);
            data.set("itemId", checkbox.dataset.serveDish);
            data.set("visitId", initialVisit);
            data.set("pin", pin);
            const response = await fetch(url("Served"), { method: "POST", body: data });
            if (!response.ok || response.redirected) throw new Error(response.status === 403 || response.status === 409 ? await response.text() : "Could not update this dish. Refresh and try again.");
            busy = false;
            await refresh();
        } catch (error) { message.textContent = error.message; }
        finally { busy = false; checkbox.disabled = false; }
    });
    form.addEventListener("submit", event => {
        tick();
        if (controls.disabled) { event.preventDefault(); event.stopImmediatePropagation(); }
    }, true);
    readHours();
    setInterval(tick, 1000);
    setInterval(() => { if (!document.hidden) refresh(); }, 10000);
    document.addEventListener("visibilitychange", () => { if (!document.hidden) { tick(); refresh(); } });
})();
