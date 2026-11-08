(() => {
    const form = document.querySelector("[data-slots-url]");
    if (!form) return;
    const { Input_Date: date, Input_Time: time, Input_GuestCount: guests, Input_PreferredZone: zone, Input_RestaurantTableId: table } = form.elements;
    let request = 0;
    async function refresh() {
        const sequence = ++request;
        if (!date.value || !guests.value) return;
        const url = new URL(form.dataset.slotsUrl, location.origin);
        for (const [key, field] of Object.entries({ date, guests, zone, table })) {
            if (field?.value) url.searchParams.set(key, field.value);
        }
        try {
            const response = await fetch(url);
            if (!response.ok) return;
            const available = await response.json();
            if (request !== sequence) return;
            const previous = time.value;
            time.replaceChildren(...available.map(value => new Option(value, value)));
            if (available.includes(previous)) time.value = previous;
            if (!time.options.length) time.add(new Option("No available times", ""));
            time.required = true;
        } catch { /* The server checks availability again on save. */ }
    }
    for (const field of [date, guests, zone, table]) field?.addEventListener("change", refresh);
    refresh();
})();
