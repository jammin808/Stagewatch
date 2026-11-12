using System.Net;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;

/// <summary>
/// A window's light and words over every viewer, grant and claim (Show Bus 12.10), the operator frame's windows block, and the desk's rules
/// around grants (windows-for-peers SW-W4): a proposal's app by its id alone, a grant a person's act at every door, the journal's rows,
/// the menus' entries and what loading repaired, said as it loads. Pure.
/// </summary>
static class WindowLightTests
{
    const string Pat = "pat00001", Pm = "pm000001";
    static readonly DateTime T0 = new(2026, 11, 12, 18, 0, 0, DateTimeKind.Utc);

    public static void Run(Action<bool, string> Check)
    {
        Lights(Check);
        Frame(Check);
        Apps(Check);
        Doors(Check);
        Journal(Check);
        Menus(Check);
        Loading(Check);
    }

    static ScreenWindow Lobby(params WindowGrant[] grants)
        => new() { Id = "wlobby1", Name = "lobby", Token = ScreenWindows.NewToken(), Content = "/clock", Revision = 3, Grants = grants.ToList() };

    static WindowGrant Page(string node = Pat, string name = "Patterns FOH")
        => new() { Node = node, Kind = "patterns", Name = name, How = ScreenWindows.HowPage, Token = ScreenWindows.NewToken() };

    static WindowGrant Draw(string node = Pm, string name = "PlayMap Stage left")
        => new() { Node = node, Kind = "playmap", Name = name, How = ScreenWindows.HowDraw };

    static ViewerObserved Viewer(string via, int connected, int rev, DateTime? shown, string how = "", string error = "")
        => new("wlobby1", via, how, connected, rev, "/clock", false, 40, shown, error, "TV", "10.0.0.5");

    static GrantObserved Grant(string node, string how, string state, int open = 0)
        => new("wlobby1", node, how == ScreenWindows.HowPage ? "patterns" : "playmap", node == Pat ? "Patterns FOH" : "PlayMap Stage left", how, true, "tls", true, open, state, "");

