using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stagewatch.Core;
using Stagewatch.Remote;

/// <summary>
/// Who may drive another node's air: <see cref="PlayoutGate"/>'s table, Settings → Remote → May drive other show apps as files keep it, and
/// the gate at every door a line comes in by — OSC, Companion TCP, HTTP, WebSocket and MCP — on real servers, a sender on another
/// machine played from loopback by each server's local-address seam, and a page of another site or a rebound name on this PC by the
/// headers a browser sends. A refused line never reaches the show thread, so no link carries it to a peer; fields and counts first,
/// words beside them.
/// </summary>
static class PlayoutGateTests
{
    static readonly IPAddress Lan = IPAddress.Parse("10.0.0.40");
    static readonly IPAddress OtherLan = IPAddress.Parse("10.0.0.41");
    static readonly CommandDoor[] Doors = [CommandDoor.Osc, CommandDoor.Companion, CommandDoor.Http, CommandDoor.WebSocket, CommandDoor.Mcp];

    public static async Task Run(Action<bool, string> Check)
    {
        Table(Check);
        TargetLines(Check);
        Proposals(Check);
        Addresses(Check);
        Settings(Check);
        await Osc(Check);
        await Companion(Check);
        await Web(Check);
        await WebThisPc(Check);
    }

    /// <summary>
    /// A line in another app's own grammar that an agent proposes (MCP's run_command target) passes its sender as a line that drives
    /// another node's air does — the same senders pass and are refused at every door — and is refused in words that name it as a line
    /// for another show app, never as playout.
    /// </summary>
    static void TargetLines(Action<bool, string> Check)
    {
        IReadOnlyList<IPAddress> none = [], listed = [Lan];
        var senders = new[]
        {
            new CommandSender(IPAddress.Loopback, true, false), new CommandSender(Lan, false, false), new CommandSender(OtherLan, false, false),
            new CommandSender(Lan, false, true), new CommandSender(Lan, true, false, OtherSite: true), new CommandSender(Lan, false, true, OtherSite: true),
            new CommandSender(Lan, false, false, OtherSite: true),
        };
        foreach (var door in Doors)
            foreach (var list in new[] { none, listed })
                foreach (var sender in senders)
                {
                    var asked = PlayoutGate.Proposes(door, sender, list);
                    var air = PlayoutGate.Check(ShowCommandKind.Playout, door, sender, list);
                    Check((asked is null) == (air is null) && (asked is null || (asked.StartsWith(PlayoutGate.TargetLine + " from another ", StringComparison.Ordinal)
                                                                                && !asked.Contains("playout", StringComparison.Ordinal))),
                          $"playout gate: {door}, {sender}, {list.Count} listed — a line for another app passes as an air line does, and is refused in its own words ({asked})");
                }
        Check(PlayoutGate.Proposes(CommandDoor.Mcp, new CommandSender(Lan, false, false), none)
              == "a line for another show app from another machine over MCP needs an MCP key while no PIN is set, or its address in " + PlayoutGate.SettingWords
              && PlayoutGate.Proposes(CommandDoor.Mcp, new CommandSender(IPAddress.Loopback, true, false, OtherSite: true), none)
              == "a line for another show app from another site's web page over MCP needs an MCP key while no PIN is set",
              "playout gate: over MCP, a line for another app from an unlisted machine or another site's page hears what would let it through");
    }

