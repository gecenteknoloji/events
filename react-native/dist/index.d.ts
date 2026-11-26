export declare const SDK_VERSION = "1.1.1";
export declare const DEFAULT_ENDPOINT = "https://api.gecenteknoloji.com";
export interface InitOptions {
    /** Only needed when the app has none of expo-application / react-native-device-info. */
    appId?: string;
    appVersion?: string;
    endpoint?: string;
    /** Log every send in the console. Defaults to __DEV__. */
    debug?: boolean;
}
/** Optional. Call once at startup only to pass appId/appVersion/endpoint/debug explicitly. */
export declare function initEvents(options?: InitOptions): void;
/** Same name and params as the Firebase event. `value` + `currency` params are read as revenue. */
export declare function trackEvent(name: string, params?: Record<string, unknown>): void;
/** Call with the player's analytics consent (Firebase `analytics_storage`). Off clears the queue. */
export declare function setEventsConsent(granted: boolean): void;
export declare function setEventsUserId(id: string | null): void;
/** Adapty `profileId`, so subscription renewals and refunds from Adapty can be joined to this install. */
export declare function setEventsAdaptyProfileId(id: string | null): void;
/** Firebase `getAppInstanceId()`, when the app has Firebase Analytics. */
export declare function setEventsAppInstanceId(id: string | null): void;
export declare function flushEvents(): Promise<void>;
/** "Delete my data". Resolves true when the server accepted the request; a new install id is used afterwards. */
export declare function deleteEventsData(): Promise<boolean>;