    static void Lights(Action<bool, string> Check)
    {
        var page = Page();
        var w = Lobby(page);
        var seen = new WindowObserved("wlobby1", 1, 3, "/clock", false, 40, T0, "", "TV", "10.0.0.5");
        var old = ScreenWindows.ContextItems([w], [seen]);
        Check(old is [{ Light: Light.Green, State: "showing /clock" }],
            $"window lights: without viewers the item reads as before — one row per window, no grant or claim words ({old[0].State})");

        // A grant held and never opened counts for nothing: nothing opened at all is grey.
        var none = ScreenWindows.ContextItems([w], [], [Viewer(Pat, 0, 0, null, ScreenWindows.HowPage)]);
        Check(none is [{ Light: Light.Grey }] && none[0].State.StartsWith("not opened yet (should show /clock) · granted to Patterns FOH", StringComparison.Ordinal),
            $"window lights: a grant held but never opened is grey, never guessed ({none[0].State})");

        // A TV showing it and a grant never opened: green, the TV's words.
        var tv = ScreenWindows.ContextItems([w], [], [Viewer("", 1, 3, T0), Viewer(Pat, 0, 0, null, ScreenWindows.HowPage)]);
        Check(tv is [{ Light: Light.Green }] && tv[0].State.StartsWith("showing /clock · granted to Patterns FOH", StringComparison.Ordinal),
            $"window lights: a TV showing it and a grant never opened is green ({tv[0].State})");

        // A TV showing it and a grant whose page was open and dropped: amber, the grant's words named by its app.
        var stale = ScreenWindows.ContextItems([w], [], [Viewer("", 1, 3, T0), Viewer(Pat, 0, 3, T0, ScreenWindows.HowPage)],
            [Grant(Pat, ScreenWindows.HowPage, "held")]);
        Check(stale is [{ Light: Light.Amber }] && stale[0].State.StartsWith("Patterns FOH: link lost", StringComparison.Ordinal)
              && stale[0].State.EndsWith(" · granted to Patterns FOH (has its address, no page open)", StringComparison.Ordinal),
            $"window lights: a TV plus a stale grant is amber, worst over the viewers, the app named ({stale[0].State})");

        // A grant's page behind the window's revision: amber, switching, whatever the TV says.
        var behind = ScreenWindows.ContextItems([w], [], [Viewer("", 1, 3, T0), Viewer(Pat, 1, 2, T0, ScreenWindows.HowPage)]);
        Check(behind is [{ Light: Light.Amber }] && behind[0].State.StartsWith("Patterns FOH: switching to /clock", StringComparison.Ordinal),
            $"window lights: a grant's page behind the window is amber ({behind[0].State})");

        // A drawing assigned but never reported counts for nothing, as its grant's state does; once it reports, it counts.
        var drawn = Lobby(Draw());
        var assigned = ScreenWindows.ContextItems([drawn], [], [Viewer(Pm, 1, 0, null, ScreenWindows.HowDraw)]);
        var reported = ScreenWindows.ContextItems([drawn], [], [Viewer(Pm, 1, 3, T0, ScreenWindows.HowDraw)]);
        Check(assigned is [{ Light: Light.Grey }] && reported is [{ Light: Light.Green }] && reported[0].State.StartsWith("PlayMap Stage left: showing /clock", StringComparison.Ordinal),
            $"window lights: a drawing counts from its first report ({assigned[0].State} | {reported[0].State})");

        // A viewer of a pass the window no longer holds (its grant withdrawn) counts for nothing.
        var gone = ScreenWindows.ContextItems([Lobby()], [], [Viewer("", 1, 3, T0), Viewer(Pat, 0, 3, T0, ScreenWindows.HowPage)]);
        Check(gone is [{ Light: Light.Green, State: "showing /clock" }], $"window lights: a withdrawn grant's viewer counts for nothing ({gone[0].State})");

        // The claims follow the grants, in the holders' words, at most eight.
        var claims = Enumerable.Range(1, 10).Select(i => new WindowPlacement("lobby", Pat, "Patterns FOH", "screen:" + i, "Screen " + i + " — LED wall")).ToList();
        var said = ScreenWindows.ContextItems([w], [], [Viewer("", 1, 3, T0), Viewer(Pat, 1, 3, T0, ScreenWindows.HowPage)], [Grant(Pat, ScreenWindows.HowPage, "shown", 1)], claims);
        Check(said[0].State.StartsWith("showing /clock · granted to Patterns FOH (shows it) · Patterns FOH says: Screen 1 — LED wall; Patterns FOH says: Screen 2", StringComparison.Ordinal)
              && said[0].State.Split("says:").Length - 1 == WindowPlacements.MaxPerWindow,
            $"window lights: the claim words follow the grants, at most {WindowPlacements.MaxPerWindow} ({said[0].State})");
        Check(!said[0].State.Contains(page.Token, StringComparison.Ordinal) && !said[0].State.Contains(w.Token, StringComparison.Ordinal),
            "window lights: no token in the words");

        Check(WindowGrants.States.All(s => WindowGrants.Short(s, ScreenWindows.HowPage).Length > 0 && WindowGrants.Mark(s).Length == 1)
              && WindowGrants.Mark("shown") == "✓" && WindowGrants.Mark("waiting") == "○" && WindowGrants.Mark("open") == "■" && WindowGrants.Mark("held") == "■"
              && WindowGrants.Mark("sent") == "○" && WindowGrants.Mark("stale") == "△",
            "window lights: every grant state has its few words and a shape (✓ shows it, ■ has it as observed, ○ sent or not linked — not yet observed, △ needs you)");
    }