    /// <summary>
    /// Show Bus §11: a line only a person on this desk runs (trusting a certificate) is never a proposal aimed at it — refused (400) as it
    /// is proposed, nothing queued and no key spoken for, so the approval desk never shows one it could not run; any other line, and
    /// the same words aimed at another node (its own grammar), is proposed as before.
    /// </summary>
    static void Proposals(Action<bool, string> Check)
    {
        const string Desk = "abc123abc123";
        var nodes = new NodeBindings();
        var desk = new ProposalDesk(() => Desk, nodes);
        string? Never(NodeRef target, string line) => target.Id == Desk ? PlayoutGate.NeverProposed(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)) : null;
        string Body(string target, string line) => "{\"from\":{\"t\":\"playmap\",\"id\":\"pm5e6f7a\"},\"target\":" + target + ",\"line\":\"" + line + "\"}";
        var trust = desk.Submit(Body("""{"t":"main"}""", "playout stage-left trust 03e9 b152"), NodeCredential.Key("kP"), (_, _) => "high", DateTime.UnixEpoch, Never);
        Check(trust is (null, { } why, false) && why == $"{PlayoutRules.TrustDeskOnly} ({PlayoutRules.TrustWhere}), never a proposal"
              && desk.All().Count == 0 && nodes.NodeOf("kP") is null,
              $"playout gate: playout trust proposed to this desk is refused as it comes — nothing queued, no key spoken for ({trust.Error})");
        var go = desk.Submit(Body("""{"t":"main"}""", "playout stage-left go show intro"), NodeCredential.Key("kP"), (_, _) => "high", DateTime.UnixEpoch, Never);
        var elsewhere = desk.Submit(Body("""{"t":"patterns","id":"pat1"}""", "playout stage-left trust 03e9b152"), NodeCredential.Key("kP"), (_, _) => "high", DateTime.UnixEpoch, Never);
        Check(go.Proposal is { State: SharedProposalState.Pending } && elsewhere.Proposal is { State: SharedProposalState.Pending } && desk.All().Count == 2
              && PlayoutGate.NeverProposed(["playout", "stage-left", "go", "show"]) is null && PlayoutGate.NeverProposed([]) is null
              && PlayoutGate.NeverProposed(["nonsense"]) is null,
              "playout gate: any other line to this desk, and a line aimed at another node in its own grammar, is proposed as before");
    }

    static void Table(Action<bool, string> Check)
    {
        IReadOnlyList<IPAddress> none = [], listed = [Lan];
        string? Gate(ShowCommandKind kind, CommandDoor door, IPAddress from, bool thisPc, bool credentialed, IReadOnlyList<IPAddress> list)
            => PlayoutGate.Check(kind, door, new CommandSender(from, thisPc, credentialed), list);

        Check(PlayoutGate.Drives(ShowCommandKind.Playout) && PlayoutGate.Drives(ShowCommandKind.Patterns)
              && Enum.GetValues<ShowCommandKind>().Count(PlayoutGate.Drives) == 2, "playout gate: decides playout and patterns lines, and no other");
        foreach (var door in Doors)
        {
            var name = door.ToString();
            Check(Gate(ShowCommandKind.Playout, door, Lan, false, false, none) is { } p && p.StartsWith("playout from another machine", StringComparison.Ordinal)
                  && p.Contains(PlayoutGate.SettingWords, StringComparison.Ordinal),
                  $"playout gate: {name} — an unlisted machine with nothing proved is refused, naming where to list it");
            Check(Gate(ShowCommandKind.Patterns, door, Lan, false, false, none) is { } q && q.StartsWith("patterns from another machine", StringComparison.Ordinal),
                  $"playout gate: {name} — a patterns line is refused in its own verb's words");
            Check(Gate(ShowCommandKind.Playout, door, IPAddress.Loopback, true, false, none) is null, $"playout gate: {name} — this PC passes");
            Check(Gate(ShowCommandKind.Playout, door, Lan, false, false, listed) is null, $"playout gate: {name} — a listed address passes");
            Check(Gate(ShowCommandKind.Playout, door, OtherLan, false, false, listed) is not null, $"playout gate: {name} — the list names addresses, not a network");
            Check(Gate(ShowCommandKind.Start, door, Lan, false, false, none) is null && Gate(ShowCommandKind.Message, door, Lan, false, false, none) is null,
                  $"playout gate: {name} — every other verb is not the gate's");
            var carries = door is CommandDoor.Http or CommandDoor.WebSocket or CommandDoor.Mcp;
            Check((Gate(ShowCommandKind.Playout, door, Lan, false, true, none) is null) == carries,
                  carries ? $"playout gate: {name} — a credential (the PIN asked for, or a key) passes"
                          : $"playout gate: {name} carries no credential, so a server's word that one was proved opens nothing");
            // A page of another site proves nothing wherever its browser runs: this PC and a listed address vouch for no page.
            string? Page(bool thisPc, bool credentialed, IReadOnlyList<IPAddress> list)
                => PlayoutGate.Check(ShowCommandKind.Playout, door, new CommandSender(Lan, thisPc, credentialed, OtherSite: true), list);
            if (carries)
            {
                Check(Page(true, false, none) == PlayoutGate.OtherSiteWords(ShowCommandKind.Playout, door) && Page(false, false, listed) == Page(true, false, none),
                      $"playout gate: {name} — another site's page is refused from this PC and from a listed address alike, in its own words");
                Check(Page(true, true, none) is null && Page(false, true, none) is null, $"playout gate: {name} — another site's page with a credential passes");
            }
            else Check(Page(true, false, none) is null, $"playout gate: {name} has no pages, so a page's mark is not its business");
        }
        // Trusting a certificate is a person's choice on the desk (Show Bus 13.8.3): no door takes the line from anyone — this PC, a listed
        // address, a credential, a page of this site — and it says so in its door's words.
        Check(PlayoutGate.DeskOnly(ShowCommandKind.PlayoutTrust) && Enum.GetValues<ShowCommandKind>().Count(PlayoutGate.DeskOnly) == 1
              && !PlayoutGate.Drives(ShowCommandKind.PlayoutTrust), "playout gate: trusting a certificate is the one desk-only line, and no line of the air's");
        foreach (var door in Doors)
        {
            var words = PlayoutGate.DeskOnlyWords(door);
            var every = new[] { new CommandSender(IPAddress.Loopback, true, true), new CommandSender(IPAddress.Loopback, true, false), new CommandSender(Lan, false, true) }
                .All(sender => PlayoutGate.Check(ShowCommandKind.PlayoutTrust, door, sender, listed) == words);
            Check(every && words.StartsWith(PlayoutRules.TrustDeskOnly, StringComparison.Ordinal) && words.Contains(PlayoutRules.TrustWhere, StringComparison.Ordinal)
                  && words.EndsWith("whoever sends it", StringComparison.Ordinal),
                  $"playout gate: {door} — playout trust is refused from this PC, a listed address and with a credential, naming where a person does it ({words})");
        }
        Check(PlayoutGate.OtherSiteWords(ShowCommandKind.Patterns, CommandDoor.Http)
              == "patterns from another site's web page over HTTP needs a control key while no PIN is set"
              && PlayoutGate.OtherSiteWords(ShowCommandKind.Playout, CommandDoor.WebSocket).Contains("over a WebSocket needs a control key", StringComparison.Ordinal)
              && PlayoutGate.OtherSiteWords(ShowCommandKind.Playout, CommandDoor.Mcp).Contains("over MCP needs an MCP key", StringComparison.Ordinal),
              "playout gate: another site's page hears its door and its credential");

        // Another site's page, by the headers a browser sets itself and a page's script cannot.
        foreach (var (origin, host, site, other, why) in new (string?, string?, string?, bool, string)[]
                 {
                     (null, "127.0.0.1:9924", null, false, "a device or script sends neither header"),
                     (null, "127.0.0.1:9924", "none", false, "typed in the address bar"),
                     ("http://evil.example", "127.0.0.1:9924", "same-origin", false, "Sec-Fetch-Site's word stands over Origin's"),
                     (null, "127.0.0.1:9924", "cross-site", true, "an img or a form of another site"),
                     (null, "127.0.0.1:9924", "Same-Site", true, "another port or app on this host"),
                     ("http://evil.example", "127.0.0.1:9924", null, true, "an Origin that is not this Host"),
                     ("http://127.0.0.1:8080", "127.0.0.1:9924", null, true, "another port's page"),
                     ("null", "127.0.0.1:9924", null, true, "a sandboxed or file page"),
                     ("http://127.0.0.1:9924", "127.0.0.1:9924", null, false, "Stagewatch's own page"),
                     ("HTTP://LOCALHOST:9924", "localhost:9924", null, false, "its own page by name, in any case"),
                     ("http://127.0.0.1:9924", null, null, true, "an Origin with no Host to match"),
                 })
            Check(PlayoutGate.OtherSite(origin, host, site) == other, $"playout gate: another site's page? {other} — {why}");

        // A Host that names this PC's server over loopback; a rebound name or a forwarder's port is another machine's.
        foreach (var (host, port, names, why) in new (string?, int, bool, string)[]
                 {
                     (null, 9924, true, "no Host: a device or a script"),
                     ("127.0.0.1:9924", 9924, true, "the loopback address and the port"),
                     ("localhost:9924", 9924, true, "localhost"),
                     ("LocalHost:9924", 9924, true, "localhost in any case"),
                     ("[::1]:9924", 9924, true, "IPv6 loopback, bracketed"),
                     ("127.0.0.2:9924", 9924, true, "any loopback address"),
                     ("127.0.0.1", 80, true, "no port on port 80"),
                     ("[::1]", 443, true, "no port on port 443"),
                     ("127.0.0.1", 9924, false, "no port on another port"),
                     ("127.0.0.1:8080", 9924, false, "a forwarder's port"),
                     ("evil.example:9924", 9924, false, "a name rebound to 127.0.0.1"),
                     ("stage-pc:9924", 9924, false, "a machine name"),
                     ("10.0.0.40:9924", 9924, false, "a LAN address"),
                     ("localhost.:9924", 9924, false, "a dotted root name"),
                     ("127.1:9924", 9924, false, "an address no browser writes"),
                     ("0177.0.0.1:9924", 9924, false, "an octal address"),
                     ("::1:9924", 9924, false, "IPv6 unbracketed"),
                     ("[fe80::1]:9924", 9924, false, "IPv6 not loopback"),
                     ("127.0.0.1:09924", 9924, false, "a port with a leading zero"),
                     ("127.0.0.1:", 9924, false, "an empty port"),
                     ("127.0.0.1:+9924", 9924, false, "a port with a sign"),
                     ("[::1", 9924, false, "an open bracket"),
                 })
            Check(PlayoutGate.NamesThisPc(host, port) == names, $"playout gate: Host \"{host}\" names this PC at {port}? {names} — {why}");
        Check(PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Osc)
              == "playout from another machine over OSC needs its address in Settings → Remote → May drive other show apps (OSC carries no PIN)",
              "playout gate: OSC's words");
        Check(PlayoutGate.Words(ShowCommandKind.Patterns, CommandDoor.Companion)
              == "patterns from another machine over Companion TCP needs its address in Settings → Remote → May drive other show apps (Companion TCP carries no PIN)",
              "playout gate: Companion TCP's words");
        Check(PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Http)
              == "playout from another machine over HTTP needs a control key while no PIN is set, or its address in Settings → Remote → May drive other show apps",
              "playout gate: HTTP's words");
        Check(PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Mcp).Contains("an MCP key while no PIN is set", StringComparison.Ordinal)
              && PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.WebSocket).Contains("over a WebSocket needs a control key", StringComparison.Ordinal),
              "playout gate: MCP's and the WebSocket's words name their own credential");

        // Addresses compare by their bytes: an IPv4-mapped sender is its IPv4 address, and an IPv6 scope is not part of it.
        Check(PlayoutGate.IsLoopback(IPAddress.Loopback) && PlayoutGate.IsLoopback(IPAddress.IPv6Loopback)
              && PlayoutGate.IsLoopback(IPAddress.Parse("::ffff:127.0.0.1")) && !PlayoutGate.IsLoopback(Lan) && !PlayoutGate.IsLoopback(IPAddress.Parse("::ffff:10.0.0.40")),
              "playout gate: this PC is loopback, IPv4-mapped included");
        Check(PlayoutGate.Listed(IPAddress.Parse("::ffff:10.0.0.40"), listed) && PlayoutGate.Listed(Lan, [IPAddress.Parse("::ffff:10.0.0.40")]),
              "playout gate: an IPv4-mapped sender or entry is its IPv4 address");
        Check(PlayoutGate.Listed(IPAddress.Parse("fe80::1%3"), [IPAddress.Parse("fe80::1")]) && !PlayoutGate.Listed(IPAddress.Parse("fe80::2"), [IPAddress.Parse("fe80::1")]),
              "playout gate: a link-local sender matches its listed address whatever its scope");
    }

    static void Addresses(Action<bool, string> Check)
    {
        foreach (var good in new[] { "10.0.0.40", " 192.168.1.20 ", "fe80::1", "2001:db8::7", "::ffff:10.0.0.5" })
            Check(PlayoutGate.Parse(good) is not null, $"may drive playout: \"{good}\" is an address");
        Check(PlayoutGate.Parse("::ffff:10.0.0.5") is { } mapped && mapped.ToString() == "10.0.0.5", "may drive playout: an IPv4-mapped entry is kept as its IPv4 address");
        foreach (var bad in new[] { "", " ", "10.1", "1", "010.0.0.1", "0x0a.0.0.1", "10.0.0.40:9925", "[::1]", "[::1]:80", "10.0.0.0/24", "stage-pc.local",
                                    "0.0.0.0", "::", "255.255.255.255", "10.0.0.40 10.0.0.41", "10.0.0.256", "127.0.0.1", "127.0.0.2", "::1", "::ffff:127.0.0.1" })
            Check(PlayoutGate.Parse(bad) is null, $"may drive playout: \"{bad}\" is not one address, so it is never listed");
        Check(PlayoutGate.Parse(null) is null, "may drive playout: nothing is no address");
        Check(PlayoutGate.AddressWords.Contains("loopback", StringComparison.Ordinal), "may drive playout: the words say loopback is never listed (this PC has its own rule)");

        var notes = new LoadNotes();
        var many = Enumerable.Range(1, 18).Select(i => "10.0.1." + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var kept = PlayoutGate.Sanitise([.. many.Take(3), "10.0.1.2", "stage-pc", null, .. many.Skip(3)], notes);
        Check(kept.Count == PlayoutGate.MaxFrom && PlayoutGate.MaxFrom == 16 && kept.SequenceEqual(many.Take(16)),
              "may drive playout: the first 16 addresses are kept in order, a repeat once");
        Check(notes.Words.Count == 3 && notes.Words[0] == "May drive other show apps: \"stage-pc\" left out: " + PlayoutGate.AddressWords
              && notes.Words[1] == "May drive other show apps: \"\" left out: " + PlayoutGate.AddressWords
              && notes.Words[2] == "May drive other show apps keeps 16 addresses: 2 more left out",
              $"may drive playout: what a load leaves out is said, a repeat silently kept once ({string.Join(" | ", notes.Words)})");
        Check(PlayoutGate.Addresses(["10.0.0.40", "bad", "10.0.0.40"]) is [var one] && one.Equals(Lan), "may drive playout: the gate's copy holds the readable addresses once");
    }

    static void Settings(Action<bool, string> Check)
    {
        using (var file = Scratch.File("playout-from", ".json"))
        {
            File.WriteAllText(file.Path, """{"remote":{"pin":"1234","playoutFrom":["10.0.0.40","stage-pc.local","10.0.0.40","::ffff:10.0.0.41"]}}""");
            using (var store = new SettingsStore(file.Path))
            {
                Check(store.Settings.Remote.PlayoutFrom.SequenceEqual(new[] { "10.0.0.40", "10.0.0.41" }), "may drive playout: a settings file keeps its addresses, readable and once");
                Check(store.Notes.Contains("May drive other show apps: \"stage-pc.local\" left out: " + PlayoutGate.AddressWords), "may drive playout: a name left out is said at load");
                store.SaveNow();
            }
            var remote = JsonNode.Parse(File.ReadAllText(file.Path))!["remote"]!.AsObject();
            Check(remote.Last().Key == "playoutFrom" && remote["playoutFrom"]!.AsArray().Select(a => (string?)a).SequenceEqual(new[] { "10.0.0.40", "10.0.0.41" }),
                  "may drive playout: saved last in remote, as kept");
        }
        using (var fresh = Scratch.File("playout-from-fresh", ".json"))
        using (var store = new SettingsStore(fresh.Path))
            Check(store.Settings.Remote.PlayoutFrom.Count == 0 && store.Notes.Count == 0, "may drive playout: empty unless the operator lists an address");
        using (var nulled = Scratch.File("playout-from-null", ".json"))
        {
            File.WriteAllText(nulled.Path, """{"remote":{"pin":"1234","playoutFrom":null}}""");
            using var store = new SettingsStore(nulled.Path);
            Check(store.Settings.Remote.PlayoutFrom is { Count: 0 }, "may drive playout: a list nulled by hand is empty, never null");
        }
    }

    static async Task Osc(Action<bool, string> Check)
    {
        var control = new FakeControl();
        var access = new Access();
        var port = FreeUdpPort();
        var rejected = new List<string>();
        using var osc = new OscServer(control, access) { IsThisPc = _ => false };   // every sender is "another machine"
        osc.CommandRejected += m => { lock (rejected) rejected.Add(m); };
        osc.Start(port, bindAll: false, null, 0);
        using var sender = new UdpClient();
        async Task Send(string address, params object[] args)
        {
            var b = OscCodec.EncodeMessage(address, args);
            await sender.SendAsync(b, b.Length, new IPEndPoint(IPAddress.Loopback, port));
        }
        async Task<bool> Ran(ShowCommandKind kind, int at)
        {
            for (var i = 0; i < 100; i++)
            {
                lock (control.Commands) if (control.Commands.Skip(at).Any(c => c.Kind == kind)) return true;
                await Task.Delay(20);
            }
            return false;
        }
        int Count() { lock (control.Commands) return control.Commands.Count; }
        bool Any(ShowCommandKind kind) { lock (control.Commands) return control.Commands.Any(c => c.Kind == kind); }

        await Send("/stagewatch/playout", "stage-left", "go", "show", "intro");
        await Send("/stagewatch/patterns", "pause");
        await Send("/stagewatch/playout/pause");
        await Send("/stagewatch/peer", "playmap", "UNDO");
        await Send("/stagewatch/desk/approve", "next");
        await Send("/stagewatch/window", "grant", "lobby", "9f8e7d6c");
        await Send("/stagewatch/start");   // the same sender's other verbs run as they always have; the server reads in order
        Check(await Ran(ShowCommandKind.Start, 0), "osc gate: start from another machine still runs");
        Check(!Any(ShowCommandKind.Playout) && !Any(ShowCommandKind.Patterns) && !Any(ShowCommandKind.Peer) && !Any(ShowCommandKind.Desk)
              && Windows(control).Count == 0 && osc.MessagesRejected == 6,
              $"osc gate: playout, patterns, peer, desk and a window grant from an unlisted machine never reach the show thread ({osc.MessagesRejected} rejected)");
        lock (rejected)
            Check(rejected.Count == 6 && rejected[0] == "OSC: " + PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Osc)
                  && rejected[1] == "OSC: " + PlayoutGate.Words(ShowCommandKind.Patterns, CommandDoor.Osc)
                  && rejected[3] == "OSC: " + PlayoutGate.Words(ShowCommandKind.Peer, CommandDoor.Osc)
                  && rejected[4] == "OSC: " + PlayoutGate.Words(ShowCommandKind.Desk, CommandDoor.Osc)
                  && rejected[5] == "OSC: " + PlayoutGate.GrantWords(CommandDoor.Osc),
                  "osc gate: the refusal's words reach the status line, each in its own verb's words");

        access.From = [IPAddress.Loopback];   // the sender's address, listed
        var at = Count();
        await Send("/stagewatch/playout", "stage-left", "go", "show", "intro");
        Check(await Ran(ShowCommandKind.Playout, at), "osc gate: a listed address drives playout");
        // A person's line (peer, desk) from a listed address reaches the show thread named a deck's: OSC by name, the address for the desk's
        // journal alone; the desk then decides it as a person's. Its OSC addresses: /stagewatch/peer ,ss and /stagewatch/desk/approve ,s.
        var rejectedAt = osc.MessagesRejected;
        await Send("/stagewatch/peer", "playmap", "UNDO");
        await Send("/stagewatch/desk/approve", "p17", "high");
        await Send("/stagewatch/desk/reject", "next");
        await Send("/stagewatch/next");
        Check(await Ran(ShowCommandKind.Next, at) && osc.MessagesRejected == rejectedAt, "osc gate: peer and desk from a listed address pass the door");
        List<ShowCommand> people;
        lock (control.Commands) people = [.. control.Commands.Where(c => PlayoutGate.Person(c.Kind))];
        Check(people is [{ Kind: ShowCommandKind.Peer } peer, { Kind: ShowCommandKind.Desk } approve, { Kind: ShowCommandKind.Desk } reject]
              && peer.Get<PeerArgs>() is { App: "playmap", Line: "UNDO" } && approve.Get<DeskArgs>() is { Action: "approve", Id: "p17", High: true }
              && reject.Get<DeskArgs>() is { Action: "reject", Id: "next" }
              && people.All(c => c.By is { Role: "deck", Door: "OSC", Who: "OSC, 127.0.0.1" }),
              $"osc gate: peer and desk reach the show thread whole, each named a deck's by the door, its address for the journal alone ({string.Join(" | ", people.Select(c => c.By?.Who))})");
        // A window grant is a person's act (Show Bus §12.4) that OSC proves no person for: from a listed address it reaches the show thread
        // named a deck's, so the host asks it of the operator on the desk and never makes it; the same sender's other window lines name no one.
        var grantsAt = Count();
        await Send("/stagewatch/window", "grant", "lobby", "9f8e7d6c");
        await Send("/stagewatch/window", "show", "lobby", "/clock");
        Check(await Ran(ShowCommandKind.Window, grantsAt) && osc.MessagesRejected == rejectedAt, "osc gate: a window grant from a listed address passes the door");
        List<ShowCommand> windowLines;
        lock (control.Commands) windowLines = [.. control.Commands.Skip(grantsAt).Where(c => c.Kind == ShowCommandKind.Window)];
        var grantLine = windowLines.FirstOrDefault(c => c.Get<WindowArgs>().Action == "grant");
        Check(grantLine.By is { Asks: true, Door: "OSC" } && windowLines.Where(c => c.Get<WindowArgs>().Action != "grant").All(c => c.By is null),
              "osc gate: the grant is named a deck's — the host asks it of the operator — and the window show after it is no one's");

        // The default seam: a sender on this PC passes with nothing listed.
        var local = new FakeControl();
        var localPort = FreeUdpPort();
        using var plain = new OscServer(local);
        plain.Start(localPort, bindAll: false, null, 0);
        var bytes = OscCodec.EncodeMessage("/stagewatch/patterns", "pause");
        await sender.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, localPort));
        var ok = false;
        for (var i = 0; i < 100 && !ok; i++) { lock (local.Commands) ok = local.Commands.Any(c => c.Kind == ShowCommandKind.Patterns); if (!ok) await Task.Delay(20); }
        Check(ok, "osc gate: this PC (loopback) drives patterns, as before");
        bytes = OscCodec.EncodeMessage("/stagewatch/window", "grant", "lobby", "9f8e7d6c");
        await sender.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, localPort));
        bytes = OscCodec.EncodeMessage("/stagewatch/window", "withdraw", "lobby");
        await sender.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, localPort));
        ok = false;
        for (var i = 0; i < 100 && !ok; i++) { ok = Windows(local).Any(a => a.Action == "withdraw"); if (!ok) await Task.Delay(20); }
        List<ShowCommand> localGrants;
        lock (local.Commands) localGrants = [.. local.Commands.Where(c => c.Kind == ShowCommandKind.Window && c.Get<WindowArgs>().Action == "grant")];
        Check(ok && localGrants is [{ By: { Asks: true, Door: "OSC" } }] && plain.MessagesRejected == 0,
              "osc gate: this PC's window grant passes only to be asked of the operator (OSC proves no person), and its withdraw after it runs");
        Check(typeof(OscServer).GetConstructors().Single().GetParameters() is [{ } first, { } second]
              && first.ParameterType == typeof(IShowCommands) && second.ParameterType == typeof(IShowAccess) && second.HasDefaultValue,
              "osc gate: OSC needs commands and frames, and who may drive playout (none given: this PC only)");
    }

    static async Task Companion(Action<bool, string> Check)
    {
        var bridge = new FakeBridge { Allowed = true };
        var port = FreeTcpPort();
        var rejected = new List<string>();
        await using var server = new Stagewatch.Companion.CompanionServer(bridge) { IsThisPc = _ => false };
        server.CommandRejected += m => { lock (rejected) rejected.Add(m); };
        server.Start(port, bindAllInterfaces: false);
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);
        async Task Say(string line) => await stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));
        async Task<string?> Answer(string prefix)
        {
            for (var i = 0; i < 20; i++)
            {
                using var cts = new CancellationTokenSource(2000);
                string? line;
                try { line = await reader.ReadLineAsync(cts.Token); }
                catch (OperationCanceledException) { return null; }
                if (line is null) return null;
                if (line.StartsWith(prefix, StringComparison.Ordinal)) return line;
            }
            return null;
        }
        bool Any(ShowCommandKind kind) { lock (bridge.Commands) return bridge.Commands.Any(c => c.Kind == kind); }

        await Say("PATTERNS PAUSE");
        Check(await Answer("ERROR ") == "ERROR " + PlayoutGate.Words(ShowCommandKind.Patterns, CommandDoor.Companion),
              "companion gate: patterns from an unlisted machine is refused in words");
        await Say("@c1 playout stage-left go show intro");
        Check(await Answer("ERROR @c1 ") == "ERROR @c1 " + PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Companion),
              "companion gate: a playout line with a request id is refused under its id");
        await Say("@c2 next");
        Check(await Answer("OK @c2") == "OK @c2 accepted" && Any(ShowCommandKind.Next), "companion gate: other verbs from that machine run as before");
        Check(!Any(ShowCommandKind.Patterns) && !Any(ShowCommandKind.Playout), "companion gate: nothing refused reached the show thread");
        lock (rejected)
            Check(rejected.Count == 2 && rejected[1] == "Companion: " + PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Companion),
                  "companion gate: each refusal reaches the status line");
        await Say("FROBNICATE");
        Check(await Answer("ERROR ") is { } unknown && !unknown.Contains("playout", StringComparison.Ordinal) && server.MessagesRejected == 3,
              $"companion gate: diagnostics count every line refused at the door, the grammar's and the gate's ({server.MessagesRejected})");

        await Say("@c0 peer playmap UNDO");
        Check(await Answer("ERROR @c0 ") == "ERROR @c0 " + PlayoutGate.Words(ShowCommandKind.Peer, CommandDoor.Companion) && !Any(ShowCommandKind.Peer),
              "companion gate: a peer line from an unlisted machine is refused under its id, in words that name the setting");
        await Say("@g0 window grant lobby 9f8e7d6c");
        Check(await Answer("ERROR @g0 ") == "ERROR @g0 " + PlayoutGate.GrantWords(CommandDoor.Companion) && Windows(bridge.Commands).Count == 0,
              "companion gate: a window grant from an unlisted machine is refused under its id, and the show thread never hears it");

        bridge.From = [IPAddress.Loopback];
        await Say("@c3 patterns pause");
        Check(await Answer("OK @c3") == "OK @c3 accepted" && Any(ShowCommandKind.Patterns), "companion gate: a listed address drives patterns");
        // A person's line (peer, desk) from a listed address reaches the show thread named a deck's: Companion TCP by name, the address
        // for the desk's journal alone, with or without a request id.
        var rejectedAt = server.MessagesRejected;
        await Say("@c4 PEER playmap \"PLAYLIST CUE \\\"Main show\\\" #3\"");
        Check(await Answer("OK @c4") == "OK @c4 accepted", "companion gate: a peer line from a listed address passes under its id");
        await Say("DESK APPROVE p18-k3m9 HIGH");
        await Say("@c5 desk reject next");
        Check(await Answer("OK @c5") == "OK @c5 accepted" && server.MessagesRejected == rejectedAt, "companion gate: desk lines from a listed address pass, with an id and without");
        List<ShowCommand> people;
        lock (bridge.Commands) people = [.. bridge.Commands.Where(c => PlayoutGate.Person(c.Kind))];
        Check(people is [{ Kind: ShowCommandKind.Peer } peer, { Kind: ShowCommandKind.Desk } approve, { Kind: ShowCommandKind.Desk } reject]
              && peer.Get<PeerArgs>() is { App: "playmap", Line: "PLAYLIST CUE \"Main show\" #3" }
              && approve.Get<DeskArgs>() is { Action: "approve", Id: "p18-k3m9", High: true } && reject.Get<DeskArgs>() is { Action: "reject", Id: "next" }
              && people.All(c => c.By is { Role: "deck", Door: "Companion TCP", Who: "Companion TCP, 127.0.0.1" }),
              $"companion gate: peer and desk reach the show thread whole, each named a deck's by the door ({string.Join(" | ", people.Select(c => c.By?.Who))})");
        // A window grant is a person's act (Show Bus §12.4) that Companion TCP proves no person for: from a listed address it reaches the
        // show thread named a deck's, so the host asks it of the operator on the desk and never makes it; a withdraw names no one.
        await Say("@c6 window grant lobby 9f8e7d6c");
        Check(await Answer("OK @c6") == "OK @c6 accepted" && server.MessagesRejected == rejectedAt, "companion gate: a window grant from a listed address passes the door");
        await Say("@c7 window withdraw lobby");
        Check(await Answer("OK @c7") == "OK @c7 accepted", "companion gate: a withdraw from the same deck runs");
        List<ShowCommand> windowLines;
        lock (bridge.Commands) windowLines = [.. bridge.Commands.Where(c => c.Kind == ShowCommandKind.Window)];
        Check(windowLines is [{ By: { Asks: true, Door: "Companion TCP" } } grantLine, { By: null } withdrawLine]
              && grantLine.Get<WindowArgs>() is { Action: "grant", Who: "lobby", Argument: "9f8e7d6c" } && withdrawLine.Get<WindowArgs>().Action == "withdraw",
              "companion gate: the grant is named a deck's, which the host asks of the operator; the withdraw is no one's");
    }

    /// <summary>The window lines a surface heard, in order.</summary>
    static List<WindowArgs> Windows(List<ShowCommand> commands)
    {
        lock (commands) return [.. commands.Where(c => c.Kind == ShowCommandKind.Window).Select(c => c.Get<WindowArgs>()!)];
    }

    static List<WindowArgs> Windows(FakeControl control) => Windows(control.Commands);

    static async Task Web(Action<bool, string> Check)
    {
        var control = new GateControl();
        string? pin = null;
        var port = FreeTcpPort();
        var rejected = new List<string>();
        await using var web = new WebServer(control, () => pin) { IsThisPc = _ => false };
        web.CommandRejected += m => { lock (rejected) rejected.Add(m); };
        web.Start(port, bindAll: false);
        control.Snapshots.Publish(new ShowSnapshot { Text = "05:00", Seconds = 300 });
        control.Snapshots.MarkAlive();
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var (ctlKey, ctlSecret) = ApiKeys.Create("Deck", ApiKeyScope.Control, DateTime.UtcNow);
        var (mcpKey, mcpSecret) = ApiKeys.Create("Agent", ApiKeyScope.Mcp, DateTime.UtcNow);
        control.Keys = [ctlKey, mcpKey];
        HttpRequestMessage Post(string path, string? bearer = null, string? requestId = null, string? body = null)
        {
            var m = new HttpRequestMessage(HttpMethod.Post, path);
            if (bearer is not null) m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            if (requestId is not null) m.Headers.Add("X-Request-Id", requestId);
            if (body is not null) m.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return m;
        }
        int Count(ShowCommandKind kind) { lock (control.Commands) return control.Commands.Count(c => c.Kind == kind); }

        // HTTP with no PIN set: the open door lets anyone run the show, but another node's air needs a key or a listed address.
        var refused = await http.SendAsync(Post("/api/patterns/pause"));
        var refusedBody = JsonNode.Parse(await refused.Content.ReadAsStringAsync())!;
        Check(refused.StatusCode == HttpStatusCode.Forbidden && (bool?)refusedBody["ok"] == false
              && (string?)refusedBody["error"] == PlayoutGate.Words(ShowCommandKind.Patterns, CommandDoor.Http) && Count(ShowCommandKind.Patterns) == 0,
              "http gate: no PIN set, an unlisted machine's patterns is refused 403 in words, and nothing runs");
        var receipt = await http.SendAsync(Post("/api/playout/stage-left/go/show/intro", requestId: "g1"));
        var receiptBody = JsonNode.Parse(await receipt.Content.ReadAsStringAsync())!;
        Check(receipt.StatusCode == HttpStatusCode.Forbidden && (string?)receiptBody["type"] == "receipt" && (string?)receiptBody["id"] == "g1"
              && (bool?)receiptBody["ok"] == false && (string?)receiptBody["error"] == PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Http)
              && Count(ShowCommandKind.Playout) == 0,
              "http gate: with a request id the refusal is a refused receipt under that id");
        Check((await http.SendAsync(Post("/api/start"))).StatusCode == HttpStatusCode.OK && Count(ShowCommandKind.Start) == 1,
              "http gate: every other verb runs through the open door as before");
        Check((await http.SendAsync(Post("/api/patterns/pause", ctlSecret))).StatusCode == HttpStatusCode.OK && Count(ShowCommandKind.Patterns) == 1,
              "http gate: a control key drives patterns from another machine");
        lock (rejected)
            Check(rejected.Count == 2 && rejected[0] == "Remote: " + PlayoutGate.Words(ShowCommandKind.Patterns, CommandDoor.Http), "http gate: each refusal reaches the status line");

        // MCP with no PIN set: the sender must be one that may send such a line, then a person decides, always.
        async Task<JsonNode> Mcp(string line, string? bearer = null)
        {
            var body = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 7, method = "tools/call", @params = new { name = "run_command", arguments = new { command = line, reason = "clip overran" } } });
            var r = await http.SendAsync(Post("/mcp", bearer, body: body));
            return JsonNode.Parse(await r.Content.ReadAsStringAsync())!["result"]!;
        }
        static string Text(JsonNode result) => (string?)result["content"]![0]!["text"] ?? "";
        var gated = await Mcp("playout stage-left go show intro");
        Check((bool?)gated["isError"] == true && Text(gated) == "Refused: " + PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Mcp)
              && control.ProposedCount == 0 && Count(ShowCommandKind.Playout) == 0,
              "mcp gate: no PIN set and no key, an unlisted machine's playout line is refused, never even proposed");
        lock (rejected)
            Check(rejected.Count == 3 && rejected[2] == "MCP: " + PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Mcp),
                  "mcp gate: the refusal reaches the status line like the other doors'");
        var proposed = await Mcp("playout stage-left go show intro", mcpSecret);
        Check((bool?)proposed["isError"] == false && Text(proposed).Contains("approval", StringComparison.Ordinal) && control.ProposedCount == 1
              && control.LastMust && Count(ShowCommandKind.Playout) == 0,
              "mcp gate: with an MCP key, a playout line is a proposal even with agents acting directly, and nothing runs");
        var direct = await Mcp("nudge 30");
        Check((bool?)direct["isError"] == false && Count(ShowCommandKind.Nudge) == 1 && control.ProposedCount == 1,
              "mcp gate: any other line from that machine runs directly as before when agents act directly");
        control.IgnoreMust = true;   // a host that would let it run: the endpoint still never runs it
        var unqueued = await Mcp("patterns pause", mcpSecret);
        Check((bool?)unqueued["isError"] == true && Text(unqueued) == IShowAgents.NobodyApproves && Count(ShowCommandKind.Patterns) == 1,
              "mcp gate: a line that drives another app's air never runs directly, even when the host queues nothing");
        control.IgnoreMust = false;
        // A line for another app asks for that app's air: from an unlisted machine with no key it is refused before the desk hears it,
        // seen on the status line; with an MCP key it reaches the desk, and the desk's refusal is seen there too, never with the line.
        async Task<JsonNode> ToApp(string line, string? bearer = null)
        {
            var body = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 8, method = "tools/call", @params = new { name = "run_command", arguments = new { command = line, target = "pm1" } } });
            var r = await http.SendAsync(Post("/mcp", bearer, body: body));
            return JsonNode.Parse(await r.Content.ReadAsStringAsync())!["result"]!;
        }
        int seen;
        lock (rejected) seen = rejected.Count;
        var unkeyed = await ToApp("UNDO");
        var targetWords = PlayoutGate.Proposes(CommandDoor.Mcp, new CommandSender(IPAddress.Loopback, false, false), []);
        Check((bool?)unkeyed["isError"] == true && Text(unkeyed) == "Refused: " + targetWords && control.ToApps == 0,
              $"mcp gate: no PIN set and no key, an unlisted machine's line for another app is refused before the desk hears it ({Text(unkeyed)})");
        var withKey = await ToApp("UNDO", mcpSecret);
        var unlisted = await ToApp("UNLISTED", mcpSecret);
        Check((bool?)withKey["isError"] == false && control.ToApps == 1 && Text(withKey).StartsWith("Queued for the operator's approval as p1-k3m9", StringComparison.Ordinal)
              && (bool?)unlisted["isError"] == true && control.ToApps == 1,
              "mcp gate: with an MCP key, a line for another app reaches the desk, which decides what it queues");
        lock (rejected)
            Check(rejected.Count == seen + 2 && rejected[seen] == "MCP: " + targetWords && rejected[seen + 1] == "MCP: PlayMap Hall does not list that line — see its verbs"
                  && !rejected.Exists(r => r.Contains("UNLISTED", StringComparison.Ordinal)),
                  "mcp gate: the gate's refusal and the desk's each reach the status line, never the line itself");

        // A window grant is a person's act (Show Bus §12.4): an agent only proposes one, even where agents act directly.
        var proposedAt = control.ProposedCount;
        var agentGrant = await Mcp("window grant lobby 9f8e7d6c", mcpSecret);
        Check((bool?)agentGrant["isError"] == false && control.ProposedCount == proposedAt + 1 && Count(ShowCommandKind.Window) == 0,
              "mcp gate: an agent's window grant is a proposal even with agents acting directly, and nothing runs");

        // WebSocket: a control page let in with no PIN set proved nothing.
        using (var ws = new ClientWebSocket())
        {
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?role=control"), CancellationToken.None);
            await Send(ws, """{"cmd":"patterns","args":["pause"]}""");
            var error = await Next(ws, "\"type\":\"error\"");
            Check(error is not null && (string?)JsonNode.Parse(error)!["error"] == PlayoutGate.Words(ShowCommandKind.Patterns, CommandDoor.WebSocket),
                  "ws gate: no PIN set, an unlisted machine's control page is refused patterns in words");
            await Send(ws, """{"cmd":"playout","args":["stage-left","pause"],"id":"w1"}""");
            var wsReceipt = await Next(ws, "\"type\":\"receipt\"");
            Check(wsReceipt is not null && JsonNode.Parse(wsReceipt) is { } rr && (string?)rr["id"] == "w1" && (bool?)rr["ok"] == false
                  && (string?)rr["error"] == PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.WebSocket),
                  "ws gate: with an id the refusal is a refused receipt");
            await Send(ws, """{"cmd":"next","args":[]}""");
            var ran = false;
            for (var i = 0; i < 100 && !ran; i++) { ran = Count(ShowCommandKind.Next) == 1; if (!ran) await Task.Delay(20); }
            Check(ran && Count(ShowCommandKind.Playout) == 0 && Count(ShowCommandKind.Patterns) == 1, "ws gate: other verbs run; nothing refused ran");
        }
        // A control page let in by a control key with no PIN set proved that key.
        using (var ws = new ClientWebSocket())
        {
            ws.Options.SetRequestHeader("Authorization", "Bearer " + ctlSecret);
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?role=control"), CancellationToken.None);
            await Send(ws, """{"cmd":"playout","args":["stage-left","pause"],"id":"w0"}""");
            var keyed = await Next(ws, "\"type\":\"receipt\"");
            Check(keyed is not null && JsonNode.Parse(keyed) is { } kr && (string?)kr["id"] == "w0" && (bool?)kr["ok"] == true && Count(ShowCommandKind.Playout) == 1,
                  "ws gate: no PIN set, a control page let in by a control key drives playout from another machine");
        }

        // A window grant is a person's act (Show Bus §12.4): over HTTP and a WebSocket it passes on the PIN or a control key alone, never
        // on an address — a listed one included.
        var grantPath = "/api/window/grant/lobby/9f8e7d6c";
        var noKey = await http.SendAsync(Post(grantPath));
        Check(noKey.StatusCode == HttpStatusCode.Forbidden && (string?)JsonNode.Parse(await noKey.Content.ReadAsStringAsync())!["error"] == PlayoutGate.GrantWords(CommandDoor.Http)
              && Count(ShowCommandKind.Window) == 0,
              "http gate: no PIN set, a window grant with no key is refused 403 in words, and nothing runs");
        control.From = [IPAddress.Loopback];
        var listedGrant = await http.SendAsync(Post(grantPath));
        Check(listedGrant.StatusCode == HttpStatusCode.Forbidden && (string?)JsonNode.Parse(await listedGrant.Content.ReadAsStringAsync())!["error"] == PlayoutGate.GrantWords(CommandDoor.Http)
              && Count(ShowCommandKind.Window) == 0,
              "http gate: a listed address vouches for no person: its window grant with no key is refused");
        control.From = [];
        Check((await http.SendAsync(Post(grantPath, ctlSecret))).StatusCode == HttpStatusCode.OK && Count(ShowCommandKind.Window) == 1,
              "http gate: a control key makes a window grant");
        using (var ws = new ClientWebSocket())
        {
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?role=control"), CancellationToken.None);
            await Send(ws, """{"cmd":"window","args":["grant","lobby","9f8e7d6c"],"id":"wg1"}""");
            var wsGrant = await Next(ws, "\"type\":\"receipt\"");
            Check(wsGrant is not null && JsonNode.Parse(wsGrant) is { } gr && (string?)gr["id"] == "wg1" && (bool?)gr["ok"] == false
                  && (string?)gr["error"] == PlayoutGate.GrantWords(CommandDoor.WebSocket) && Count(ShowCommandKind.Window) == 1,
                  "ws gate: no PIN set, a control page with no key is refused a window grant in words");
        }
        using (var ws = new ClientWebSocket())
        {
            ws.Options.SetRequestHeader("Authorization", "Bearer " + ctlSecret);
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?role=control"), CancellationToken.None);
            await Send(ws, """{"cmd":"window","args":["grant","lobby","9f8e7d6c"],"id":"wg2"}""");
            var wsGrant = await Next(ws, "\"type\":\"receipt\"");
            Check(wsGrant is not null && JsonNode.Parse(wsGrant) is { } gr && (string?)gr["id"] == "wg2" && (bool?)gr["ok"] == true && Count(ShowCommandKind.Window) == 2,
                  "ws gate: a control page let in by a control key makes a window grant");
        }

        // A listed address passes every door with nothing proved.
        control.From = [IPAddress.Loopback];
        Check((await http.SendAsync(Post("/api/playout/stage-left/pause"))).StatusCode == HttpStatusCode.OK && Count(ShowCommandKind.Playout) == 2,
              "http gate: a listed address drives playout with no key");
        control.From = [];

        // With a PIN set, the PIN given right is the credential; a missing one is refused at the door as before.
        pin = "4321";
        Check((await http.SendAsync(Post("/api/patterns/pause"))).StatusCode == HttpStatusCode.Unauthorized, "http gate: with a PIN set, no PIN is refused at the door");
        Check((await http.SendAsync(Post("/api/patterns/pause?pin=4321"))).StatusCode == HttpStatusCode.OK && Count(ShowCommandKind.Patterns) == 2,
              "http gate: the PIN asked for and given right drives patterns from another machine");
        using (var ws = new ClientWebSocket())
        {
            ws.Options.SetRequestHeader("Authorization", "Bearer 4321");
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?role=control"), CancellationToken.None);
            await Send(ws, """{"cmd":"playout","args":["stage-left","pause"],"id":"w2"}""");
            var wsOk = await Next(ws, "\"type\":\"receipt\"");
            Check(wsOk is not null && JsonNode.Parse(wsOk) is { } ok && (string?)ok["id"] == "w2" && (bool?)ok["ok"] == true && Count(ShowCommandKind.Playout) == 3,
                  "ws gate: a control page let in by the PIN drives playout");
        }
    }

    /// <summary>
    /// This PC by the default seam (loopback): a program here drives another node's air with nothing proved, but a page of another site
    /// in a browser here, a name rebound to 127.0.0.1 or a forwarder's port does not; and a surface with nobody to approve an agent's
    /// line refuses it as an error, never as queued.
    /// </summary>
    static async Task WebThisPc(Action<bool, string> Check)
    {
        var control = new PlainControl();
        var port = FreeTcpPort();
        await using var web = new WebServer(control, () => null);
        web.Start(port, bindAll: false);
        control.Snapshots.Publish(new ShowSnapshot { Text = "05:00", Seconds = 300 });
        control.Snapshots.MarkAlive();
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var (ctlKey, ctlSecret) = ApiKeys.Create("Deck", ApiKeyScope.Control, DateTime.UtcNow);
        control.Keys = [ctlKey];
        int Count(ShowCommandKind kind) { lock (control.Commands) return control.Commands.Count(c => c.Kind == kind); }
        async Task<(HttpStatusCode Status, string? Error)> Get(string path, string? host = null, string? origin = null, string? site = null, string? bearer = null)
        {
            using var m = new HttpRequestMessage(HttpMethod.Get, path);
            if (host is not null) m.Headers.Host = host;
            if (origin is not null) m.Headers.TryAddWithoutValidation("Origin", origin);
            if (site is not null) m.Headers.TryAddWithoutValidation("Sec-Fetch-Site", site);
            if (bearer is not null) m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using var r = await http.SendAsync(m);
            var body = await r.Content.ReadAsStringAsync();
            return (r.StatusCode, r.IsSuccessStatusCode ? null : (string?)JsonNode.Parse(body)?["error"]);
        }
        var page = PlayoutGate.OtherSiteWords(ShowCommandKind.Patterns, CommandDoor.Http);
        var machine = PlayoutGate.Words(ShowCommandKind.Patterns, CommandDoor.Http);

        Check(await Get("/api/patterns/pause") is (HttpStatusCode.OK, null) && Count(ShowCommandKind.Patterns) == 1,
              "this pc: a program here drives patterns with no PIN set and nothing proved");
        Check(await Get("/api/patterns/pause", host: $"localhost:{port}", origin: $"http://localhost:{port}", site: "same-origin") is (HttpStatusCode.OK, null)
              && Count(ShowCommandKind.Patterns) == 2,
              "this pc: Stagewatch's own page by localhost drives patterns");
        Check(await Get("/api/patterns/pause", site: "cross-site") == (HttpStatusCode.Forbidden, page) && Count(ShowCommandKind.Patterns) == 2,
              "this pc: an img of another site in a browser here is refused, and nothing runs");
        Check(await Get("/api/patterns/pause", origin: "http://evil.example") == (HttpStatusCode.Forbidden, page) && Count(ShowCommandKind.Patterns) == 2,
              "this pc: a script of another origin in a browser here is refused");
        Check(await Get("/api/patterns/pause", host: $"evil.example:{port}", origin: $"http://evil.example:{port}", site: "same-origin")
              == (HttpStatusCode.Forbidden, machine) && Count(ShowCommandKind.Patterns) == 2,
              "this pc: a name rebound to 127.0.0.1 is another machine's, however same-origin its page is");
        Check(await Get("/api/patterns/pause", host: "127.0.0.1:8080") == (HttpStatusCode.Forbidden, machine) && Count(ShowCommandKind.Patterns) == 2,
              "this pc: a forwarder's port over loopback is another machine's");
        Check(await Get("/api/start", site: "cross-site") is (HttpStatusCode.OK, null) && Count(ShowCommandKind.Start) == 1,
              "this pc: every other verb keeps its rules (the open door), another site's page included");
        control.From = [IPAddress.Loopback];
        Check(await Get("/api/patterns/pause", site: "cross-site") == (HttpStatusCode.Forbidden, page) && Count(ShowCommandKind.Patterns) == 2,
              "this pc: a listed address vouches for no page of another site");
        control.From = [];
        Check(await Get("/api/patterns/pause", site: "cross-site", bearer: ctlSecret) is (HttpStatusCode.OK, null) && Count(ShowCommandKind.Patterns) == 3,
              "this pc: another site's page with a control key passes on the key");
        // A program on this PC drives another node's air with nothing proved, but a window grant is a person's act (Show Bus §12.4):
        // this PC authenticates nobody, so only the PIN or a control key makes one.
        Check(await Get("/api/window/grant/lobby/9f8e7d6c") == (HttpStatusCode.Forbidden, PlayoutGate.GrantWords(CommandDoor.Http)) && Count(ShowCommandKind.Window) == 0,
              "this pc: a program here with nothing proved is refused a window grant in words, and nothing runs");
        Check(await Get("/api/window/grant/lobby/9f8e7d6c", bearer: ctlSecret) is (HttpStatusCode.OK, null) && Count(ShowCommandKind.Window) == 1,
              "this pc: with a control key it makes the grant");

        // A WebSocket's upgrade says whose page it is, for every line it sends.
        using (var ws = new ClientWebSocket())
        {
            ws.Options.SetRequestHeader("Origin", "http://evil.example");
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?role=control"), CancellationToken.None);
            await Send(ws, """{"cmd":"playout","args":["stage-left","pause"],"id":"x1"}""");
            var receipt = await Next(ws, "\"type\":\"receipt\"");
            Check(receipt is not null && JsonNode.Parse(receipt) is { } rr && (bool?)rr["ok"] == false
                  && (string?)rr["error"] == PlayoutGate.OtherSiteWords(ShowCommandKind.Playout, CommandDoor.WebSocket) && Count(ShowCommandKind.Playout) == 0,
                  "this pc: a control page of another site in a browser here is refused playout on its socket");
        }
        using (var ws = new ClientWebSocket())
        {
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?role=control"), CancellationToken.None);
            await Send(ws, """{"cmd":"playout","args":["stage-left","pause"],"id":"x2"}""");
            var receipt = await Next(ws, "\"type\":\"receipt\"");
            Check(receipt is not null && JsonNode.Parse(receipt) is { } rr && (bool?)rr["ok"] == true && Count(ShowCommandKind.Playout) == 1,
                  "this pc: a control socket a program here opened drives playout");
        }

        // MCP on a surface that keeps IShowAgents' defaults: nobody approves, so the line is refused as an error and nothing runs.
        async Task<JsonNode> Mcp(string line, string? origin = null)
        {
            var body = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 9, method = "tools/call", @params = new { name = "run_command", arguments = new { command = line, reason = "test" } } });
            using var m = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (origin is not null) m.Headers.TryAddWithoutValidation("Origin", origin);
            using var r = await http.SendAsync(m);
            return JsonNode.Parse(await r.Content.ReadAsStringAsync())!["result"]!;
        }
        static string Text(JsonNode result) => (string?)result["content"]![0]!["text"] ?? "";
        var nobody = await Mcp("playout stage-left go show intro");
        Check((bool?)nobody["isError"] == true && Text(nobody) == IShowAgents.NobodyApproves && Count(ShowCommandKind.Playout) == 1,
              "this pc: an agent's playout line where nobody approves is an error, never read as queued, and nothing runs");
        var direct = await Mcp("nudge 30");
        Check((bool?)direct["isError"] == false && Count(ShowCommandKind.Nudge) == 1, "this pc: any other agent line runs directly on such a surface, as before");
        // A certificate is trusted by a person on the desk, never by a line: refused on a socket and to an agent this PC runs, and
        // neither is a proposal.
        using (var ws = new ClientWebSocket())
        {
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?role=control"), CancellationToken.None);
            await Send(ws, """{"cmd":"playout","args":["stage-left","trust","03e9b152"],"id":"x3"}""");
            var receipt = await Next(ws, "\"type\":\"receipt\"");
            Check(receipt is not null && JsonNode.Parse(receipt) is { } rr && (bool?)rr["ok"] == false
                  && (string?)rr["error"] == PlayoutGate.DeskOnlyWords(CommandDoor.WebSocket) && Count(ShowCommandKind.PlayoutTrust) == 0,
                  "this pc: playout trust on a control socket a program here opened is refused in words, and nothing runs");
        }
        var trust = await Mcp("playout stage-left trust 03e9 b152");
        Check((bool?)trust["isError"] == true && Text(trust) == "Refused: " + PlayoutGate.DeskOnlyWords(CommandDoor.Mcp) && Count(ShowCommandKind.PlayoutTrust) == 0,
              $"this pc: an agent's playout trust is refused at the door, never a proposal ({Text(trust)})");
        var fromPage = await Mcp("patterns pause", origin: "http://evil.example");
        Check((bool?)fromPage["isError"] == true && Text(fromPage) == "Refused: " + PlayoutGate.OtherSiteWords(ShowCommandKind.Patterns, CommandDoor.Mcp)
              && Count(ShowCommandKind.Patterns) == 3,
              "this pc: an agent call from another site's page is refused at the door");
    }

    static Task Send(ClientWebSocket ws, string json) => ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

    /// <summary>The next message holding <paramref name="marker"/>, frames and beats passed over; null when none comes.</summary>
    static async Task<string?> Next(ClientWebSocket ws, string marker)
    {
        var buf = new byte[65536];
        for (var i = 0; i < 40; i++)
        {
            using var cts = new CancellationTokenSource(3000);
            WebSocketReceiveResult r;
            try { r = await ws.ReceiveAsync(buf, cts.Token); }
            catch (OperationCanceledException) { return null; }
            if (r.MessageType == WebSocketMessageType.Close) return null;
            var text = Encoding.UTF8.GetString(buf, 0, r.Count);
            if (text.Contains(marker, StringComparison.Ordinal)) return text;
        }
        return null;
    }

    static int FreeTcpPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p;
    }

    static int FreeUdpPort()
    {
        using var u = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)u.Client.LocalEndPoint!).Port;
    }

    sealed class Access : IShowAccess
    {
        public volatile IReadOnlyList<IPAddress> From = [];
        public IReadOnlyList<IPAddress> PlayoutFrom => From;
    }

    /// <summary>A surface whose agents act directly (no approval asked) unless a line must wait for a person.</summary>
    sealed class GateControl : IShowControl
    {
        public SnapshotHub Snapshots { get; } = new();
        public List<ShowCommand> Commands { get; } = new();
        public void Execute(ShowCommand command) { lock (Commands) Commands.Add(command); }
        public RemoteConfig GetRemoteConfig() => new([], [], []);
        public volatile IReadOnlyList<ApiKey> Keys = [];
        public IReadOnlyList<ApiKey> ApiKeys => Keys;
        public volatile IReadOnlyList<IPAddress> From = [];
        public IReadOnlyList<IPAddress> PlayoutFrom => From;
        private int _proposed;
        public int ProposedCount => Volatile.Read(ref _proposed);
        public volatile bool LastMust;
        public volatile bool IgnoreMust;
        public AgentReply? ProposeFromAgent(string commandLine, string reason, bool mustPropose)
        {
            if (!mustPropose || IgnoreMust) return null;
            LastMust = true;
            return AgentReply.Waiting($"Queued for the operator's approval (proposal #{Interlocked.Increment(ref _proposed)}).");
        }
        private int _toApps;
        public int ToApps => Volatile.Read(ref _toApps);
        public AgentReply ProposeToApp(string app, string line, string reason)
            => line == "UNLISTED" ? AgentReply.Refused("Refused: PlayMap Hall does not list that line — see its verbs")
                : AgentReply.Waiting($"Queued for the operator's approval as p{Interlocked.Increment(ref _toApps)}-k3m9 (risk review).");
    }

    /// <summary>A surface that keeps every IShowAgents default (as the rig does): nobody here approves an agent's line.</summary>
    sealed class PlainControl : IShowControl
    {
        public SnapshotHub Snapshots { get; } = new();
        public List<ShowCommand> Commands { get; } = new();
        public void Execute(ShowCommand command) { lock (Commands) Commands.Add(command); }
        public RemoteConfig GetRemoteConfig() => new([], [], []);
        public volatile IReadOnlyList<ApiKey> Keys = [];
        public IReadOnlyList<ApiKey> ApiKeys => Keys;
        public volatile IReadOnlyList<IPAddress> From = [];
        public IReadOnlyList<IPAddress> PlayoutFrom => From;
    }
}
