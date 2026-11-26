// Floor size picker for Admin > Floor Layouts > Create/Edit.
// The admin drags on a metre grid to draw the floor; the Width/Height inputs stay in sync.
(function () {
    const drawer = document.getElementById('floor-drawer');
    if (!drawer) return;

    const grid = document.getElementById('floor-drawer-grid');
    const rect = document.getElementById('floor-drawer-rect');
    const label = document.getElementById('floor-drawer-size');
    const widthInput = document.getElementById('floor-width');
    const heightInput = document.getElementById('floor-height');
    const maxW = +drawer.dataset.maxW, maxH = +drawer.dataset.maxH, min = +drawer.dataset.min;

    let origin = { x: 0, y: 0 };   // top-left of the drawn rectangle, in metres (display only)
    let start = null;

    const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
    // Pixels per metre. Uses the smaller of width and height, so maxW x maxH metres always fit inside the grid
    // even if the grid isn't exactly 60:40 on some screen.
    const cell = () => Math.min(grid.clientWidth / maxW, grid.clientHeight / maxH);

    // Pointer position in whole metres, measured inside the grid's border, never past the edges.
    function metresAt(e) {
        const r = grid.getBoundingClientRect();
        const c = cell();
        return {
            x: clamp(Math.round((e.clientX - r.left - grid.clientLeft) / c), 0, maxW),
            y: clamp(Math.round((e.clientY - r.top - grid.clientTop) / c), 0, maxH)
        };
    }

    function draw(x, y, w, h) {
        // Keep the rectangle on the grid: x + w never past maxW, y + h never past maxH.
        w = clamp(w, 0, maxW); h = clamp(h, 0, maxH);
        x = clamp(x, 0, maxW - w); y = clamp(y, 0, maxH - h);
        const c = cell();
        grid.style.backgroundSize = `${c}px ${c}px`;
        rect.style.left = `${x * c}px`;
        rect.style.top = `${y * c}px`;
        rect.style.width = `${w * c}px`;
        rect.style.height = `${h * c}px`;
        label.textContent = `${w} m × ${h} m`;
        rect.classList.toggle('is-too-small', w < min || h < min);
    }

    function fromInputs() {
        const w = clamp(parseInt(widthInput.value, 10) || min, 1, maxW);
        const h = clamp(parseInt(heightInput.value, 10) || min, 1, maxH);
        origin.x = clamp(origin.x, 0, maxW - w);
        origin.y = clamp(origin.y, 0, maxH - h);
        draw(origin.x, origin.y, w, h);
    }

    grid.addEventListener('pointerdown', e => {
        e.preventDefault();
        grid.setPointerCapture(e.pointerId);
        start = metresAt(e);
        draw(start.x, start.y, 0, 0);
    });

    grid.addEventListener('pointermove', e => {
        if (!start) return;
        const p = metresAt(e);
        const x = Math.min(start.x, p.x), y = Math.min(start.y, p.y);
        draw(x, y, Math.abs(p.x - start.x), Math.abs(p.y - start.y));
    });

    grid.addEventListener('pointerup', e => {
        if (!start) return;
        const p = metresAt(e);
        let w = Math.abs(p.x - start.x), h = Math.abs(p.y - start.y);
        origin = { x: Math.min(start.x, p.x), y: Math.min(start.y, p.y) };
        start = null;
        // A plain click (or a tiny drag) keeps the minimum size instead of a 0 m floor.
        // Growing a click near the right/bottom edge to the minimum moves it left/up instead of past the edge
        // (fromInputs clamps the origin).
        w = Math.max(w, min); h = Math.max(h, min);
        widthInput.value = clamp(w, min, maxW);
        heightInput.value = clamp(h, min, maxH);
        fromInputs();
    });

    widthInput.addEventListener('input', fromInputs);
    heightInput.addEventListener('input', fromInputs);
    window.addEventListener('resize', fromInputs);
    fromInputs();
})();
