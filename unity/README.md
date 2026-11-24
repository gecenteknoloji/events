# Gecen Teknoloji events: Unity

Install with Package Manager > Add package from git URL (the URL with the version tag is in the repository README). Unity 2021.3 or newer, iOS and Android. No keys, no setup: it starts before the first scene and reads `Application.identifier` and `Application.version`.

```csharp
using GecenTeknoloji.Events;

GtEvents.Track("level_complete", ("level_index", 12), ("moves", 31));   // next to the Firebase LogEvent call
GtEvents.Track("ad_impression", ("placement", "level_end"), ("value", 0.0042), ("currency", "USD"));
GtEvents.SetConsent(analyticsStorageGranted);                             // from the consent flow
GtEvents.SetAdaptyProfileId(profile.ProfileId);                           // Adapty apps only
GtEvents.DeleteData(ok => { /* Settings > Delete my data */ });
```

The queue is kept in `Application.persistentDataPath/gt-events/` and sent every 30 seconds, at 20 events and when the app goes to background. Development Builds log `[GtEvents] flush 204 <count>`. In the Editor events are only logged; set `GtEvents.SendInEditor = true` to really send.

Core tests run outside Unity with the .NET inside the Unity install: `Tests~/run-tests.sh` (add `--live` to post one real batch). Protocol: `PROTOCOL.md` at the repository root.
