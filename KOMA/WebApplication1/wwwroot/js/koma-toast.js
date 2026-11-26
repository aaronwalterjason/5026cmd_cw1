// Shared pop-up after an action (markup: Pages/Shared/_Toast.cshtml, styles: css/koma-toast.css).
//
// 1) After a normal form post + redirect, the server sets TempData and the layout shows the pop-up; this script
//    fades it out after a few seconds and makes × close it.
// 2) From a script (fetch actions):
//      KomaToast.show({ text, title, kind })   show now. kind: 'ok' (green tick, default) | 'note' (amber !)
//      KomaToast.next({ text, title, kind })   show after the page reloads (e.g. before location.reload())
(() => {
    const STAY = 3200, KEY = 'koma.toast.next';

    function stack() {
        let s = document.getElementById('kt-stack');
        if (!s) {
            s = document.createElement('div');
            s.id = 'kt-stack'; s.className = 'kt-stack'; s.setAttribute('aria-live', 'polite');
            document.body.appendChild(s);
        }
        return s;
    }

    function hide(el) {
        if (!el || el.classList.contains('is-leaving')) return;
        el.classList.add('is-leaving');
        setTimeout(() => el.remove(), 300);
    }

    function wire(el) {
        el.querySelector('.kt-close')?.addEventListener('click', () => hide(el));
        // A note (something didn't happen) stays a little longer so it can be read.
        let timer = setTimeout(() => hide(el), el.classList.contains('is-note') ? STAY * 2 : STAY);
        el.addEventListener('mouseenter', () => clearTimeout(timer));
        el.addEventListener('mouseleave', () => { timer = setTimeout(() => hide(el), 1200); });
    }

    function show(o) {
        o = typeof o === 'string' ? { text: o } : (o || {});
        const note = o.kind === 'note';
        const el = document.createElement('div');
        el.className = 'kt' + (note ? ' is-note' : '');
        el.setAttribute('role', note ? 'alert' : 'status');
        el.innerHTML = '<span class="kt-icon" aria-hidden="true"><svg viewBox="0 0 24 24">'
            + (note ? '<path d="M12 7v6"/><path d="M12 17h.01"/>' : '<path d="M5 12.5l4.5 4.5L19 7.5"/>')
            + '</svg></span><span class="kt-text"><strong></strong><span></span></span>'
            + '<button type="button" class="kt-close" aria-label="Close">&times;</button>';
        el.querySelector('strong').textContent = o.title || (note ? 'Not done' : 'Done');
        el.querySelector('.kt-text > span').textContent = o.text || '';
        stack().appendChild(el);
        wire(el);
        return el;
    }

    function next(o) {
        try { sessionStorage.setItem(KEY, JSON.stringify(typeof o === 'string' ? { text: o } : o)); } catch (_) { }
    }

    function start() {
        document.querySelectorAll('#kt-stack .kt').forEach(wire);
        try {
            const saved = sessionStorage.getItem(KEY);
            if (saved) { sessionStorage.removeItem(KEY); show(JSON.parse(saved)); }
        } catch (_) { }
    }

    window.KomaToast = { show, next };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
})();
