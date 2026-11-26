# Event protocol v1

Every app (React Native, Unity, native) talks to the collector the same way. The client modules in this repo implement it; anything else must follow this page exactly.

## Endpoint

`POST https://api.gecenteknoloji.com/v1/events`, `content-type: application/json`, body at most 256 KiB.

No keys, no auth header. The server adds country and receive time itself.

## Batch

```json
{
  "v": 1,
  "sdk": "rn/1.0.0",
  "app_id": "com.gecenteknoloji.jgswdrp",
  "platform": "android",
  "app_version": "1.0.16",
  "install_id": "3f2b8c1e-9a4d-4e6f-8b7a-1c2d3e4f5a6b",
  "user_id": null,
  "app_instance_id": null,
  "adapty_profile_id": null,
  "locale": "tr-TR",
  "device_model": "Pixel 8",
  "os_version": "15",
  "dropped": 0,
  "events": [
    { "name": "level_start", "params": { "level_index": 5 }, "ts": 1795521600000, "session_id": "s-1", "seq": 0 },
    { "name": "ad_impression", "params": { "placement": "level_end" }, "value": 0.0042, "currency": "USD", "ts": 1795521601000, "session_id": "s-1", "seq": 1 }
  ]
}
```

| Field | Rule |
|---|---|
| `v` | Always `1` |
| `sdk` | `<kind>/<version>`, e.g. `rn/1.0.0`, `unity/1.0.0` |
| `app_id` | Android package / iOS bundle id, read from the app at runtime |
| `platform` | `ios` or `android` |
| `install_id` | Random UUID v4, lowercase, created on first launch and stored on the device. Not derived from any device or ad identifier |
| `user_id`, `app_instance_id`, `adapty_profile_id` | Optional. App's own account id, Firebase app instance id, Adapty profile id |
| `locale`, `device_model`, `os_version`, `app_version` | Optional, short strings |
| `dropped` | Events the device discarded because its queue was full since the last successful send |
| `events` | 1 to 100 events |
| `name` | `^[a-z][a-z0-9_]{0,39}$`, names from the event catalog |
| `params` | Optional flat object, at most 25 keys (`^[a-z][a-z0-9_]{0,39}$`), values string (max 100 chars), finite number or boolean |
| `value`, `currency` | Optional. Revenue amount and ISO 4217 code (`USD`) |
| `ts` | Device time in Unix milliseconds |
| `session_id`, `seq` | Session id (new after 30 minutes in background) and a counter starting at 0 in each session. Together they identify an event, so a resent batch can be de-duplicated |

One invalid event rejects the whole batch.

Apps may add their own events and params (a game-specific mode, a new reward, an extra field on an existing event) within these rules. The collector stores any valid name; nothing is registered on the server side. Do not reuse a catalog name with a different meaning.

## Responses and what the client does

| Status | Meaning | Client |
|---|---|---|
| `204` | Stored | Remove the batch from the queue |
| `400` | Invalid batch | Remove it, do not retry (log in dev builds) |
| `413` | Too large | Remove it, send smaller batches |
| `429` | Rate limited (30 requests / 60 s per install) | Keep it, retry with backoff |
| `503`, other `5xx`, network error, timeout | Temporary | Keep it, retry with backoff |

Client rules: queue at most 2000 events on the device (drop the oldest and count them in `dropped`), send when 20 events are queued, every 30 seconds and when the app goes to background, 10 second timeout, backoff 30 s doubling up to 15 minutes. If the player declined analytics consent, do not queue and clear the queue. Never throw, never block the game.

## Delete my data

`POST https://api.gecenteknoloji.com/v1/delete` with `{ "app_id": "...", "install_id": "..." }` returns `202`. After that the client creates a new `install_id`. Stored events of that install are removed by the nightly purge; its test rows (see Test report) are removed at once. The test key stays on the device.

## Test report

Lets the developer (or their AI assistant) check an integration against the event catalog on the data the collector actually received, for their own test device only.

- **Development builds only** (React Native `__DEV__`, Unity `Debug.isDebugBuild`). Release builds never send a test key.
- On first use the client creates a **test key**: 64 lowercase hex characters (two random UUID v4 values without dashes), stored on the device next to the queue. It is not derived from any device identifier and is not reset by "delete my data".
- Every `/v1/events` request from a development build carries the header `X-GT-Test-Key: <key>`. Format: `^[0-9a-f]{64}$`; anything else is ignored. The batch itself is handled exactly as without the header.
- The collector stores only the key's SHA-256. Every stored test batch is tagged with it, and a report shows only the batches sent with the key it is asked with. Test batches are kept **7 days**, at most 5000 events per install and key, and are stored after the answer, so they never delay it. "Delete my data" removes them at once, and a later batch of a deleted install id is not kept. Resent events (same `session_id` and `seq`) are counted once.
- After the first `204` the client logs once, in development builds: `[gt-events] test report: curl ...` (Unity `[GtEvents] test report: curl ...`), a ready command for the request below.

