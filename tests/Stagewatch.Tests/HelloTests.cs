using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Integrations;

/// <summary>
/// Show Bus 1.3 §2, the hello: what a beat did and the words the desk shows for it, from that beat's own reason; the doors it names,
/// from what each server bound; the hearing on injected time — one id heard from two addresses is one peer dialled where it was first
/// heard, an id two or more running nodes say is marked in conflict however many take turns, a restart is not, and another machine
/// saying this node's own id marks this node; the show network leaves an id in conflict out of its rows and shows it apart, while the
/// AI's facts show it amber; and a live beacon on its own ports, whose hello is heard where it is sent, whose failing target never stops
/// the next, and which tells its own echo from a copy of itself. ShowBusSpecTests holds the same code to §14's numbers.
/// </summary>
static class HelloTests
{
    public static async Task Run(Action<bool, string> Check)
    {
        Beats(Check);
        Hearing(Check);
        Network(Check);
        await Live(Check);
    }

    static SlidesBeacon.Hello From(string id, string? inst, string host = "FOH-PC", string kind = "patterns")
        => new(kind, 1, id, "FOH", host, 0, false, Proto: 3, Host: host, Inst: inst);

    static readonly IPEndPoint Out = new(IPAddress.Broadcast, 9928), Here = new(IPAddress.Loopback, 9928);

