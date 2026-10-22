(() => {
    "use strict";
    const form = document.getElementById("preorder-form");
    if (!form) return;
    const cards = [...document.querySelectorAll(".po-dish")];
    const lines = document.getElementById("cart-lines");
    const dialog = document.getElementById("review-dialog");
    const review = document.getElementById("review-order");
    const currency = cents => "RM " + (cents / 100).toLocaleString("en-MY", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const items = cards.map(card => ({
        card, name: card.dataset.name, cents: Math.round(Number(card.dataset.price) * 100),
        input: card.querySelector("input"), category: card.dataset.category
    }));
    const qty = item => Math.max(0, Math.min(99, Math.trunc(Number(item.input.value) || 0)));
    const el = (tag, text, className) => {
        const node = document.createElement(tag);
        if (text !== undefined) node.textContent = text;
        if (className) node.className = className;
        return node;
    };
    const button = (label, action, className, accessibleLabel) => {
        const node = el("button", label, className);
        node.type = "button";
        if (accessibleLabel) node.setAttribute("aria-label", accessibleLabel);
        node.addEventListener("click", action);
        return node;
    };
    function change(item, delta) {
        item.input.value = Math.max(0, Math.min(99, qty(item) + delta));
        render();
    }
    function render() {
        const focusKey = document.activeElement?.dataset.focusKey;
        lines.replaceChildren();
        let total = 0, count = 0;
        for (const item of items) {
            const quantity = qty(item);
            item.card.classList.toggle("is-selected", quantity > 0);
            item.card.querySelector('[data-delta="-1"]').disabled = quantity === 0;
            item.card.querySelector('[data-delta="1"]').disabled = quantity === 99;
            if (!quantity) continue;
            count += quantity; total += quantity * item.cents;
            const row = el("div", undefined, "po-cart-line");
            row.append(el("p", item.name), el("strong", currency(quantity * item.cents)), el("small", currency(item.cents) + " each"));
            const stepper = el("div", undefined, "po-stepper");
            for (const delta of [-1, 1]) {
                const control = button(delta < 0 ? "−" : "+", () => change(item, delta), "", (delta < 0 ? "Remove one " : "Add one ") + item.name);
                control.dataset.focusKey = item.card.dataset.id + ":" + delta;
                control.disabled = delta < 0 ? quantity === 0 : quantity === 99;
                stepper.append(control);
                if (delta < 0) stepper.append(el("span", String(quantity)));
            }
            const remove = button("Remove", () => { item.input.value = 0; render(); item.input.focus(); }, "po-remove", "Remove " + item.name + " from cart");
            row.append(stepper, remove); lines.append(row);
        }
        document.getElementById("cart-empty").hidden = count > 0;
        document.getElementById("cart-count").textContent = String(count);
        document.getElementById("cart-total").textContent = currency(total);
        document.getElementById("expected-total").value = (total / 100).toFixed(2);
        review.disabled = count === 0;
        if (focusKey) [...lines.querySelectorAll("button")].find(b => b.dataset.focusKey === focusKey)?.focus();
    }
    for (const item of items) {
        item.card.querySelectorAll("[data-delta]").forEach(b => b.addEventListener("click", () => change(item, Number(b.dataset.delta))));
        item.input.addEventListener("input", render);
        item.input.addEventListener("change", () => { item.input.value = qty(item); render(); });
        item.card.querySelector("img")?.addEventListener("error", event => { event.target.hidden = true; });
    }
    let category = "all";
    function filter() {
        const search = document.getElementById("menu-search").value.trim().toLocaleLowerCase();
        let visible = 0;
        for (const item of items) {
            const match = (category === "all" || category === item.category) &&
                (item.name + " " + item.card.dataset.description).toLocaleLowerCase().includes(search);
            item.card.hidden = !match;
            if (match) visible++;
        }
        document.getElementById("menu-count").textContent = visible + (visible === 1 ? " dish to discover" : " dishes to discover");
        document.getElementById("menu-empty").hidden = visible > 0;
    }
    document.querySelectorAll(".po-filters button").forEach(b => b.addEventListener("click", () => {
        category = b.dataset.category;
        document.querySelectorAll(".po-filters button").forEach(other => {
            other.classList.toggle("is-active", other === b); other.setAttribute("aria-pressed", String(other === b));
        });
        filter();
    }));
    document.getElementById("menu-search").addEventListener("input", filter);
    review.addEventListener("click", () => {
        if (!form.reportValidity()) return;
        const summary = document.getElementById("review-lines");
        summary.replaceChildren();
        for (const item of items.filter(item => qty(item) > 0)) {
            const row = el("div", undefined, "po-review-line");
            row.append(el("span", qty(item) + " × " + item.name), el("strong", currency(qty(item) * item.cents)));
            summary.append(row);
        }
        document.getElementById("review-total").textContent = document.getElementById("cart-total").textContent;
        dialog.showModal();
    });
    document.getElementById("keep-browsing").addEventListener("click", () => dialog.close());
    form.addEventListener("submit", event => {
        if (!dialog.open) { event.preventDefault(); review.click(); return; }
        document.getElementById("confirm-order").disabled = true;
        document.getElementById("confirm-order").textContent = "Saving…";
    });
    render();
})();
