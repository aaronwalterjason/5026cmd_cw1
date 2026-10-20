// Works out where to draw chairs around a table on the floor layout.
// Used by the layout designer and the floor previews on the Floor Layouts page.
// Chairs are only drawn - they are not stored in the database. The number of chairs is the table's Capacity.
//
// All values are in metres. A table is { x, y, w, h, shape, capacity }.
window.KomaChairs = (function () {
    'use strict';

    const CHAIR = 0.44;       // chair diameter
    const GAP = 0.05;         // space between table edge and chair
    const BENCH = 0.4;        // booth bench depth

    // Space needed around the table for its chairs: { l, t, r, b }.
    // Keep in sync with ChairPadding() in Pages/Admin/Layouts/Designer.cshtml.cs.
    function padding(shape) {
        switch (shape) {
            case 'Booth': return { l: 0, t: 0.5, r: 0, b: 0.5 };   // benches on the two long sides
            case 'Counter': return { l: 0, t: 0, r: 0, b: 0.5 };   // stools along the front
            default: return { l: 0.5, t: 0.5, r: 0.5, b: 0.5 };   // chairs can go on any side
        }
    }

    // The table plus the space its chairs take up.
    function footprint(t) {
        const p = padding(t.shape);
        return { x: t.x - p.l, y: t.y - p.t, w: t.w + p.l + p.r, h: t.h + p.t + p.b };
    }

    // Chair rectangles in floor coordinates: [{ x, y, w, h, kind: 'chair' | 'bench' }].
    function positions(t) {
        const n = Math.max(0, Math.min(20, parseInt(t.capacity, 10) || 0));
        if (!n) return [];
        const off = GAP + CHAIR / 2;   // distance from table edge to chair centre
        const chair = (cx, cy) => ({ x: cx - CHAIR / 2, y: cy - CHAIR / 2, w: CHAIR, h: CHAIR, kind: 'chair' });

        if (t.shape === 'Round') {
            const cx = t.x + t.w / 2, cy = t.y + t.h / 2;
            const rx = t.w / 2 + off, ry = t.h / 2 + off;
            return Array.from({ length: n }, (_, i) => {
                const a = -Math.PI / 2 + (2 * Math.PI * i) / n;
                return chair(cx + rx * Math.cos(a), cy + ry * Math.sin(a));
            });
        }

        if (t.shape === 'Booth') {
            // One bench above and one below; seats are split between them.
            const top = Math.ceil(n / 2), bottom = n - top;
            const benches = [{ x: t.x, y: t.y - GAP - BENCH, w: t.w, h: BENCH, kind: 'bench', seats: top }];
            if (bottom > 0) benches.push({ x: t.x, y: t.y + t.h + GAP, w: t.w, h: BENCH, kind: 'bench', seats: bottom });
            return benches;
        }

        if (t.shape === 'Counter') {
            const cy = t.y + t.h + off;
            return Array.from({ length: n }, (_, i) => chair(t.x + ((i + 0.5) / n) * t.w, cy));
        }

        // Square / Rectangle: give each chair to the side with the most room left per chair,
        // so long sides fill first and seats stay evenly spaced.
        const sides = [
            { len: t.w, count: 0, at: f => chair(t.x + f * t.w, t.y - off) },          // top
            { len: t.w, count: 0, at: f => chair(t.x + f * t.w, t.y + t.h + off) },    // bottom
            { len: t.h, count: 0, at: f => chair(t.x - off, t.y + f * t.h) },          // left
            { len: t.h, count: 0, at: f => chair(t.x + t.w + off, t.y + f * t.h) }     // right
        ];
        for (let i = 0; i < n; i++) {
            let best = sides[0];
            for (const s of sides) if (s.len / (s.count + 1) > best.len / (best.count + 1) + 1e-9) best = s;
            best.count++;
        }
        return sides.flatMap(s => Array.from({ length: s.count }, (_, k) => s.at((k + 0.5) / s.count)));
    }

    return { padding, footprint, positions };
})();
