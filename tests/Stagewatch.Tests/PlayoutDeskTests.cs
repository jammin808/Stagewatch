using System.Net;
using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Integrations;

/// <summary>
/// The desk's playout seams, pure, as the WPF desk calls them (its walk-through is the maintainer's PC work): Settings' links — a
/// rename carrying its running-order cues, the lead's box, a found peer's name, a link's line — May drive playout as typed, an HTTP
/// trigger's secret set and a PIN moved out of an address only where it would then be sent, the cue editor's choices, words and
/// cues, the VT chip's entries greyed by the plan their press runs or the peer's WHOAMI, and a running-order row's cue and GO by
/// hand through the planner. Fields first, words beside them.
/// </summary>
static class PlayoutDeskTests
{
    public static void Run(Action<bool, string> Check)
    {
        Links(Check);
        MayDrive(Check);
        TriggerSecrets(Check);
        CueEditor(Check);
        Menus(Check);
        RowByHand(Check);
    }

    static PlayoutCue Cue(string link, string list, string item) => new() { Link = link, List = list, Item = item };

    static void Links(Action<bool, string> Check)
    {
        var s = new AppSettings();
        s.Rundown.Items = [new RundownItem { Title = "Open", Playout = [Cue("stage-left", "pl1", "it1"), Cue("patterns", "", "c1")] }];
        s.OtherRundowns.Add(new NamedRundown { Name = "Day 2", Rundown = new Rundown { Items = [new RundownItem { Title = "Close", Playout = [Cue("stage-left", "pl1", "it2")] }] } });
        Check(PlayoutRules.CuesNaming(s, "stage-left") == 2 && PlayoutRules.CuesNaming(s, "patterns") == 1 && PlayoutRules.CuesNaming(s, "nobody") == 0,
              "desk: the cues naming a link are counted across the show's running orders");
        Check(PlayoutRules.RenameCues(s, "stage-left", "sl") == 2 && PlayoutRules.CuesNaming(s, "sl") == 2 && PlayoutRules.CuesNaming(s, "stage-left") == 0
              && s.Rundown.Items[0].Playout[1].Link == "patterns",
              "desk: a renamed link takes its cues with it, in every running order, and no other link's");
        // Renames in order, a swap through a third name among them.
        PlayoutRules.RenameCues(s, "sl", "tmp");
        PlayoutRules.RenameCues(s, "patterns", "sl");
        PlayoutRules.RenameCues(s, "tmp", "patterns");
        Check(s.Rundown.Items[0].Playout is [{ Link: "patterns", Item: "it1" }, { Link: "sl", Item: "c1" }], "desk: renames taken in order swap two links' cues");
        RenameChain(Check);
        Reaching(Check);

        Check(PlayoutRules.LeadProblem(" 450 ", out var lead) is null && lead == 450 && PlayoutRules.LeadProblem("100", out var low) is null && low == 100
              && PlayoutRules.LeadProblem("2000", out _) is null,
              "desk: the lead's box takes a whole number of 100-2000 ms");
        Check(new[] { "99", "2001", "", "3e2", "-300", "300.5", "٣٠٠" }.All(t => PlayoutRules.LeadProblem(t, out var ms) is { } why && ms == 0
                  && why.StartsWith("The lead of a GO at one instant is 100 to 2000 ms", StringComparison.Ordinal)),
              "desk: a lead out of range, not a number or not ASCII digits is said, and none is kept");

        Check(PlayoutRules.SuggestName("Stage Left (PlayMap)", "playmap", []) == "stage-left-playmap"
              && PlayoutRules.SuggestName("FOH", "patterns", ["foh"]) == "foh-2"
              && PlayoutRules.SuggestName("FOH", "patterns", ["foh", "foh-2"]) == "foh-3"
              && PlayoutRules.SuggestName("Pause", "playmap", []) == "playmap"
              && PlayoutRules.SuggestName("vMix", "patterns", ["patterns"]) == "patterns-2"
              && PlayoutRules.SuggestName("Éclairage ×", "", []) == "clairage"
              && PlayoutRules.SuggestName("", null, []) == "link",
              "desk: a found peer's name becomes a link's: tidied, never an action or another integration's, and none taken");
        var longName = PlayoutRules.SuggestName(new string('a', 40), "playmap", [new string('a', 21)]);
        Check(longName.Length <= PlayoutRules.MaxNameChars && PlayoutRules.ValidName(longName) && longName.EndsWith("-2", StringComparison.Ordinal),
              $"desk: a long name is cut with room for its number ({longName})");
        Check(new[] { "Stage Left", "pause", "vmix", "trust", "", "---", "Ω" }.All(h => PlayoutRules.ValidName(PlayoutRules.SuggestName(h, "patterns", []))),
              "desk: every suggested name is one the name rule takes");

        var line = PlayoutRules.Describe(new PlayoutLinkSettings { Name = "stage-left", Kind = "playmap", Host = "10.0.0.12", Port = 9941, Transport = "wss", Node = "a1b2c3d4", NextOnEnd = true, TokenProtected = "c2VjcmV0" });
        Check(line == "stage-left · PlayMap Stage left at 10.0.0.12:9941 · wss · VT, next on end · node a1b2c3d4" && !line.Contains("c2VjcmV0", StringComparison.Ordinal),
              $"desk: Settings' line for a link says where it dials and what it feeds, never its token ({line})");
        var auto = PlayoutRules.Describe(new PlayoutLinkSettings { Name = "foh", Host = "fe80::1", Enabled = false, FollowVt = false });
        Check(auto == "foh · Foh at [fe80::1]:9697 · kind heard (off)", $"desk: a link of a kind heard, switched off ({auto})");
    }

