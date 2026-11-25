/**
 * Platform-agnostic core of the events client. React Native wiring lives in
 * index.ts; everything here gets its I/O through `ClientDeps`, so it runs in
 * Node tests unchanged.
 *
 * Contract: ../../PROTOCOL.md. Nothing here throws into the game.
 */
import { LIMITS, classifyStatus, cleanEvent, isQueuedEvent, newerVersion, nextBackoffMs, pickBatch, pushCapped, } from './queue';
export const STORAGE_KEY = 'gt_events.v1';
const PERSIST_DEBOUNCE_MS = 1000;
const MAX_BATCHES_PER_FLUSH = 50;
function fallbackUUID() {
    const h = '0123456789abcdef';
    let s = '';
    for (let i = 0; i < 36; i++) {
        if (i === 8 || i === 13 || i === 18 || i === 23)
            s += '-';
        else if (i === 14)
            s += '4';
        else if (i === 19)
            s += h[8 + Math.floor(Math.random() * 4)];
        else
            s += h[Math.floor(Math.random() * 16)];
    }
    return s;
}
async function withTimeout(run, ms) {
    const controller = typeof AbortController === 'function' ? new AbortController() : undefined;
    let timer;
    const timeout = new Promise((_, reject) => {
        timer = setTimeout(() => {
            controller === null || controller === void 0 ? void 0 : controller.abort();
            reject(new Error('timeout'));
        }, ms);
    });
    try {
        return await Promise.race([run(controller === null || controller === void 0 ? void 0 : controller.signal), timeout]);
    }
    finally {
        if (timer !== undefined)
            clearTimeout(timer);
    }
}
export function createEventsClient(identity, deps) {
    const log = (m) => {
        var _a;
        try {
            (_a = deps.log) === null || _a === void 0 ? void 0 : _a.call(deps, m);
        }
        catch {
            /* logging must never break anything */
        }
    };
    const uuid = () => {
        try {
            const id = deps.randomUUID().toLowerCase();
            if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(id))
                return id;
        }
        catch {
            /* fall through */
        }
        return fallbackUUID();
    };
    let installId = null;
    let testKey = null;
    let reportLoggedFor = null;
    let consent = true;
    let consentSetBeforeLoad = false;
    let events = [];
    let dropped = 0;
    let userId = null;
    let adaptyProfileId = null;
    let appInstanceId = null;
    let sessionId = uuid();
    let seq = 0;
    let backgroundAt = null;
    let attempt = 0;
    let nextTryAt = 0;
    let inFlight = null;
    let persistTimer = null;
    let updateNoticeShown = false;
    const ready = (async () => {
        try {
            const raw = await deps.store.getItem(STORAGE_KEY);
            if (raw) {
                const s = JSON.parse(raw);
                if (typeof s.install_id === 'string')
                    installId = s.install_id;
                if (deps.dev && typeof s.test_key === 'string' && /^[0-9a-f]{64}$/.test(s.test_key))
                    testKey = s.test_key;
                if (!consentSetBeforeLoad && s.consent === false)
                    consent = false;
                if (Array.isArray(s.events)) {
                    const restored = s.events.filter(isQueuedEvent);
                    const merged = [...restored, ...events];
                    const over = merged.length - LIMITS.queueCap;
                    if (over > 0) {
                        merged.splice(0, over);
                        dropped += over;
                    }
                    events = merged;
                }
                if (typeof s.dropped === 'number' && s.dropped > 0)
                    dropped += Math.floor(s.dropped);
            }
        }
        catch (e) {
            log(`events: could not restore state (${String(e)})`);
        }
        if (!installId)
            installId = uuid();
        if (deps.dev && !testKey)
            testKey = (uuid() + uuid()).replace(/-/g, '');
        if (!consent)
            events = [];
    })();
    async function persistNow() {
        try {
            await ready;
            if (persistTimer !== null) {
                clearTimeout(persistTimer);
                persistTimer = null;
            }
            await deps.store.setItem(STORAGE_KEY, JSON.stringify({ install_id: installId, ...(testKey ? { test_key: testKey } : {}), consent, events, dropped }));
        }
        catch (e) {
            log(`events: could not save state (${String(e)})`);
        }
    }
    function schedulePersist() {
        if (persistTimer !== null)
            return;
        persistTimer = setTimeout(() => {
            persistTimer = null;
            void persistNow();
        }, PERSIST_DEBOUNCE_MS);
    }
    function envelope() {
        const env = { v: 1, ...identity, install_id: installId, dropped };
        if (userId)
            env.user_id = userId;
        if (adaptyProfileId)
            env.adapty_profile_id = adaptyProfileId;
        if (appInstanceId)
            env.app_instance_id = appInstanceId;
        return env;
    }
    function noticeUpdate(header) {
        if (updateNoticeShown)
            return;
        const [kind, installed] = identity.sdk.split('/');
        const latest = newerVersion(installed !== null && installed !== void 0 ? installed : '0', header, kind !== null && kind !== void 0 ? kind : '');
        if (latest) {
            updateNoticeShown = true;
            log(`events: a newer client is available: ${kind}/${latest} (installed ${identity.sdk}). See CHANGELOG.md`);
        }
    }
    async function send(path, body, extraHeaders) {
        var _a;
        try {
            const res = await withTimeout((signal) => deps.fetch(deps.endpoint + path, { method: 'POST', headers: { 'content-type': 'application/json', ...extraHeaders }, body, signal }), LIMITS.timeoutMs);
            try {
                noticeUpdate((_a = res.headers) === null || _a === void 0 ? void 0 : _a.get('X-GT-SDK-Latest'));
            }
            catch {
                /* never throw */
            }
            return res.status;
        }
        catch {
            return 'network';
        }
    }
    async function drain() {
        for (let i = 0; i < MAX_BATCHES_PER_FLUSH; i++) {
            if (!consent || events.length === 0 || !installId)
                return;
            if (deps.now() < nextTryAt)
                return;
            const env = envelope();
            const droppedSent = dropped;
            const { body, count } = pickBatch(env, events);
            const status = await send('/v1/events', body, deps.dev && testKey ? { 'X-GT-Test-Key': testKey } : undefined);
            const outcome = classifyStatus(status);
            log(`events flush ${String(status)} ${count}`);
            if (status === 204 && deps.dev && testKey && installId && reportLoggedFor !== installId) {
                reportLoggedFor = installId;
                log(`test report: curl -s -X POST ${deps.endpoint}/v1/test/report -H 'content-type: application/json' ` +
                    `-d '{"app_id":"${identity.app_id}","install_id":"${installId}","test_key":"${testKey}"}'`);
            }
            if (outcome === 'retry') {
                nextTryAt = deps.now() + nextBackoffMs(attempt);
                attempt += 1;
                schedulePersist();
                return;
            }
            events.splice(0, count);
            dropped = Math.max(0, dropped - droppedSent);
            attempt = 0;
            nextTryAt = 0;
            schedulePersist();
        }
    }
    function flush() {
        if (inFlight)
            return inFlight;
        inFlight = (async () => {
            try {
                await ready;
                await drain();
            }
            catch (e) {
                log(`events: flush failed (${String(e)})`);
            }
            finally {
                inFlight = null;
            }
        })();
        return inFlight;
    }
    const interval = setInterval(() => {
        if (events.length > 0)
            void flush();
    }, LIMITS.flushIntervalMs);
    return {
        ready,
        track(name, params) {
            try {
                if (!consent)
                    return;
                const clean = cleanEvent(name, params);
                if (!clean) {
                    log(`events: dropped event with invalid name "${String(name)}"`);
                    return;
                }
                dropped += pushCapped(events, { ...clean, ts: deps.now(), session_id: sessionId, seq: seq++ });
                schedulePersist();
                if (events.length >= LIMITS.flushAt)
                    void flush();
            }
            catch (e) {
                log(`events: track failed (${String(e)})`);
            }
        },
        setConsent(granted) {
            try {
                consent = granted;
                consentSetBeforeLoad = true;
                if (!granted) {
                    events = [];
                    dropped = 0;
                }
                void persistNow();
            }
            catch {
                /* never throw */
            }
        },
        setUserId(id) {
            userId = id;
        },
        setAdaptyProfileId(id) {
            adaptyProfileId = id;
        },
        setAppInstanceId(id) {
            appInstanceId = id;
        },
        onForeground() {
            try {
                if (backgroundAt !== null && deps.now() - backgroundAt >= LIMITS.sessionGapMs) {
                    sessionId = uuid();
                    seq = 0;
                }
                backgroundAt = null;
            }
            catch {
                /* never throw */
            }
        },
        onBackground() {
            try {
                backgroundAt = deps.now();
                void persistNow();
                void flush();
            }
            catch {
                /* never throw */
            }
        },
        flush,
        async deleteData() {
            try {
                await ready;
                if (!installId)
                    return false;
                const status = await send('/v1/delete', JSON.stringify({ app_id: identity.app_id, install_id: installId }));
                if (status !== 202 && status !== 200)
                    return false;
                installId = uuid();
                events = [];
                dropped = 0;
                sessionId = uuid();
                seq = 0;
                attempt = 0;
                nextTryAt = 0;
                await persistNow();
                return true;
            }
            catch {
                return false;
            }
        },
        persistNow,
        dispose() {
            clearInterval(interval);
            if (persistTimer !== null)
                clearTimeout(persistTimer);
        },
    };
}
