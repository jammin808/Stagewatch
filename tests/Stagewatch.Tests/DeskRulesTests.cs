using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Remote;
using Stagewatch.Show;

/// <summary>
/// The desk's rules (Show Bus §11), pure and on fixed time: ids carry the desk's run, and an id of an earlier run is refused in words;
/// a line is refused, never cut — over proposal.line.chars, with a control character, an invisible format character, half of a surrogate
/// pair, a key, a window's address or a filled secret placeholder, even one only an unproved table names; a from of the desk's own (its
/// id, kind main or mcp) is refused, a target naming the desk's id is the desk, and a person's verb proposed to the desk is refused; a target
/// raises the risk of its own proposal under its bound key, never lowers it; the desk records each target's last collect and neither
/// approves nor sends a line for one that has not asked within proposal.collect.ms; a person's line is born approved with a role; a
/// proposal's JSON ends with when it was collected and the form that rated it; Stagewatch's own commands rows are §6's notation, every
/// form a line its own parser takes at a risk no lower than its own; and the lines the other apps document parse and rate as they
/// say (fixtures/peer-lines.json, at the pins tools/interop/siblings.json holds).
/// </summary>
static class DeskRulesTests
{
    static readonly DateTime T0 = new(2026, 10, 28, 19, 0, 0, DateTimeKind.Utc);
    const string DeskId = "desk01";

    public static void Run(Action<bool, string> Check)
    {
        Ids(Check);
        Fence(Check);
        DeskOwn(Check);
        UnprovedSecret(Check);
        SelfRaise(Check);
        LastCollect(Check);
        PersonLines(Check);
        Json(Check);
        OwnRows(Check);
        PeerLines(Check);
    }

    static ProposalDesk Desk(NodeBindings nodes, string? run = null) => new(() => DeskId, nodes, run);

