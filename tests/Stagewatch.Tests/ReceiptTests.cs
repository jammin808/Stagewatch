using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Show;

/// <summary>
/// What an acceptor's receipts say and keep (Show Bus §3, 13.12.4–6), pure: §3's last fields in their order and the record's fields; the
/// record of what each connector's ids came to — run once, a repeat answered from it, accepted while it still runs, RECEIPTS for its own
/// ids, only 13.12.1's shape kept, its connectors bounded; and a command handed to the show thread — its receipt within the deadline, else
/// accepted and one follow-up within receipt.final.ms of its read (what came of it, or unconfirmed), and how late it ran. Fields first,
/// words beside them.
/// </summary>
static class ReceiptTests
{
    public static async Task Run(Action<bool, string> Check)
    {
        Tail(Check);
        Record(Check);
        await ShowThread(Check);
    }

    static JsonElement Json(string s)
    {
        using var doc = JsonDocument.Parse(s);
        return doc.RootElement.Clone();
    }

    static void Tail(Action<bool, string> Check)
    {
        var applied = new CommandReceipt("s1-0a1b2c3d", true, "applied", "Next item", null, new Dictionary<string, long> { ["messageId"] = 3 });
        var names = Json(System.Text.Encoding.UTF8.GetString((applied with { IsFinal = true, Repeat = true, LateMs = 1200 }).ToJson()))
            .EnumerateObject().Select(p => p.Name).ToList();
        Check(names.SequenceEqual(["type", "id", "ok", "stage", "effect", "error", "refs", "final", "repeat", "lateMs"]),
            "receipts: §3's last fields ride after refs, each only where it applies, in their order: final, repeat, lateMs (" + string.Join(",", names) + ")");
        var plain = Json(System.Text.Encoding.UTF8.GetString(applied.ToJson()));
        Check(!plain.TryGetProperty("final", out _) && !plain.TryGetProperty("repeat", out _) && !plain.TryGetProperty("lateMs", out _) && !plain.TryGetProperty("frame", out _),
            "receipts: a receipt that is not a follow-up, not answered from the record and not late carries none of §3's last fields");
        var record = (applied with { IsFinal = true, LateMs = 1200 }).RecordJson();
        Check(record == "{\"ok\":true,\"stage\":\"applied\",\"effect\":\"Next item\",\"error\":null,\"refs\":{\"messageId\":3},\"lateMs\":1200}",
            "receipts: the record keeps a receipt's fields after its id, its lateMs among them, never final or repeat: " + record);
        Check(PeerReceipts.Repeat(record) == "{\"ok\":true,\"stage\":\"applied\",\"effect\":\"Next item\",\"error\":null,\"refs\":{\"messageId\":3},\"repeat\":true,\"lateMs\":1200}",
            "receipts: a repeat answered from the record says repeat before its lateMs, in §3's order: " + PeerReceipts.Repeat(record));
        // A ref named like a last field stays inside refs: only a field of the receipt's own object moves repeat ahead of it.
        var odd = new CommandReceipt("a1", true, "applied", "says \",\"lateMs\":1 in words", null, new Dictionary<string, long> { ["a"] = 1 },
            new Dictionary<string, string> { ["frame"] = "f1" }).RecordJson();
        Check(PeerReceipts.Repeat(odd).EndsWith("\"refs\":{\"a\":1,\"frame\":\"f1\"},\"repeat\":true}", StringComparison.Ordinal),
            "receipts: a ref or words that read like lateMs or frame never move where repeat goes: " + PeerReceipts.Repeat(odd));
        Check(applied.LateNote is null && (applied with { LateMs = 1500 }).LateNote == "ran 1500 ms after it was read (lateMs)",
            "receipts: a line carrier says how late a command ran in words of its own");
    }

