# Changelog

Versions are git tags (`v1.0.0`). React Native and Unity are released together with the same number.

## 1.1.0 (2026-11-25)

- Test report: development builds create a test key, send it as `X-GT-Test-Key` and log once a ready `curl` command for `POST /v1/test/report`. The report checks the events of this test install against the event catalog (unknown or misspelled names, missing params, revenue, balance continuity, offer pairs, coverage per question). Only the key's SHA-256 is stored; release builds send nothing extra.

## 1.0.0 (2026-11-24)

- First release: queue on the device (2000 events), batches of up to 100, sends at 20 events, every 30 s and when the app goes to background, retry with backoff (30 s doubling to 15 min).
- Analytics consent (`setEventsConsent` / `GtEvents.SetConsent`), "delete my data" (`deleteEventsData` / `GtEvents.DeleteData`).
- Revenue from `value` + `currency` params; Adapty profile id and Firebase app instance id in the envelope.
- Development builds log once when a newer version is released.