    static void Beats(Action<bool, string> Check)
    {
        Check(ShowBusHello.Said([(Out, true), (Here, true)]) == HelloSaid.Said && ShowBusHello.Said([(Out, true), (Here, false)]) == HelloSaid.Said
              && ShowBusHello.Said([(Out, false), (Here, true)]) == HelloSaid.ThisPcOnly && ShowBusHello.Said([(Out, false), (Here, false)]) == HelloSaid.NotSaid
              && ShowBusHello.Said([]) == HelloSaid.NotSaid,
            "hello: a beat says it when a target off this PC took it, on this PC only when only the copy went, not at all when every target failed");

        HelloStatus Status(HelloSaid said, int beats, string words = "", HelloScope scope = HelloScope.Network, int port = 9924)
            => new(said, beats, words, Array.Empty<HelloTarget>()) { Scope = scope, WebPort = port };
        Check(ShowBusHello.Line(Status(HelloSaid.Said, 1), "Hall A", "3f9c2a1b7d0e") == "Said on the network as Hall A (3f9c…)"
              && ShowBusHello.Line(Status(HelloSaid.ThisPcOnly, 4, "Network is unreachable"), "Hall A", "3f9c2a1b7d0e") == "Said on this PC only — no network: Network is unreachable"
              && ShowBusHello.Line(Status(HelloSaid.NotSaid, 1, "Permission denied"), "Hall A", "3f9c2a1b7d0e") == "Not said: Permission denied"
              && ShowBusHello.Line(Status(HelloSaid.Silent, 9, scope: HelloScope.None), "Hall A", "3f9c2a1b7d0e") == "Not said — Remote is set to this PC; other apps reach it at 127.0.0.1:9924"
              && ShowBusHello.Line(Status(HelloSaid.Silent, 9, scope: HelloScope.Off, port: 0), "Hall A", "3f9c2a1b7d0e") == "Not said — Remote is off, so no other app can reach this Stagewatch"
              && ShowBusHello.Line(Status(HelloSaid.Silent, 9, scope: HelloScope.Refused), "Hall A", "ab12") == "Not said — this install's id (ab12) is not one the Show Bus allows"
              && ShowBusHello.Line(HelloStatus.NotYet, "Hall A", "3f9c2a1b7d0e") == "Saying hello on the network…",
            "hello: the Show network's first line says whether the hello is out, and if not why — the beat's own reason, carried in its status");

        // Another machine saying this Stagewatch's id: its line says so after what the beat did, the context's bus:hello is amber with
        // it alone, and the advice warns; both causes at once stay within §6's state.
        var twin = new HelloConflict("3f9c2a1b7d0e", "", "", "FOH-PC2", "10.0.0.7");
        var twinItem = ShowBusHello.Item(Status(HelloSaid.Said, 5), twin);
        var both = ShowBusHello.Item(Status(HelloSaid.NotSaid, ShowBusHello.FailedBeats, new string('w', 300)), new HelloConflict("3f9c2a1b7d0e", "", "", new string('h', 64), "10.0.0.7"));
        Check(ShowBusHello.Line(Status(HelloSaid.Said, 5), "Hall A", "3f9c2a1b7d0e", twin)
                  == "Said on the network as Hall A (3f9c…) — another machine says it is this Stagewatch: FOH-PC2 at 10.0.0.7 — give one of them a new id"
              && twinItem is { Id: "bus:hello", Light: Light.Amber, State: "another machine says it is this Stagewatch: FOH-PC2 at 10.0.0.7 — give one of them a new id" }
              && both is { Light: Light.Amber } && both.State.StartsWith("this Stagewatch's hello can't go out: ", StringComparison.Ordinal) && both.State.Length <= 160
              && ShowBusHello.OwnAdvice(twin) is { Key: "bus:hello:conflict", Level: AdviceLevel.Warn } && ShowBusHello.OwnAdvice(null) is null,
            "hello: another machine saying this Stagewatch's id is on its hello line, amber in its own context (P1: its own health) and in the advice");

        // The context's amber only once every target has failed three beats in a row; a broadcast failing alone is not amber.
        var item = ShowBusHello.Item(Status(HelloSaid.NotSaid, ShowBusHello.FailedBeats, new string('w', 300)));
        Check(ShowBusHello.Item(Status(HelloSaid.NotSaid, ShowBusHello.FailedBeats - 1, "x")) is null && ShowBusHello.Item(Status(HelloSaid.ThisPcOnly, 50, "x")) is null
              && ShowBusHello.Item(Status(HelloSaid.Silent, 50, scope: HelloScope.None)) is null
              && item is { Id: "bus:hello", Kind: "health", Light: Light.Amber } && item.State.StartsWith("this Stagewatch's hello can't go out: ", StringComparison.Ordinal)
              && item.State.EndsWith(" — other apps can't find it", StringComparison.Ordinal) && item.State.Length <= 160 && item.Label.Length <= 80,
            $"hello: bus:hello is amber health once every target failed {ShowBusHello.FailedBeats} beats, within §6's sizes, and never for a broadcast alone");

        // The doors: each server's door as it bound it, never the settings; a server not running is no door, nor a certificate
        // without a web server.
        const string Fp = "sha256:0303030303030303030303030303030303030303030303030303030303030303";
        Check(ShowBusHello.Doors(new BoundDoor(9924, true), Fp, new BoundDoor(9923, false), null) == new HelloDoors(9924, true, Fp, 9923, false, 0, false)
              && ShowBusHello.Doors(null, Fp, null, new BoundDoor(9925, true)) == new HelloDoors(0, false, null, 0, false, 9925, true)
              && ShowBusHello.Main("3f9c2a1b7d0e", "c3d4e5f6", "Hall A", "FOH-PC", ShowBusHello.Doors(new BoundDoor(9924, false), null, null, null), false, 1).Scope == HelloScope.None,
            "hello: the doors a hello names are the servers' doors as bound, a door not open is none, and a web server on this PC only says no hello");

        // The builder: from what the servers bound; no hello with no web server, an id §2 refuses or a malformed inst, each with its reason.
        var lan = new HelloDoors(9924, true, null, 9923, true, 0, false);
        Check(ShowBusHello.Main("3f9c2a1b7d0e", "c3d4e5f6", "Hall A", "FOH-PC", lan, false, 1) is { Scope: HelloScope.Network, Hello: { } h }
              && h.Ports!.Keys.Order(StringComparer.Ordinal).SequenceEqual(["companion", "http", "ws"]) && !h.Caps!.Contains("mcp") && h.Tls is null
              && ShowBusHello.Main("ab12", "c3d4e5f6", "Hall A", "FOH-PC", lan, false, 1) is { Hello: null, Scope: HelloScope.Refused }
              && ShowBusHello.Main("3f9c2a1b7d0e", "C3D4E5F6", "Hall A", "FOH-PC", lan, false, 1) is { Hello: null, Scope: HelloScope.Refused }
              && ShowBusHello.Main("3f9c2a1b7d0e", "c3d4e5f6", "Hall A", "FOH-PC", lan with { WebPort = 0 }, false, 1) is { Hello: null, Scope: HelloScope.Off }
              && ShowBusHello.Main("3f9c2a1b7d0e", "c3d4e5f6", "Hall\u0007 A\n", "FOH-PC", lan, true, 1).Hello is { Name: "Hall A" } m && m.Caps![^1] == "mcp",
            "hello: the builder names the doors bound to the network, mcp only when allowed, no control characters, and nothing for no web server, a bad id or inst");
        Check(ShowBusHello.Cut("abc", 0) == "" && ShowBusHello.Cut("abc", -1) == "" && ShowBusHello.Cut("", 0) == "" && ShowBusHello.Cut("a😀", 2) == "a"
              && ShowBusHello.Cut("abc", 3) == "abc",
            "hello: cutting to no room gives nothing (never a throw on the beacon's timer thread), and never splits a surrogate pair");

        // On the wire: no null field, and presenting only from an agent that presents.
        var main = ShowBusHello.Main("3f9c2a1b7d0e", "c3d4e5f6", "Hall A", "FOH-PC", lan, false, 1).Hello!;
        using var doc = JsonDocument.Parse(ShowBusHello.Bytes(main));
        var fields = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Check(!fields.Contains("tls") && !fields.Contains("presenting") && doc.RootElement.EnumerateObject().All(p => p.Value.ValueKind != JsonValueKind.Null)
              && doc.RootElement.GetProperty("proto").GetInt32() == ShowBusHello.Proto && doc.RootElement.GetProperty("inst").GetString() == "c3d4e5f6"
              && JsonDocument.Parse(ShowBusHello.Bytes(ShowBusHello.Agent("lectern1", "c3d4e5f6", "Lectern", "LECTERN-PC", true, 1))).RootElement.GetProperty("presenting").GetBoolean()
              && !JsonDocument.Parse(ShowBusHello.Bytes(ShowBusHello.Agent("lectern1", "c3d4e5f6", "Lectern", "LECTERN-PC", false, 1))).RootElement.TryGetProperty("presenting", out _),
            "hello: on the wire a hello carries no null field, and presenting only from a Slides agent showing a slide (" + string.Join(", ", fields) + ")");

        // Names are counted in UTF-16 units on both sides: 32 emoji (64 units) are read whole, 64 emoji are cut at 32, at a pair's edge.
        string Emoji(int n) => string.Concat(Enumerable.Repeat("😀", n));
        Check(ShowBusHello.Read(Datagram(Emoji(32), "FOH-PC")) is { Name: var e32 } && e32 == Emoji(32)
              && ShowBusHello.Read(Datagram(Emoji(64), "FOH-PC")) is { Name: var e64 } && e64 == Emoji(32),
            "hello: a reader keeps a name of hello.name.chars UTF-16 units whole and cuts a longer one at a surrogate pair's edge");
        // Control characters never reach the desk's words or a log line from a heard hello.
        Check(ShowBusHello.Read(Datagram("Hall\nA", "FOH\r\nPC\u0007")) is { Name: "HallA", Host: "FOHPC", Machine: "FOHPC" },
            "hello: a reader strips control characters from a heard name and host, as a sender does");

        // What it reads: a malformed one is dropped, never trusted.
        Check(ShowBusHello.Read("{\"t\":\"patterns\",\"v\":2,\"id\":\"9f8e7d6c\"}"u8.ToArray()) is null
              && ShowBusHello.Read("{\"t\":\"bad kind\",\"v\":1,\"id\":\"9f8e7d6c\"}"u8.ToArray()) is null
              && ShowBusHello.Read("not json"u8.ToArray()) is null && ShowBusHello.Read(new byte[SlidesBeacon.ReadBytes + 1]) is null
              && ShowBusHello.Read("{\"t\":\"patterns\",\"v\":1,\"id\":\"9f8e7d6c\",\"name\":null,\"caps\":[null,\"video\",\"Video\"],\"ports\":{\"http\":9696,\"bad key!\":1,\"wire\":70000}}"u8.ToArray())
                  is { Name: "", Caps: ["video"] } r && r.Ports!.Keys.SequenceEqual(["http"]),
            "hello: a hello of another format, a bad kind, not JSON or too long is dropped; null fields, bad caps and bad doors are left out");
    }

