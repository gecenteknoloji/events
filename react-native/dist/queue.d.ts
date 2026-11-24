/**
 * Pure helpers for the event queue. No React Native, no I/O.
 * Every number here comes from ../../PROTOCOL.md.
 */
export declare const LIMITS: {
    readonly maxEvents: 100;
    readonly maxBodyBytes: number;
    /** Batches are cut below this so the envelope never pushes them over the server limit. */
    readonly safeBodyBytes: number;
    readonly maxParams: 25;
    readonly maxStringLen: 100;
    readonly queueCap: 2000;
    readonly flushAt: 20;
    readonly flushIntervalMs: 30000;
    readonly timeoutMs: 10000;
    readonly sessionGapMs: number;
    readonly backoffBaseMs: 30000;
    readonly backoffMaxMs: number;
};
export type ParamValue = string | number | boolean;
export type Params = Record<string, ParamValue>;
export interface QueuedEvent {
    name: string;
    params: Params | null;
    value?: number;
    currency?: string;
    ts: number;
    session_id: string;
    seq: number;
}
/**
 * Turns whatever the game passed into a valid event, or null when the name is
 * unusable. Bad params are removed or trimmed instead of failing the event, so
 * one sloppy call site never costs a whole batch (the server rejects whole
 * batches). `value` and `currency` become top-level revenue fields.
 */
export declare function cleanEvent(name: unknown, params: unknown): Pick<QueuedEvent, 'name' | 'params' | 'value' | 'currency'> | null;
/** Adds an event, dropping the oldest ones past the cap. Returns how many were dropped. */
export declare function pushCapped(events: QueuedEvent[], ev: QueuedEvent, cap?: number): number;
export type Outcome = 'done' | 'drop' | 'retry';
export declare function classifyStatus(status: number | 'network'): Outcome;
export declare function nextBackoffMs(attempt: number): number;
export declare function utf8Length(s: string): number;
/** First events of the queue that fit in one request, and the request body. */
export declare function pickBatch(envelope: Record<string, unknown>, events: QueuedEvent[]): {
    body: string;
    count: number;
};
/** Returns the newer version for `kind` from an "rn/1.2.0 unity/1.1.0" header, or null. */
export declare function newerVersion(installed: string, header: string | null | undefined, kind: string): string | null;
export declare function isQueuedEvent(x: unknown): x is QueuedEvent;