    static void Frame(Action<bool, string> Check)
    {
        var page = Page();
        var w = Lobby(page);
        var held = new ScreenWindow { Id = "wfoyer1", Name = "foyer", Token = ScreenWindows.NewToken(), Content = "/viewer", Hold = true, Revision = 2 };
        var items = ScreenWindows.ContextItems([w, held], [], [Viewer("", 1, 3, T0)], [Grant(Pat, ScreenWindows.HowPage, "shown", 1)]);
        var facts = ScreenWindows.Facts([w, held], items, [new WindowPlacement("lobby", Pat, "Patterns FOH", "screen:2", "Screen 2 — LED wall")]);
        var views = ScreenWindows.Views([w, held], facts);
        Check(views.Count == 2 && views[0] == new WindowView("lobby", Light.Green, true, false, "/clock", "Patterns FOH", "Patterns FOH says: Screen 2 — LED wall")
              && views[1] is { Name: "foyer", Light: Light.Grey, InStep: false, Hold: true, Showing: ScreenWindows.Holding, Granted: "", On: "" },
            $"frame windows: one view a window, in order, the context item's light, in step only when green ({string.Join(" | ", views)})");

        var snap = new ShowSnapshot { Windows = views };
        var op = Encoding.UTF8.GetString(SnapshotJson.Serialize(snap, FrameAccess.Operator));
        var pub = Encoding.UTF8.GetString(SnapshotJson.Serialize(snap, FrameAccess.Public));
        var pubDetail = Encoding.UTF8.GetString(SnapshotJson.Serialize(snap, FrameAccess.PublicDetail));
        using var doc = JsonDocument.Parse(op);
        var last = doc.RootElement.EnumerateObject().Last();
        Check(last.Name == "windows" && last.Value.GetArrayLength() == 2
              && last.Value[0].GetProperty("name").GetString() == "lobby" && last.Value[0].GetProperty("light").GetString() == "green"
              && last.Value[0].GetProperty("inStep").GetBoolean() && !last.Value[0].GetProperty("hold").GetBoolean()
              && last.Value[0].GetProperty("showing").GetString() == "/clock" && last.Value[0].GetProperty("granted").GetString() == "Patterns FOH"
              && last.Value[0].GetProperty("on").GetString() == "Patterns FOH says: Screen 2 — LED wall"
              && last.Value[1].GetProperty("hold").GetBoolean(),
            "frame windows: the operator frame's last block, name, light, inStep, hold, showing, granted, on");
        Check(!pub.Contains("\"windows\"", StringComparison.Ordinal) && !pubDetail.Contains("\"windows\"", StringComparison.Ordinal),
            "frame windows: the public and public-detail frames carry no windows");
        Check(!op.Contains(w.Token, StringComparison.Ordinal) && !op.Contains(page.Token, StringComparison.Ordinal) && !op.Contains("/w/", StringComparison.Ordinal),
            "frame windows: no token or address in the operator frame");
        var empty = Encoding.UTF8.GetString(SnapshotJson.Serialize(new ShowSnapshot(), FrameAccess.Operator));
        Check(!empty.Contains("\"windows\"", StringComparison.Ordinal), "frame windows: an operator frame with no window carries no windows block, so it reads as it did");
    }

    static void Apps(Action<bool, string> Check)
    {
        IReadOnlyList<ShowApp> heard =
        [
            new("patterns", Pat, "Patterns FOH", ["pages"], true),
            new("playmap", "X", "", ["window-draw"], false),
            new("playmap", "pm000002", "X", ["window-draw"], true),
        ];
        Check(ScreenWindows.FindApp(heard, "Patterns FOH").App?.Id == Pat && ScreenWindows.FindApp(heard, "Patterns FOH", idOnly: true).App is null
              && ScreenWindows.FindApp(heard, "Patterns FOH", idOnly: true).Error.StartsWith("No show app with the id \"Patterns FOH\" on the show network — a proposal names its app by its Show Bus id", StringComparison.Ordinal),
            "apps: a proposal's app is found by its id alone — the name an operator may type is not a proposal's");
        Check(ScreenWindows.FindApp(heard, Pat, idOnly: true).App?.Id == Pat && ScreenWindows.FindApp(heard, "X", idOnly: true).App?.Id == "X",
            "apps: by id, a proposal finds the app the id names, never the app whose name is that word");
        IReadOnlyList<ShowApp> notHeard = [new("playmap", "pm000002", "X", ["window-draw"], true)];
        Check(ScreenWindows.FindApp(notHeard, "X").App?.Id == "pm000002" && ScreenWindows.FindApp(notHeard, "X", idOnly: true).App is null,
            "apps: a node X not heard itself cannot be granted an app that merely has the name X through a proposal (SW22.3's gap)");

        // The desk's list of apps: from one join, its own id and a conflicted id left out, abilities from the key-verified link else the hello.
        var link = new NodeLinkFact("patterns", Pat, NodeProof.Key, "kP", "Patterns FOH", "10.0.0.7", T0, 1, null, Can: ["pages"]);
        var rows = new List<NetworkRow>
        {
            new(new PeerSummary("patterns", Pat, "Patterns FOH", Light.Green, "", "10.0.0.7", 1, false), null, NodeProof.None, link, [], true),
            new(new PeerSummary("playmap", Pm, "PlayMap Stage left", Light.Green, "", "10.0.0.8", 1, false), null, NodeProof.None, null, [], true),
            new(new PeerSummary("main", "abc123abc123", "Hall A", Light.Green, "", "", 1, false), null, NodeProof.None, null, [], true),
            new(new PeerSummary("playmap", "pm000009", "Twin", Light.Amber, "", "", 1, false), null, NodeProof.None, null, [], true),
            new(new PeerSummary("playmap", "bad id!", "Odd", Light.Grey, "", "", 1, false), null, NodeProof.None, null, [], true),
        };
        var view = new NetworkView(rows, 0, []) { Conflicts = [new HeardNode("playmap", "pm000009", "Twin", "10.0.0.9", 1)] };
        var caps = new Dictionary<string, IReadOnlyList<string>> { [Pm] = ["window-draw", "pages"], [Pat] = ["window-draw"] };
        var apps = ShowNetwork.Apps(view, caps, "abc123abc123");
        Check(apps is [{ Id: Pat, Verified: true } p, { Id: Pm, Verified: false } m] && p.Can.SequenceEqual(["pages"]) && m.Can.SequenceEqual(["window-draw"]),
            $"apps: the desk hears each row's app once — its own id, a conflicted id and an invalid id left out; can= from the verified link, else the hello for its kind ({string.Join(", ", apps.Select(a => a.Id + ":" + string.Join('/', a.Can)))})");
    }