    /// <summary>
    /// What Settings says a rename moves, before Save moves it: the count through the renames made so far, in their order, is what
    /// <see cref="PlayoutRules.RenameCues"/> leaves naming each link once Save has run them — a swap through a third name, a rename
    /// then a removal, a chain, and cues that already named a rename's new name (adopted, and counted).
    /// </summary>
    static void RenameChain(Action<bool, string> Check)
    {
        static AppSettings Show()
        {
            var s = new AppSettings();
            s.Rundown.Items = [new RundownItem { Title = "Open", Playout = [Cue("a", "pl1", "it1"), Cue("a", "pl1", "it2"), Cue("b", "", "c1"), Cue("x", "", "c9")] }];
            s.OtherRundowns.Add(new NamedRundown { Name = "Day 2", Rundown = new Rundown { Items = [new RundownItem { Title = "Close", Playout = [Cue("b", "", "c2"), Cue("a", "pl2", "it3")] }] } });
            return s;
        }
        var chains = new List<(string From, string To)>[]
        {
            [("a", "t"), ("b", "a"), ("t", "b")],   // a swap through a third name
            [("a", "c")],                           // a rename (then removed: its cues name c, kept and skipped)
            [("a", "b2"), ("b2", "b3")],            // a chain
            [("a", "x")],                           // onto a name orphan cues carry: they go with it
            [("b", "a"), ("a", "z")],               // b onto a, then that link renamed on
        };
        foreach (var renames in chains)
        {
            var before = Show();
            var after = Show();
            foreach (var (from, to) in renames) PlayoutRules.RenameCues(after, from, to);
            var names = new[] { "a", "b", "c", "t", "x", "z", "b2", "b3" };
            var said = names.Select(n => PlayoutRules.CuesNaming(before, n, renames)).ToList();
            var moved = names.Select(n => PlayoutRules.CuesNaming(after, n)).ToList();
            Check(said.SequenceEqual(moved),
                  $"desk: the count Settings says for each name through renames {string.Join(", ", renames.Select(r => r.From + "→" + r.To))} is what Save leaves ({string.Join(" ", said)} / {string.Join(" ", moved)})");
        }
        var show = Show();
        Check(PlayoutRules.CuesNaming(show, "b", [("a", "t"), ("b", "a"), ("t", "b")]) == 3 && PlayoutRules.CuesNaming(show, "a", [("a", "t"), ("b", "a"), ("t", "b")]) == 2
              && PlayoutRules.CuesNaming(show, "x", [("a", "x")]) == 4 && PlayoutRules.CuesNaming(show, "a", [("a", "x")]) == 0
              && PlayoutRules.CuesNaming(show, "x", []) == 1 && PlayoutRules.CuesNaming(show, "x", [("x", "y")]) == 0,
              "desk: a swap counts each link's cues under the other's name; a rename onto a name orphan cues carry counts them too");
    }

