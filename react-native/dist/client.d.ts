export interface KeyValueStore {
    getItem(key: string): Promise<string | null>;
    setItem(key: string, value: string): Promise<void>;
    removeItem(key: string): Promise<void>;
}
export interface ClientDeps {
    store: KeyValueStore;
    endpoint: string;
    now(): number;
    randomUUID(): string;
    fetch(url: string, init: {
        method: 'POST';
        headers: Record<string, string>;
        body: string;
        signal?: AbortSignal;
    }): Promise<{
        status: number;
        headers?: {
            get(name: string): string | null;
        };
    }>;
    log?(message: string): void;
    /** Development build: enables the test key header and the test report log line. */
    dev?: boolean;
}
export interface Identity {
    sdk: string;
    app_id: string;
    platform: 'ios' | 'android';
    app_version?: string;
    locale?: string;
    device_model?: string;
    os_version?: string;
}
export interface EventsClient {
    readonly ready: Promise<void>;
    track(name: string, params?: Record<string, unknown>): void;
    setConsent(granted: boolean): void;
    setUserId(id: string | null): void;
    setAdaptyProfileId(id: string | null): void;
    setAppInstanceId(id: string | null): void;
    onForeground(): void;
    onBackground(): void;
    flush(): Promise<void>;
    deleteData(): Promise<boolean>;
    persistNow(): Promise<void>;
    dispose(): void;
}
export declare const STORAGE_KEY = "gt_events.v1";
export declare function createEventsClient(identity: Identity, deps: ClientDeps): EventsClient;
