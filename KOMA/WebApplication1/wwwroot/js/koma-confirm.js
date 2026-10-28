// Shared confirmation pop-up for important decisions (markup: Pages/Shared/_ConfirmDialog.cshtml,
// styles: ".kc" in koma-theme.css).
//
// 1) Any <form data-confirm-title="..."> asks before it submits. Optional attributes:
//      data-confirm-text   sentence under the title
//      data-confirm-ok     one-word confirm button   (default "Confirm")
//      data-confirm-no     one-word cancel button    (default "Cancel")
//      data-confirm-tone   danger (red, default) | warn (amber) | navy
//      data-confirm-icon   trash (default) | x | shield | check | undo | info | plus
//    Works for forms added later too (e.g. the order detail panel that is loaded by fetch).
// 2) From a script: KomaConfirm.open({ title, text, rows, ok, no, tone, icon, onOk }).
//    rows: [{ label, from, to }] or ready-made <li> strings. Without onOk it is a message with one "OK" button.
(() => {
    const dlg = document.getElementById('koma-confirm');
    if (!dlg) return;

    const ICONS = {
        trash: '<path d="M3 6h18"/><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/>',
        x: '<circle cx="12" cy="12" r="9"/><path d="M15 9l-6 6M9 9l6 6"/>',
        shield: '<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>',
        check: '<path d="M5 12.5l4.5 4.5L19 7.5"/>',
        undo: '<path d="M3 7v6h6"/><path d="M21 17a9 9 0 0 0-15-6.7L3 13"/>',
        info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v6"/><path d="M12 7.5h.01"/>',
        plus: '<path d="M12 5v14M5 12h14"/>'
    };
    const $ = sel => dlg.querySelector(sel);
    const esc = t => String(t ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
    let pending = null;

    function open(o) {
        const tone = o.tone || 'danger';
        dlg.dataset.tone = tone;
        $('.kc-band').innerHTML = `<svg viewBox="0 0 24 24">${ICONS[o.icon] || ICONS.trash}</svg>`;
        $('.kc-title').textContent = o.title || 'Are you sure?';
        const text = $('.kc-text');
        text.textContent = o.text || '';
        text.hidden = !o.text;
        const rows = o.rows || [];
        const list = $('.kc-list');
        list.innerHTML = rows.map(r => typeof r === 'string' ? r
            : `<li><span>${esc(r.label)}</span><s>${esc(r.from)}</s><b aria-hidden="true">→</b><strong>${esc(r.to)}</strong></li>`).join('');
        list.hidden = !rows.length;

        const ok = $('.kc-ok'), no = $('.kc-no');
        ok.hidden = !o.onOk;
        ok.textContent = o.ok || 'Confirm';
        ok.className = 'btn kc-ok ' + (tone === 'danger' ? 'mn-btn-danger-solid' : 'btn-primary');
        no.textContent = o.onOk ? (o.no || 'Cancel') : 'OK';
        pending = o.onOk || null;

        if (dlg.open) dlg.close();
        dlg.showModal();
        // Saves focus the confirm button; risky choices focus the safe one, so Enter never deletes by accident.
        (o.onOk && tone === 'navy' ? ok : no).focus();
    }

    $('.kc-ok').addEventListener('click', () => { const go = pending; pending = null; dlg.close(); go && go(); });
    $('.kc-no').addEventListener('click', () => dlg.close());
    dlg.addEventListener('click', e => { if (e.target === dlg) dlg.close(); });   // click outside closes
    dlg.addEventListener('close', () => { pending = null; });

    document.addEventListener('submit', e => {
        const form = e.target;
        if (!(form instanceof HTMLFormElement) || !form.dataset.confirmTitle) return;
        e.preventDefault();
        const d = form.dataset;
        open({
            title: d.confirmTitle, text: d.confirmText, ok: d.confirmOk, no: d.confirmNo,
            tone: d.confirmTone, icon: d.confirmIcon,
            onOk: () => HTMLFormElement.prototype.submit.call(form)   // real submit, skips this check
        });
    });

    window.KomaConfirm = { open, close: () => dlg.close() };
})();