    /// <summary>Find's peer is a link's when the link is set to its node, else when it dials its address and port.</summary>
    static void Reaching(Action<bool, string> Check)
    {
        var links = new List<PlayoutLinkSettings>
        {
            new() { Name = "stage-left", Kind = "playmap", Host = "10.0.0.12", Port = 9941, Node = "pm5e6f7a" },
            new() { Name = "foh", Host = "FOH-PC", Port = 9697 },
        };
        Check(PlayoutRules.Reaching(links, "pm5e6f7a", "10.0.0.99", 9941)?.Name == "stage-left"
              && PlayoutRules.Reaching(links, "", "10.0.0.12", 9941)?.Name == "stage-left"
              && PlayoutRules.Reaching(links, "9f8e7d6c", "foh-pc", 9697)?.Name == "foh"
              && PlayoutRules.Reaching(links, "", "10.0.0.12", 9697) is null
              && PlayoutRules.Reaching(links, "9f8e7d6c", "10.0.0.5", 9697) is null,
              "desk: a found peer is a link's by its node, else by its address (any case) and port — never by one of them alone");
    }

    static void MayDrive(Action<bool, string> Check)
    {
        var (kept, notes) = PlayoutGate.Typed("10.0.0.40, 10.0.0.41;\n fe80::2\t10.0.0.40\r\n");
        Check(kept.SequenceEqual(new[] { "10.0.0.40", "10.0.0.41", "fe80::2" }) && notes.Count == 0,
              "desk: May drive playout takes addresses by commas, semicolons, spaces and lines, a repeat once");
        var (some, said) = PlayoutGate.Typed("10.0.0.40 stage-pc 127.0.0.1 10.0.0.0/24 010.0.0.1");
        Check(some.SequenceEqual(new[] { "10.0.0.40" }) && said.Count == 4 && said.All(n => n.StartsWith("May drive playout: \"", StringComparison.Ordinal) && n.Contains(PlayoutGate.AddressWords, StringComparison.Ordinal)),
              "desk: a name, loopback, a range or an address .NET would widen is left out and said, never widened");
        var (over, overNotes) = PlayoutGate.Typed(string.Join(" ", Enumerable.Range(1, 20).Select(n => "10.0.1." + n.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        Check(over.Count == PlayoutGate.MaxFrom && overNotes is [var more] && more.Contains("4 more left out", StringComparison.Ordinal),
              "desk: past 16 addresses is said");
        Check(PlayoutGate.Typed(PlayoutGate.Shown(kept)).Kept.SequenceEqual(kept) && PlayoutGate.Typed("").Kept.Count == 0 && PlayoutGate.Typed(null).Notes.Count == 0,
              "desk: the box shows the list so that reading it back keeps it; an empty box is none");
    }

    static void TriggerSecrets(Action<bool, string> Check)
    {
        static bool Loopback(IPAddress a) => IPAddress.IsLoopback(a);
        var rule = new TriggerRule { Name = "GO VT", Action = TriggerAction.Http, Target = "https://10.0.0.12:9940/api/cmd", Payload = "PLAYLIST GO show #4" };
        Check(TriggerAuthRules.Label(rule) == "none", "desk: a rule with no secret says none");
        Check(TriggerAuthRules.SetProfile(rule, "PlayMap", "cHJvdA==") is null && rule.Auth is { Kind: "playmap", SecretProtected: "cHJvdA==" }
              && TriggerAuthRules.Label(rule) == "PlayMap token (X-PlayMap-Token) · secret set" && TriggerAuthRules.Problem(rule, Loopback) is null,
              "desk: a secret set on a rule, its kind's words shown and never the secret");
        Check(TriggerAuthRules.SetProfile(rule, "patterns", null) is null && rule.Auth is { Kind: "patterns", SecretProtected: "cHJvdA==" },
              "desk: a kind changed keeps the secret kept");
        Check(TriggerAuthRules.SetProfile(rule, "basic", "eA==") is { } unknown && unknown.Contains("isn't a kind of secret", StringComparison.Ordinal)
              && rule.Auth is { Kind: "patterns", SecretProtected: "cHJvdA==" },
              "desk: a kind Stagewatch doesn't know is said, and nothing changes");
        var bare = new TriggerRule { Name = "x", Action = TriggerAction.Http, Target = "http://127.0.0.1/" };
        Check(TriggerAuthRules.SetProfile(bare, "bearer", null) is null && TriggerAuthRules.Label(bare) == "Bearer token (Authorization) · no secret — type it"
              && TriggerAuthRules.Problem(bare, Loopback) is { } noSecret && noSecret.Contains("no secret", StringComparison.Ordinal),
              "desk: a profile with no secret says to type it, and Save refuses it");
        Check(TriggerAuthRules.SetProfile(rule, "none", "eA==") is null && rule.Auth is null && TriggerAuthRules.Label(rule) == "none",
              "desk: none takes the profile away, secret and all");
        Check(TriggerAuthRules.KindWords(TriggerAuthRules.Stagewatch) == "Stagewatch PIN or key (X-Stagewatch-Pin)"
              && TriggerAuthRules.Kinds.All(k => TriggerAuthRules.KindWords(k).Length > 0),
              "desk: each kind's words name its header");

        // A PIN in an address: the warning's label, then the move, made only where the rule would then be sent.
        var local = new TriggerRule { Name = "next", Action = TriggerAction.Http, Target = "http://127.0.0.1:9924/api/next?pin=12%2034&x=1" };
        Check(TriggerAuthRules.Label(local) == "none — a secret in its address", "desk: a rule carrying a secret in its address says so in its label");
        string? protectedWith = null;
        Check(TriggerAuthRules.MovePin(local, pin => { protectedWith = pin; return "UFJPVA=="; }, Loopback) is null
              && local is { Target: "http://127.0.0.1:9924/api/next?x=1", Auth: { Kind: "stagewatch", SecretProtected: "UFJPVA==" } } && protectedWith == "12 34"
              && TriggerAuthRules.Warning(local) is null && TriggerAuthRules.Problem(local, Loopback) is null,
              "desk: a PIN to this PC moves into the rule's secret, unescaped and protected, and the warning is gone");
        var lan = new TriggerRule { Name = "lan", Action = TriggerAction.Http, Target = "http://10.0.0.5:9924/api/next?pin=1234" };
        var lanBefore = JsonSerializer.Serialize(lan);
        Check(TriggerAuthRules.MovePin(lan, _ => "never", Loopback) is { } plain && plain.StartsWith("it would then be refused: a secret goes over plain HTTP only to this PC", StringComparison.Ordinal)
              && JsonSerializer.Serialize(lan) == lanBefore,
              "desk: a PIN to another machine over plain HTTP is not moved — it would then be refused — and the rule fires as before");
        var tls = new TriggerRule { Name = "tls", Action = TriggerAction.Http, Target = "https://10.0.0.5:9925/api/next?pin=1234#top" };
        Check(TriggerAuthRules.MovePin(tls, _ => "cA==", Loopback) is null && tls.Target == "https://10.0.0.5:9925/api/next#top" && tls.Auth?.Kind == "stagewatch",
              "desk: a PIN to an https:// target moves (its certificate is pinned on first use)");
        var placeholder = new TriggerRule { Name = "ph", Action = TriggerAction.Http, Target = "http://127.0.0.1/api?pin={ITEM}" };
        var osc = new TriggerRule { Name = "osc", Action = TriggerAction.Osc, Target = "127.0.0.1:53000?pin=1" };
        var carries = new TriggerRule { Name = "c", Action = TriggerAction.Http, Target = "http://127.0.0.1/?pin=1", Auth = new TriggerAuth { Kind = "bearer", SecretProtected = "eA==" } };
        var tab = new TriggerRule { Name = "t", Action = TriggerAction.Http, Target = "http://127.0.0.1/?pin=a%09b" };
        Check(TriggerAuthRules.MovePin(placeholder, _ => "x", Loopback) is { } ph && ph.Contains("names no PIN", StringComparison.Ordinal)
              && TriggerAuthRules.MovePin(osc, _ => "x", Loopback) is not null && TriggerAuthRules.MovePin(carries, _ => "x", Loopback) is not null
              && TriggerAuthRules.MovePin(tab, _ => "x", Loopback) is { } ctl && ctl.Contains("can't be sent in a header", StringComparison.Ordinal)
              && placeholder.Auth is null && osc.Auth is null && carries.Auth?.Kind == "bearer" && tab.Auth is null,
              "desk: a placeholder PIN, an OSC rule, a rule with a secret already and a PIN no header carries are not moved, each said");
    }

    static void CueEditor(Action<bool, string> Check)
    {
        var index = PlayoutIndex.ReadIndex("""{"sig":"s1","playlists":[{"id":"pl1","name":"Main show","items":[{"id":"it1","label":"Intro"},{"id":"it 2","label":"Spaced"},{"id":"it3","label":""}]},{"id":"pl2","name":"","items":[{"id":"a","label":"A"}]}]}""");
        var (choices, left) = PlayoutCueChoices.Of(index);
        Check(choices.Select(c => (c.List, c.Item, c.Label)).SequenceEqual(new[] { ("pl1", "it1", "Main show · Intro"), ("pl1", "it3", "Main show · it3"), ("pl2", "a", "pl2 · A") })
              && left == 1 && PlayoutCueChoices.LeftWords(left).StartsWith("1 listed id isn't offered", StringComparison.Ordinal),
              "desk: the picker offers a PlayMap node's items by playlist, an id a cue can't keep left out and said");
        var cues = PlayoutIndex.ReadCues("""{"listRev":3,"cues":[{"id":"c1","number":"12","name":"Walk-in"},{"id":"c2","number":"13","name":"Dark","enabled":false}]}""");
        var (cueChoices, none) = PlayoutCueChoices.Of(cues);
        Check(cueChoices is [{ List: "", Item: "c1", Label: "12 Walk-in", Off: false }, { Item: "c2", Off: true } off] && none == 0 && off.ToString() == "13 Dark (switched off)"
              && PlayoutCueChoices.Of(null).Choices.Count == 0 && PlayoutCueChoices.LeftWords(0) == "",
              "desk: the picker offers a Patterns desk's cues, one switched off marked; nothing while the peer lists nothing");

        Check(PlayoutCueChoices.StandbyWords("playmap").Contains("first frame on air at load", StringComparison.Ordinal)
              && PlayoutCueChoices.StandbyWords("patterns").Contains("moves the Patterns desk's own standby", StringComparison.Ordinal)
              && PlayoutCueChoices.StandbyWords(null) is var both && both.Contains("first frame on air", StringComparison.Ordinal) && both.Contains("own standby", StringComparison.Ordinal),
              "desk: the standby option says what it does on each kind, both while the kind is not known");

        var (made, why) = PlayoutCueChoices.Make(" Stage-Left ", " pl1 ", "it1", " Main show · Intro ", standby: true, together: true);
        Check(why is null && made is { Link: "stage-left", List: "pl1", Item: "it1", Label: "Main show · Intro", Standby: true, Together: true }
              && PlayoutRules.SanitiseCues([made]) is [var kept] && JsonSerializer.Serialize(kept) == JsonSerializer.Serialize(made),
              "desk: a cue made in the editor is one a load keeps as it is");
        Check(PlayoutCueChoices.Make("", "", "c1", "", false, false).Why is { } noLink && noLink.StartsWith("Choose a playout link", StringComparison.Ordinal)
              && PlayoutCueChoices.Make("go", "", "c1", "", false, false).Why is not null
              && PlayoutCueChoices.Make("foh", "", "", "", false, false).Why is { } nothing && nothing.StartsWith("Choose what it cues", StringComparison.Ordinal)
              && PlayoutCueChoices.Make("foh", "", "it 2", "", false, false).Why is { } spaced && spaced.Contains("pick it from the list", StringComparison.Ordinal)
              && PlayoutCueChoices.Make("foh", "pl#1", "it1", "", false, false).Why is { } badList && badList.Contains("isn't a playlist id", StringComparison.Ordinal)
              && PlayoutCueChoices.Make("foh", "", "c1", new string('x', 81), false, false).Why is { } longLabel && longLabel.Contains("at most 80", StringComparison.Ordinal)
              && PlayoutCueChoices.Make("foh", "", "c1", "a\tb", false, false).Why is not null,
              "desk: a cue the running order would not keep is refused in words, never made");
        Check(PlayoutCueChoices.Describe(made!) == "Stage left: Main show · Intro · stands by at load · together"
              && PlayoutCueChoices.Describe(Cue("foh", "", "c1")) == "Foh: c1"
              && PlayoutCueChoices.Describe(Cue("foh", "pl1", "it1")) == "Foh: pl1 it1",
              "desk: the editor lists a cue by its link, its label (else its ids) and its options");
        Check(PlayoutCueChoices.Summary([]) == "Add…" && PlayoutCueChoices.Summary([made!]) == "1 cue" && PlayoutCueChoices.Summary([made!, made!]) == "2 cues",
              "desk: the grid's button says how many cues an item has");
    }

    static PlayoutPeerState PatternsState(string role, bool? pausable = null)
        => new() { Whole = true, Kind = PlayoutRules.Patterns, HasVideo = true, Video = new("opener.mp4", "", 60, 10, true, false, false, Transport: true, Role: role, Pausable: pausable) };

    static void Menus(Action<bool, string> Check)
    {
        var pat = new PlayoutLinkSettings { Name = "patterns", Enabled = true };
        var facts = new PlayoutFacts(PlayoutRules.Patterns, PatternsState("playlist"), 100, null);
        var offers = PlayoutMenus.Vt(pat, facts, null, connected: true, licensed: true, 300);
        Check(offers.Select(o => o.Action).SequenceEqual(new[] { "play", "pause", "stop", "restart", "end", "seek" })
              && offers.Single(o => o.Action == "play").Enabled && offers.Single(o => o.Action == "restart").Enabled && offers.Single(o => o.Action == "end").Enabled
              && offers.Single(o => o.Action == "pause").Why is { } paused && paused.Contains("its own clock keeps running, so it is not paused", StringComparison.Ordinal)
              && offers.Single(o => o.Action == "stop").Why is { } stopped && stopped.Contains("so it is not stopped", StringComparison.Ordinal),
              "desk: the VT chip greys Patterns' pause and stop for a playlist item in Patterns' own words, and offers the rest");
        Check(offers.Where(o => !o.Enabled).Select(o => o.Action).SequenceEqual(new[] { "pause", "stop" }),
              "desk: for a Patterns playlist item exactly Pause and Stop are greyed — Play, Restart, Last 10 s and Seek are offered");
        Check(PlayoutMenus.Vt(pat, new PlayoutFacts(PlayoutRules.Patterns, PatternsState("playlist", pausable: true), 100, null), null, true, true, 300)
                  .Single(o => o.Action == "pause").Enabled,
              "desk: a Patterns desk that says its clip pauses has Pause offered");
        var pm = new PlayoutLinkSettings { Name = "stage-left", Kind = "playmap", Enabled = true };
        var pmFacts = new PlayoutFacts(PlayoutRules.PlayMap, new PlayoutPeerState { Whole = true, Kind = PlayoutRules.PlayMap, HasVideo = true, Video = new("intro.mov", "", 90, 1, true, false, false, Transport: true, Role: "playlist") }, 100, null);
        Check(PlayoutMenus.Vt(pm, pmFacts, null, true, true, 300).All(o => o.Enabled), "desk: a PlayMap node is never greyed by its clip's role");
        var readOnly = PlayoutMenus.Vt(pm, pmFacts, new PlayoutWhoami(true, "remote", true, ["WHOAMI", "STATE"]), true, true, 300);
        Check(readOnly.All(o => o.Why == PlayoutLink.ReadOnlyWords("PlayMap Stage left")) && PlayoutLink.ReadOnlyWords("PlayMap Stage left").Contains("SHOW SET REMOTE-VIEW-ONLY OFF", StringComparison.Ordinal),
              "desk: a node whose WHOAMI lets this PC only read greys every entry in the words the link's status says");
        Check(PlayoutMenus.Vt(pm, pmFacts, new PlayoutWhoami(true, "remote", true, null), true, true, 300).All(o => o.Enabled)
              && PlayoutMenus.Vt(pm, pmFacts, new PlayoutWhoami(true, "remote", true, ["VIDEO PAUSE"]), true, true, 300).All(o => o.Enabled),
              "desk: a WHOAMI that lets this PC drive greys nothing");
        Check(PlayoutMenus.Vt(pm, pmFacts, null, connected: false, true, 300).All(o => o.Why == "PlayMap Stage left isn't connected — nothing would be sent")
              && PlayoutMenus.Vt(pm, pmFacts, null, true, licensed: false, 300).All(o => o.Why == PlayoutRules.LicenceWords),
              "desk: a link not connected, or no licence, greys every entry with why");
        var wrong = PlayoutMenus.Vt(new PlayoutLinkSettings { Name = "foh", Kind = "patterns", Enabled = true }, pmFacts, null, true, true, 300);
        Check(wrong.All(o => o.Why is { } w && w.Contains("answers as PlayMap, but its settings say Patterns", StringComparison.Ordinal)),
              "desk: a peer answering as another kind greys every entry, as its press is refused");
        Check(PlayoutMenus.Line("stage-left", "pause") == "playout stage-left pause" && PlayoutMenus.Line("stage-left", "end") == "playout stage-left end 10"
              && PlayoutMenus.Line("foh", "seek", "1:23") == "playout foh seek 1:23",
              "desk: an entry runs the line a deck sends");
        foreach (var (action, _) in PlayoutMenus.VtActions)
        {
            var text = PlayoutMenus.Line("stage-left", action, action == "seek" ? "1:23" : "");
            var tokens = text.Split(' ');
            Check(ShowCommandParser.TryParse(tokens[0], tokens.Skip(1).ToList(), out var cmd, out _) && cmd.Kind == ShowCommandKind.Playout
                  && cmd.Get<PlayoutArgs>() is { Link: "stage-left" } a && a.Action == action,
                  $"desk: the VT chip's {action} line is the grammar's playout {action}");
        }
        Check(PlayoutMenus.RowWhy(new RundownItem(), true) is { } noCues && noCues.Contains("no playout cues", StringComparison.Ordinal)
              && PlayoutMenus.RowWhy(new RundownItem { Playout = [Cue("foh", "", "c1")] }, false) == PlayoutRules.LicenceWords
              && PlayoutMenus.RowWhy(new RundownItem { Playout = [Cue("foh", "", "c1")] }, true) is null,
              "desk: a row's Cue and GO playout now are greyed with why: no cues, or no licence");

        // A row's press finds its item again by place and id: never another item that moved into its place.
        RundownItem open = new() { Id = "i1", Title = "Open" }, close = new() { Id = "i2", Title = "Close" };
        Check(PlayoutMenus.RowItem([open, close], 1, "i2") == close && PlayoutMenus.RowItem([open, close], 0, "i1") == open
              && PlayoutMenus.RowItem([close, open], 1, "i2") is null && PlayoutMenus.RowItem([open], 1, "i2") is null
              && PlayoutMenus.RowItem([open, close], -1, "i1") is null && PlayoutMenus.RowItem([new RundownItem { Id = "" }], 0, "") is null
              && PlayoutMenus.ChangedUnder.EndsWith("nothing sent", StringComparison.Ordinal),
              "desk: a row's press takes its item only where it still is under its id — moved, removed or another running order is refused");
    }

    static void RowByHand(Action<bool, string> Check)
    {
        var rows = new List<(int Mark, string Words)>();
        var status = new List<string>();
        var licensed = true;
        var settings = new List<PlayoutLinkSettings> { new() { Name = "off-link", Enabled = false } };
        var director = new PlayoutDirector(new PlayoutLinks(), () => settings, () => licensed, () => 300, (_, mark, row) => rows.Add((mark, row)), status.Add);
        var item = new RundownItem { Id = "i1", Title = "Open", Playout = [Cue("stage-left", "pl1", "it1"), Cue("off-link", "", "c1")] };
        director.GoNow(2, item);
        Check(rows is [(2, var a), (2, var b)] && a.Contains("playout cue pl1 it1 on Stage left: skipped — no playout link called stage-left on this PC", StringComparison.Ordinal)
              && b.Contains("Off link is switched off", StringComparison.Ordinal) && status is [var s1, _] && s1.StartsWith("Item 3: ", StringComparison.Ordinal),
              "desk: GO playout now runs the item's cues through the planner, each skip a row and a status line against its item");
        rows.Clear();
        status.Clear();
        director.CueNow(2, item);
        Check(rows.Count == 2 && rows.All(r => r.Words.Contains("not stood by", StringComparison.Ordinal)),
              "desk: Cue playout now stands every cue by, set to stand by at load or not, through the same guards");
        rows.Clear();
        licensed = false;
        director.GoNow(2, item);
        director.CueNow(2, item);
        Check(rows is [(2, var g), (2, var c)] && g == "2 playout cues not sent: " + PlayoutRules.LicenceWords && c == "2 playout cues not stood by: " + PlayoutRules.LicenceWords,
              "desk: without the licence, GO and Cue playout now are said not sent, one row each");
        rows.Clear();
        director.GoNow(0, new RundownItem { Title = "Empty" });
        director.CueNow(0, new RundownItem { Title = "Empty" });
        Check(rows is [(0, var e1), (0, var e2)] && e1.StartsWith("no playout cues to GO", StringComparison.Ordinal) && e2.StartsWith("no playout cues to stand by", StringComparison.Ordinal),
              "desk: an item with no cues says so, and nothing is sent");
        Check(item.Playout.All(c => !c.Standby), "desk: Cue playout now changes no cue's own standby setting");
    }
}
