// Gecen Teknoloji events: Unity entry point. Add this package to the project,
// then call GtEvents.Track wherever the game already logs an analytics event.
// It starts by itself before the first scene, reads Application.identifier /
// Application.version, keeps its queue on disk and sends in the background.
// Nothing here throws into the game.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace GecenTeknoloji.Events
{
    public static class GtEvents
    {
        public const string SdkVersion = "1.1.1";

        /// <summary>Collector base URL. Change only for testing.</summary>
        public static string Endpoint = "https://api.gecenteknoloji.com";

        /// <summary>Editor play mode only logs events. Set true to really send from the Editor.</summary>
        public static bool SendInEditor = false;

        /// <summary>Log every send. Defaults to Debug.isDebugBuild.</summary>
        public static bool DebugLog = Debug.isDebugBuild;

        internal static readonly object Gate = new object();
        internal static EventsCore Core;
        static GtEventsRunner runner;
        static bool disabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() => Ensure();

        internal static bool Ensure()
        {
            if (Core != null) return true;
            if (disabled) return false;
            try
            {
                string platform;
                switch (Application.platform)
                {
                    case RuntimePlatform.Android: platform = "android"; break;
                    case RuntimePlatform.IPhonePlayer: platform = "ios"; break;
                    default:
                        if (!Application.isEditor) { disabled = true; return false; }
                        platform = Application.platform == RuntimePlatform.OSXEditor ? "ios" : "android";
                        break;
                }
                string appId = Application.identifier;
                if (string.IsNullOrEmpty(appId)) { disabled = true; Warn("disabled: Application.identifier is empty"); return false; }

                var identity = new Identity
                {
                    Sdk = "unity/" + SdkVersion,
                    AppId = appId,
                    Platform = platform,
                    AppVersion = Cut(Application.version, 32),
                    Locale = Cut(CultureInfo.CurrentCulture?.Name, 35),
                    DeviceModel = Cut(SystemInfo.deviceModel, 64),
                    OsVersion = Cut(SystemInfo.operatingSystem, 32),
                };
                Core = new EventsCore(identity, () => Guid.NewGuid().ToString("D"), NowMs, Debug.isDebugBuild);
                Storage.Restore(Core);

                var go = new GameObject("GtEvents") { hideFlags = HideFlags.HideInHierarchy };
                UnityEngine.Object.DontDestroyOnLoad(go);
                runner = go.AddComponent<GtEventsRunner>();
                return true;
            }
            catch (Exception e)
            {
                disabled = true;
                Warn("disabled: " + e.Message);
                return false;
            }
        }

        static string Cut(string s, int max) => string.IsNullOrEmpty(s) ? null : (s.Length > max ? s.Substring(0, max) : s);

        internal static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        static bool updateNoticeShown;

        internal static void NoticeUpdate(string header)
        {
            if (updateNoticeShown || !DebugLog) return;
            string latest = Rules.NewerVersion(SdkVersion, header, "unity");
            if (latest == null) return;
            updateNoticeShown = true;
            Debug.LogWarning("[GtEvents] a newer package is available: unity/" + latest + " (installed unity/" + SdkVersion + "). See CHANGELOG.md");
        }

        internal static void Warn(string m)
        {
            if (DebugLog) Debug.LogWarning("[GtEvents] " + m);
        }

        internal static void Log(string m)
        {
            if (DebugLog) Debug.Log("[GtEvents] " + m);
        }

        /// <summary>Same name and params as the Firebase event. "value" + "currency" params are read as revenue.</summary>
        public static void Track(string name, IDictionary<string, object> parameters)
        {
            try
            {
                if (!Ensure()) return;
                lock (Gate)
                {
                    if (!Core.Track(name, parameters)) { Log("not queued: " + name); return; }
                }
                Storage.MarkDirty();
                if (Core.ShouldFlushNow) runner?.RequestFlush();
            }
            catch (Exception e) { Warn("Track failed: " + e.Message); }
        }

        public static void Track(string name, params (string key, object value)[] parameters)
        {
            var dict = new Dictionary<string, object>(parameters?.Length ?? 0);
            if (parameters != null) foreach (var p in parameters) dict[p.key] = p.value;
            Track(name, dict);
        }

        /// <summary>Player's analytics consent (Firebase analytics_storage). Off clears the queue.</summary>
        public static void SetConsent(bool granted)
        {
            try
            {
                if (!Ensure()) return;
                lock (Gate) Core.SetConsent(granted);
                Storage.SaveNow(Core);
            }
            catch (Exception e) { Warn("SetConsent failed: " + e.Message); }
        }

        public static void SetUserId(string id) { if (Ensure()) lock (Gate) Core.UserId = id; }

        /// <summary>Adapty profileId, so renewals and refunds from Adapty join this install.</summary>
        public static void SetAdaptyProfileId(string id) { if (Ensure()) lock (Gate) Core.AdaptyProfileId = id; }

        /// <summary>Firebase app instance id, when the game has Firebase Analytics.</summary>
        public static void SetAppInstanceId(string id) { if (Ensure()) lock (Gate) Core.AppInstanceId = id; }

        public static void Flush() { if (Ensure()) runner?.RequestFlush(); }

        /// <summary>"Delete my data". Calls back true when the server accepted it; a new install id is used afterwards.</summary>
        public static void DeleteData(Action<bool> done = null)
        {
            if (!Ensure() || runner == null) { done?.Invoke(false); return; }
            runner.StartCoroutine(runner.Delete(done));
        }
    }

    /// <summary>Queue on disk: one JSON event per line, plus a few PlayerPrefs keys.</summary>
    internal static class Storage
    {
        const string KeyInstall = "gt_events.install_id";
        const string KeyConsent = "gt_events.consent";
        const string KeyDropped = "gt_events.dropped";
        const string KeyTestKey = "gt_events.test_key";
        static bool dirty;

        static string QueuePath => Path.Combine(Application.persistentDataPath, "gt-events", "queue.jsonl");

        public static void MarkDirty() => dirty = true;
        public static bool Dirty => dirty;

        public static void Restore(EventsCore core)
        {
            try
            {
                string id = PlayerPrefs.GetString(KeyInstall, null);
                bool? consent = PlayerPrefs.HasKey(KeyConsent) ? PlayerPrefs.GetInt(KeyConsent) == 1 : (bool?)null;
                int dropped = PlayerPrefs.GetInt(KeyDropped, 0);
                string[] lines = File.Exists(QueuePath) ? File.ReadAllLines(QueuePath, Encoding.UTF8) : null;
                core.Restore(id, consent, dropped, lines);
                if (core.TestKey != null) core.RestoreTestKey(PlayerPrefs.GetString(KeyTestKey, null));
            }
            catch (Exception e) { GtEvents.Warn("restore failed: " + e.Message); }
            SaveNow(core);
        }

        public static void SaveNow(EventsCore core)
        {
            try
            {
                string[] lines;
                string id;
                bool consent;
                int dropped;
                string testKey;
                lock (GtEvents.Gate)
                {
                    lines = new string[core.Count];
                    for (int i = 0; i < lines.Length; i++) lines[i] = core.QueuedJson[i];
                    id = core.InstallId;
                    consent = core.Consent;
                    dropped = core.Dropped;
                    testKey = core.TestKey;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(QueuePath));
                string tmp = QueuePath + ".tmp";
                File.WriteAllLines(tmp, lines, new UTF8Encoding(false));
                if (File.Exists(QueuePath)) File.Delete(QueuePath);
                File.Move(tmp, QueuePath);
                PlayerPrefs.SetString(KeyInstall, id);
                PlayerPrefs.SetInt(KeyConsent, consent ? 1 : 0);
                PlayerPrefs.SetInt(KeyDropped, dropped);
                if (testKey != null) PlayerPrefs.SetString(KeyTestKey, testKey);
                PlayerPrefs.Save();
                dirty = false;
            }
            catch (Exception e) { GtEvents.Warn("save failed: " + e.Message); }
        }
    }

    [AddComponentMenu("")]
    internal sealed class GtEventsRunner : MonoBehaviour
    {
        const float SaveEverySeconds = 2f;
        float lastFlush;
        float lastSave;
        bool sending;
        bool flushRequested;

        public void RequestFlush() => flushRequested = true;

        void Update()
        {
            try
            {
                float t = Time.unscaledTime;
                if (!sending && (flushRequested || t - lastFlush >= Limits.FlushIntervalMs / 1000f))
                {
                    flushRequested = false;
                    lastFlush = t;
                    bool can;
                    lock (GtEvents.Gate) can = GtEvents.Core.CanSend;
                    if (can) StartCoroutine(Drain());
                }
                if (Storage.Dirty && t - lastSave >= SaveEverySeconds)
                {
                    lastSave = t;
                    Storage.SaveNow(GtEvents.Core);
                }
            }
            catch (Exception e) { GtEvents.Warn("update failed: " + e.Message); }
        }

        void OnApplicationPause(bool paused)
        {
            try
            {
                lock (GtEvents.Gate)
                {
                    if (paused) GtEvents.Core.OnBackground();
                    else GtEvents.Core.OnForeground();
                }
                if (paused)
                {
                    Storage.SaveNow(GtEvents.Core);
                    flushRequested = true;
                    if (!sending) StartCoroutine(Drain());
                }
            }
            catch (Exception e) { GtEvents.Warn("pause failed: " + e.Message); }
        }

        void OnApplicationQuit() => Storage.SaveNow(GtEvents.Core);

        IEnumerator Drain()
        {
            if (sending) yield break;
            sending = true;
            try
            {
                for (int i = 0; i < 50; i++)
                {
                    string body;
                    int count, droppedSent;
                    lock (GtEvents.Gate)
                    {
                        if (!GtEvents.Core.CanSend) break;
                        body = GtEvents.Core.BuildBatch(out count, out droppedSent);
                    }
                    if (Application.isEditor && !GtEvents.SendInEditor)
                    {
                        GtEvents.Log("editor, not sent: " + body);
                        lock (GtEvents.Gate) GtEvents.Core.OnSendResult(204, count, droppedSent);
                        continue;
                    }
                    int status = -1;
                    yield return Post("/v1/events", body, s => status = s);
                    Outcome outcome;
                    lock (GtEvents.Gate) outcome = GtEvents.Core.OnSendResult(status, count, droppedSent);
                    GtEvents.Log("flush " + status + " " + count);
                    if (status == 204) LogTestReport();
                    Storage.MarkDirty();
                    if (outcome == Outcome.Retry) break;
                }
            }
            finally { sending = false; }
        }

        static void LogTestReport()
        {
            try
            {
                string line;
                lock (GtEvents.Gate) line = GtEvents.Core.TestReportLineOnce(GtEvents.Endpoint);
                if (line != null) Debug.Log("[GtEvents] " + line);
            }
            catch { }
        }

        internal IEnumerator Delete(Action<bool> done)
        {
            string body;
            lock (GtEvents.Gate) body = GtEvents.Core.BuildDeleteBody();
            int status = -1;
            yield return Post("/v1/delete", body, s => status = s);
            bool ok = status == 202 || status == 200;
            if (ok)
            {
                lock (GtEvents.Gate) GtEvents.Core.ResetAfterDelete();
                Storage.SaveNow(GtEvents.Core);
            }
            try { done?.Invoke(ok); } catch (Exception e) { GtEvents.Warn("DeleteData callback threw: " + e.Message); }
        }

        static IEnumerator Post(string path, string body, Action<int> result)
        {
            UnityWebRequest req = null;
            try
            {
                req = new UnityWebRequest(GtEvents.Endpoint + path, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)) { contentType = "application/json" },
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = Limits.TimeoutSeconds,
                };
                req.SetRequestHeader("Content-Type", "application/json");
                string testKey;
                lock (GtEvents.Gate) testKey = GtEvents.Core.TestKeyHeaderFor(path);
                if (testKey != null) req.SetRequestHeader("X-GT-Test-Key", testKey);
            }
            catch (Exception e)
            {
                GtEvents.Warn("request setup failed: " + e.Message);
                req?.Dispose();
                result(-1);
                yield break;
            }
            yield return req.SendWebRequest();
            int status = (req.result == UnityWebRequest.Result.ConnectionError || req.responseCode == 0) ? -1 : (int)req.responseCode;
            try { GtEvents.NoticeUpdate(req.GetResponseHeader("X-GT-SDK-Latest")); } catch { }
            req.Dispose();
            result(status);
        }
    }
}
