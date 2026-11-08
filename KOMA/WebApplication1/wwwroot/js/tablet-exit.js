(() => {
    const dialog = document.getElementById("tablet-exit");
    const form = dialog.querySelector("form");
    const error = form.querySelector('[role="alert"]');
    const submit = form.querySelector('[type="submit"]');
    document.getElementById("open-tablet-exit").addEventListener("click", () => {
        form.reset();
        error.textContent = "";
        dialog.showModal();
    });
    document.getElementById("cancel-tablet-exit").addEventListener("click", () => dialog.close());
    form.addEventListener("submit", async event => {
        event.preventDefault();
        submit.disabled = true;
        try {
            const response = await fetch(form.action, { method: "POST", body: new FormData(form) });
            if (response.redirected && response.ok) {
                location.replace(response.url);
                return;
            }
            error.textContent = response.status === 403 ? await response.text() : "Could not exit. Try again.";
            form.elements.pin.select();
        } catch {
            error.textContent = "Could not reach the server. Try again.";
        } finally {
            submit.disabled = false;
        }
    });
})();