    static void Record(Action<bool, string> Check)
    {
        Check(ReceiptRecord.Keeps("s1-0a1b2c3d") && ReceiptRecord.Keeps("ag12-9f8e7d6c") && !ReceiptRecord.Keeps("c5") && !ReceiptRecord.Keeps("s0-0a1b2c3d")
              && !ReceiptRecord.Keeps("abcde1-0a1b2c3d") && !ReceiptRecord.Keeps("s1-") && !ReceiptRecord.Keeps(null) && !ReceiptRecord.Keeps("s1-" + new string('a', 30)),
            "receipts: the record keeps an id of 13.12.1's shape alone — 1-4 letters, a counter from 1, its connector's inst, within request.id.chars");

        var r = new ReceiptRecord();
        const string A = "patterns:9f8e7d6c", B = "playmap:pm1";
        var first = r.Begin(A, "s1-0a1b2c3d", 0);
        var whileRunning = r.Begin(A, "s1-0a1b2c3d", 10);
        var fields = new CommandReceipt("s1-0a1b2c3d", true, "applied", "Next item", null).RecordJson();
        r.Keep(A, "s1-0a1b2c3d", fields, 20);
        var repeat = r.Begin(A, "s1-0a1b2c3d", 30);
        var otherConnector = r.Begin(B, "s1-0a1b2c3d", 30);
        Check(first is null && whileRunning == "{\"ok\":true,\"stage\":\"accepted\",\"effect\":\"\",\"repeat\":true}"
              && repeat == PeerReceipts.Repeat(fields) && otherConnector is null,
            $"receipts: an id runs once — while it runs a repeat is answered accepted, once settled with its receipt and repeat; another connector's same id is its own ({whileRunning} | {repeat})");
        Check(r.Begin(A, "c5", 40) is null && r.Begin(A, "c5", 50) is null,
            "receipts: an id of another shape is never held, so it runs each time it is sent");

        var answer = Json(r.Answer(A, ["s1-0a1b2c3d", "s2-0a1b2c3d", "s1-0a1b2c3d"], "a41f09c2", 60));
        Check(answer.GetProperty("inst").GetString() == "a41f09c2" && answer.GetProperty("receipts").EnumerateObject().Count() == 2
              && answer.GetProperty("receipts").GetProperty("s1-0a1b2c3d").GetProperty("effect").GetString() == "Next item"
              && answer.GetProperty("receipts").GetProperty("s2-0a1b2c3d").GetString() == "unknown"
              && Json(r.Answer(B, ["s1-0a1b2c3d"], "a41f09c2", 60)).GetProperty("receipts").GetProperty("s1-0a1b2c3d").ValueKind == JsonValueKind.Object,
            "receipts: RECEIPTS answers a connector's own ids with this node's inst — each its kept receipt, unknown for one never read, an id once");
        Check(ReceiptRecord.AskOf([]) is null && ReceiptRecord.AskOf(Enumerable.Range(1, PeerLink.ReceiptsAskMax + 1).Select(i => "s" + i + "-0a1b2c3d")) is null
              && ReceiptRecord.AskOf(["s1-0a1b2c3d", "bad id"]) is null && ReceiptRecord.AskOf(["s1-0a1b2c3d", "s1-0a1b2c3d"]) is ["s1-0a1b2c3d"],
            "receipts: an ask names 1 to receipts.ask.max request ids, each once");

        // Kept receipts.kept.ms: an id older than that is forgotten, and runs again (the connector's ids never repeat within it).
        var aged = r.Begin(A, "s1-0a1b2c3d", 20 + PeerLink.ReceiptsKeptMs + 1);
        Check(aged is null, "receipts: an id is held receipts.kept.ms after it settled, no longer");

        // At most Connectors connectors: the one heard from longest ago is forgotten first.
        var many = new ReceiptRecord();
        many.Keep("c:0", "s1-0a1b2c3d", fields, 0);
        for (var i = 1; i <= ReceiptRecord.Connectors; i++) many.Keep("c:" + i, "s1-0a1b2c3d", fields, i);
        Check(many.Begin("c:0", "s1-0a1b2c3d", 100) is null && many.Begin("c:" + ReceiptRecord.Connectors, "s1-0a1b2c3d", 100) is not null,
            $"receipts: the record keeps {ReceiptRecord.Connectors} connectors, forgetting the one heard from longest ago");
    }

