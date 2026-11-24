// Gecen Teknoloji events: engine-independent core (queue, batching, retry
// rules, JSON). No UnityEngine here, so it is unit-tested outside Unity
// (Tests~). Contract: PROTOCOL.md in the teknik-rehber folder.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GecenTeknoloji.Events
{
    public static class Limits
    {
        public const int MaxEvents = 100;
        public const int MaxBodyBytes = 256 * 1024;
        public const int SafeBodyBytes = 240 * 1024;
        public const int MaxParams = 25;
        public const int MaxStringLen = 100;
        public const int QueueCap = 2000;
        public const int FlushAt = 20;
        public const long FlushIntervalMs = 30_000;
        public const int TimeoutSeconds = 10;
        public const long SessionGapMs = 30 * 60_000;
        public const long BackoffBaseMs = 30_000;
        public const long BackoffMaxMs = 15 * 60_000;
    }

    public enum Outcome { Done, Drop, Retry }

    public sealed class Identity
    {
        public string Sdk;
        public string AppId;
        public string Platform; // "ios" | "android"
        public string AppVersion;
        public string Locale;
        public string DeviceModel;
        public string OsVersion;
    }

    public static class Rules
    {
        static readonly Regex NameRe = new Regex("^[a-z][a-z0-9_]{0,39}$", RegexOptions.CultureInvariant);
        static readonly Regex CurrencyRe = new Regex("^[A-Z]{3}$", RegexOptions.CultureInvariant);
        static readonly Regex UuidRe = new Regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.CultureInvariant);

        public static bool IsName(string s) => s != null && NameRe.IsMatch(s);
        public static bool IsCurrency(string s) => s != null && CurrencyRe.IsMatch(s);
        public static bool IsUuid(string s) => s != null && UuidRe.IsMatch(s);

        /// <summary>-1 means network error or timeout.</summary>
        public static Outcome Classify(int status)
        {
            if (status >= 200 && status < 300) return Outcome.Done;
            if (status == 400 || status == 413) return Outcome.Drop;
            return Outcome.Retry;
        }

        /// <summary>Newer version for `kind` from an "rn/1.2.0 unity/1.1.0" header, or null.</summary>
        public static string NewerVersion(string installed, string header, string kind)
        {
            if (string.IsNullOrEmpty(header)) return null;
            foreach (var part in header.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int slash = part.IndexOf('/');
                if (slash <= 0 || part.Substring(0, slash) != kind) continue;
                string v = part.Substring(slash + 1);
                if (!Regex.IsMatch(v, "^[0-9]+(\\.[0-9]+){0,2}$")) return null;
                int[] a = Parse(v), b = Parse(installed ?? "0");
                for (int i = 0; i < 3; i++)
                {
                    if (a[i] > b[i]) return v;
                    if (a[i] < b[i]) return null;
                }
                return null;
            }
            return null;
        }

        static int[] Parse(string v)
        {
            var r = new int[3];
            var p = v.Split('.');
            for (int i = 0; i < 3 && i < p.Length; i++) int.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out r[i]);
            return r;
        }

        public static long BackoffMs(int attempt)
        {
            double ms = Limits.BackoffBaseMs * Math.Pow(2, attempt);
            return ms >= Limits.BackoffMaxMs ? Limits.BackoffMaxMs : (long)ms;
        }
    }

    public static class Json
    {
        public static void String(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static void Number(StringBuilder sb, double d) => sb.Append(d.ToString("R", CultureInfo.InvariantCulture));

        public static void Field(StringBuilder sb, ref bool first, string key, string value)
        {
            if (value == null) return;
            if (!first) sb.Append(',');
            first = false;
            String(sb, key);
            sb.Append(':');
            String(sb, value);
        }

        public static void RawField(StringBuilder sb, ref bool first, string key, string raw)
        {
            if (!first) sb.Append(',');
            first = false;
            String(sb, key);
            sb.Append(':').Append(raw);
        }
    }

    /// <summary>
    /// Queue state and protocol rules. Events are stored as finished JSON
    /// objects, so persistence is one line per event and needs no parser.
    /// Not thread-safe by itself; GtEvents locks around it.
    /// </summary>
    public sealed class EventsCore
    {
        readonly Identity identity;
        readonly Func<string> newUuid;
        readonly Func<long> nowMs;
        readonly List<string> queue = new List<string>();

        string sessionId;
        int seq;
        long backgroundAt = -1;
        int attempt;
        long nextTryAt;

        public string InstallId { get; private set; }
        public bool Consent { get; private set; } = true;
        public int Dropped { get; private set; }
        public string UserId;
        public string AdaptyProfileId;
        public string AppInstanceId;

        public int Count => queue.Count;
        public IReadOnlyList<string> QueuedJson => queue;

        public EventsCore(Identity identity, Func<string> newUuid, Func<long> nowMs)
        {
            this.identity = identity;
            this.newUuid = newUuid;
            this.nowMs = nowMs;
            sessionId = Uuid();
            InstallId = Uuid();
        }

        string Uuid()
        {
            try
            {
                string id = newUuid().ToLowerInvariant();
                if (Rules.IsUuid(id)) return id;
            }
            catch { }
            return Guid.NewGuid().ToString("D").ToLowerInvariant();
        }

        /// <summary>Loads persisted state. Events tracked before this call are kept after the restored ones.</summary>
        public void Restore(string installId, bool? consent, int dropped, IEnumerable<string> queuedJson)
        {
            if (Rules.IsUuid(installId)) InstallId = installId;
            if (consent.HasValue) Consent = consent.Value;
            if (dropped > 0) Dropped += dropped;
            var restored = new List<string>();
            if (queuedJson != null)
                foreach (var line in queuedJson)
                    if (!string.IsNullOrEmpty(line) && line[0] == '{' && line[line.Length - 1] == '}') restored.Add(line);
            queue.InsertRange(0, restored);
            Trim();
            if (!Consent) { queue.Clear(); Dropped = 0; }
        }

        void Trim()
        {
            int over = queue.Count - Limits.QueueCap;
            if (over > 0)
            {
                queue.RemoveRange(0, over);
                Dropped += over;
            }
        }

        /// <summary>Returns false when the event was not queued (no consent, bad name).</summary>
        public bool Track(string name, IDictionary<string, object> parameters)
        {
            if (!Consent || !Rules.IsName(name)) return false;
            var sb = new StringBuilder(128);
            sb.Append('{');
            bool first = true;
            Json.Field(sb, ref first, "name", name);

            double? value = null;
            string currency = null;
            if (parameters != null && parameters.Count > 0)
            {
                var ps = new StringBuilder();
                int n = 0;
                foreach (var kv in parameters)
                {
                    string k = kv.Key;
                    object v = kv.Value;
                    if (k == "value" && TryNumber(v, out double dv)) { value = dv; continue; }
                    if (k == "currency" && v is string cs && Rules.IsCurrency(cs.ToUpperInvariant())) { currency = cs.ToUpperInvariant(); continue; }
                    if (n >= Limits.MaxParams || !Rules.IsName(k)) continue;
                    string encoded = EncodeParam(v);
                    if (encoded == null) continue;
                    ps.Append(n == 0 ? "" : ",");
                    Json.String(ps, k);
                    ps.Append(':').Append(encoded);
                    n++;
                }
                if (n > 0) Json.RawField(sb, ref first, "params", "{" + ps + "}");
            }
            if (value.HasValue)
            {
                var vb = new StringBuilder();
                Json.Number(vb, value.Value);
                Json.RawField(sb, ref first, "value", vb.ToString());
            }
            Json.Field(sb, ref first, "currency", currency);
            Json.RawField(sb, ref first, "ts", nowMs().ToString(CultureInfo.InvariantCulture));
            Json.Field(sb, ref first, "session_id", sessionId);
            Json.RawField(sb, ref first, "seq", (seq++).ToString(CultureInfo.InvariantCulture));
            sb.Append('}');

            queue.Add(sb.ToString());
            Trim();
            return true;
        }

        static bool TryNumber(object v, out double d)
        {
            switch (v)
            {
                case int i: d = i; return true;
                case long l: d = l; return true;
                case float f: d = f; return !float.IsNaN(f) && !float.IsInfinity(f);
                case double x: d = x; return !double.IsNaN(x) && !double.IsInfinity(x);
                case decimal m: d = (double)m; return true;
                case short s: d = s; return true;
                case byte b: d = b; return true;
                case uint ui: d = ui; return true;
                default: d = 0; return false;
            }
        }

        static string EncodeParam(object v)
        {
            if (v is string s)
            {
                var sb = new StringBuilder();
                Json.String(sb, s.Length > Limits.MaxStringLen ? s.Substring(0, Limits.MaxStringLen) : s);
                return sb.ToString();
            }
            if (v is bool b) return b ? "true" : "false";
            if (TryNumber(v, out double d))
            {
                var sb = new StringBuilder();
                Json.Number(sb, d);
                return sb.ToString();
            }
            return null; // objects, arrays, null, NaN: removed instead of failing the batch
        }

        public bool ShouldFlushNow => Consent && queue.Count >= Limits.FlushAt;

        public bool CanSend => Consent && queue.Count > 0 && nowMs() >= nextTryAt;

        string Envelope()
        {
            var sb = new StringBuilder(256);
            bool first = true;
            Json.RawField(sb, ref first, "v", "1");
            Json.Field(sb, ref first, "sdk", identity.Sdk);
            Json.Field(sb, ref first, "app_id", identity.AppId);
            Json.Field(sb, ref first, "platform", identity.Platform);
            Json.Field(sb, ref first, "app_version", identity.AppVersion);
            Json.Field(sb, ref first, "install_id", InstallId);
            Json.Field(sb, ref first, "user_id", UserId);
            Json.Field(sb, ref first, "adapty_profile_id", AdaptyProfileId);
            Json.Field(sb, ref first, "app_instance_id", AppInstanceId);
            Json.Field(sb, ref first, "locale", identity.Locale);
            Json.Field(sb, ref first, "device_model", identity.DeviceModel);
            Json.Field(sb, ref first, "os_version", identity.OsVersion);
            Json.RawField(sb, ref first, "dropped", Dropped.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Request body for the first events of the queue that fit in one request.</summary>
        public string BuildBatch(out int count, out int droppedSent)
        {
            droppedSent = Dropped;
            string env = Envelope();
            count = Math.Min(Limits.MaxEvents, queue.Count);
            string body = Body(env, count);
            while (count > 1 && Encoding.UTF8.GetByteCount(body) > Limits.SafeBodyBytes)
            {
                count = (count + 1) / 2;
                body = Body(env, count);
            }
            return body;
        }

        string Body(string env, int count)
        {
            var sb = new StringBuilder(env.Length + count * 160);
            sb.Append('{').Append(env).Append(",\"events\":[");
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(queue[i]);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>Applies the server answer for a batch built with BuildBatch. status -1 = network error.</summary>
        public Outcome OnSendResult(int status, int count, int droppedSent)
        {
            var outcome = Rules.Classify(status);
            if (outcome == Outcome.Retry)
            {
                nextTryAt = nowMs() + Rules.BackoffMs(attempt);
                attempt++;
                return outcome;
            }
            queue.RemoveRange(0, Math.Min(count, queue.Count));
            Dropped = Math.Max(0, Dropped - droppedSent);
            attempt = 0;
            nextTryAt = 0;
            return outcome;
        }

        public void SetConsent(bool granted)
        {
            Consent = granted;
            if (!granted) { queue.Clear(); Dropped = 0; }
        }

        public void OnBackground() => backgroundAt = nowMs();

        public void OnForeground()
        {
            if (backgroundAt >= 0 && nowMs() - backgroundAt >= Limits.SessionGapMs)
            {
                sessionId = Uuid();
                seq = 0;
            }
            backgroundAt = -1;
        }

        public string BuildDeleteBody()
        {
            var sb = new StringBuilder("{");
            bool first = true;
            Json.Field(sb, ref first, "app_id", identity.AppId);
            Json.Field(sb, ref first, "install_id", InstallId);
            return sb.Append('}').ToString();
        }

        /// <summary>After the server accepted "delete my data": new identity, empty queue.</summary>
        public void ResetAfterDelete()
        {
            InstallId = Uuid();
            sessionId = Uuid();
            seq = 0;
            queue.Clear();
            Dropped = 0;
            attempt = 0;
            nextTryAt = 0;
        }
    }
}
