/**
 * Pure helpers for the event queue. No React Native, no I/O.
 * Every number here comes from ../../PROTOCOL.md.
 */
export const LIMITS = {
    maxEvents: 100,
    maxBodyBytes: 256 * 1024,
    /** Batches are cut below this so the envelope never pushes them over the server limit. */
    safeBodyBytes: 240 * 1024,
    maxParams: 25,
    maxStringLen: 100,
    queueCap: 2000,
    flushAt: 20,
    flushIntervalMs: 30000,
    timeoutMs: 10000,
    sessionGapMs: 30 * 60000,
    backoffBaseMs: 30000,
    backoffMaxMs: 15 * 60000,
};
const NAME_RE = /^[a-z][a-z0-9_]{0,39}$/;
const CURRENCY_RE = /^[A-Z]{3}$/;
/**
 * Turns whatever the game passed into a valid event, or null when the name is
 * unusable. Bad params are removed or trimmed instead of failing the event, so
 * one sloppy call site never costs a whole batch (the server rejects whole
 * batches). `value` and `currency` become top-level revenue fields.
 */
export function cleanEvent(name, params) {
    if (typeof name !== 'string' || !NAME_RE.test(name))
        return null;
    const out = {};
    let count = 0;
    let value;
    let currency;
    if (typeof params === 'object' && params !== null && !Array.isArray(params)) {
        for (const [k, v] of Object.entries(params)) {
            if (k === 'value' && typeof v === 'number' && Number.isFinite(v)) {
                value = v;
                continue;
            }
            if (k === 'currency' && typeof v === 'string' && CURRENCY_RE.test(v.toUpperCase())) {
                currency = v.toUpperCase();
                continue;
            }
            if (count >= LIMITS.maxParams || !NAME_RE.test(k))
                continue;
            if (typeof v === 'string')
                out[k] = v.slice(0, LIMITS.maxStringLen);
            else if (typeof v === 'number' && Number.isFinite(v))
                out[k] = v;
            else if (typeof v === 'boolean')
                out[k] = v;
            else
                continue;
            count += 1;
        }
    }
    const ev = { name, params: count > 0 ? out : null };
    if (value !== undefined)
        ev.value = value;
    if (currency !== undefined)
        ev.currency = currency;
    return ev;
}
/** Adds an event, dropping the oldest ones past the cap. Returns how many were dropped. */
export function pushCapped(events, ev, cap = LIMITS.queueCap) {
    events.push(ev);
    const over = events.length - cap;
    if (over > 0) {
        events.splice(0, over);
        return over;
    }
    return 0;
}
export function classifyStatus(status) {
    if (status === 'network')
        return 'retry';
    if (status >= 200 && status < 300)
        return 'done';
    if (status === 400 || status === 413)
        return 'drop';
    return 'retry';
}
export function nextBackoffMs(attempt) {
    return Math.min(LIMITS.backoffBaseMs * 2 ** attempt, LIMITS.backoffMaxMs);
}
export function utf8Length(s) {
    let n = 0;
    for (let i = 0; i < s.length; i++) {
        const c = s.charCodeAt(i);
        if (c < 0x80)
            n += 1;
        else if (c < 0x800)
            n += 2;
        else if (c >= 0xd800 && c <= 0xdbff) {
            n += 4;
            i += 1;
        }
        else
            n += 3;
    }
    return n;
}
/** First events of the queue that fit in one request, and the request body. */
export function pickBatch(envelope, events) {
    let count = Math.min(LIMITS.maxEvents, events.length);
    let body = JSON.stringify({ ...envelope, events: events.slice(0, count) });
    while (count > 1 && utf8Length(body) > LIMITS.safeBodyBytes) {
        count = Math.ceil(count / 2);
        body = JSON.stringify({ ...envelope, events: events.slice(0, count) });
    }
    return { body, count };
}
/** Returns the newer version for `kind` from an "rn/1.2.0 unity/1.1.0" header, or null. */
export function newerVersion(installed, header, kind) {
    var _a, _b, _c, _d;
    if (!header)
        return null;
    const parse = (v) => v.split('.').map((n) => parseInt(n, 10) || 0);
    for (const part of header.split(/\s+/)) {
        const [k, v] = part.split('/');
        if (k !== kind || !v || !/^\d+(\.\d+){0,2}$/.test(v))
            continue;
        const a = parse(v);
        const b = parse(installed);
        for (let i = 0; i < 3; i++) {
            if (((_a = a[i]) !== null && _a !== void 0 ? _a : 0) > ((_b = b[i]) !== null && _b !== void 0 ? _b : 0))
                return v;
            if (((_c = a[i]) !== null && _c !== void 0 ? _c : 0) < ((_d = b[i]) !== null && _d !== void 0 ? _d : 0))
                return null;
        }
        return null;
    }
    return null;
}
export function isQueuedEvent(x) {
    if (typeof x !== 'object' || x === null)
        return false;
    const e = x;
    return (typeof e.name === 'string' &&
        typeof e.ts === 'number' &&
        typeof e.session_id === 'string' &&
        typeof e.seq === 'number');
}