    static void Doors(Action<bool, string> Check)
    {
        ShowCommandParser.TryParseLine("window grant lobby " + Pat, out var grant, out _);
        ShowCommandParser.TryParseLine("window withdraw lobby " + Pat, out var withdraw, out _);
        ShowCommandParser.TryParseLine("window show lobby /clock", out var show, out _);
        var lan = IPAddress.Parse("10.0.0.40");
        CommandSender Sender(bool thisPc = false, bool credentialed = false, bool otherSite = false) => new(thisPc ? IPAddress.Loopback : lan, thisPc, credentialed, otherSite);
        Check(PlayoutGate.Grants(grant) && !PlayoutGate.Grants(withdraw) && !PlayoutGate.Grants(show), "doors: a window grant is told apart from every other window line");
        foreach (var door in new[] { CommandDoor.Osc, CommandDoor.Companion })
            Check(PlayoutGate.Check(grant, door, Sender(thisPc: true, credentialed: true), [lan]) == PlayoutGate.GrantWords(door)
                  && PlayoutGate.GrantWords(door).StartsWith("window grant is a person's act — ", StringComparison.Ordinal)
                  && PlayoutGate.Check(show, door, Sender(), []) is null && PlayoutGate.Check(withdraw, door, Sender(), []) is null,
                $"doors: {door} refuses a window grant whoever sends it, this PC and a listed address included, and still takes other window lines");
        foreach (var door in new[] { CommandDoor.Http, CommandDoor.WebSocket })
        {
            Check(PlayoutGate.Check(grant, door, Sender(credentialed: true), []) is null && PlayoutGate.Check(grant, door, Sender(thisPc: true, credentialed: true), []) is null,
                $"doors: {door} takes a grant with the PIN or a control key, from this PC or another machine");
            Check(PlayoutGate.Check(grant, door, Sender(), []) == PlayoutGate.GrantWords(door)
                  && PlayoutGate.Check(grant, door, Sender(thisPc: true), [lan]) == PlayoutGate.GrantWords(door)
                  && PlayoutGate.Check(grant, door, Sender(), [lan]) == PlayoutGate.GrantWords(door)
                  && PlayoutGate.Check(grant, door, Sender(thisPc: true, otherSite: true), [lan]) == PlayoutGate.GrantWords(door, otherSite: true)
                  && PlayoutGate.Check(withdraw, door, Sender(), []) is null && PlayoutGate.Check(show, door, Sender(), []) is null,
                $"doors: {door} refuses a grant with no credential — this PC and a listed address authenticate nobody (Show Bus §12.4) — and from another site's page, in words; other window lines keep their rules");
        }
        Check(PlayoutGate.Check(grant, CommandDoor.Mcp, Sender(), []) is null,
            "doors: MCP passes a grant to the endpoint only to be queued as a proposal (McpEndpoint: an agent proposes, a person decides)");

        var proposal = new SharedProposal("p1", new NodeRef("patterns", Pat, "Patterns FOH"), new NodeRef("main", "abc123abc123", ""), "window grant lobby " + Pat,
            "the operator asked for it", "high", true, SharedProposalState.Pending, T0);
        Check(ProposalDesk.Headline(proposal) == "window grant lobby " + Pat && ProposalDesk.Headline(proposal with { Line = "note hi" }) == "the operator asked for it"
              && ProposalDesk.Headline(proposal with { Reason = "" }) == "window grant lobby " + Pat,
            "doors: the desk shows a window grant's line as its headline, never the peer's reason; other lines keep their reason");
    }

