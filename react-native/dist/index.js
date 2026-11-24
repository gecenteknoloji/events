/**
 * Gecen Teknoloji events, React Native client. Copy this folder (src/) into the
 * app, then call `trackEvent` wherever the app already logs an analytics event.
 * Nothing else is required: the client starts itself on first use and reads
 * package name, version and device info at runtime.
 *
 * Optional packages are used when the app already has them, never required:
 *   storage: react-native-mmkv (v3 or v4), else @react-native-async-storage/async-storage, else memory only
 *   app id / version / model: expo-application, react-native-device-info, expo-device
 *   uuid: crypto.randomUUID, else expo-crypto, else a built-in generator
 *
 * Protocol: ../../PROTOCOL.md
 */
import { AppState, Platform } from 'react-native';
import { createEventsClient } from './client';
export const SDK_VERSION = '1.0.0';
export const DEFAULT_ENDPOINT = 'https://api.gecenteknoloji.com';
// Metro treats a require inside try/catch as optional: a missing package is
// skipped at build time instead of failing the bundle.
function optional(load) {
    try {
        return load();
    }
    catch {
        return undefined;
    }
}
const isDev = typeof __DEV__ !== 'undefined' && !!__DEV__;
function devLog(message) {
    if (isDev)
        console.log(`[gt-events] ${message}`);
}
function pickStore() {
    var _a, _b;
    const mmkvModule = optional(() => require('react-native-mmkv'));
    if (mmkvModule) {
        const mmkv = (_a = optional(() => { var _a; return (_a = mmkvModule.createMMKV) === null || _a === void 0 ? void 0 : _a.call(mmkvModule, { id: 'gt-events' }); })) !== null && _a !== void 0 ? _a : optional(() => (mmkvModule.MMKV ? new mmkvModule.MMKV({ id: 'gt-events' }) : undefined));
        if (mmkv) {
            return {
                getItem: async (k) => { var _a; return (_a = mmkv.getString(k)) !== null && _a !== void 0 ? _a : null; },
                setItem: async (k, v) => mmkv.set(k, v),
                removeItem: async (k) => {
                    if (typeof mmkv.remove === 'function')
                        mmkv.remove(k);
                    else
                        mmkv.delete(k);
                },
            };
        }
    }
    const asyncStorage = optional(() => require('@react-native-async-storage/async-storage'));
    const as = (_b = asyncStorage === null || asyncStorage === void 0 ? void 0 : asyncStorage.default) !== null && _b !== void 0 ? _b : asyncStorage;
    if (as && typeof as.getItem === 'function') {
        return {
            getItem: (k) => as.getItem(k),
            setItem: (k, v) => as.setItem(k, v),
            removeItem: (k) => as.removeItem(k),
        };
    }
    devLog('no react-native-mmkv or async-storage found; events queued while offline are lost on restart');
    const mem = new Map();
    return {
        getItem: async (k) => { var _a; return (_a = mem.get(k)) !== null && _a !== void 0 ? _a : null; },
        setItem: async (k, v) => void mem.set(k, v),
        removeItem: async (k) => void mem.delete(k),
    };
}
function randomUUID() {
    const c = globalThis.crypto;
    if (c && typeof c.randomUUID === 'function')
        return c.randomUUID();
    const expoCrypto = optional(() => require('expo-crypto'));
    if (expoCrypto && typeof expoCrypto.randomUUID === 'function')
        return expoCrypto.randomUUID();
    throw new Error('no uuid source'); // client.ts falls back to its own generator
}
function str(x) {
    return typeof x === 'string' && x.length > 0 ? x : undefined;
}
function detectIdentity(opts) {
    var _a, _b, _c, _d, _e, _f, _g, _h;
    const app = optional(() => require('expo-application'));
    const deviceInfoModule = optional(() => require('react-native-device-info'));
    const deviceInfo = (_a = deviceInfoModule === null || deviceInfoModule === void 0 ? void 0 : deviceInfoModule.default) !== null && _a !== void 0 ? _a : deviceInfoModule;
    const device = optional(() => require('expo-device'));
    const appId = (_c = (_b = str(opts.appId)) !== null && _b !== void 0 ? _b : str(app === null || app === void 0 ? void 0 : app.applicationId)) !== null && _c !== void 0 ? _c : str(optional(() => deviceInfo === null || deviceInfo === void 0 ? void 0 : deviceInfo.getBundleId()));
    const appVersion = (_e = (_d = str(opts.appVersion)) !== null && _d !== void 0 ? _d : str(app === null || app === void 0 ? void 0 : app.nativeApplicationVersion)) !== null && _e !== void 0 ? _e : str(optional(() => deviceInfo === null || deviceInfo === void 0 ? void 0 : deviceInfo.getVersion()));
    const model = (_g = (_f = str(device === null || device === void 0 ? void 0 : device.modelName)) !== null && _f !== void 0 ? _f : str(optional(() => deviceInfo === null || deviceInfo === void 0 ? void 0 : deviceInfo.getModel()))) !== null && _g !== void 0 ? _g : str((_h = Platform.constants) === null || _h === void 0 ? void 0 : _h.Model);
    const locale = str(optional(() => Intl.DateTimeFormat().resolvedOptions().locale));
    return { appId, appVersion, model, locale };
}
let client = null;
let disabled = false;
function start(opts = {}) {
    var _a, _b;
    if (client || disabled)
        return client;
    try {
        const platform = Platform.OS;
        const { appId, appVersion, model, locale } = detectIdentity(opts);
        if ((platform !== 'ios' && platform !== 'android') || !appId) {
            disabled = true;
            devLog(!appId
                ? 'disabled: app id not found. Install expo-application or react-native-device-info, or call initEvents({ appId })'
                : `disabled: platform ${platform} is not supported`);
            return null;
        }
        const debug = (_a = opts.debug) !== null && _a !== void 0 ? _a : isDev;
        client = createEventsClient({
            sdk: `rn/${SDK_VERSION}`,
            app_id: appId,
            platform,
            app_version: appVersion,
            locale: locale === null || locale === void 0 ? void 0 : locale.slice(0, 35),
            device_model: model === null || model === void 0 ? void 0 : model.slice(0, 64),
            os_version: String(Platform.Version).slice(0, 32),
        }, {
            store: pickStore(),
            endpoint: (_b = opts.endpoint) !== null && _b !== void 0 ? _b : DEFAULT_ENDPOINT,
            now: () => Date.now(),
            randomUUID,
            fetch: (url, init) => fetch(url, init),
            log: debug ? (m) => console.log(`[gt-events] ${m}`) : undefined,
        });
        const c = client;
        AppState.addEventListener('change', (state) => {
            if (state === 'active')
                c.onForeground();
            else if (state === 'background')
                c.onBackground();
        });
        return client;
    }
    catch (e) {
        disabled = true;
        devLog(`disabled: ${String(e)}`);
        return null;
    }
}
/** Optional. Call once at startup only to pass appId/appVersion/endpoint/debug explicitly. */
export function initEvents(options) {
    start(options);
}
/** Same name and params as the Firebase event. `value` + `currency` params are read as revenue. */
export function trackEvent(name, params) {
    var _a;
    (_a = start()) === null || _a === void 0 ? void 0 : _a.track(name, params);
}
/** Call with the player's analytics consent (Firebase `analytics_storage`). Off clears the queue. */
export function setEventsConsent(granted) {
    var _a;
    (_a = start()) === null || _a === void 0 ? void 0 : _a.setConsent(granted);
}
export function setEventsUserId(id) {
    var _a;
    (_a = start()) === null || _a === void 0 ? void 0 : _a.setUserId(id);
}
/** Adapty `profileId`, so subscription renewals and refunds from Adapty can be joined to this install. */
export function setEventsAdaptyProfileId(id) {
    var _a;
    (_a = start()) === null || _a === void 0 ? void 0 : _a.setAdaptyProfileId(id);
}
/** Firebase `getAppInstanceId()`, when the app has Firebase Analytics. */
export function setEventsAppInstanceId(id) {
    var _a;
    (_a = start()) === null || _a === void 0 ? void 0 : _a.setAppInstanceId(id);
}
export function flushEvents() {
    var _a, _b;
    return (_b = (_a = start()) === null || _a === void 0 ? void 0 : _a.flush()) !== null && _b !== void 0 ? _b : Promise.resolve();
}
/** "Delete my data". Resolves true when the server accepted the request; a new install id is used afterwards. */
export function deleteEventsData() {
    var _a, _b;
    return (_b = (_a = start()) === null || _a === void 0 ? void 0 : _a.deleteData()) !== null && _b !== void 0 ? _b : Promise.resolve(false);
}
