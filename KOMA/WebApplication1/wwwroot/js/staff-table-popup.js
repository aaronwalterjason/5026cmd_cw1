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



    let tableId = null;
    let requestNumber = 0;




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

    async function openTable(
        id,
        bookingId = null,
        sessionId = null,
        tab = null
    ) {
        const sequence = ++requestNumber;

        tableId = id;



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

        if (button) openTable(button.dataset.openTable);
    });

    body.addEventListener("click", event => {
        if (event.target.closest("[data-open-payment]")) body.querySelector(".visit-payment").showModal();
        if (event.target.closest("[data-close-payment]")) body.querySelector(".visit-payment").close();
        const button = event.target.closest("[data-booking], [data-session]");
        if (button) openTable(tableId, button.dataset.booking, button.dataset.session);
    });
    body.addEventListener("submit", async event => {
        const form = event.target.closest(".payment-form, .walk-in-move");
        if (!form) return;
        event.preventDefault();
        const data = new FormData(form);
        const submit = form.querySelector("button");
        submit.disabled = true;
        try {
            const response = await fetch(form.action, { method: "POST", body: data });
            if (form.matches(".walk-in-move")) {
                if (!response.ok || response.redirected) throw new Error("Could not move the guest. Reopen the table to check availability.");
                location.reload();
                return;
            }
            form.closest("dialog").close();
            if (!response.ok || response.redirected) throw new Error("Payment could not be confirmed. Reopen Food orders to check the bill.");
            await openTable(data.get("TableId"), data.get("BookingId"), data.get("SessionId"), "#food-tab");
        } catch (error) {
            form.closest("dialog")?.close();
            submit.disabled = false;
            body.querySelector(".panel-message").textContent = error.message;
        }
    });
    modalEl.addEventListener("hide.bs.modal", () => { ++requestNumber; });
    drawChairs();
})();