    static void Journal(Action<bool, string> Check)
    {
        var page = Page();
        var draw = Draw();
        var w = Lobby(page, draw);
        var taken = new GrantsTaken();
        Check(taken.Note([w], [Grant(Pat, ScreenWindows.HowPage, "sent"), Grant(Pm, ScreenWindows.HowDraw, "waiting")]).Count == 0,
            "journal: a grant sent and not acknowledged is no row yet");
        var rows = taken.Note([w], [Grant(Pat, ScreenWindows.HowPage, "held"), Grant(Pm, ScreenWindows.HowDraw, "open", 1)]);
        Check(rows is ["Patterns FOH took lobby's address", "PlayMap Stage left took lobby to draw"] && !rows.Any(r => r.Contains(page.Token, StringComparison.Ordinal)),
            $"journal: each grant its app took is one row, never an address ({string.Join(" | ", rows)})");
        Check(taken.Note([w], [Grant(Pat, ScreenWindows.HowPage, "shown", 1), Grant(Pm, ScreenWindows.HowDraw, "stale", 1)]).Count == 0,
            "journal: once per grant, whatever it does next");
        var withdrawn = Lobby(draw);
        Check(taken.Note([withdrawn], [Grant(Pm, ScreenWindows.HowDraw, "shown", 1)]).Count == 0
              && taken.Note([w], [Grant(Pat, ScreenWindows.HowPage, "held")]) is ["Patterns FOH took lobby's address"],
            "journal: a grant that ends is forgotten, so the next grant to that app is told again");
        var again = Page();
        again.MadeUtc = T0.AddSeconds(1);
        Check(taken.Note([Lobby(again, draw)], [Grant(Pat, ScreenWindows.HowPage, "held")]) is ["Patterns FOH took lobby's address"],
            "journal: a grant made again between two ticks (withdrawn and granted inside one second) is a new grant, told again");
    }

    static void Menus(Action<bool, string> Check)
    {
        IReadOnlyList<ShowApp> heard =
        [
            new("patterns", Pat, "Patterns FOH", ["pages"], true),
            new("playmap", Pm, "PlayMap Stage left", ["window-draw"], false),
            new("messenger", "ag000001", "AGMessage Office", [], true),
        ];
        var w = Lobby(Page());
        var grantTo = ScreenWindows.GrantChoices(w, heard);
        Check(grantTo is [{ Label: "PlayMap Stage left (to draw, as it says)", Line: "window grant lobby " + Pm }],
            $"menus: Grant to offers each heard app that can hold a window and does not hold it, by its id ({string.Join(" | ", grantTo)})");
        Check(ScreenWindows.GrantChoices(Lobby(Page("a1"), Page("a2"), Page("a3"), Page("a4")), heard).Count == 0, "menus: a window granted to four offers none");
        Check(ScreenWindows.WithdrawChoices(w) is [{ Label: "Patterns FOH", Line: "window withdraw lobby " + Pat }], "menus: Withdraw names each grant, the line by the app's id");
        var foyer = new ScreenWindow { Id = "wfoyer1", Name = "foyer", Token = ScreenWindows.NewToken() };
        Check(ScreenWindows.WindowsFor([w, foyer], heard[0]) is [{ Label: "foyer", Line: "window grant foyer " + Pat }]
              && ScreenWindows.WindowsFor([w, foyer], heard[2]).Count == 0,
            "menus: the Show network row's Grant a window offers the windows the app does not hold, none for an app that offers neither ability");
        foreach (var c in grantTo.Concat(ScreenWindows.WithdrawChoices(w)).Concat(ScreenWindows.WindowsFor([w, foyer], heard[0])))
            Check(ShowCommandParser.TryParseLine(c.Line, out var cmd, out _) && cmd.Kind == ShowCommandKind.Window, $"menus: \"{c.Line}\" is a line the grammar takes");
    }

