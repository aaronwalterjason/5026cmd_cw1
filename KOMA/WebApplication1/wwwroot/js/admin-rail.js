// Admin icon rail: flyouts open on hover/keyboard focus (CSS), and on tap/click for touch screens.
(function () {
    const rail = document.getElementById('admin-rail');
    if (!rail) return;
    const groups = rail.querySelectorAll('.admin-rail-group');

    function closeAll(except) {
        groups.forEach(g => {
            if (g === except) return;
            g.classList.remove('is-open');
            const b = g.querySelector('button.admin-rail-btn');
            if (b) b.setAttribute('aria-expanded', 'false');
        });
    }

    rail.querySelectorAll('button.admin-rail-btn').forEach(btn => {
        btn.addEventListener('click', e => {
            e.stopPropagation();
            const group = btn.closest('.admin-rail-group');
            const open = !group.classList.contains('is-open');
            closeAll(group);
            group.classList.toggle('is-open', open);
            btn.setAttribute('aria-expanded', String(open));
            if (open) group.querySelector('.admin-flyout a')?.focus({ preventScroll: true });
        });
    });

    document.addEventListener('click', e => { if (!rail.contains(e.target)) closeAll(); });
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape') {
            const open = rail.querySelector('.admin-rail-group.is-open button');
            closeAll();
            open?.focus();
        }
    });
})();