    static async Task ShowThread(Action<bool, string> Check)
    {
        static async Task<T?> Settle<T>(Task<T> t) where T : class? => await Task.WhenAny(t, Task.Delay(5000)) == t ? await t : null;

        // Run within the deadline: the run's own receipt, and how late it began.
        var time = new ShowWorkflowTests.ManualTime(new DateTime(2026, 11, 12, 20, 0, 0, DateTimeKind.Utc));
        var readAt = time.GetTimestamp();
        var done = new CommandReceipt("s1-0a1b2c3d", true, "applied", "Next item", null);
        var inTime = await ShowReceipts.Of("s1-0a1b2c3d", Task.FromResult(1), (_, _) => Task.FromResult(done), TimeSpan.FromSeconds(3), time, readAt);
        Check(ReferenceEquals(inTime, done), "receipts: a command the show thread ran within the deadline is answered with what the run made of it");

        // Not run within the deadline: accepted at once, and its follow-up once the thread runs it — final, with how late it ran.
        var run = new TaskCompletionSource<long?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = ShowReceipts.Of("s2-0a1b2c3d", run.Task, (late, _) => Task.FromResult(ShowReceipts.Noted(done with { Id = "s2-0a1b2c3d" }, late)),
            TimeSpan.FromSeconds(3), time, readAt);
        time.Advance(TimeSpan.FromSeconds(3));
        var accepted = await Settle(first);
        time.Advance(TimeSpan.FromSeconds(2));
        run.SetResult(ShowReceipts.Late(time.GetElapsedTime(readAt)));
        var final = accepted?.Final is { } f ? await Settle(f) : null;
        Check(accepted is { Ok: true, Stage: "accepted", Effect: "", IsFinal: false } && final is { Ok: true, Stage: "applied", IsFinal: true, LateMs: 5000 },
            $"receipts: a command the show thread had not run in the deadline is accepted, and its follow-up says what came of it once it ran, final, lateMs 5000 ({accepted} | {final})");

        // Never run within receipt.final.ms: the follow-up is accepted and unconfirmed, final — it may still run.
        var stuck = new TaskCompletionSource<long?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readLater = time.GetTimestamp();
        var waiting = ShowReceipts.Of("s3-0a1b2c3d", stuck.Task, (late, _) => Task.FromResult(ShowReceipts.Noted(done with { Id = "s3-0a1b2c3d" }, late)),
            TimeSpan.FromSeconds(3), time, readLater);
        time.Advance(TimeSpan.FromSeconds(3));
        var stillAccepted = await Settle(waiting);
        var pending = stillAccepted?.Final;
        time.Advance(ShowReceipts.FinalWithin);
        var unrun = pending is not null ? await Settle(pending) : null;
        Check(unrun is { Ok: true, Stage: "accepted", IsFinal: true } && unrun.Effect.StartsWith("unconfirmed — the show thread had not run it 30 s after it was read", StringComparison.Ordinal),
            $"receipts: a command still not run receipt.final.ms after its read ends unconfirmed, final, in words that say it may still run ({unrun?.Effect})");

        // It runs at last: what it came to is handed on for the record (Later), with how late it ran — a RECEIPTS ask reads that, not the
        // unconfirmed follow-up. One that never runs within receipts.kept.ms hands on nothing.
        time.Advance(TimeSpan.FromSeconds(2));
        stuck.SetResult(ShowReceipts.Late(time.GetElapsedTime(readLater)));
        var ranLate = unrun?.Later is { } lt ? await Settle(lt) : null;
        Check(ranLate is { Ok: true, Stage: "applied", IsFinal: true, LateMs: 35000 },
            $"receipts: a command reported unrun that runs later hands on what it came to for the record, with lateMs 35000 ({ranLate})");
        var never = new TaskCompletionSource<long?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readNever = time.GetTimestamp();
        var neverFirst = ShowReceipts.Of("s6-0a1b2c3d", never.Task, (_, _) => Task.FromResult(done), TimeSpan.FromSeconds(3), time, readNever);
        time.Advance(TimeSpan.FromSeconds(3));
        var neverPending = (await Settle(neverFirst))?.Final;
        time.Advance(ShowReceipts.FinalWithin);
        var neverUnrun = neverPending is not null ? await Settle(neverPending) : null;
        var neverLater = neverUnrun?.Later;
        time.Advance(ShowReceipts.KeptWithin);
        var neverRan = neverLater is not null && await Task.WhenAny(neverLater, Task.Delay(5000)) == neverLater ? await neverLater : done;
        Check(neverLater is not null && neverRan is null,
            "receipts: a command the show thread never runs within receipts.kept.ms of its read hands nothing on for the record");

        // A follow-up that waits on another node is that node's final answer, still final, with how late the command began.
        var peerFinal = Task.FromResult(new CommandReceipt("s4-0a1b2c3d", true, "applied", "PlayMap took it", null) { IsFinal = true });
        var handed = ShowReceipts.Noted(CommandReceipt.Accepted("s4-0a1b2c3d") with { Final = peerFinal }, 1800);
        var handedFinal = handed.Final is { } hf ? await Settle(hf) : null;
        Check(handed.LateMs == 1800 && handedFinal is { IsFinal: true, LateMs: 1800, Effect: "PlayMap took it" } && ShowReceipts.Noted(done, null) == done,
            "receipts: how late a command began rides its receipt and its follow-up; a command in time is unchanged");
        Check(ShowReceipts.LateLine(ShowCommandKind.Next, "s2-0a1b2c3d", 5000) == "Next (s2-0a1b2c3d) ran 5 s after it was read — the show thread was busy; its receipt says lateMs 5000",
            "receipts: the journal's row for a late command names it, its id and how late");
        await Delivered(Check);
    }

