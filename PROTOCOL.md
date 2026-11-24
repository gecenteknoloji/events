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

`POST https://api.gecenteknoloji.com/v1/delete` with `{ "app_id": "...", "install_id": "..." }` returns `202`. After that the client creates a new `install_id`. Stored events of that install are removed by the nightly purge.

## Adapty webhook

`POST https://api.gecenteknoloji.com/v1/webhooks/adapty/<app_id>` with header `Authorization: <secret>`. The key is per app: ask the app owner for it before setting up the webhook. Set it once in Adapty (Integrations > Webhook): that URL with the app's package name at the end for production and sandbox, and the key in "Authorization header value". Apps without an issued key, and wrong keys, get `401`; nothing is accepted on first use. The app comes from the URL, the platform from `event_properties.store`. Each event becomes one row with `source = "adapty"`, `name = "adapty_<event_type>"`, `value` = `price_usd` (currency `USD`), other scalar `event_properties` in `params` (Adapty's local `currency` is stored as `local_currency`). Rows join app events through `adapty_profile_id`, which the client sets with `setEventsAdaptyProfileId` / `GtEvents.SetAdaptyProfileId`.

## Updates

Every accepted batch answers with `X-GT-SDK-Latest: rn/<version> unity/<version>`. Clients log once in development builds when a newer version exists. Releases and changes: `CHANGELOG.md`.
