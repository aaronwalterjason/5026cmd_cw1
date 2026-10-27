// Floor layout designer for Admin > Floor Layouts > (floor).
// Dining areas and tables are kept in memory while the admin works; "Save layout" sends them
// all to the server as JSON (see Pages/Admin/Layouts/Designer.cshtml.cs), which checks and saves them.
//
// Coordinates are in METRES from the floor's top-left corner, snapped to 0.5 m.
(function () {
    'use strict';

    const data = JSON.parse(document.getElementById('layout-data').textContent);
    const floorW = data.floor.width, floorH = data.floor.height;

    const els = {
        wrap: document.getElementById('lay-wrap'),
        floor: document.getElementById('lay-floor'),
        list: document.getElementById('lay-list'),
        counts: document.getElementById('lay-counts'),
        props: document.getElementById('lay-props'),
        message: document.getElementById('lay-message'),
        legend: document.getElementById('lay-legend'),
        form: document.getElementById('layout-form'),
        payload: document.getElementById('layout-payload'),
        tools: document.querySelectorAll('.lay-tool')
    };

    // ---------- State ----------
    const areas = data.areas.map(a => ({ ...a }));     // {id,name,zone,description,isActive,x,y,w,h}
    const tables = data.tables.map(t => ({ ...t }));   // {id,areaId,number,capacity,shape,isActive,x,y,w,h}
    let nextTempId = -1;            // new items get negative ids until the server saves them
    let tool = 'select';
    let selected = null;            // { kind: 'area' | 'table', id }
    let dirty = false;
    let scale = 20;                 // pixels per metre (recalculated to fit the screen)
    let drag = null;                // the current mouse/touch action

    const SNAP = 0.5;
    const MIN_AREA = 1;
    const MIN_TABLE = 0.5;
    const DEFAULT_SIZE = { Square: [1, 1], Round: [1, 1], Rectangle: [2, 1], Booth: [2, 1.5], Counter: [3, 1] };
    const DEFAULT_SEATS = { Square: 4, Round: 4, Rectangle: 6, Booth: 4, Counter: 3 };

    // ---------- Small helpers ----------
    const snap = v => Math.round(v / SNAP) * SNAP;
    const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
    const zoneClass = z => 'zone-' + (z || 'indoor').trim().toLowerCase().replace(/\s+/g, '-');
    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const fmt = v => (Math.round(v * 10) / 10).toString();
    const findArea = id => areas.find(a => a.id === id);
    const findTable = id => tables.find(t => t.id === id);
    const tablesIn = area => tables.filter(t => t.areaId === area.id);
    const EPS = 0.001;
    const overlaps = (a, b) => a.x < b.x + b.w - EPS && b.x < a.x + a.w - EPS && a.y < b.y + b.h - EPS && b.y < a.y + a.h - EPS;
    const inside = (r, o) => r.x >= o.x - EPS && r.y >= o.y - EPS && r.x + r.w <= o.x + o.w + EPS && r.y + r.h <= o.y + o.h + EPS;
    const areaAt = (x, y) => [...areas].reverse().find(a => x >= a.x && x <= a.x + a.w && y >= a.y && y <= a.y + a.h);

    // A table's "footprint" = the table plus the space its chairs need (see layout-chairs.js).
    // Tables are checked by footprint, so chairs never overlap other tables or stick out of the area.
    const Chairs = window.KomaChairs;
    const fp = t => Chairs.footprint(t);
    const pad = t => Chairs.padding(t.shape);
    const tableFits = (t, area) => !!area && inside(fp(t), area);
    const tableClashes = (t, ignore) => tables.some(o => o !== t && o !== ignore && overlaps(fp(t), fp(o)));

    function markDirty() {
        dirty = true;
        document.getElementById('layout-save').classList.add('btn-warning');
        document.getElementById('layout-save').classList.remove('btn-primary');
        document.getElementById('layout-save').textContent = 'Save layout •';
    }

    let messageTimer = null;
    function say(text, kind = 'info') {
        els.message.textContent = text;
        els.message.className = 'lay-message is-' + kind;
        clearTimeout(messageTimer);
        if (kind !== 'hint') messageTimer = setTimeout(showHint, 5000);
    }
    function showHint() {
        const hints = {
            select: 'Click an area or table to edit it. Drag to move, drag the corner handle to resize. Arrow keys nudge, Delete removes.',
            area: 'Click and drag on the floor to draw a dining area.',
            table: 'Click inside a dining area to add a table there.'
        };
        say(hints[tool], 'hint');
    }

    function nextTableNumber() {
        const used = new Set([...data.otherTableNumbers, ...tables.map(t => t.number)].map(n => n.toUpperCase()));
        let n = 1;
        while (used.has('T' + n)) n++;
        return 'T' + n;
    }

    function nextAreaName() {
        const used = new Set(areas.map(a => a.name.toLowerCase()));
        let n = areas.length + 1;
        while (used.has(('Dining area ' + n).toLowerCase())) n++;
        return 'Dining area ' + n;
    }

    // ---------- Scale: fit the floor into the centre column ----------
    function fit() {
        const availW = els.wrap.clientWidth - 2;
        const availH = Math.max(300, window.innerHeight - els.wrap.getBoundingClientRect().top - 90);
        scale = Math.max(8, Math.min(48, Math.floor(availW / floorW), Math.floor(availH / floorH)));
        els.floor.style.width = floorW * scale + 'px';
        els.floor.style.height = floorH * scale + 'px';
        els.floor.style.backgroundSize = `${scale}px ${scale}px, ${scale * 5}px ${scale * 5}px, ${scale * 5}px ${scale * 5}px`;
        renderFloor();
    }

    function place(el, r) {
        el.style.left = r.x * scale + 'px';
        el.style.top = r.y * scale + 'px';
        el.style.width = r.w * scale + 'px';
        el.style.height = r.h * scale + 'px';
    }

    // ---------- Rendering ----------
    function renderFloor() {
        els.floor.innerHTML = '';
        for (const a of areas) {
            const el = document.createElement('div');
            el.className = `lay-area ${zoneClass(a.zone)}${a.isActive ? '' : ' is-inactive'}${isSel('area', a.id) ? ' is-selected' : ''}`;
            el.dataset.kind = 'area';
            el.dataset.id = a.id;
            el.innerHTML = `<div class="lay-area-label"><strong>${esc(a.name || '(no name)')}</strong><span>${esc(a.zone)} · ${fmt(a.w)}×${fmt(a.h)} m</span></div>`;
            place(el, a);
            if (isSel('area', a.id)) el.appendChild(handle());
            els.floor.appendChild(el);
        }
        // Chairs (drawn only - their number is the table's seat count).
        for (const t of tables) {
            for (const c of Chairs.positions(t)) {
                const ch = document.createElement('div');
                ch.className = `lay-${c.kind}${t.isActive ? '' : ' is-inactive'}${isSel('table', t.id) ? ' is-selected' : ''}`;
                ch.dataset.chairOf = t.id;
                place(ch, c);
                els.floor.appendChild(ch);
            }
        }
        for (const t of tables) {
            const el = document.createElement('div');
            el.className = `lay-table shape-${t.shape.toLowerCase()}${t.isActive ? '' : ' is-inactive'}${isSel('table', t.id) ? ' is-selected' : ''}`;
            el.dataset.kind = 'table';
            el.dataset.id = t.id;
            el.title = `Table ${t.number} · ${t.capacity} seats · ${t.shape}`;
            el.innerHTML = `<span class="lay-table-label">${esc(t.number)}<small>${t.capacity}</small></span>`;
            place(el, t);
            if (isSel('table', t.id)) el.appendChild(handle());
            els.floor.appendChild(el);
        }
        els.floor.classList.toggle('is-empty', areas.length === 0);
        els.floor.dataset.tool = tool;
    }

    function handle() {
        const h = document.createElement('div');
        h.className = 'lay-handle';
        h.title = 'Drag to resize';
        return h;
    }

    const isSel = (kind, id) => selected && selected.kind === kind && selected.id === id;

    function renderList() {
        const seats = tables.reduce((s, t) => s + (+t.capacity || 0), 0);
        els.counts.textContent = `${areas.length} areas · ${tables.length} tables · ${seats} seats`;
        if (!areas.length) {
            els.list.innerHTML = '<p class="small text-muted px-1 mb-0">Nothing yet. Choose <strong>Draw dining area</strong> and drag on the floor.</p>';
            return;
        }
        els.list.innerHTML = areas.map(a => `
            <button type="button" class="lay-list-item ${isSel('area', a.id) ? 'active' : ''}" data-kind="area" data-id="${a.id}">
                <span class="lay-swatch ${zoneClass(a.zone)}"></span>${esc(a.name || '(no name)')}
            </button>
            ${tablesIn(a).map(t => `
                <button type="button" class="lay-list-item is-table ${isSel('table', t.id) ? 'active' : ''}" data-kind="table" data-id="${t.id}">
                    ${esc(t.number)} <span class="text-muted">· ${t.capacity} seats</span>
                </button>`).join('')}
        `).join('');
    }

    function renderLegend() {
        els.legend.innerHTML = data.zones.map(z => `<span><span class="lay-swatch ${zoneClass(z)}"></span>${esc(z)}</span>`).join('')
            + '<span><span class="lay-swatch is-inactive-swatch"></span>Inactive</span>';
    }

    function renderProps() {
        const p = els.props;
        if (!selected) {
            p.innerHTML = `
                <h2 class="h6">${esc(data.floor.name)}</h2>
                <p class="small text-muted mb-2">${floorW} m × ${floorH} m · 1 square = 1 m</p>
                <ol class="small ps-3 mb-3">
                    <li>Choose <strong>Draw dining area</strong> and drag on the floor.</li>
                    <li>Name the area and pick its zone here.</li>
                    <li>Choose <strong>Add table</strong> and click inside the area.</li>
                    <li>Click <strong>Save layout</strong>.</li>
                </ol>
                <p class="small text-muted mb-0">Shortcuts: V select · A area · T table · Del delete · Esc cancel</p>`;
            return;
        }

        if (selected.kind === 'area') {
            const a = findArea(selected.id);
            p.innerHTML = `
                <div class="d-flex justify-content-between align-items-center mb-2">
                    <h2 class="h6 mb-0">Dining area</h2>
                    <span class="lay-swatch ${zoneClass(a.zone)}"></span>
                </div>
                <label class="form-label small mb-1" for="p-name">Name</label>
                <input id="p-name" class="form-control form-control-sm mb-2" maxlength="80" value="${esc(a.name)}" />
                <label class="form-label small mb-1" for="p-zone">Zone</label>
                <select id="p-zone" class="form-select form-select-sm mb-2">
                    ${data.zones.map(z => `<option ${z === a.zone ? 'selected' : ''}>${esc(z)}</option>`).join('')}
                </select>
                <label class="form-label small mb-1" for="p-desc">Description</label>
                <textarea id="p-desc" class="form-control form-control-sm mb-2" rows="2" maxlength="250">${esc(a.description)}</textarea>
                ${sizeFields(a)}
                <div class="form-check mb-3">
                    <input id="p-active" type="checkbox" class="form-check-input" ${a.isActive ? 'checked' : ''} />
                    <label class="form-check-label small" for="p-active">Active (open for bookings)</label>
                </div>
                <p class="small text-muted">${tablesIn(a).length} table(s) · ${tablesIn(a).reduce((s, t) => s + (+t.capacity || 0), 0)} seats</p>
                <div class="d-flex gap-2">
                    <button type="button" class="btn btn-sm btn-outline-primary" id="p-add-table">+ Add table here</button>
                    <button type="button" class="btn btn-sm btn-outline-danger ms-auto" id="p-delete">Delete area</button>
                </div>`;
            bind('p-name', 'input', e => { a.name = e.target.value; changed(false); });
            bind('p-zone', 'change', e => { a.zone = e.target.value; changed(true); });
            bind('p-desc', 'input', e => { a.description = e.target.value; markDirty(); });
            bind('p-active', 'change', e => { a.isActive = e.target.checked; changed(false); });
            bind('p-add-table', 'click', () => addTable(a, a.x + a.w / 2, a.y + a.h / 2));
            bind('p-delete', 'click', deleteSelected);
            bindSize(a, 'area');
            return;
        }

        const t = findTable(selected.id);
        const area = findArea(t.areaId);
        p.innerHTML = `
            <h2 class="h6">Table</h2>
            <label class="form-label small mb-1" for="p-number">Table number</label>
            <input id="p-number" class="form-control form-control-sm mb-2" maxlength="20" value="${esc(t.number)}" />
            <div class="row g-2 mb-2">
                <div class="col-6">
                    <label class="form-label small mb-1" for="p-cap">Seats</label>
                    <input id="p-cap" type="number" min="1" max="20" class="form-control form-control-sm" value="${t.capacity}" />
                </div>
                <div class="col-6">
                    <label class="form-label small mb-1" for="p-shape">Shape</label>
                    <select id="p-shape" class="form-select form-select-sm">
                        ${data.shapes.map(s => `<option ${s === t.shape ? 'selected' : ''}>${s}</option>`).join('')}
                    </select>
                </div>
            </div>
            ${sizeFields(t)}
            <div class="form-check mb-2">
                <input id="p-active" type="checkbox" class="form-check-input" ${t.isActive ? 'checked' : ''} />
                <label class="form-check-label small" for="p-active">Active (can be booked)</label>
            </div>
            <p class="small text-muted">In <a href="#" id="p-area-link">${esc(area ? area.name : '?')}</a>. Drag the table into another area to move it there.</p>
            <div class="d-flex">
                <button type="button" class="btn btn-sm btn-outline-danger ms-auto" id="p-delete">Delete table</button>
            </div>`;
        bind('p-number', 'input', e => { t.number = e.target.value.trim(); changed(false); });
        bind('p-cap', 'input', e => { t.capacity = clamp(parseInt(e.target.value, 10) || 1, 1, 20); changed(false); });
        bind('p-cap', 'change', () => {
            if (t.shape !== 'Round' && t.shape !== 'Booth' && t.capacity > 4 && t.w <= 1 && t.h <= 1)
                say(`${t.capacity} chairs are tight around a ${t.w}×${t.h} m table - consider making it bigger.`, 'info');
        });
        bind('p-shape', 'change', e => {
            t.shape = e.target.value;
            // Resize to the new shape's usual size if it still fits.
            const [w, h] = DEFAULT_SIZE[t.shape] || [1, 1];
            const trial = { ...t, w, h };
            if (tableFits(trial, area) && !tableClashes(trial, t)) { t.w = w; t.h = h; }
            else if (!tableFits(t, area) || tableClashes(t)) say('There is not enough room for the chairs of this shape here. Move or resize the table.', 'error');
            changed(true);
        });
        bind('p-active', 'change', e => { t.isActive = e.target.checked; changed(false); });
        bind('p-area-link', 'click', e => { e.preventDefault(); if (area) select('area', area.id); });
        bind('p-delete', 'click', deleteSelected);
        bindSize(t, 'table');
    }

    function sizeFields(r) {
        return `
            <div class="row g-2 mb-2 lay-size-fields">
                <div class="col-3"><label class="form-label small mb-0" for="p-x">X</label><input id="p-x" type="number" step="0.5" class="form-control form-control-sm" value="${r.x}" /></div>
                <div class="col-3"><label class="form-label small mb-0" for="p-y">Y</label><input id="p-y" type="number" step="0.5" class="form-control form-control-sm" value="${r.y}" /></div>
                <div class="col-3"><label class="form-label small mb-0" for="p-w">W</label><input id="p-w" type="number" step="0.5" min="0.5" class="form-control form-control-sm" value="${r.w}" /></div>
                <div class="col-3"><label class="form-label small mb-0" for="p-h">L</label><input id="p-h" type="number" step="0.5" min="0.5" class="form-control form-control-sm" value="${r.h}" /></div>
            </div>
            <p class="form-text mt-0 mb-2">Position and size in metres.</p>`;
    }

    // Typing in X/Y/W/L: apply only if the result is still valid, otherwise show why.
    function bindSize(item, kind) {
        ['x', 'y', 'w', 'h'].forEach(k => bind('p-' + k, 'change', e => {
            const v = snap(parseFloat(e.target.value));
            if (isNaN(v)) { e.target.value = item[k]; return; }
            const trial = { ...item, [k]: v };
            const problem = kind === 'area' ? areaProblem(item, trial) : tableProblem(item, trial, findArea(item.areaId));
            if (problem) { say(problem, 'error'); e.target.value = item[k]; return; }
            if (kind === 'area' && (k === 'x' || k === 'y')) {
                const dx = trial.x - item.x, dy = trial.y - item.y;
                tablesIn(item).forEach(t => { t.x += dx; t.y += dy; });
            }
            item[k] = v;
            changed(false);
        }));
    }

    function bind(id, ev, fn) {
        const el = document.getElementById(id);
        if (el) el.addEventListener(ev, fn);
    }

    // Something about an item changed: redraw floor + list (and the panel if asked).
    function changed(refreshPanel) {
        markDirty();
        renderFloor();
        renderList();
        if (refreshPanel) renderProps();
    }

    function renderAll() { renderFloor(); renderList(); renderProps(); }

    function select(kind, id) {
        selected = kind ? { kind, id } : null;
        renderAll();
    }

    function setTool(t) {
        tool = t;
        els.tools.forEach(b => b.classList.toggle('active', b.dataset.tool === t));
        els.floor.dataset.tool = t;
        showHint();
    }

    // ---------- Rules ----------
    function areaProblem(area, r) {
        if (r.w < MIN_AREA || r.h < MIN_AREA) return `A dining area must be at least ${MIN_AREA} m × ${MIN_AREA} m.`;
        if (!inside(r, { x: 0, y: 0, w: floorW, h: floorH })) return 'That would go outside the floor.';
        const other = areas.find(o => o !== area && overlaps(r, o));
        if (other) return `That would overlap '${other.name}'.`;
        if (area) {
            // When resizing, the area must still contain all of its tables (moving shifts the tables too).
            const sameSpot = r.x === area.x && r.y === area.y;
            if (sameSpot && tablesIn(area).some(t => !inside(fp(t), r))) return 'Some tables or chairs would end up outside this area. Move them first.';
        }
        return null;
    }

    function tableProblem(table, r, area) {
        if (r.w < MIN_TABLE || r.h < MIN_TABLE) return `A table must be at least ${MIN_TABLE} m × ${MIN_TABLE} m.`;
        if (!area) return 'Tables must be inside a dining area.';
        if (!tableFits(r, area)) return `The table and its chairs must stay inside '${area.name}'.`;
        const other = tables.find(o => o !== table && overlaps(fp(r), fp(o)));
        if (other) return `That would be too close to table ${other.number} (chairs would overlap).`;
        return null;
    }

    // ---------- Creating & deleting ----------
    function createArea(r) {
        const problem = areaProblem(null, r);
        if (problem) { say(problem, 'error'); return; }
        const a = { id: nextTempId--, name: nextAreaName(), zone: data.zones[0], description: '', isActive: true, ...r };
        areas.push(a);
        markDirty();
        setTool('select');
        select('area', a.id);
        say(`'${a.name}' added. Give it a name and zone on the right, then add tables.`, 'success');
        const name = document.getElementById('p-name');
        if (name) { name.focus(); name.select(); }
    }

    function addTable(area, cx, cy) {
        const shape = 'Square';
        const [w, h] = DEFAULT_SIZE[shape];
        const p = Chairs.padding(shape);
        if (area.w < w + p.l + p.r || area.h < h + p.t + p.b) { say(`'${area.name}' is too small for a table and its chairs.`, 'error'); return; }
        const r = {
            x: clamp(snap(cx - w / 2), area.x + p.l, area.x + area.w - w - p.r),
            y: clamp(snap(cy - h / 2), area.y + p.t, area.y + area.h - h - p.b),
            w, h, shape, capacity: DEFAULT_SEATS[shape]
        };
        // If that spot is taken, look for the nearest free spot in the area.
        if (!tableFits(r, area) || tableClashes(r)) {
            const spot = freeSpot(area, r, cx, cy);
            if (!spot) { say(`There is no free space left in '${area.name}'.`, 'error'); return; }
            r.x = spot.x; r.y = spot.y;
        }
        const t = { id: nextTempId--, areaId: area.id, number: nextTableNumber(), isActive: true, ...r };
        tables.push(t);
        markDirty();
        select('table', t.id);
        say(`Table ${t.number} added to '${area.name}'.`, 'success');
    }

    function freeSpot(area, proto, cx, cy) {
        const { w, h } = proto;
        let best = null, bestDist = Infinity;
        for (let y = area.y; y + h <= area.y + area.h + EPS; y += SNAP)
            for (let x = area.x; x + w <= area.x + area.w + EPS; x += SNAP) {
                const r = { ...proto, x, y };
                if (!tableFits(r, area) || tableClashes(r)) continue;
                const d = (x + w / 2 - cx) ** 2 + (y + h / 2 - cy) ** 2;
                if (d < bestDist) { best = r; bestDist = d; }
            }
        return best;
    }

    function deleteSelected() {
        if (!selected) return;
        if (selected.kind === 'area') {
            const a = findArea(selected.id);
            const count = tablesIn(a).length;
            const removeArea = () => {
                for (let i = tables.length - 1; i >= 0; i--) if (tables[i].areaId === a.id) tables.splice(i, 1);
                areas.splice(areas.indexOf(a), 1);
                say(`'${a.name}' removed. Click Save layout to keep this change.`, 'info');
                selected = null;
                markDirty();
                renderAll();
            };
            if (!count) { removeArea(); return; }
            // Area still has tables: ask first, using the shared KOMA pop-up (js/koma-confirm.js).
            if (window.KomaConfirm) {
                KomaConfirm.open({
                    tone: 'danger', icon: 'trash', title: `Delete \u2018${a.name}\u2019 and its ${count} table${count === 1 ? '' : 's'}?`,
                    ok: 'Delete', no: 'Cancel', onOk: removeArea
                });
            } else if (confirm(`Delete '${a.name}' and its ${count} table(s)?`)) {
                removeArea();
            }
            return;
        } else {
            const t = findTable(selected.id);
            tables.splice(tables.indexOf(t), 1);
            say(`Table ${t.number} removed. Click Save layout to keep this change.`, 'info');
        }
        selected = null;
        markDirty();
        renderAll();
    }

    // ---------- Mouse / touch on the floor ----------
    function metresAt(e) {
        const r = els.floor.getBoundingClientRect();
        return { x: (e.clientX - r.left) / scale, y: (e.clientY - r.top) / scale };
    }

    els.floor.addEventListener('pointerdown', e => {
        if (e.button !== 0) return;
        const p = metresAt(e);
        const target = e.target.closest('[data-kind]');
        const onHandle = e.target.classList.contains('lay-handle');
        els.floor.focus({ preventScroll: true });

        if (tool === 'area') {
            const start = { x: clamp(snap(p.x), 0, floorW), y: clamp(snap(p.y), 0, floorH) };
            const ghost = document.createElement('div');
            ghost.className = 'lay-ghost';
            els.floor.appendChild(ghost);
            drag = { mode: 'draw', start, ghost };
        } else if (tool === 'table') {
            const area = areaAt(p.x, p.y);
            if (!area) { say('Click inside a dining area to add a table.', 'error'); return; }
            addTable(area, p.x, p.y);
            return;
        } else if (target) {
            const kind = target.dataset.kind, id = +target.dataset.id;
            if (!isSel(kind, id)) select(kind, id);
            const item = kind === 'area' ? findArea(id) : findTable(id);
            drag = {
                mode: onHandle ? 'resize' : 'move',
                kind, item, start: p, moved: false,
                orig: { x: item.x, y: item.y, w: item.w, h: item.h },
                origTables: kind === 'area' ? tablesIn(item).map(t => ({ t, x: t.x, y: t.y })) : []
            };
        } else {
            select(null);
            return;
        }
        els.floor.setPointerCapture(e.pointerId);
        e.preventDefault();
    });

    els.floor.addEventListener('pointermove', e => {
        if (!drag) return;
        const p = metresAt(e);

        if (drag.mode === 'draw') {
            const end = { x: clamp(snap(p.x), 0, floorW), y: clamp(snap(p.y), 0, floorH) };
            drag.rect = {
                x: Math.min(drag.start.x, end.x), y: Math.min(drag.start.y, end.y),
                w: Math.abs(end.x - drag.start.x), h: Math.abs(end.y - drag.start.y)
            };
            place(drag.ghost, drag.rect);
            drag.ghost.dataset.size = `${fmt(drag.rect.w)} × ${fmt(drag.rect.h)} m`;
            drag.ghost.classList.toggle('is-invalid', !!areaProblem(null, drag.rect));
            return;
        }

        const dx = snap(p.x - drag.start.x), dy = snap(p.y - drag.start.y);
        if (dx || dy) drag.moved = true;
        const it = drag.item, o = drag.orig;

        if (drag.mode === 'move') {
            it.x = clamp(o.x + dx, 0, floorW - o.w);
            it.y = clamp(o.y + dy, 0, floorH - o.h);
            if (drag.kind === 'area') {
                const mx = it.x - o.x, my = it.y - o.y;
                drag.origTables.forEach(ot => { ot.t.x = ot.x + mx; ot.t.y = ot.y + my; });
                drag.invalid = !!areas.find(a => a !== it && overlaps(it, a));
            } else {
                const target = areaAt(it.x + it.w / 2, it.y + it.h / 2);
                drag.invalid = !tableFits(it, target) || tableClashes(it);
            }
        } else { // resize from the bottom-right corner
            const min = drag.kind === 'area' ? MIN_AREA : MIN_TABLE;
            const bound = drag.kind === 'area' ? { x: 0, y: 0, w: floorW, h: floorH } : findArea(it.areaId);
            const p = drag.kind === 'area' ? { r: 0, b: 0 } : pad(it);
            it.w = clamp(o.w + dx, min, Math.max(min, bound.x + bound.w - p.r - o.x));
            it.h = clamp(o.h + dy, min, Math.max(min, bound.y + bound.h - p.b - o.y));
            drag.invalid = drag.kind === 'area'
                ? !!areaProblem(it, { ...it })
                : !!tableProblem(it, { ...it }, bound);
        }
        renderFloor();
        const el = els.floor.querySelector(`[data-kind="${drag.kind}"][data-id="${it.id}"]`);
        if (el) el.classList.toggle('is-invalid', !!drag.invalid);
    });

    function endDrag() {
        if (!drag) return;
        const d = drag;
        drag = null;

        if (d.mode === 'draw') {
            d.ghost.remove();
            if (d.rect && (d.rect.w > 0 || d.rect.h > 0)) createArea(d.rect);
            else say('Drag on the floor to draw the dining area (click alone does nothing).', 'error');
            return;
        }
        if (!d.moved) { renderAll(); return; }

        if (d.invalid) {
            // Put everything back where it was.
            Object.assign(d.item, d.orig);
            d.origTables.forEach(ot => { ot.t.x = ot.x; ot.t.y = ot.y; });
            say(d.kind === 'area'
                ? 'Dining areas cannot overlap, and must still fit their tables. The change was undone.'
                : 'A table and its chairs must sit fully inside a dining area without touching other tables. The change was undone.', 'error');
        } else {
            if (d.kind === 'table' && d.mode === 'move') {
                const area = areaAt(d.item.x + d.item.w / 2, d.item.y + d.item.h / 2);
                if (area && area.id !== d.item.areaId) {
                    d.item.areaId = area.id;
                    say(`Table ${d.item.number} moved to '${area.name}'.`, 'success');
                }
            }
            markDirty();
        }
        renderAll();
    }
    els.floor.addEventListener('pointerup', endDrag);
    els.floor.addEventListener('pointercancel', endDrag);

    // ---------- Keyboard ----------
    document.addEventListener('keydown', e => {
        const typing = /^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement?.tagName);
        if (typing) return;
        if (e.key === 'v' || e.key === 'V') setTool('select');
        else if (e.key === 'a' || e.key === 'A') setTool('area');
        else if (e.key === 't' || e.key === 'T') setTool('table');
        else if (e.key === 'Escape') { setTool('select'); select(null); }
        else if ((e.key === 'Delete' || e.key === 'Backspace') && selected) { e.preventDefault(); deleteSelected(); }
        else if (selected && e.key.startsWith('Arrow')) {
            e.preventDefault();
            const dx = e.key === 'ArrowLeft' ? -SNAP : e.key === 'ArrowRight' ? SNAP : 0;
            const dy = e.key === 'ArrowUp' ? -SNAP : e.key === 'ArrowDown' ? SNAP : 0;
            nudge(dx, dy);
        }
    });

    function nudge(dx, dy) {
        if (selected.kind === 'area') {
            const a = findArea(selected.id);
            const r = { ...a, x: a.x + dx, y: a.y + dy };
            if (!inside(r, { x: 0, y: 0, w: floorW, h: floorH }) || areas.some(o => o !== a && overlaps(r, o))) return;
            a.x = r.x; a.y = r.y;
            tablesIn(a).forEach(t => { t.x += dx; t.y += dy; });
        } else {
            const t = findTable(selected.id);
            const r = { ...t, x: t.x + dx, y: t.y + dy };
            if (tableProblem(t, r, findArea(t.areaId))) return;
            t.x = r.x; t.y = r.y;
        }
        changed(true);
    }

    // ---------- Tools, list, save ----------
    els.tools.forEach(b => b.addEventListener('click', () => setTool(b.dataset.tool)));

    els.list.addEventListener('click', e => {
        const item = e.target.closest('[data-kind]');
        if (item) { setTool('select'); select(item.dataset.kind, +item.dataset.id); }
    });

    els.form.addEventListener('submit', e => {
        // Quick checks so the admin gets instant feedback; the server checks everything again.
        const unnamed = areas.find(a => !a.name.trim());
        const noNumber = tables.find(t => !t.number.trim());
        const seen = new Set();
        const dup = tables.find(t => { const k = t.number.trim().toUpperCase(); if (seen.has(k)) return true; seen.add(k); return false; });
        if (unnamed) { e.preventDefault(); select('area', unnamed.id); say('Give this dining area a name before saving.', 'error'); return; }
        if (noNumber) { e.preventDefault(); select('table', noNumber.id); say('Give this table a number before saving.', 'error'); return; }
        if (dup) { e.preventDefault(); select('table', dup.id); say(`Table number ${dup.number} is used twice.`, 'error'); return; }

        els.payload.value = JSON.stringify({ areas, tables });
        dirty = false;
    });

    window.addEventListener('beforeunload', e => {
        if (dirty) { e.preventDefault(); e.returnValue = ''; }
    });
    window.addEventListener('resize', fit);

    // ---------- Start ----------
    renderLegend();
    fit();
    renderList();
    renderProps();
    showHint();
    if (data.areas.length && document.querySelector('.alert-danger')) markDirty();   // unsaved work came back with errors
})();