    static byte[] Datagram(string name, string host, string? tls = null)
        => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?> { ["t"] = "patterns", ["v"] = 1, ["proto"] = 3, ["id"] = "9f8e7d6c", ["name"] = name, ["host"] = host, ["tls"] = tls });

    static void Hearing(Action<bool, string> Check)
    {
        long now = 0;

        // One node heard from its broadcast (its LAN address) and its copy here (127.0.0.1), alternating each second for 20 s: one
        // peer, dialled where it was first heard, never flipping.
        var both = new HelloHearing(() => now);
        var dialled = new List<string>();
        for (var s = 0; s < 20; s++)
        {
            now = s * 1000L;
            both.Hear(From("9f8e7d6c", "a1b2c3d4"), s % 2 == 0 ? "127.0.0.1" : "10.0.0.5");
            dialled.Add(both.Peers().Single().Address);
        }
        var peer = both.Peers().Single();
        Check(dialled.All(a => a == "127.0.0.1") && peer.Addresses.Order(StringComparer.Ordinal).SequenceEqual(["10.0.0.5", "127.0.0.1"]) && peer.Conflict is null,
            "hello: a node heard from two addresses is one peer, dialled where it was first heard while that is heard, never flipping");

        // Once the dialled address goes stale, the newest is dialled: 127.0.0.1 was last heard at 18 s.
        now = 18_000 + (long)SlidesBeacon.StaleAfter.TotalMilliseconds;
        both.Hear(From("9f8e7d6c", "a1b2c3d4"), "10.0.0.5");
        var kept = both.Peers().Single().Address;
        now += 1;
        both.Hear(From("9f8e7d6c", "a1b2c3d4"), "10.0.0.5");
        Check(kept == "127.0.0.1" && both.Peers().Single() is { Address: "10.0.0.5", Addresses: ["10.0.0.5"] },
            "hello: the dialled address moves to the newest only once it has been unheard past the stale time");

        // Two running nodes with one id: inst A, B, A within the stale time is a conflict naming both, never merged.
        var clone = new HelloHearing(() => now);
        now = 0;
        clone.Hear(From("3f9c2a1b7d0e", "aaaaaaaa", "FOH-PC"), "10.0.0.5");
        now = 2000;
        clone.Hear(From("3f9c2a1b7d0e", "bbbbbbbb", "FOH-PC2"), "10.0.0.7");
        now = 3000;
        clone.Hear(From("3f9c2a1b7d0e", "aaaaaaaa", "FOH-PC"), "10.0.0.5");
        var marked = clone.Peers().Single().Conflict;
        Check(marked is { HostA: "FOH-PC2", AddressA: "10.0.0.7", HostB: "FOH-PC", AddressB: "10.0.0.5" }
              && marked.Words == "two machines say they are 3f9c2a1b7d0e: FOH-PC2 at 10.0.0.7 and FOH-PC at 10.0.0.5",
            "hello: one id from two running nodes (inst A, B, A) is in conflict, naming both hosts and addresses");
        // While they keep alternating the mark stands; once one stops, it clears a minute after the last alternation.
        for (var s = 4; s < 30; s++)
        {
            now = s * 1000L;
            clone.Hear(From("3f9c2a1b7d0e", s % 2 == 0 ? "aaaaaaaa" : "bbbbbbbb", s % 2 == 0 ? "FOH-PC" : "FOH-PC2"), s % 2 == 0 ? "10.0.0.5" : "10.0.0.7");
        }
        var standing = clone.Peers().Single().Conflict is not null;
        // From 30 s only A (the clone was switched off): its switch back from B at 30 s is the last alternation.
        var clearAt = 30_000 + (long)SlidesBeacon.ConflictClearsAfter.TotalMilliseconds;
        for (now = 30_000; now <= clearAt; now += 1000) clone.Hear(From("3f9c2a1b7d0e", "aaaaaaaa", "FOH-PC"), "10.0.0.5");
        now = clearAt;
        var lastMinute = clone.Peers().Single().Conflict is not null;
        now += 1;
        Check(standing && lastMinute && clone.Peers().Single().Conflict is null,
            "hello: the conflict mark stands while the alternation goes on, and clears a minute after it stops");

        // A restart (A, then only B) is not a conflict; nor is one inst from two addresses; nor two insts far apart in time.
        var restart = new HelloHearing(() => now);
        now = 0;
        for (var s = 0; s < 5; s++) { now = s * 2000L; restart.Hear(From("pat00001", "aaaaaaaa"), "10.0.0.5"); }
        for (var s = 5; s < 30; s++) { now = s * 2000L; restart.Hear(From("pat00001", "bbbbbbbb"), s % 2 == 0 ? "10.0.0.5" : "192.168.1.5"); }
        var late = new HelloHearing(() => now);
        now = 0;
        late.Hear(From("pat00002", "aaaaaaaa"), "10.0.0.5");
        now = 10_000;
        late.Hear(From("pat00002", "bbbbbbbb"), "10.0.0.5");
        now = 11_000;
        late.Hear(From("pat00002", "aaaaaaaa"), "10.0.0.5");
        Check(restart.Peers().Single().Conflict is null && late.Peers().Single().Conflict is null,
            "hello: a restart, one inst from two addresses, and an inst back after a gap longer than the stale time are no conflict");

        // Three or more copies of one install (one disk image on several machines): whatever order their hellos come in, an inst
        // heard within the stale time coming back after another is the conflict — A, B, C, A and A, B, C, B; and six copies in turn,
        // more than the senders a reader keeps, are still seen. A run of restarts (each inst once) is not a conflict.
        HelloConflict? Turns(string id, params string[] insts)
        {
            var hearing = new HelloHearing(() => now);
            now = 0;
            for (var i = 0; i < insts.Length; i++)
            {
                now = i * 667L;
                hearing.Hear(From(id, insts[i], "PC-" + insts[i][..1]), "10.0.0." + (10 + insts[i][0] - 'a').ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return hearing.Peers().Single().Conflict;
        }
        const string A = "aaaaaaaa", B = "bbbbbbbb", C = "cccccccc", D = "dddddddd", E = "eeeeeeee", F = "ffffffff";
        var abca = Turns("pat00003", A, B, C, A);
        var abcb = Turns("pat00004", A, B, C, B);
        var six = Turns("pat00005", A, B, C, D, E, F, A, B, C, D, E, F, A, B, C, D, E, F);
        var restarts = Turns("pat00006", A, B, C, D, E, F);
        var noneYet = Turns("pat00007", A, B, C);
        Check(abca is { HostA: "PC-c", AddressA: "10.0.0.12", HostB: "PC-a", AddressB: "10.0.0.10" } && abcb is { HostA: "PC-c", HostB: "PC-b" }
              && six is not null && restarts is null && noneYet is null,
            "hello: three or more copies of one id are in conflict whatever order they take turns in (A B C A, A B C B, six in turn); a run of restarts is not");

        // Another machine saying this node's own id: marked, and clear a minute after the last such hello.
        var self = new HelloHearing(() => now);
        now = 0;
        var clean = self.OwnConflict() is null;
        self.HearOwn(From("3f9c2a1b7d0e", "bbbbbbbb", "FOH-PC2"), "10.0.0.7");
        var own = self.OwnConflict();
        now = (long)SlidesBeacon.ConflictClearsAfter.TotalMilliseconds;
        var ownStands = self.OwnConflict() is not null;
        now += 1;
        Check(clean && own is { Id: "3f9c2a1b7d0e", Other: "FOH-PC2 at 10.0.0.7" } && ownStands && self.OwnConflict() is null && self.Peers().Count == 0,
            "hello: another machine saying this node's own id marks this node (never a peer), until a minute passes without it");

        // The certificate each address's own hello named: never another address's.
        var certs = new HelloHearing(() => now);
        now = 0;
        const string FpA = "sha256:0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a";
        certs.Hear(From("9f8e7d6c", "a1b2c3d4") with { Tls = FpA }, "10.0.0.5");
        now = 500;
        certs.Hear(From("9f8e7d6c", "a1b2c3d4"), "127.0.0.1");
        var withCerts = certs.Peers().Single();
        Check(withCerts.TlsAt("10.0.0.5") == FpA && withCerts.TlsAt("127.0.0.1") is null && withCerts.TlsAt("10.0.0.9") is null && withCerts.Hello.Tls is null,
            "hello: the certificate a hello named is kept for the address it was heard at, never lent to another");

        // From a node that sends no inst, two hosts within the stale time are a conflict; the same host is one node.
        var old = new HelloHearing(() => now);
        now = 0;
        old.Hear(From("ag-1234", null, "LAPTOP"), "10.0.0.8");
        now = 1000;
        old.Hear(From("ag-1234", null, "LAPTOP"), "10.0.0.9");
        var oneHost = old.Peers().Single().Conflict is null;
        now = 2000;
        old.Hear(From("ag-1234", null, "LAPTOP2"), "10.0.0.10");
        Check(oneHost && old.Peers().Single().Conflict is { HostA: "LAPTOP", HostB: "LAPTOP2" },
            "hello: from a node that sends no inst, two hosts within the stale time are a conflict, one host from two addresses is not");

        // Forgotten after a minute unheard; at most MaxPeers ids, room again once one is forgotten.
        var table = new HelloHearing(() => now);
        now = 0;
        for (var i = 0; i < SlidesBeacon.MaxPeers; i++) table.Hear(From("p" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture), null), "10.0.1.1");
        var full = !table.Hear(From("pnew01", null), "10.0.1.2");
        now = (long)SlidesBeacon.ForgetAfter.TotalMilliseconds + 1;
        Check(full && table.Hear(From("pnew01", null), "10.0.1.2") && table.Peers() is [{ Hello.Id: "pnew01" }],
            "hello: the table drops a new id while it is full, forgets a peer unheard for a minute, and then has room");
    }

    static void Network(Action<bool, string> Check)
    {
        var now = new DateTime(2026, 10, 25, 20, 0, 0, DateTimeKind.Utc);
        const string Words = "two machines say they are pm5e6f7a: STAGE-L at 10.0.0.12 and STAGE-R at 10.0.0.13";
        var view = ShowNetwork.Summaries(
            [new HeardNode("patterns", "pat00001", "FOH desk", "10.0.0.5", 1), new HeardNode("playmap", "pm5e6f7a", "Stage left", "10.0.0.12", 1) { Conflict = Words }],
            Array.Empty<HeldContext>(), Array.Empty<NodeLinkFact>(), "desk01", now);
        Check(view.Rows.Select(r => r.Peer.Id).SequenceEqual(["pat00001"]) && view.Conflicts is [{ Id: "pm5e6f7a", Conflict: Words }]
              && ShowNetwork.NetworkLine(view, 0, 0) == "Show network: 1 app · 1 id said by two machines"
              && ShowNetwork.PeerAdvice(view) is [{ Key: "peer:pm5e6f7a:conflict", Level: AdviceLevel.Warn } advice] && advice.Text.StartsWith("PlayMap: " + Words, StringComparison.Ordinal),
            "hello: an id in conflict adds no row to the show network (so none to the context); the desk's line counts it and the advice warns");
        // The AI's facts show it as the desk does: its own amber fact, never a context row.
        Check(Stagewatch.Show.AiContext.PeerFacts(view) is [{ Kind: "patterns", Light: Light.Grey }, { Kind: "playmap", Name: "Stage left", Light: Light.Amber, Headline: Words, Parts.Count: 0 }],
            "hello: the AI's facts list an id in conflict amber with the conflict's words, after the rows the context lists");
        // An id in conflict that a context names keeps that row, amber with the conflict's words first, for the AI as on the desk.
        var told = PeerContexts.Parse("{\"v\":1,\"node\":{\"t\":\"playmap\",\"id\":\"pm5e6f7a\",\"name\":\"Stage left\"},\"light\":\"red\",\"headline\":\"output 2 lost\",\"items\":[]}", "10.0.0.12", now);
        var withContext = ShowNetwork.Summaries([new HeardNode("playmap", "pm5e6f7a", "Stage left", "10.0.0.12", 1) { Conflict = Words }],
            told is null ? [] : [new HeldContext(told, NodeProof.None)], Array.Empty<NodeLinkFact>(), "desk01", now);
        Check(told is not null && withContext.Rows is [{ Peer.Id: "pm5e6f7a" }] && withContext.Conflicts.Count == 1
              && Stagewatch.Show.AiContext.PeerFacts(withContext) is [{ Light: Light.Amber, Headline: var h }] && h == Words + " — output 2 lost",
            "hello: a context posted under an id in conflict is still a row (the context lists it), and the AI reads it amber with the conflict first");
        var onlyConflict = ShowNetwork.Summaries([new HeardNode("playmap", "pm5e6f7a", "Stage left", "10.0.0.12", 1) { Conflict = Words }],
            Array.Empty<HeldContext>(), Array.Empty<NodeLinkFact>(), "desk01", now);
        Check(onlyConflict.Rows.Count == 0 && ShowNetwork.NetworkLine(onlyConflict, 0, 0) == "Show network: 0 apps · 1 id said by two machines",
            "hello: a show network of one id in conflict lists no app, and still says so");
    }

    static async Task Live(Action<bool, string> Check)
    {
        // A beacon on its own port, whose hello goes only to a port the test's own socket binds: the hello it says, as heard there.
        var listen = FreeUdpPort();
        using var hear = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var to = (IPEndPoint)hear.Client.LocalEndPoint!;
        var doors = new HelloDoors(9924, true, null, 9923, false, 9925, true);
        var inst = ShowBusHello.NewInst();
        // The first target fails (port 0 takes no datagram), so the copy here must still go: each send is on its own.
        using var beacon = new SlidesBeacon(() => ShowBusHello.Main("3f9c2a1b7d0e", inst, "Hall A", "FOH-PC", doors, true, 1), listen,
            [new IPEndPoint(IPAddress.Loopback, 0), to]);
        var got = hear.ReceiveAsync();
        var heard = await Task.WhenAny(got, Task.Delay(3000)) == got ? got.Result.Buffer : null;
        using (var doc = heard is null ? null : JsonDocument.Parse(heard))
        {
            var root = doc?.RootElement;
            Check(root is { } r && r.GetProperty("t").GetString() == "main" && r.GetProperty("proto").GetInt32() == 3 && r.GetProperty("inst").GetString() == inst
                  && r.GetProperty("caps").EnumerateArray().Any(c => c.GetString() == "windows") && r.GetProperty("ports").GetProperty("http").GetInt32() == 9924
                  && r.GetProperty("ports").TryGetProperty("osc", out _) && !r.GetProperty("ports").TryGetProperty("companion", out _) && heard!.Length <= ShowBusHello.HelloBytes,
                "hello: a beacon's hello is heard where it is sent within 3 s: kind main, proto 3, its inst, cap windows, only the doors bound to the network");
        }
        var settled = System.Diagnostics.Stopwatch.StartNew();
        while (beacon.Status.Said == HelloSaid.NotYet && settled.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(20);   // the beat publishes after its sends
        var status = beacon.Status;
        Check(status is { Said: HelloSaid.ThisPcOnly, Words.Length: > 0 } && status.Targets.Any(t => t.Target.EndsWith(":0", StringComparison.Ordinal) && t.Failed > 0 && t.Sent == 0)
              && status.Targets.Any(t => t.Target == to.ToString() && t.Sent > 0 && t.ThisPc),
            $"hello: a target that fails never stops the next, and the status says only the copy here went ({status.Said}: {status.Words})");

        // It hears: a hello from a peer on this machine, kept by its id with the address it came from; its own is not a peer.
        using var peer = new UdpClient(AddressFamily.InterNetwork);
        var datagram = ShowBusHello.Bytes(ShowBusHello.Agent("lectern1", "a1b2c3d4", "Lectern", "LECTERN-PC", true, 1));
        var echo = ShowBusHello.Bytes(ShowBusHello.Main("3f9c2a1b7d0e", inst, "Hall A", "FOH-PC", doors, true, 1).Hello!);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!beacon.Peers().Any(p => p.Hello.Id == "lectern1") && sw.Elapsed < TimeSpan.FromSeconds(3))
        {
            peer.Send(datagram, datagram.Length, new IPEndPoint(IPAddress.Loopback, listen));
            peer.Send(echo, echo.Length, new IPEndPoint(IPAddress.Loopback, listen));
            await Task.Delay(100);
        }
        Check(beacon.Peers() is [{ Hello: { Id: "lectern1", Presenting: true, Proto: 3, Inst: "a1b2c3d4" }, Address: "127.0.0.1" }] && beacon.OwnConflict is null,
            "hello: a beacon hears a peer's hello with its proto, inst and address, never lists its own, and takes its own echo for itself");

        // A copy of this install saying its id with another inst: never a peer, and this beacon marks itself one of two.
        var copy = ShowBusHello.Bytes(ShowBusHello.Main("3f9c2a1b7d0e", "0badc0de", "Hall A", "FOH-PC2", doors, true, 1).Hello!);
        var sc = System.Diagnostics.Stopwatch.StartNew();
        while (beacon.OwnConflict is null && sc.Elapsed < TimeSpan.FromSeconds(3))
        {
            peer.Send(copy, copy.Length, new IPEndPoint(IPAddress.Loopback, listen));
            await Task.Delay(100);
        }
        Check(beacon.OwnConflict is { Id: "3f9c2a1b7d0e", HostB: "FOH-PC2", AddressB: "127.0.0.1" } && beacon.Peers().All(p => p.Hello.Id != "3f9c2a1b7d0e"),
            "hello: a beacon that hears its own id with another inst marks itself one of two, and never lists it as a peer");

        // Silent: a hello whose doors are on this machine only goes nowhere, whatever its targets.
        using var quiet = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var quietTo = (IPEndPoint)quiet.Client.LocalEndPoint!;
        using var silent = new SlidesBeacon(() => ShowBusHello.Main("4a5b6c7d8e9f", inst, "Hall B", "FOH-PC", doors with { WebBindAll = false }, false, 1),
            FreeUdpPort(), [quietTo]);
        // Every target failing: not said, and after three beats the context's amber.
        using var dead = new SlidesBeacon(() => ShowBusHello.Main("5b6c7d8e9f0a", inst, "Hall C", "FOH-PC", doors, false, 1), FreeUdpPort(),
            [new IPEndPoint(IPAddress.Loopback, 0)]);
        var none = quiet.ReceiveAsync();
        var wait = Task.Delay(TimeSpan.FromMilliseconds(200) + 2 * SlidesBeacon.Every + TimeSpan.FromMilliseconds(600));
        Check(await Task.WhenAny(none, wait) == wait && silent.Status is { Said: HelloSaid.Silent, Beats: >= 2, Scope: HelloScope.None, WebPort: 9924 },
            "hello: a beacon whose doors are on this machine only says nothing, and its status says Silent, why, and the port it would name");
        // The beats run on a timer: wait for the third failing one, up to a deadline, rather than for a fixed time.
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!dead.Status.CannotGoOut && deadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(50);
        Check(dead.Status is { Said: HelloSaid.NotSaid, CannotGoOut: true } d && ShowBusHello.Item(d) is { Light: Light.Amber },
            $"hello: a beacon whose every target fails says Not said, and after {ShowBusHello.FailedBeats} beats the context's amber ({dead.Status.Said}, {dead.Status.Beats})");

        // A target other than IPv4 is refused when the beacon is made, never at a beat on its timer thread.
        var refused = false;
        try { using var v6 = new SlidesBeacon(() => (null, HelloScope.Network), FreeUdpPort(), [new IPEndPoint(IPAddress.IPv6Loopback, 9)]); }
        catch (ArgumentException) { refused = true; }
        Check(refused, "hello: a beacon takes IPv4 targets only, refused when it is made");
    }

    static int FreeUdpPort()
    {
        using var u = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)u.Client.LocalEndPoint!).Port;
    }
}
