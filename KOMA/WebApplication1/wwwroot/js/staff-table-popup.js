(() => {
    const host = document.getElementById("floor-host");
    const modalEl = document.getElementById("table-modal");

    if (!host || !modalEl) return;

    const body = document.getElementById("table-modal-body");
    const title = document.getElementById("table-modal-title");
    const outsideError = document.getElementById("workspace-error");

    if (!window.bootstrap?.Modal) {
        outsideError.textContent =
            "The popup could not start. Check that Bootstrap JavaScript is loaded.";
        return;
    }

    const modal = bootstrap.Modal.getOrCreateInstance(modalEl);

    let dirty = false;
    let busy = false;
    let tableId = null;
    let requestNumber = 0;
    let displayStale = false;

    const dirtyForms = new Set();

    function drawChairs() {
        if (!window.KomaChairs?.positions) return;

        host.querySelectorAll(".reservation-map").forEach(box => {
            box.querySelectorAll(".lay-preview-chair")
                .forEach(chair => chair.remove());

            box.querySelectorAll(".reservation-map-table").forEach(el => {
                const chairs = KomaChairs.positions({
                    x: Number(el.dataset.x),
                    y: Number(el.dataset.y),
                    w: Number(el.dataset.w),
                    h: Number(el.dataset.h),
                    shape: el.dataset.shape,
                    capacity: Number(el.dataset.capacity)
                });

                chairs.forEach(c => {
                    const chair = document.createElement("div");

                    chair.className =
                        "lay-preview-chair" +
                        (c.kind === "bench" ? " is-bench" : "");

                    chair.setAttribute("aria-hidden", "true");

                    Object.assign(chair.style, {
                        left: c.x / Number(box.dataset.floorW) * 100 + "%",
                        top: c.y / Number(box.dataset.floorH) * 100 + "%",
                        width: c.w / Number(box.dataset.floorW) * 100 + "%",
                        height: c.h / Number(box.dataset.floorH) * 100 + "%"
                    });

                    box.insertBefore(chair, el);
                });
            });
        });
    }

    async function getHtml(url) {
        const response = await fetch(url, {
            credentials: "same-origin",
            cache: "no-store"
        });

        if (response.redirected) {
            throw new Error(
                "Your sign-in may have expired. Reload and sign in again.");
        }

        if (!response.ok) {
            throw new Error(
                "The selection changed or could not be loaded. " +
                "Close the popup and refresh.");
        }

        return response.text();
    }

    function showMessage(message, ok = false) {
        let target = body.querySelector(".panel-message");

        if (!target) {
            target = document.createElement("div");
            body.prepend(target);
        }

        target.className =
            "panel-message alert " +
            (ok ? "alert-success" : "alert-danger");

        target.setAttribute("role", "status");
        target.textContent = message;
    }

    async function openTable(
        id,
        bookingId = null,
        sessionId = null,
        tab = null
    ) {
        const sequence = ++requestNumber;

        tableId = id;
        dirty = false;
        dirtyForms.clear();

        title.textContent = "Loading table details…";
        body.textContent = "Loading…";
        modal.show();

        try {
            const board = host.querySelector(".floor-board");
            const url = new URL(board.dataset.panelUrl, location.origin);

            url.searchParams.set("TableId", id);

            if (bookingId) {
                url.searchParams.set("BookingId", bookingId);
            }

            if (sessionId) {
                url.searchParams.set("SessionId", sessionId);
            }

            const html = await getHtml(url);

            // Ignore an older response if the popup was closed or changed.
            if (sequence !== requestNumber) return false;

            // This is server-rendered Razor HTML from our own application.
            body.innerHTML = html;

            const panel = body.querySelector(".table-panel");

            if (!panel) {
                throw new Error("The table details could not be displayed.");
            }

            title.textContent = panel.dataset.title;

            if (tab && body.querySelector(tab)) {
                bootstrap.Tab
                    .getOrCreateInstance(body.querySelector(tab))
                    .show();
            }

            return true;
        } catch (error) {
            if (sequence === requestNumber) {
                title.textContent = "Table details";
                body.textContent = error.message;
            }

            return false;
        }
    }

    host.addEventListener("click", event => {
        const button = event.target.closest("[data-open-table]");

        if (!button || busy) return;

        if (displayStale) {
            outsideError.textContent =
                "Refresh the page before opening another table.";
            return;
        }

        openTable(button.dataset.openTable);
    });

    function markDirty(event) {
        const form = event.target.closest("form");

        if (form) {
            dirty = true;
            dirtyForms.add(form);
        }
    }

    body.addEventListener("input", markDirty);
    body.addEventListener("change", markDirty);

    body.addEventListener("click", event => {
        const button = event.target.closest(
            "[data-booking], [data-session]");

        if (!button || busy || displayStale) return;

        if (dirty && !confirm("Discard unsaved changes?")) return;

        openTable(
            tableId,
            button.dataset.booking,
            button.dataset.session);
    });

    modalEl.addEventListener("hide.bs.modal", event => {
        if (busy || (dirty && !confirm("Discard unsaved changes?"))) {
            event.preventDefault();
            return;
        }

        ++requestNumber;
        dirty = false;
        dirtyForms.clear();
    });

    window.addEventListener("beforeunload", event => {
        if (dirty || busy) {
            event.preventDefault();
            event.returnValue = "";
        }
    });

    body.addEventListener("submit", async event => {
        const form = event.target;

        if (!(form instanceof HTMLFormElement)) return;

        event.preventDefault();

        if (busy || !form.reportValidity()) return;

        if (displayStale) {
            showMessage("Refresh the page before making another change.");
            return;
        }

        const otherFormChanged =
            [...dirtyForms].some(other => other !== form);

        if (otherFormChanged && !confirm(
            "This action will refresh the popup and discard changes " +
            "in the other form. Continue?"
        )) {
            return;
        }

        const data = new FormData(form);

        // Include the clicked button, such as Confirm or Cancel.
        if (event.submitter?.name) {
            data.append(
                event.submitter.name,
                event.submitter.value);
        }

        const activeTab = body.querySelector("#food-tab.active")
            ? "#food-tab"
            : "#booking-tab";

        const postedUrl = new URL(form.action);

        const cancelled =
            postedUrl.searchParams.get("handler") === "BookingStatus" &&
            data.get("confirm") === "false";

        const keepBooking = cancelled
            ? null
            : postedUrl.searchParams.get("BookingId");

        const keepSession = postedUrl.searchParams.get("SessionId");

        const controls = [...body.querySelectorAll(
            "input, select, textarea, button")];

        const previousDisabled = controls.map(control => control.disabled);

        busy = true;
        controls.forEach(control => control.disabled = true);

        let saved = false;

        try {
            const response = await fetch(form.action, {
                method: "POST",
                body: data,
                credentials: "same-origin"
            });

            if (response.redirected ||
                !response.ok ||
                !response.headers.get("content-type")
                    ?.includes("application/json")) {
                throw new Error(
                    "The request could not be completed. " +
                    "Refresh and check its result before retrying.");
            }

            const result = await response.json();

            if (!result.ok) {
                showMessage(result.message);
                return;
            }

            saved = true;
            dirty = false;
            dirtyForms.clear();

            const refreshUrl = host.querySelector(".floor-board")
                .dataset.refreshUrl;

            host.innerHTML = await getHtml(refreshUrl);
            drawChairs();

            const reopened = await openTable(
                tableId,
                keepBooking,
                keepSession,
                activeTab);

            if (!reopened) {
                throw new Error("The selected visit changed.");
            }

            outsideError.textContent = "";
            showMessage(result.message, true);
        } catch (error) {
            // A failed response can happen after the server saved.
            // Require a fresh page before another write.
            displayStale = true;

            showMessage(saved
                ? "Saved, but the display could not refresh. " +
                  "Close this popup and refresh the page."
                : error.message);

            outsideError.textContent =
                "Refresh the page and check the latest table information " +
                "before making another change.";
        } finally {
            busy = false;

            controls.forEach((control, index) => {
                if (control.isConnected) {
                    control.disabled = previousDisabled[index];
                }
            });
        }
    });

    drawChairs();
})();