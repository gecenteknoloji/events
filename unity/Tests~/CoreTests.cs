// Unit tests for GtEventsCore.cs, run outside Unity (the "~" folder is ignored
// by Unity). Build and run with Unity's bundled .NET: see run-tests.sh.
// `--live` also posts one real batch to the collector and expects 204.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using GecenTeknoloji.Events;

static class CoreTests
{
    static int failures;
    static long now = 1795521600000; // 2026-11-24T12:00:00Z
    static int uuidN;

    static EventsCore NewCore(string appId = "com.gecenteknoloji.test") =>
        new EventsCore(
            new Identity { Sdk = "unity/1.0.0", AppId = appId, Platform = "android", AppVersion = "2.0.1", DeviceModel = "Pixel 8" },
            () => $"00000000-0000-4000-8000-{(++uuidN).ToString().PadLeft(12, '0')}",
            () => now);

    static void Check(bool ok, string what)
    {
        if (ok) Console.WriteLine("  ok   " + what);
        else { failures++; Console.WriteLine("  FAIL " + what); }
    }

    static JsonElement Batch(EventsCore c, out int count)
    {
        string body = c.BuildBatch(out count, out _);
        return JsonDocument.Parse(body).RootElement;
    }

    static int Main(string[] args)
    {
        Console.WriteLine("status and backoff");
        Check(new[] { 204, 202, 400, 413, 429, 503, 500, -1 }.Select(Rules.Classify).SequenceEqual(new[] {
            Outcome.Done, Outcome.Done, Outcome.Drop, Outcome.Drop, Outcome.Retry, Outcome.Retry, Outcome.Retry, Outcome.Retry }), "classification follows PROTOCOL.md");
        Check(new[] { 0, 1, 2, 5, 10 }.Select(Rules.BackoffMs).SequenceEqual(new long[] { 30_000, 60_000, 120_000, 900_000, 900_000 }), "backoff 30 s doubling, cap 15 min");

        Check(Rules.NewerVersion("1.0.0", "rn/1.2.0 unity/1.1.0", "unity") == "1.1.0", "newer unity version read from header");
        Check(Rules.NewerVersion("1.10.0", "unity/1.9.0", "unity") == null, "numeric compare, not text");
        Check(Rules.NewerVersion("1.0.0", "rn/2.0.0", "unity") == null && Rules.NewerVersion("1.0.0", null, "unity") == null, "other kind or no header: null");

        Console.WriteLine("batch shape");
        {
            var c = NewCore();
            c.Track("level_start", new Dictionary<string, object> { ["level_index"] = 5 });
            c.Track("level_complete", new Dictionary<string, object> { ["level_index"] = 5, ["moves"] = 12L });
            var b = Batch(c, out int n);
            Check(n == 2, "two events in batch");
            Check(b.GetProperty("v").GetInt32() == 1 && b.GetProperty("sdk").GetString() == "unity/1.0.0", "envelope v and sdk");
            Check(b.GetProperty("app_id").GetString() == "com.gecenteknoloji.test" && b.GetProperty("platform").GetString() == "android", "envelope app_id and platform");
            Check(b.GetProperty("install_id").GetString() == c.InstallId && Rules.IsUuid(c.InstallId), "install id is a uuid");
            var ev = b.GetProperty("events");
            Check(ev[0].GetProperty("name").GetString() == "level_start" && ev[0].GetProperty("seq").GetInt32() == 0 && ev[1].GetProperty("seq").GetInt32() == 1, "names and seq");
            Check(ev[0].GetProperty("ts").GetInt64() == now && ev[0].GetProperty("params").GetProperty("level_index").GetDouble() == 5, "ts and params");
            Check(!b.TryGetProperty("user_id", out _), "missing optional ids are omitted");
        }

        Console.WriteLine("params cleaning and revenue");
        {
            var c = NewCore();
            Check(!c.Track("Bad-Name", null), "bad name is not queued");
            var p = new Dictionary<string, object> {
                ["placement"] = "level_end", ["value"] = 0.0042, ["currency"] = "usd",
                ["nested"] = new Dictionary<string, object>(), ["nan"] = double.NaN, ["long"] = new string('y', 150),
                ["Bad-Key"] = 1, ["flag"] = true, ["quote"] = "a\"b\\c\n\u0001", };
            for (int i = 0; i < 30; i++) p["k" + i] = i;
            c.Track("ad_impression", p);
            var e = Batch(c, out _).GetProperty("events")[0];
            Check(e.GetProperty("value").GetDouble() == 0.0042 && e.GetProperty("currency").GetString() == "USD", "value/currency lifted to top level, currency upper-cased");
            var ps = e.GetProperty("params");
            Check(ps.EnumerateObject().Count() <= 25, "at most 25 params");
            Check(ps.GetProperty("long").GetString().Length == 100, "long string cut to 100");
            Check(!ps.TryGetProperty("nested", out _) && !ps.TryGetProperty("nan", out _) && !ps.TryGetProperty("Bad-Key", out _), "bad params removed");
            Check(ps.GetProperty("quote").GetString() == "a\"b\\c\n\u0001", "JSON escaping round-trips");
            Check(ps.GetProperty("flag").GetBoolean(), "bool param kept");
        }

        Console.WriteLine("send results");
        {
            var c = NewCore();
            c.Track("app_open", null);
            c.BuildBatch(out int n, out int d);
            c.OnSendResult(503, n, d);
            Check(c.Count == 1 && !c.CanSend, "503 keeps the event and waits");
            now += 30_000;
            Check(c.CanSend, "can send after 30 s");
            c.BuildBatch(out n, out d);
            c.OnSendResult(-1, n, d);
            now += 59_000;
            Check(!c.CanSend, "network error doubles the wait");
            now += 1_000;
            c.BuildBatch(out n, out d);
            c.OnSendResult(204, n, d);
            Check(c.Count == 0, "204 empties the queue");
            c.Track("bad_batch", null);
            c.BuildBatch(out n, out d);
            c.OnSendResult(400, n, d);
            Check(c.Count == 0, "400 drops the batch");
        }

        Console.WriteLine("limits");
        {
            var c = NewCore();
            for (int i = 0; i < 250; i++) c.Track("tap", new Dictionary<string, object> { ["i"] = i });
            var sizes = new List<int>();
            while (c.Count > 0) { c.BuildBatch(out int n, out int d); sizes.Add(n); c.OnSendResult(204, n, d); }
            Check(sizes.SequenceEqual(new[] { 100, 100, 50 }), "250 events drain as 100, 100, 50");

            var big = new Dictionary<string, object>();
            for (int i = 0; i < 25; i++) big["p" + i] = new string('x', 100);
            for (int i = 0; i < 100; i++) c.Track("big", big);
            int total = 0, batches = 0; bool allSmall = true;
            while (c.Count > 0)
            {
                string body = c.BuildBatch(out int n, out int d);
                allSmall &= Encoding.UTF8.GetByteCount(body) < Limits.MaxBodyBytes;
                total += n; batches++;
                c.OnSendResult(204, n, d);
            }
            Check(batches > 1 && allSmall && total == 100, "large params split under the body limit");

            for (int i = 0; i < Limits.QueueCap + 5; i++) c.Track("tap", new Dictionary<string, object> { ["i"] = i });
            var first = Batch(c, out int fn);
            Check(c.Count == Limits.QueueCap && first.GetProperty("dropped").GetInt32() == 5, "queue cap keeps newest, reports dropped");
            Check(first.GetProperty("events")[0].GetProperty("params").GetProperty("i").GetInt32() == 5, "oldest events were the dropped ones");
            c.BuildBatch(out fn, out int fd);
            c.OnSendResult(204, fn, fd);
            Check(Batch(c, out _).GetProperty("dropped").GetInt32() == 0, "dropped resets after it was sent");
        }

        Console.WriteLine("consent, session, delete, restore");
        {
            var c = NewCore();
            c.Track("a", null);
            c.SetConsent(false);
            Check(c.Count == 0 && !c.Track("b", null), "consent off clears and ignores");
            c.SetConsent(true);
            c.Track("a", null);
            c.OnBackground(); now += 29 * 60_000; c.OnForeground();
            c.Track("b", null);
            c.OnBackground(); now += 31 * 60_000; c.OnForeground();
            c.Track("c", null);
            var ev = Batch(c, out _).GetProperty("events");
            Check(ev[0].GetProperty("session_id").GetString() == ev[1].GetProperty("session_id").GetString(), "29 min in background keeps the session");
            Check(ev[2].GetProperty("session_id").GetString() != ev[0].GetProperty("session_id").GetString() && ev[2].GetProperty("seq").GetInt32() == 0, "31 min starts a new session");

            string oldId = c.InstallId;
            var del = JsonDocument.Parse(c.BuildDeleteBody()).RootElement;
            Check(del.GetProperty("app_id").GetString() == "com.gecenteknoloji.test" && del.GetProperty("install_id").GetString() == oldId, "delete body");
            c.ResetAfterDelete();
            Check(c.InstallId != oldId && c.Count == 0, "after delete: new install id, empty queue");

            var saved = c;
            saved.Track("kept", null);
            var lines = saved.QueuedJson.ToArray();
            var r = NewCore();
            r.Track("early", null);
            r.Restore(saved.InstallId, true, 3, lines.Concat(new[] { "garbage", "" }));
            var rb = Batch(r, out int rn);
            Check(r.InstallId == saved.InstallId && rn == 2, "restore keeps install id and valid lines only");
            Check(rb.GetProperty("events")[0].GetProperty("name").GetString() == "kept" && rb.GetProperty("dropped").GetInt32() == 3, "restored events come first, dropped carried over");
            var off = NewCore();
            off.Track("x", null);
            off.Restore(null, false, 0, lines);
            Check(off.Count == 0 && !off.Consent, "restored consent=false clears everything");
        }

        if (args.Contains("--live"))
        {
            Console.WriteLine("live");
            var c = NewCore("com.gecenteknoloji.smoketest");
            now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            c.Track("app_open", null);
            c.Track("ad_impression", new Dictionary<string, object> { ["placement"] = "unity_smoke", ["value"] = 0.001, ["currency"] = "USD" });
            string body = c.BuildBatch(out _, out _);
            using var http = new HttpClient();
            var res = http.PostAsync("https://api.gecenteknoloji.com/v1/events", new StringContent(body, Encoding.UTF8, "application/json")).Result;
            Check((int)res.StatusCode == 204, $"live collector accepted the Unity batch ({(int)res.StatusCode})");
        }

        Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
        return failures == 0 ? 0 : 1;
    }
}