    static void Loading(Action<bool, string> Check)
    {
        var shared = ScreenWindows.NewToken();
        var page = Page();
        var notes = new LoadNotes();
        var kept = ScreenWindows.Kept(
        [
            new ScreenWindow { Id = "w1", Name = "lobby", Token = shared, Grants = [page, new WindowGrant { Node = "bad id!", How = ScreenWindows.HowPage, Token = ScreenWindows.NewToken() },
                new WindowGrant { Node = Pm, How = "paint", Name = "PlayMap Stage left" }, new WindowGrant { Node = "pm000003", How = ScreenWindows.HowDraw, Name = new string('n', 70) }] },
            new ScreenWindow { Id = "w2", Name = "foyer", Token = shared },
            new ScreenWindow { Id = "w3", Name = "bad name!", Token = ScreenWindows.NewToken() },
            null,
        ], null, notes);
        var words = notes.Words;
        Check(kept.Count == 2 && kept[0].Grants.Count == 2 && kept[1].Token != shared
              && words.Contains("Windows: window \"bad name!\" was dropped — its name is not a window name")
              && words.Contains("Windows: an empty window entry was dropped")
              && words.Any(n => n.StartsWith("Windows: foyer shared its address with another window and was given a new one", StringComparison.Ordinal))
              && words.Contains("Windows: the grant of lobby to node bad id! was dropped — it names no valid Show Bus id; a person grants it again")
              && words.Contains("Windows: the grant of lobby to PlayMap Stage left was dropped — it says neither page nor draw; a person grants it again")
              && words.Contains($"Windows: the name lobby is granted under was cut to {ScreenWindows.MaxAppName} characters"),
            $"loading: what loading repaired in the windows and their grants is said as it loads ({string.Join(" | ", words)})");
        Check(!words.Any(n => n.Contains(shared, StringComparison.Ordinal) || n.Contains(page.Token, StringComparison.Ordinal)), "loading: no note carries a token");
        var quiet = new LoadNotes();
        ScreenWindows.Kept(kept, null, quiet);
        Check(quiet.Count == 0, "loading: a list that needed nothing says nothing");

        using var file = Scratch.File("window-load-notes", ".json");
        File.WriteAllText(file.Path, JsonSerializer.Serialize(new
        {
            remote = new
            {
                pin = "1234",
                windows = new object[]
                {
                    new { id = "w1", name = "lobby", token = ScreenWindows.NewToken(), grants = new object?[] { null, new { node = Pat, how = "page", token = "short" } } },
                },
            },
        }));
        using var store = new SettingsStore(file.Path);
        Check(store.Settings.Remote.Windows is [{ Name: "lobby", Grants.Count: 0 }]
              && store.Notes.Contains("Windows: an empty grant of lobby was dropped")
              && store.Notes.Contains($"Windows: the grant of lobby to {NodeBindings.Describe("", Pat)} was dropped — its address is not one Stagewatch makes; a person grants it again"),
            $"loading: the settings store repairs the windows as it loads and says so at once — the status line, the show log and Diagnostics read its notes ({string.Join(" | ", store.Notes)})");

        // A window given a new address keeps it: loading writes its repairs back at once, so the next load finds them made and says nothing.
        using var sharing = Scratch.File("window-load-shared", ".json");
        File.WriteAllText(sharing.Path, JsonSerializer.Serialize(new
        {
            remote = new
            {
                pin = "1234",
                windows = new object[] { new { id = "w1", name = "lobby", token = shared }, new { id = "w2", name = "foyer", token = shared } },
            },
        }));
        string given;
        using (var first = new SettingsStore(sharing.Path))
        {
            given = first.Settings.Remote.Windows[1].Token;
            Check(given != shared && first.Notes.Any(n => n.StartsWith("Windows: foyer shared its address with another window and was given a new one", StringComparison.Ordinal)),
                "loading: a window that shared its address is given a new one, and the load says so");
        }
        using var second = new SettingsStore(sharing.Path);
        Check(second.Settings.Remote.Windows is [{ Name: "lobby" }, { Name: "foyer" } foyer] && foyer.Token == given && second.Notes.Count == 0,
            $"loading: the next load keeps the new address and says nothing again ({string.Join(" | ", second.Notes)})");
    }
}