    /// <summary>
    /// What a carrier sends and its record keeps (<see cref="ReceiptRecord.Deliver"/>): the receipt, then the follow-up; the settling one
    /// kept; what a command reported unrun came to kept when it runs; and a receipt that never comes settled unconfirmed, never held running.
    /// </summary>
    static async Task Delivered(Action<bool, string> Check)
    {
        const string C = "patterns:9f8e7d6c";
        async Task<(List<CommandReceipt> Sent, ReceiptRecord Record)> Deliver(string id, Task<CommandReceipt> receipt)
        {
            var sent = new List<CommandReceipt>();
            var record = new ReceiptRecord();
            record.Begin(C, id, 0);
            var delivering = ReceiptRecord.Deliver(id, receipt, r => { lock (sent) sent.Add(r); return Task.CompletedTask; },
                r => record.Keep(C, id, r.RecordJson(), 1));
            Check(await Task.WhenAny(delivering, Task.Delay(5000)) == delivering, "receipts: a delivery ends: " + id);
            return (sent, record);
        }
        string Held(ReceiptRecord record, string id) => Json(record.Answer(C, [id], "a41f09c2", 2)).GetProperty("receipts").GetProperty(id).GetRawText();

        // The show thread went away before it said: one final, accepted and unconfirmed; the record holds that, never "running".
        var (gone, goneRecord) = await Deliver("s5-0a1b2c3d", Task.FromException<CommandReceipt>(new TaskCanceledException()));
        Check(gone is [{ IsFinal: true, Stage: "accepted" } only] && only.Effect.StartsWith("unconfirmed", StringComparison.Ordinal)
              && Held(goneRecord, "s5-0a1b2c3d").Contains("unconfirmed", StringComparison.Ordinal),
            $"receipts: a receipt that never comes is sent and kept unconfirmed, final, so the record never holds the id running ({Held(goneRecord, "s5-0a1b2c3d")})");

        // Accepted, then a follow-up that never comes: the follow-up is unconfirmed, and the record holds it.
        var (lost, lostRecord) = await Deliver("s6-0a1b2c3d",
            Task.FromResult(CommandReceipt.Accepted("s6-0a1b2c3d") with { Final = Task.FromException<CommandReceipt>(new InvalidOperationException("gone")) }));
        Check(lost is [{ Stage: "accepted", IsFinal: false }, { Stage: "accepted", IsFinal: true }] && Held(lostRecord, "s6-0a1b2c3d").Contains("unconfirmed", StringComparison.Ordinal),
            "receipts: a follow-up that never comes is sent and kept unconfirmed");

        // Reported unrun at its follow-up, then run: the carrier sends two, and the record moves on to what it came to, with lateMs.
        var ran = new CommandReceipt("s7-0a1b2c3d", true, "applied", "Next item", null) { IsFinal = true, LateMs = 41000 };
        var unrun = ShowReceipts.Unrun("s7-0a1b2c3d") with { Later = Task.FromResult<CommandReceipt?>(ran) };
        var (late, lateRecord) = await Deliver("s7-0a1b2c3d", Task.FromResult(CommandReceipt.Accepted("s7-0a1b2c3d") with { Final = Task.FromResult(unrun) }));
        var held = Json(Held(lateRecord, "s7-0a1b2c3d"));
        Check(late.Count == 2 && late[1].Effect.StartsWith("unconfirmed", StringComparison.Ordinal)
              && held.GetProperty("stage").GetString() == "applied" && held.GetProperty("lateMs").GetInt64() == 41000,
            $"receipts: what a command reported unrun came to is kept once it runs, and a RECEIPTS ask reads it, with lateMs ({held.GetRawText()})");
    }
}
