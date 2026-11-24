# Gecen Teknoloji events SDK

Client for the Gecen Teknoloji event collector (`https://api.gecenteknoloji.com`). It queues events on the device, sends them in batches and keeps them while offline. No keys, no accounts, no console setup: package name, version and device info are read at runtime.

Contract between apps and the collector: [PROTOCOL.md](PROTOCOL.md). Releases: [CHANGELOG.md](CHANGELOG.md).

## React Native

```bash
npm install github:gecenteknoloji/events#v1.0.0
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
https://github.com/gecenteknoloji/events.git?path=/unity#v1.0.0
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

## Adapty webhook

Adapty dashboard > Integrations > Webhook, production and sandbox URL `https://api.gecenteknoloji.com/v1/webhooks/adapty/<package-name>`, "Authorization header value": the key for this app, asked from the app owner before setup.