`POST https://api.gecenteknoloji.com/v1/test/report`, body `{ "app_id": "...", "install_id": "...", "test_key": "..." }` (at most 1 KiB), rate limit as for events.

| Status | Meaning |
|---|---|
| `200` | JSON report (below) |
| `400` | Bad body |
| `404` | No test data for this app, install and key. A wrong key and missing data answer the same way |
| `429` | Rate limited |

Report fields: `verdict` (`pass` when `errors` is empty, else `fail`), `summary` (events, sessions, first and last receive time, sdk, app version, platform, dropped), `errors`, `warnings` and `info` (each `{ code, message, event?, count?, examples? }`), `coverage` (for each question S1-S6 of the measurement guide and each event family: catalog events seen and not seen), `game_specific` (names outside the catalog). Error codes: `likely_typo` (name outside the catalog within 2 edits of a catalog name), `missing_param` (required param absent), `bad_value` (fixed-value param outside its list), `missing_revenue` (`ad_impression` / `purchase` without positive `value` and `currency`), `balance_gap` (`balance_after` does not follow from the previous balance and `amount`, so a currency source or sink is not sent), `zero_amount`, `purchase_failed_without_stage`. Warning codes: `offer_unpaired` (`offer_shown` and `offer_action` counts differ per placement), `level_unfinished` (`level_start` with no `level_complete` / `level_fail` / `level_quit` in the same session), `dropped_events`. Info: `not_seen` (catalog events not seen; an event for a feature the game does not have stays unseen and is written as "feature missing" in the coverage table).

## Adapty webhook

`POST https://api.gecenteknoloji.com/v1/webhooks/adapty/<app_id>` with header `Authorization: <secret>`. The key is per app: ask the app owner for it before setting up the webhook. Set it once in Adapty (Integrations > Webhook): that URL with the app's package name at the end for production and sandbox, and the key in "Authorization header value". Apps without an issued key, and wrong keys, get `401`; nothing is accepted on first use. The app comes from the URL, the platform from `event_properties.store`. Each event becomes one row with `source = "adapty"`, `name = "adapty_<event_type>"`, `value` = `price_usd` (currency `USD`), other scalar `event_properties` in `params` (Adapty's local `currency` is stored as `local_currency`). Rows join app events through `adapty_profile_id`, which the client sets with `setEventsAdaptyProfileId` / `GtEvents.SetAdaptyProfileId`.

## Server refund

For apps that verify purchases on their own server (apps that use Adapty get refunds through the Adapty webhook instead). When the store reports a refund (App Store Server Notifications `REFUND`, Google Play RTDN or the Voided Purchases API), the server sends it here. The client never sends `refund`.

`POST https://api.gecenteknoloji.com/v1/server/<app_id>/refund` with header `Authorization: Bearer <key>` and a JSON body of at most 4 KB, one refund per request. The key is per app: ask the app owner for it before setting up the server. It stays on the server and never goes into the app.

```json
{
  "v": 1,
  "platform": "ios",
  "environment": "production",
  "transaction_id": "2000000123456789",
  "ts": 1764150000000,
  "value": 4.99,
  "currency": "USD",
  "sku": "coins_500",
  "original_transaction_id": "2000000123456789",
  "reason": "customer_request",
  "install_id": null,
  "user_id": null
}
```

| Field | Rule |
|---|---|
| `v` | Required, `1` |
| `platform` | Required, `ios` or `android` |
| `environment` | Required, `production` or `sandbox`. Sandbox refunds are checked and answered but not stored |
| `transaction_id` | Required, 1-100 chars. The same value as `transaction_id` of the app's `purchase` event; this is how the refund joins the purchase and its install |
| `ts` | Required, refund time from the store notification, Unix ms, at most 365 days in the past and 1 day in the future |
| `value`, `currency` | Optional, together: refunded amount (>= 0) and ISO 4217 code. Prefer the same amount and currency as the `purchase` event |
| `sku`, `original_transaction_id`, `reason` | Optional, 1-100 chars |
| `install_id` | Optional, the module's install id if the server has it (UUID) |
| `user_id` | Optional, app's own account id, 1-128 chars |

Unknown fields are ignored. The request becomes one row with `source = "server"`, `name = "refund"`, `sdk = "server/1"`, `transaction_id`, `environment` and the optional strings in `params`, and `client_ts = ts`.

| Response | Server does |
|---|---|
| `200 {"stored": true}` / `200 {"stored": false}` (sandbox) | Done |
| `400` (bad body, reason in the text), `413` | Log and drop, do not retry |
| `401` | Wrong or missing key, or the app has no key: fix the configuration |
| `404` | Bad app id in the URL |
| `429`, `503` | Retry later with backoff. Retrying the same refund is safe: rows with the same `transaction_id` and `ts` count once |

## Updates

Every accepted batch answers with `X-GT-SDK-Latest: rn/<version> unity/<version>`. Clients log once in development builds when a newer version exists. Releases and changes: `CHANGELOG.md`.