    static string Body(string fromKind, string fromId, string targetKind, string targetId, string line, string? risk = null, string targetName = "")
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["from"] = new { t = fromKind, id = fromId, name = "" },
            ["target"] = new { t = targetKind, id = targetId, name = targetName },
            ["line"] = line,
            ["reason"] = "",
            ["risk"] = risk,
        });

    static RiskVerdict Listed(string risk, string form = "x") => new(risk, form, "words of " + form, true, false);

    /// <summary>Ids p&lt;n&gt;-&lt;run&gt;: unique across a desk's runs, p&lt;n&gt; resolving within this run, another run's refused in words.</summary>
    static void Ids(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var first = Desk(nodes, "k3m9");
        var a = first.Submit(Body("patterns", "pat1", "main", "", "note one"), NodeCredential.Key("kP"), (_, _) => "low", T0).Proposal;
        var b = first.Submit(Body("patterns", "pat1", "main", "", "note two"), NodeCredential.Key("kP"), (_, _) => "low", T0).Proposal;
        Check(a is { Id: "p1-k3m9" } && b is { Id: "p2-k3m9" } && first.Run == "k3m9", $"desk ids: each id is p<n> and the desk's run ({a?.Id}, {b?.Id})");
        Check(first.Get("p1")?.Id == "p1-k3m9" && first.Resolve("p2") == "p2-k3m9" && first.Get("p1-k3m9") == a && first.Get("p3") is null && first.Resolve("p3") is null,
            "desk ids: p<n> alone names this run's proposal, the whole id names it too, and an id it never made names none");
        var second = Desk(nodes, "q7zx");
        var c = second.Submit(Body("patterns", "pat1", "main", "", "note three"), NodeCredential.Key("kP"), (_, _) => "low", T0).Proposal;
        Check(c is { Id: "p1-q7zx" } && c.Id != a!.Id && second.Get("p1-k3m9") is null
              && second.Unknown("p1-k3m9") == "no such proposal — this desk has restarted since p1-k3m9 was made (it runs as q7zx now)"
              && second.Unknown("p9") == "no such proposal" && second.Unknown("x1-k3m9") == "no such proposal",
            "desk ids: a desk started again numbers from p1 under a new run, and refuses an earlier run's id in words that say it restarted");
        Check(second.Result("p1-k3m9", true, "done", NodeCredential.Key("kP"), T0) == second.Unknown("p1-k3m9")
              && second.Approve("p1-k3m9", "operator", T0) == (null, second.Unknown("p1-k3m9")),
            "desk ids: a result or an approval for an earlier run's id is refused with the same words, nothing taken");
        var runs = Enumerable.Range(0, 64).Select(_ => ProposalDesk.NewRun()).ToList();
        Check(runs.All(ProposalDesk.ValidRun) && runs.Distinct(StringComparer.Ordinal).Count() > 1 && runs.All(r => r.Length == ProposalDesk.RunChars)
              && new ProposalDesk(() => DeskId, nodes).Run is { Length: ProposalDesk.RunChars } drawn && ProposalDesk.ValidRun(drawn),
            "desk ids: a run is 4 base-32 symbols drawn from the platform's random numbers");
        var refused = false;
        try { _ = Desk(nodes, "K3M9"); }
        catch (ArgumentException) { refused = true; }
        Check(refused && !ProposalDesk.ValidRun("k3m") && !ProposalDesk.ValidRun("k3m0"), "desk ids: a run of another shape is never taken");
        Check(first.NextPending() == a, "desk ids: the next pending proposal is the oldest, the first made at one instant");
        first.Approve(a!.Id, "operator", T0);
        Check(first.NextPending() == b, "desk ids: once it is decided, the next is the one after it");
    }

    /// <summary>§11's line rules: refused, never cut, with words, before anything is queued or any key bound.</summary>
    static void Fence(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var windowToken = ScreenWindows.NewToken();
        var desk = new ProposalDesk(() => DeskId, nodes, "k3m9") { WindowTokens = () => [windowToken] };
        (SharedProposal? P, string? Error) Propose(string line, string key = "kP")
        {
            var (p, error, _) = desk.Submit(Body("patterns", "pat1", "patterns", "pat1", line), NodeCredential.Key(key), (_, _) => "review", T0);
            return (p, error);
        }
        var longest = "note " + new string('x', ProposalDesk.MaxLine - 5);
        Check(ProposalDesk.MaxLine == 2048 && Propose(longest) is ({ } kept, null) && kept.Line == longest,
            "desk fence: a line of 2048 characters is taken whole");
        Check(Propose(longest + "y", "kQ") == (null, LineFence.TooLong) && nodes.NodeOf("kQ") is null,
            "desk fence: a line of 2049 characters is refused, never cut, and binds no key");
        string[] controls = ["note a\tb", "note a\nb", "note a\u0007b", "note a\u0085b"];
        Check(controls.All(l => Propose(l, "kQ") == (null, LineFence.Control)) && nodes.NodeOf("kQ") is null,
            "desk fence: a line with a control character is refused, never cleaned");
        // What a person reads differs from what runs: words drawn in another order, a word split by nothing visible, words only a machine reads.
        string[] hidden = ["note a\u202Eb", "note \u2066abc\u2069", "sig\u200Bnal go all", "\uFEFFnote hi", "note a\u00ADb", "note \U000E0041\U000E0042", "note a\u2060b"];
        Check(hidden.All(l => Propose(l, "kQ") == (null, LineFence.Format)) && nodes.NodeOf("kQ") is null,
            "desk fence: an invisible format character — a direction override or isolate, a zero-width space, a byte-order mark, a soft hyphen, tag characters, a word joiner — is refused");
        // A JSON writer sends half a pair as U+FFFD (or as the escape below); a line given as text, as a person's line is, can hold one.
        string[] halves = ["note a\uD800b", "note a\uDC00", "note \uD83D", "note \uDE00\uD83D"];
        Check(halves.All(l => LineFence.Check(l) == LineFence.Surrogate), "desk fence: half of a surrogate pair is refused");
        string[] joined = ["message \"\U0001F469\u200D\U0001F4BB ready\"", "note \u0645\u06CC\u200C\u062E\u0648\u0627\u0647\u0645", "note \U0001F44D\uFE0F"];
        Check(joined.All(l => Propose(l) is ({ }, null)),
            "desk fence: the zero-width joiner and non-joiner, which emoji and scripts need, a variation selector and a whole surrogate pair pass");
        var escaped = "{\"from\":{\"t\":\"patterns\",\"id\":\"pat1\"},\"target\":{\"t\":\"patterns\",\"id\":\"pat1\"},\"line\":\"note a\\ud800b\"}";
        var halfName = "{\"from\":{\"t\":\"patterns\",\"id\":\"pat1\",\"name\":\"FOH\\udc00\"},\"target\":{\"t\":\"patterns\",\"id\":\"pat1\"},\"line\":\"note hi\",\"reason\":\"\\ud800\"}";
        Check(desk.Submit(escaped, NodeCredential.Key("kQ"), (_, _) => "review", T0) is (null, LineFence.Surrogate, false) && nodes.NodeOf("kQ") is null
              && ProposalDesk.FromOf(halfName) is ({ Name: "" }, null)
              && desk.Submit(halfName, NodeCredential.Key("kP"), (_, _) => "review", T0) is ({ Reason: "", From.Name: "" }, null, false),
            "desk fence: a line sent as an escape of half a surrogate pair is refused in words, never thrown; free words of one are left out");
        var shown = desk.Submit(Body("patterns", "pat1", "patterns", "pat1", "note hi", targetName: "Hall\u202E B"), NodeCredential.Key("kP"), (_, _) => "review", T0).Proposal;
        var reasoned = desk.Submit(JsonSerializer.Serialize(new { from = new { t = "patterns", id = "pat1", name = "FOH\u202E ok" }, target = new { t = "patterns", id = "pat1" },
            line = "note hi", reason = "\u2066because\u2069 it\u200B is late\t" }), NodeCredential.Key("kP"), (_, _) => "review", T0).Proposal;
        Check(shown is { Target.Name: "Hall B" } && reasoned is { From.Name: "FOH ok", Reason: "because it is late" },
            "desk fence: free words beside a line — a name, a reason — keep no control or invisible format character");
        foreach (var odd in new[] { "pat1\u200B", "pat\t1", "pat\u202E1" })
            Check(desk.Submit(JsonSerializer.Serialize(new { from = new { t = "patterns", id = odd }, target = new { t = "patterns", id = "pat1" }, line = "note hi" }),
                    NodeCredential.Key("kP"), (_, _) => "review", T0) is (null, ProposalDesk.NoFrom, false)
                  && desk.Submit(JsonSerializer.Serialize(new { from = new { t = "patterns", id = "pat1" }, target = new { t = "patterns", id = odd }, line = "note hi" }),
                    NodeCredential.Key("kP"), (_, _) => "review", T0) is (null, "target.t is required", false),
                $"desk fence: an id holding a character no id has ({odd.Length} chars) names no node — never one with that character left out");
        string[] keys =
        [
            "LINK STAGEWATCH h swk_abcdefghijkmnpqrstuvwxyz23456789", "STAGE MESSAGE \"ptk_3vq8xk2m9tz7w4hc6r5n2b8d7f4g9j3k\"",
            "note zzkey_abcdefghijkmnpqrstuvwxyz23456789-and-more", "LINK STAGEWATCH h swk_x", "note agn_Zq-8Tm_0aQ", "note (pmk_short)",
        ];
        Check(keys.All(l => Propose(l, "kQ") == (null, LineFence.Key)) && nodes.NodeOf("kQ") is null,
            "desk fence: a key is refused wherever it stands — 13.5.1's shape under any prefix, quoted or inside a word, and any word an app's key prefix opens");
        string[] notKeys = ["note use the swk_ key", "note risk_assessment", "note my_var_abc", "note swk", "note xswk_abc"];
        Check(notKeys.All(l => Propose(l) is ({ }, null)), "desk fence: a key's prefix alone, or snake_case words, are no key");
        Check(Propose("window show lobby http://10.0.0.6:9924/w/" + ScreenWindows.NewToken(), "kQ") == (null, LineFence.Window)
              && Propose("note " + windowToken, "kQ") == (null, LineFence.Window) && nodes.NodeOf("kQ") is null,
            "desk fence: a window's address, or a token one of this desk's windows holds now, is refused");
        Check(Propose("note see /w/short") is ({ }, null), "desk fence: /w/ followed by no window's token is no address");
        var table = VerbTable.Compile([new PeerCommand("LINK STAGEWATCH <host[:port]>|\"\"|OFF [<key>]", "review")]);
        var secret = desk.Submit(Body("playmap", "pm1", "playmap", "pm1", "LINK STAGEWATCH h hunter2"), NodeCredential.Key("kM"), (_, l) => table.Rate(l), T0);
        var marker = desk.Submit(Body("playmap", "pm1", "playmap", "pm1", "LINK STAGEWATCH h ‹secret›"), NodeCredential.Key("kM"), (_, l) => table.Rate(l), T0);
        Check(secret is (null, { } words, false) && words == LineFence.SecretWords("playmap pm1") && marker is ({ Risk: "review", Form: { } }, null, false),
            "desk fence: a filled secret placeholder is refused in words that send the secret to its node; ‹secret› in its place is taken");
        Check(LineFence.Check("note fine") is null && LineFence.Check(new string('x', LineFence.MaxChars)) is null,
            "desk fence: a plain line within its length passes the fence");
    }

    /// <summary>A from of this desk's own is refused (403), and a person's verb is never a proposal to this desk.</summary>
    static void DeskOwn(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var desk = Desk(nodes, "k3m9");
        foreach (var kind in new[] { "main", "mcp", "MCP" })
            Check(desk.Submit(Body(kind, "x9", "main", "", "note hi"), NodeCredential.Operator, (_, _) => "low", T0) is (null, { } words, true)
                  && words == ProposalDesk.DeskOwn(kind),
                $"desk own: a from of kind {kind} is this desk's own, refused from the network (403)");
        Check(desk.Submit(Body("patterns", DeskId, "main", "", "note hi"), NodeCredential.Key("kD"), (_, _) => "low", T0) is (null, _, true)
              && nodes.NodeOf("kD") is null && desk.All().Count == 0,
            "desk own: a from with the desk's own id is refused, binding no key");
        foreach (var line in new[] { "peer playmap \"PLAYLIST CUE show #3\"", "DESK approve p1" })
            Check(desk.Submit(Body("patterns", "pat1", "main", "", line), NodeCredential.Key("kP"), (_, _) => "high", T0) is (null, { } words, false)
                  && words.EndsWith("is a person's at this desk — never a proposal", StringComparison.Ordinal),
                $"desk own: '{line}' is a person's verb at this desk, never a proposal to it");
        Check(desk.Submit(Body("patterns", "pat1", "patterns", "pat1", "desk approve p1"), NodeCredential.Key("kP"), (_, _) => "high", T0) is ({ }, null, false),
            "desk own: the same words to another node are that node's line, rated by its table");
        // §11: the desk's own id names the desk, whatever kind the proposer gives it.
        var rated = new List<NodeRef>();
        var mine = desk.Submit(Body("patterns", "pat1", "patterns", DeskId, "note hi", targetName: "Hall A"), NodeCredential.Key("kP"),
            (t, _) => { rated.Add(t); return "low"; }, T0).Proposal;
        Check(mine is { TargetIsDesk: true, Target: { Kind: "main", Id: DeskId, Name: "Hall A" } } && rated is [{ Kind: "main", Id: DeskId }],
            "desk own: a target naming the desk's own id under another kind is this desk, named as it is, and rated by the desk's own rules");
        Check(desk.Submit(Body("patterns", "pat1", "playmap", DeskId, "peer playmap \"OUTPUTS OFF\""), NodeCredential.Key("kP"), (_, _) => "high", T0) is (null, { } peer, false)
              && peer.EndsWith("is a person's at this desk — never a proposal", StringComparison.Ordinal),
            "desk own: a person's verb to the desk's own id under another kind is refused as one to the desk");
        Check(desk.Approve(mine!.Id, "operator", T0) is ({ State: SharedProposalState.Approved, TargetIsDesk: true }, null)
              && desk.Collect("pat1", NodeCredential.Key("kP"), T0) is ({ } items, null) && items.All(x => x.Id != mine.Id),
            "desk own: a line for the desk is approved without waiting for a collect, and no other node collects it");
    }

    /// <summary>
    /// A filled secret placeholder is refused even when only a table no proof stands behind names the slot — posted with no
    /// credential, of another kind, or pulled — while such a table still lowers no risk; a verified table's verdict stands.
    /// </summary>
    static void UnprovedSecret(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var peers = new PeerContexts(nodes);
        static string Context(string kind, string id) => JsonSerializer.Serialize(new
        {
            v = 1,
            node = new { t = kind, id, name = "Side" },
            commands = new object[] { new { verb = "LINK STAGEWATCH <host[:port]>|\"\"|OFF [<key>]", risk = "low" }, new { verb = "STATUS", risk = "low" } },
        });
        Check(peers.Accept(Context("playmap", "pm7"), "10.0.0.7", T0, DeskId, NodeCredential.None) == (200, null, NodeProof.None),
            "unproved secret: a context posted with no credential is held, proving nothing");
        var pm = new NodeRef("playmap", "pm7", "PlayMap Side");
        Check(peers.VerdictFor(pm, "LINK STAGEWATCH h hunter2", T0) is { Secret: true, Listed: false, Risk: "high" }
              && peers.VerdictFor(pm, "LINK STAGEWATCH h ‹secret›", T0) == RiskVerdict.Unlisted && peers.VerdictFor(pm, "STATUS", T0) == RiskVerdict.Unlisted,
            "unproved secret: its table finds a filled secret, and lowers nothing — a line it lists is still high and unlisted");
        Check(peers.VerdictFor(pm with { Kind = "patterns" }, "LINK STAGEWATCH h hunter2", T0) is { Secret: true }
              && peers.VerdictFor(new NodeRef("playmap", "pm8", ""), "LINK STAGEWATCH h hunter2", T0) is { Secret: false },
            "unproved secret: a table held for the id refuses whatever kind the proposer names; another id's table counts for nothing");
        var pulled = PeerContexts.Parse(Context("playmap", "pm9"), "10.0.0.9", T0)!;
        var pm9 = new NodeRef("playmap", "pm9", "");
        Check(peers.VerdictFor(pm9, "STATUS", T0, pulled) is { Listed: true, Risk: "low" } && peers.VerdictFor(pm9, "LINK STAGEWATCH h hunter2", T0, pulled) is { Secret: true, Listed: true }
              && peers.VerdictFor(pm9, "LINK STAGEWATCH h hunter2", T0) == RiskVerdict.Unlisted,
            "unproved secret: a pulled table that proves something rates every line itself; without it the id has no table");
        nodes.Speak(NodeCredential.Key("k9"), "playmap", "pm9", T0);
        Check(peers.VerdictFor(pm9, "LINK STAGEWATCH h hunter2", T0, pulled) is { Secret: true, Listed: false, Risk: "high" }
              && peers.VerdictFor(pm9, "STATUS", T0, pulled) == RiskVerdict.Unlisted,
            "unproved secret: a pulled table that proves nothing once a key speaks for the id still finds a filled secret, and lowers nothing");
        var desk = Desk(nodes, "k3m9");
        Check(desk.Submit(Body("patterns", "pat1", "playmap", "pm7", "LINK STAGEWATCH h hunter2"), NodeCredential.Key("kP"),
                  (t, l) => peers.VerdictFor(t, l, T0), T0) is (null, { } words, false) && words == LineFence.SecretWords("playmap pm7")
              && nodes.NodeOf("kP") is null && desk.All().Count == 0,
            "unproved secret: the desk refuses the line in words that send the secret to its node, binding no key and queuing nothing");
        Check(peers.Accept(Context("playmap", "pm7"), "10.0.0.7", T0, DeskId, NodeCredential.Key("kM")) == (200, null, NodeProof.Key)
              && peers.VerdictFor(pm, "STATUS", T0) is { Listed: true, Risk: "low" } && peers.VerdictFor(pm, "LINK STAGEWATCH h hunter2", T0) is { Secret: true, Listed: true },
            "unproved secret: once its key posts it, the table is verified and rates every line itself");
    }

    /// <summary>A target raises the risk of its own proposal under the key bound to its id, never lowers it.</summary>
    static void SelfRaise(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var desk = Desk(nodes, "k3m9");
        string RiskOf(string fromId, string targetId, string stated, string table, NodeCredential credential)
            => desk.Submit(Body("patterns", fromId, "patterns", targetId, "CUE GO", stated), credential, (_, _) => table, T0).Proposal?.Risk ?? "refused";
        Check(RiskOf("pat1", "pat1", "high", "low", NodeCredential.Key("kP")) == "high" && RiskOf("pat1", "pat1", "REVIEW", "low", NodeCredential.Key("kP")) == "review",
            "desk risk: a target raises its own proposal's risk under its bound key");
        Check(RiskOf("pat1", "pat1", "low", "high", NodeCredential.Key("kP")) == "high" && RiskOf("pat1", "pat1", "none", "review", NodeCredential.Key("kP")) == "review",
            "desk risk: a target never lowers its own proposal's risk, and a word that is no risk states nothing");
        Check(RiskOf("pat2", "pat2", "high", "low", NodeCredential.Operator) == "low" && RiskOf("pat1", "pm9", "high", "low", NodeCredential.Key("kP")) == "low",
            "desk risk: a risk stated by another proposer, or without the target's own key, counts for nothing");
    }

    /// <summary>The desk records each target's last collect and approves nothing for one that has not asked within proposal.collect.ms.</summary>
    static void LastCollect(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var desk = Desk(nodes, "k3m9");
        var p = desk.Submit(Body("patterns", "pat1", "playmap", "pm1", "PLAYLIST GO show", null), NodeCredential.Key("kP"), (_, _) => "high", T0).Proposal!;
        Check(desk.LastCollect("pm1") is null && desk.Approve(p.Id, "operator", T0) is (null, { } never)
              && never == "playmap pm1 has not asked this desk for approved lines since this desk started — it would not collect this"
              && desk.Get(p.Id) is { State: SharedProposalState.Pending },
            "desk collect: a proposal for a target that has never asked stays pending, refused in words");
        Check(desk.Collect("pm1", NodeCredential.Key("kM"), T0) is ({ Count: 0 }, null) && desk.LastCollect("pm1") == T0,
            "desk collect: a target's ask is recorded as its last collect, whatever it collects");
        Check(desk.Collect("pm1", NodeCredential.Key("kWrong"), T0.AddSeconds(1)) is (null, { }) && desk.LastCollect("pm1") == T0,
            "desk collect: a refused collect records nothing");
        Check(desk.Approve(p.Id, "operator", T0.AddSeconds(15)) is ({ State: SharedProposalState.Approved }, null),
            "desk collect: within 15 s of its last collect, the target's proposal is approved");
        var q = desk.Submit(Body("patterns", "pat1", "playmap", "pm1", "PLAYLIST GO show"), NodeCredential.Key("kP"), (_, _) => "high", T0).Proposal!;
        Check(ProposalDesk.CollectFresh == TimeSpan.FromSeconds(15) && desk.Approve(q.Id, "operator", T0.AddSeconds(16)) is (null, { } quiet)
              && quiet == "playmap pm1 has not asked this desk for approved lines in 16 s — it would not collect this"
              && desk.NotCollecting(q.Target, T0.AddSeconds(16)) == quiet && desk.NotCollecting(q.Target, T0.AddSeconds(15)) is null,
            "desk collect: 16 s after its last collect, it is not collecting: refused, and the words say for how long");
        Check(desk.Collect("pm1", NodeCredential.Key("kM"), T0.AddSeconds(20)).Items is [{ Id: var got, ClaimedUtc: { } at }] && got == p.Id && at == T0.AddSeconds(20)
              && desk.LastCollect("pm1") == T0.AddSeconds(20) && desk.Approve(q.Id, "operator", T0.AddSeconds(21)) is ({ }, null),
            "desk collect: a collect is recorded as the target's last, and what it collects carries when");
        var rig = new ProposalDesk(() => DeskId, nodes, "q7zx") { Fresh = TimeSpan.FromSeconds(2) };
        rig.Collect("pm1", NodeCredential.Key("kM"), T0);
        var r = rig.Submit(Body("patterns", "pat1", "playmap", "pm1", "PLAYLIST GO show"), NodeCredential.Key("kP"), (_, _) => "high", T0).Proposal!;
        Check(rig.Approve(r.Id, "operator", T0.AddSeconds(3)).Error?.Contains("in 3 s", StringComparison.Ordinal) == true,
            "desk collect: a rig may set its own window");
        var many = Desk(new NodeBindings(), "m4ny");
        for (var i = 0; i <= ProposalDesk.MaxCollectors; i++) many.Collect("t" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), NodeCredential.Operator, T0.AddSeconds(i));
        Check(many.LastCollect("t000") is null && many.LastCollect("t001") == T0.AddSeconds(1) && many.LastCollect("t256") == T0.AddSeconds(256),
            "desk collect: the desk keeps 256 targets' last collects, forgetting the longest silent");
        Check(desk.Collect(DeskId, NodeCredential.Operator, T0).Error is not null && desk.LastCollect(DeskId) is null,
            "desk collect: this desk never collects for itself");
    }

    /// <summary>A person's line (§11): born approved from this desk, decided by a role, for a listed line of a collecting target.</summary>
    static void PersonLines(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var desk = Desk(nodes, "k3m9");
        var raised = new List<SharedProposal>();
        desk.Changed += raised.Add;
        var self = new NodeRef("main", DeskId, "Hall A");
        var pm = new NodeRef("playmap", "pm1", "PlayMap Hall");
        var cue = Listed("review", "PLAYLIST CUE <playlist> <item>|#<n>");
        Check(desk.ProposeApproved(self, pm, "PLAYLIST CUE show #3", "", cue, "wire", T0) is (null, { } quiet)
              && quiet.StartsWith("PlayMap Hall has not asked this desk", StringComparison.Ordinal) && desk.All().Count == 0 && raised.Count == 0,
            "desk person: a line for a target that is not collecting is refused, and nothing is queued");
        desk.Collect("pm1", NodeCredential.Key("kM"), T0);
        var (sent, error) = desk.ProposeApproved(self, pm, " PLAYLIST CUE show #3 ", "the operator's cue", cue, "wire", T0.AddSeconds(1));
        Check(error is null && sent is { State: SharedProposalState.Approved, DecidedBy: "wire", Line: "PLAYLIST CUE show #3", Risk: "review", TargetIsDesk: false }
              && sent.Form == cue.Form && sent.FormWords == cue.Words && sent.DecidedUtc == T0.AddSeconds(1) && sent.From == self
              && raised is [{ State: SharedProposalState.Approved }] && raised[0] == sent,
            "desk person: a listed line is born approved, decided by its role, from this desk, and raises one change");
        Check(desk.Collect("pm1", NodeCredential.Key("kM"), T0.AddSeconds(2)).Items is [{ Line: "PLAYLIST CUE show #3" }],
            "desk person: its target collects it like any approved line");
        (SharedProposal?, string?) Send(NodeRef from, NodeRef target, string line, RiskVerdict verdict, string by)
            => desk.ProposeApproved(from, target, line, "", verdict, by, T0.AddSeconds(3));
        Check(Send(new NodeRef("patterns", "pat1", ""), pm, "PLAYLIST CUE show #3", cue, "wire") is (null, "a person's line comes from this desk itself")
              && Send(self, self, "note hi", cue, "wire") is (null, { })
              && Send(self, pm, "PLAYLIST CUE show #3", cue, "FOH tablet") is (null, { } role) && role.StartsWith("a person's line is decided by a role", StringComparison.Ordinal)
              && Send(self, pm, "OUTPUTS OFF", RiskVerdict.Unlisted, "wire") is (null, "PlayMap Hall does not list 'OUTPUTS OFF' — see its verbs")
              && Send(self, pm, "LINK STAGEWATCH h hunter2", cue with { Secret = true }, "wire") is (null, { } secret) && secret == LineFence.SecretWords("PlayMap Hall")
              && Send(self, pm, "PLAYLIST CUE show #3\n", cue, "wire") is (null, LineFence.Control)
              && Send(self, pm, "note " + new string('x', LineFence.MaxChars), cue, "wire") is (null, { } longWords) && longWords == LineFence.TooLong,
            "desk person: refused with words — a from that is not this desk, this desk as target, a decider that is no role, an unlisted line, a secret, the fence");
        Check(ProposalDesk.Roles.SequenceEqual(["operator", "deck", "wire", "trigger"]) && raised.Count == 1
              && Send(self, pm, "PLAYLIST CUE show #3", cue, "operator (copilot)") is (null, { } copilot) && copilot.StartsWith("a person's line is decided by a role", StringComparison.Ordinal),
            "desk person: its deciders are §11's four role words, never a key's name, an address or another word; the refusals raised nothing");
    }

    /// <summary>A proposal's JSON: §11's fields, then collected and the form that rated it.</summary>
    static void Json(Action<bool, string> Check)
    {
        var desk = Desk(new NodeBindings(), "k3m9");
        var p = desk.Submit(Body("patterns", "pat1", "patterns", "pat1", "EYE FOCUS screen 2"), NodeCredential.Key("kP"),
            (_, _) => Listed("low", "EYE FOCUS <thing>"), T0).Proposal!;
        string Of(SharedProposal x) => JsonSerializer.Serialize(x.ToJson());
        using (var before = JsonDocument.Parse(Of(p)))
            Check(before.RootElement.EnumerateObject().Select(x => x.Name).TakeLast(4).SequenceEqual(["ok", "words", "collected", "form"])
                  && before.RootElement.GetProperty("collected").ValueKind == JsonValueKind.Null && before.RootElement.GetProperty("form").GetString() == "EYE FOCUS <thing>"
                  && before.RootElement.GetProperty("id").GetString() == "p1-k3m9",
                "desk json: a proposal ends …, words, collected (null before), form");
        desk.Collect("pat1", NodeCredential.Key("kP"), T0);
        desk.Approve(p.Id, "operator", T0);
        var collected = desk.Collect("pat1", NodeCredential.Key("kP"), T0.AddSeconds(2)).Items!.Single();
        var at = new DateTimeOffset(T0.AddSeconds(2)).ToUnixTimeMilliseconds();
        using (var after = JsonDocument.Parse(Of(collected)))
        using (var held = JsonDocument.Parse(Of(desk.Get(p.Id)!)))
            Check(after.RootElement.GetProperty("collected").GetInt64() == at && held.RootElement.GetProperty("collected").GetInt64() == at,
                "desk json: once collected, collected is when, in Unix ms, in what the collect answers and in the queue");
        var unlisted = desk.Submit(Body("patterns", "pat1", "patterns", "pat1", "EYE DANCE"), NodeCredential.Key("kP"), (_, _) => RiskVerdict.Unlisted, T0).Proposal!;
        var word = desk.Submit(Body("patterns", "pat1", "patterns", "pat1", "EYE RESET"), NodeCredential.Key("kP"), (_, _) => "low", T0).Proposal!;
        using (var none = JsonDocument.Parse(Of(unlisted)))
            Check(unlisted is { Form: null, FormWords: "", Risk: "high" } && word is { Form: null, Risk: "low" } && none.RootElement.GetProperty("form").ValueKind == JsonValueKind.Null,
                "desk json: a line no form takes, or one rated by a word alone, names no form");
    }

    /// <summary>A word the desk's parser reads for a value of Stagewatch's own rows, by its name.</summary>
    static string? Fill(string name) => name switch
    {
        "window" => "lobby",
        "page" => "/clock",
        "seconds" => "10",
        "dept" => "lighting",
        "cue" => "LX12",
        "phrase" => "\"Wrap up now\"",
        "text" => "hello",
        "item" => "3",
        "m:ss" => "1:30",
        "playlist" => "Main",
        _ => null,
    };

    /// <summary>A form's tokens: split at white space outside &lt;…&gt; and […].</summary>
    static List<string> Tokens(string form)
    {
        var tokens = new List<string>();
        var token = new System.Text.StringBuilder();
        var depth = 0;
        foreach (var c in form)
        {
            if (c is '<' or '[') depth++;
            if (c is '>' or ']') depth--;
            if (c == ' ' && depth == 0)
            {
                if (token.Length > 0) tokens.Add(token.ToString());
                token.Clear();
                continue;
            }
            token.Append(c);
        }
        if (token.Length > 0) tokens.Add(token.ToString());
        return tokens;
    }

    /// <summary>
    /// Every line a form of Stagewatch's own rows takes, written out: each optional part taken or left out, each alternative of a word,
    /// each value filled by <see cref="Fill"/> (a value it has no word for writes "?", which no check passes).
    /// </summary>
    static IEnumerable<string> Lines(string form)
    {
        IEnumerable<List<string>> Seq(IReadOnlyList<string> tokens, int i)
        {
            if (i == tokens.Count)
            {
                yield return [];
                yield break;
            }
            foreach (var head in One(tokens[i]))
                foreach (var tail in Seq(tokens, i + 1))
                    yield return [.. head, .. tail];
        }
        IEnumerable<List<string>> One(string token)
        {
            if (token[0] == '[' && token[^1] == ']')
            {
                yield return [];
                foreach (var inner in Seq(Tokens(token[1..^1]), 0)) yield return inner;
            }
            else if (token[0] == '<') yield return [Fill(token[1..^1]) ?? "?"];
            else foreach (var alternative in token.Split('|')) yield return [alternative];
        }
        return Seq(Tokens(form), 0).Select(words => string.Join(' ', words));
    }

    /// <summary>
    /// Stagewatch's own commands rows are §6's notation: every form of every row — with each playout link's rows, a PlayMap link's and a
    /// Patterns link's — written out every way it can be, is a line the desk's own parser takes, at a risk no lower than the desk's own
    /// rating (Copilot.Classify); and its own table rates each such line as its row. Lines its parser refuses, the notation refuses too.
    /// </summary>
    static void OwnRows(Action<bool, string> Check)
    {
        var rows = ShowContext.CommandsFor([new PlayoutRow("stage-left", "Stage left", PlayoutRules.PlayMap, true), new PlayoutRow("foh", "FOH", PlayoutRules.Patterns, true)]);
        var table = VerbTable.Compile(rows.Select(r => new PeerCommand(r.Verb, r.Risk, r.Words)));
        static int Rank(CopilotRisk r) => r switch { CopilotRisk.Low => 0, CopilotRisk.Review => 1, _ => 2 };
        var wrong = new List<string>();
        var lines = 0;
        foreach (var row in rows)
            foreach (var form in VerbSyntax.Forms(row.Verb))
                foreach (var line in Lines(form))
                {
                    lines++;
                    var tokens = McpEndpoint.Tokenize(line);
                    var error = "no words";
                    if (tokens.Count == 0 || !ShowCommandParser.TryParse(tokens[0], tokens.Skip(1).ToList(), out var cmd, out error))
                        wrong.Add($"{line}: the desk refuses it ({error})");
                    else if (Rank(Copilot.Classify(cmd)) > VerbTable.Rank(row.Risk))
                        wrong.Add($"{line}: the desk rates it {Copilot.Classify(cmd)}, the row {row.Risk}");
                    else if (table.Rate(line) is not { Listed: true } verdict || verdict.Risk != row.Risk)
                        wrong.Add($"{line}: its own table rates it {table.Rate(line).Risk}, the row {row.Risk}");
                }
        Check(wrong.Count == 0 && lines > rows.Count * 2, $"own rows: every form of every row, every way it is written, is a line the desk takes at its row's risk or lower ({lines} lines; "
            + string.Join("; ", wrong) + ")");
        Check(rows.All(r => VerbSyntax.Forms(r.Verb).Count > 0 && !r.Verb.Contains(" / ", StringComparison.Ordinal)) && rows.Any(r => r.Verb == "start | pause")
              && rows.Any(r => r.Verb == "patterns end [<seconds>]"),
            "own rows: every row has a form in §6's notation: start | pause, patterns end [<seconds>]");
        // What the parser refuses, the notation does not offer: a flash before an urgency, an ack before a flash, words of a message unquoted.
        string[] refused = ["message hi off", "message hi ack", "message Wrap up now warn"];
        Check(refused.All(l => !ShowCommandParser.TryParse("message", McpEndpoint.Tokenize(l).Skip(1).ToList(), out _, out _) && !table.Rate(l).Listed),
            "own rows: a message line the parser refuses no form of the row takes");
    }

    /// <summary>
    /// The lines other apps document (fixtures/peer-lines.json, G05): each one this desk takes is taken whole and rates as the fixture
    /// says — a line to this desk by the desk's own parser and rules, a line to another app by that app's verified table — and each one
    /// it refuses is refused in the fixture's words, nothing queued.
    /// </summary>
    static void PeerLines(Action<bool, string> Check)
    {
        var root = ShowBusSpecTests.Master() is { } master ? Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(master)!)!, "tests", "Stagewatch.Tests", "fixtures") : null;
        Check(root is not null && File.Exists(Path.Combine(root, "peer-lines.json")), "peer lines: the fixture is in the suite's fixtures");
        if (root is null || !File.Exists(Path.Combine(root, "peer-lines.json"))) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "peer-lines.json")));
        var nodes = new NodeBindings();
        var peers = new PeerContexts(nodes);
        var targets = new Dictionary<string, NodeRef>(StringComparer.Ordinal) { ["main"] = new NodeRef("main", DeskId, "Hall A") };
        foreach (var file in new[] { "patterns-context.json", "agmessage-context.json", "playmap-context-busy.json" })
        {
            var json = File.ReadAllText(Path.Combine(root, file));
            var entry = PeerContexts.Parse(json, "10.0.0.9", T0);
            var accepted = entry is null ? (0, "not a context", NodeProof.None) : peers.Accept(json, "10.0.0.9", T0, DeskId, NodeCredential.Key("key-" + entry.Kind));
            Check(entry is not null && accepted == (200, null, NodeProof.Key) && peers.Find(entry.Id, T0)?.CommandsDropped == 0,
                $"peer lines: {file} is a context this desk keeps whole, verified by its node's key");
            if (entry is not null) targets[file] = new NodeRef(entry.Kind, entry.Id, entry.Name);
        }
        var desk = Desk(nodes, "k3m9");
        static RiskVerdict Own(string line)
        {
            var tokens = McpEndpoint.Tokenize(line);
            if (tokens.Count == 0 || !ShowCommandParser.TryParse(tokens[0], tokens.Skip(1).ToList(), out var cmd, out _)) return RiskVerdict.Unlisted;
            return new RiskVerdict(Copilot.Classify(cmd) switch { CopilotRisk.Low => "low", CopilotRisk.Review => "review", _ => "high" }, null, "", true, false);
        }
        RiskVerdict Rate(NodeRef target, string line) => target.Id == DeskId ? Own(line) : peers.VerdictFor(target, line, T0);
        NodeRef Target(JsonElement e) => e.GetProperty("target").GetString() == "main" ? targets["main"] : targets[e.GetProperty("table").GetString()!];
        string IdOf(string kind) => targets.Values.FirstOrDefault(t => t.Kind == kind)?.Id ?? kind + "1";
        var taken = 0;
        foreach (var e in doc.RootElement.GetProperty("lines").EnumerateArray())
        {
            var line = e.GetProperty("line").GetString()!;
            var from = e.GetProperty("from").GetString()!;
            var target = Target(e);
            var risk = e.GetProperty("risk").GetString();
            var source = e.GetProperty("source").GetString();
            var verdict = Rate(target, line);
            if (target.Kind == "main")
            {
                var tokens = McpEndpoint.Tokenize(line);
                var parsed = ShowCommandParser.TryParse(tokens[0], tokens.Skip(1).ToList(), out var cmd, out var error);
                Check(parsed && cmd.Kind.ToString() == e.GetProperty("kind").GetString() && verdict.Risk == risk,
                    $"peer lines: {source} — '{line}' is a {e.GetProperty("kind").GetString()} this desk's parser takes, rated {risk} ({cmd.Kind}, {verdict.Risk}{error})");
                if (line.StartsWith("message", StringComparison.Ordinal))
                    Check(cmd is { Urgency: MessageUrgency.Warning, Flash: FlashRate.Off, Flag: true }, $"peer lines: {source} — warn, steady, with Got it for the speaker");
            }
            else
                Check(verdict is { Listed: true, Secret: false } && verdict.Risk == risk && verdict.Form == e.GetProperty("form").GetString(),
                    $"peer lines: {source} — '{line}' is listed by {target.Name}'s verified table as {e.GetProperty("form").GetString()}, rated {risk} ({verdict.Form}, {verdict.Risk})");
            SharedProposal? p;
            if (from == "main")
            {
                desk.Collect(target.Id, NodeCredential.Key("key-" + target.Kind), T0);
                p = desk.ProposeApproved(targets["main"], target, line, "", verdict, "wire", T0).Proposal;
            }
            else p = desk.Submit(Body(from, IdOf(from), target.Kind, target.Id, line, targetName: target.Name), NodeCredential.Key("key-" + from), Rate, T0).Proposal;
            Check(p is not null && p.Line == line && p.Risk == risk && (target.Kind == "main" || p.Form == e.GetProperty("form").GetString()),
                $"peer lines: {source} — the desk takes '{line}' whole, at {risk} ({p?.Line}, {p?.Risk}, {p?.Form})");
            taken++;
        }
        var queued = desk.All().Count;
        var refused = 0;
        string[] fence = [LineFence.TooLong, LineFence.Control, LineFence.Format, LineFence.Surrogate, LineFence.Key, LineFence.Window];
        foreach (var e in doc.RootElement.GetProperty("refused").EnumerateArray())
        {
            var line = e.GetProperty("line").GetString()!;
            var from = e.GetProperty("from").GetString()!;
            var target = Target(e);
            var expected = e.GetProperty("error").GetString();
            var source = e.GetProperty("source").GetString();
            refused++;
            if (fence.Contains(expected))
                Check(desk.Submit(Body(from, IdOf(from), target.Kind, target.Id, line, targetName: target.Name), NodeCredential.Key("key-" + from), Rate, T0) is (null, { } fenced, false)
                      && fenced == expected,
                    $"peer lines: {source} — the desk's fence refuses '{line}', whichever node it is for: {expected}");
            else if (target.Kind == "main")
            {
                // §11: a line to this desk that its parser refuses is unknown, so high — queued for a person to see red, and it fails when run.
                var tokens = McpEndpoint.Tokenize(line);
                // The parser's words name the verb first ("message: …"); the fixture holds AGMessage's own oracle's words after it.
                Check(!ShowCommandParser.TryParse(tokens[0], tokens.Skip(1).ToList(), out _, out var error) && expected is not null && error.EndsWith(": " + expected, StringComparison.Ordinal)
                      && desk.Submit(Body(from, IdOf(from), "main", DeskId, line), NodeCredential.Key("key-" + from), Rate, T0).Proposal is { Risk: "high" },
                    $"peer lines: {source} — this desk's parser refuses '{line}' ({error}), so it rates high");
                queued++;
            }
            else
                Check(desk.Submit(Body(from, IdOf(from), target.Kind, target.Id, line, targetName: target.Name), NodeCredential.Key("key-" + from), Rate, T0) is (null, { } error, false)
                      && error == expected,
                    $"peer lines: {source} — the desk refuses '{line}': {expected}");
        }
        Check(taken == 8 && refused == 5 && desk.All().Count == queued,
            $"peer lines: every documented line is taken, and no refused line to another app is queued ({taken}, {refused}, {desk.All().Count} of {queued})");
    }
}
