const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");
const source = fs.readFileSync(path.join(__dirname, "../WebApplication1/wwwroot/js/tablet-bill.js"), "utf8");
function tablet(now, locked = false) {
    let elapsed = 0;
    const timers = [];
    const nodes = new Map();
    const node = id => {
        if (!nodes.has(id)) nodes.set(id, {
            dataset: {}, open: false, disabled: false, hidden: false, textContent: "",
            addEventListener() {}, showModal() { this.open = true; }, close() { this.open = false; },
            querySelector(selector) { return node(selector.slice(1)); }
        });
        return nodes.get(id);
    };
    node("visit-bill-state").dataset = {
        now: `2026-11-09T${now}+08:00`, cutoff: "2026-11-09T21:30:00+08:00",
        locked: String(locked), visit: "42"
    };
    const storage = new Map();
    vm.runInNewContext(source, {
        document: { getElementById: node, addEventListener() {}, hidden: false },
        performance: { now: () => elapsed },
        sessionStorage: { getItem: k => storage.get(k), setItem: (k, v) => storage.set(k, v) },
        setInterval: (fn, delay) => timers.push({ fn, delay }), Date,
        location: { href: "http://localhost/Dining/Menu" }, URL
    });
    return { node, tick: ms => { elapsed += ms; timers.find(t => t.delay === 1000).fn(); } };
}
let t = tablet("21:19:59");
assert.equal(t.node("last-call-dialog").open, false);
assert.equal(t.node("ordering-controls").disabled, false);
t.tick(1000);
assert.equal(t.node("last-call-dialog").open, true);
t.node("last-call-dialog").close();
t.tick(1000);
assert.equal(t.node("last-call-dialog").open, false, "Acknowledged warning must not repeat");
t = tablet("21:29:59");
t.node("review-dialog").open = true;
t.tick(1000);
assert.equal(t.node("ordering-controls").disabled, true);
assert.equal(t.node("review-dialog").open, false, "Open order review closes at cutoff");
assert.equal(t.node("ordering-hours-message").hidden, false);
assert.equal(t.node("open-visit-bill").disabled, false, "Bill remains accessible after last call");
t = tablet("09:00:00", true);
assert.equal(t.node("ordering-controls").disabled, true);
console.log("PASS tablet warning, acknowledgement, cutoff lock, review closure, bill access and pre-opening lock");
