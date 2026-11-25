# Gecen Teknoloji events SDK

Client for the Gecen Teknoloji event collector (`https://api.gecenteknoloji.com`). It queues events on the device, sends them in batches and keeps them while offline. No keys, no accounts, no console setup: package name, version and device info are read at runtime.

Contract between apps and the collector: [PROTOCOL.md](PROTOCOL.md). Releases: [CHANGELOG.md](CHANGELOG.md).

## What it is for

The events sent through this SDK are how the game gets better. Every week they are read to answer six questions: where players get stuck or stop, what they do in the game, whether they come back, how much each player earns, what would raise that, and what is broken. The answers turn into changes in the game: an easier level, a fixed flow, a different offer or ad placement, a new price or reward.

An integration is complete when these questions can be answered **for this game**, not when a list of events has been copied.

## Integrating (also with an AI assistant)

1. Read the measurement guide you received from the app owner in full. Section 0 explains the six questions, section 0.1 lists the steps, section 5 is the event catalog.
2. Map the game before writing code: screens and flow, the progression unit, every point where a player can stop, every source and sink of in-game currency, every ad call, product and offer. Match each to a catalog event; where the catalog has nothing for a moment that answers one of the questions, design a game-specific event. Show this coverage table to the developer and get approval first.
3. Send every event through `trackEvent` / `GtEvents.Track`, from the same place as the Firebase call. Do not modify this package.
4. Check the result yourself with the test report (below) until `verdict` is `pass`.

## Test report

Development builds (`__DEV__`, Unity Development Build) log one line after the first successful send:

```
[gt-events] test report: curl -s -X POST https://api.gecenteknoloji.com/v1/test/report -H 'content-type: application/json' -d '{"app_id":"...","install_id":"...","test_key":"..."}'
```

Run it as it is. The answer compares the events this test device sent with the event catalog: `verdict`, `errors` (misspelled names, missing params, revenue without value, gaps in `balance_after`), `warnings`, `coverage` per question and event family, and `game_specific` names. Fix every error, play again, run it again. The test key is created on the device and only its SHA-256 is stored, so the report shows this device's data to whoever holds this key and nobody else; do not paste the command anywhere public. Release builds send no key. Details: [PROTOCOL.md](PROTOCOL.md#test-report).

## React Native

```bash
npm install github:gecenteknoloji/events#v1.1.0
```

```ts
import { trackEvent, setEventsConsent, deleteEventsData, setEventsAdaptyProfileId } from 'gecenteknoloji-events';

trackEvent('level_complete', { level_index: 12, moves: 31 });            // next to the Firebase logEvent call
trackEvent('ad_impression', { placement: 'level_end', value: 0.0042, currency: 'USD' });
setEventsConsent(analyticsStorageGranted);                               // from the consent gate
setEventsAdaptyProfileId(profile.profileId);                             // Adapty apps only
const accepted = await deleteEventsData();                               // Settings > Delete my data
```

Uses these packages when the app has them: `react-native-mmkv` or `@react-native-async-storage/async-storage` (queue survives restarts), `expo-application` or `react-native-device-info` (package name and version). With neither of the last two, call `initEvents({ appId, appVersion })` once at startup. Development builds log every send: `[gt-events] events flush 204 <count>`.

Update: change the tag in `package.json` (for example `#v1.1.0`) and run `npm install`.

## Unity (2021.3+, iOS and Android)

Package Manager > Add package from git URL:

```
https://github.com/gecenteknoloji/events.git?path=/unity#v1.1.0
```

```csharp
using GecenTeknoloji.Events;

GtEvents.Track("level_complete", ("level_index", 12), ("moves", 31));     // next to the Firebase LogEvent call
GtEvents.Track("ad_impression", ("placement", "level_end"), ("value", 0.0042), ("currency", "USD"));
GtEvents.SetConsent(analyticsStorageGranted);
GtEvents.SetAdaptyProfileId(profile.ProfileId);                           // Adapty apps only
GtEvents.DeleteData(ok => { /* Settings > Delete my data */ });
```

Starts before the first scene. Development Builds log `[GtEvents] flush 204 <count>`. In the Editor events are only logged; set `GtEvents.SendInEditor = true` to really send.

Update: change the tag at the end of the git URL in `Packages/manifest.json` (for example `#v1.1.0`).

## Game-specific events

Any event or param that follows the rules in [PROTOCOL.md](PROTOCOL.md) is stored, with no setup on the collector side: add it to the app's event catalog and send it through the same `trackEvent` / `GtEvents.Track` call.

## Adapty webhook

Adapty dashboard > Integrations > Webhook, production and sandbox URL `https://api.gecenteknoloji.com/v1/webhooks/adapty/<package-name>`, "Authorization header value": the key for this app, asked from the app owner before setup.
