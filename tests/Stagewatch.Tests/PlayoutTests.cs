using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Show;

/// <summary>
/// Playout links' model and grammar: settings schema 2 and the 2.17 Patterns keys kept as link "patterns"'s mirror, the names a
/// link may take, running-order cues as files keep them, the <c>playout</c> verb in the one grammar, which link it reaches, its
/// risk, its rows in the context, its journal kind and the clock's sample times; then the wire's primitives (PlayoutWire,
/// PlayoutLineReader, PlayoutStateReader). The link reading through them is PlayoutLinkTests'. Fields first, words beside them.
/// </summary>
static class PlayoutTests
{
    /// <summary>A 2.17 settings file's integrations, as 2.17 writes them: every Patterns key present.</summary>
    const string Legacy217 = """
        {"schemaVersion":1,"integrations":{"patterns":true,"patternsHost":"10.0.0.5","patternsPort":9697,"patternsToken":"c2VjcmV0LWJsb2I=","patternsNextOnEnd":true}}
        """;

    public static void Run(Action<bool, string> Check)
    {
        Settings(Check);
        Names(Check);
        Cues(Check);
        Grammar(Check);
        Targets(Check);
        Context(Check);
        Clock(Check);
        Finder(Check);
        Proofs(Check);
        Words(Check);
        Lines(Check);
        States(Check);
    }

    static JsonNode Integrations(string path) => JsonNode.Parse(File.ReadAllText(path))!["integrations"]!;

    static void Settings(Action<bool, string> Check)
    {
        // A 2.17 file: its Patterns setup becomes link "patterns" whole, first in the list, so link:patterns and clock:patterns keep their ids.
        using (var file = Scratch.File("playout-legacy", ".json"))
        {
            File.WriteAllText(file.Path, Legacy217);
            using (var store = new SettingsStore(file.Path))
            {
                var s = store.Settings;
                Check(s.SchemaVersion == 2 && AppSettings.CurrentSchemaVersion == 2, "playout settings: a 2.17 file loads as schema 2");
                Check(s.Integrations.PlayoutLinks is [{ Name: "patterns", Enabled: true, Kind: "auto", Host: "10.0.0.5", Port: 9697,
                        TokenProtected: "c2VjcmV0LWJsb2I=", Node: "", Transport: "auto", FollowVt: true, NextOnEnd: true }],
                      "playout settings: the 2.17 Patterns keys become link patterns, host, port, token and next-on-end kept");
                Check(s.Integrations.PlayoutLeadMs == PlayoutRules.DefaultLeadMs && PlayoutRules.DefaultLeadMs == 300, "playout settings: the timed GO's lead starts at 300 ms");
                store.SaveNow();
            }
            var saved = Integrations(file.Path);
            Check(saved["playoutLinks"] is JsonArray { Count: 1 } && (string?)saved["playoutLinks"]![0]!["name"] == "patterns"
                  && (bool?)saved["patterns"] == true && (string?)saved["patternsHost"] == "10.0.0.5" && (int?)saved["patternsPort"] == 9697
                  && (string?)saved["patternsToken"] == "c2VjcmV0LWJsb2I=" && (bool?)saved["patternsNextOnEnd"] == true,
                  "playout settings: a save writes the links and the 2.17 keys as link patterns's mirror");

            // A change to link patterns reaches the mirror at the next save.
            using (var store = new SettingsStore(file.Path))
            {
                store.Update(x => { if (PlayoutRules.PatternsLink(x.Integrations) is { } own) { own.Host = "10.0.0.6"; own.Enabled = false; } });
                store.SaveNow();
            }
            saved = Integrations(file.Path);
            Check((string?)saved["patternsHost"] == "10.0.0.6" && (bool?)saved["patterns"] == false,
                  "playout settings: the mirror follows link patterns as it is edited");

            // A rollback: 2.17 drops what it does not know (playoutLinks) and keeps its keys; the next 2.20 load makes link patterns from them.
            var rolled = JsonNode.Parse(File.ReadAllText(file.Path))!;
            rolled["integrations"]!.AsObject().Remove("playoutLinks");
            rolled["integrations"]!["patternsHost"] = "10.0.0.7";
            File.WriteAllText(file.Path, rolled.ToJsonString());
            using (var store = new SettingsStore(file.Path))
                Check(store.Settings.Integrations.PlayoutLinks is [{ Name: "patterns", Host: "10.0.0.7", Enabled: false, Port: 9697 }],
                      "playout settings: after a rollback to 2.17, link patterns comes back as 2.17 left it");
        }

        // A 2.17 file whose Patterns was never set up makes no link.
        using (var file = Scratch.File("playout-unused", ".json"))
        {
            File.WriteAllText(file.Path, """{"schemaVersion":1,"integrations":{"patterns":false,"patternsHost":"127.0.0.1","patternsPort":9697,"patternsToken":"","patternsNextOnEnd":false}}""");
            using var store = new SettingsStore(file.Path);
            Check(store.Settings.Integrations.PlayoutLinks.Count == 0, "playout settings: a Patterns nobody set up makes no link");
        }

        // A fresh install has no link, and its mirror says Patterns is off and nothing more.
        using (var file = Scratch.File("playout-fresh", ".json"))
        {
            using (var store = new SettingsStore(file.Path))
            {
                Check(store.Settings.Integrations.PlayoutLinks.Count == 0 && store.Settings.SchemaVersion == 2, "playout settings: a fresh install starts with no link");
                store.SaveNow();
            }
            var saved = Integrations(file.Path);
            Check((bool?)saved["patterns"] == false && saved["patternsHost"] is null && saved["patternsToken"] is null,
                  "playout settings: with no link patterns the mirror says Patterns is off, and nothing more");
        }

        // Keys an older Stagewatch changed while the links stayed (they differ from the mirror) win for link patterns alone.
        using (var file = Scratch.File("playout-older", ".json"))
        {
            File.WriteAllText(file.Path, """
                {"schemaVersion":2,"integrations":{"patterns":true,"patternsHost":"10.0.0.9","patternsPort":9698,"patternsToken":"bmV3","patternsNextOnEnd":false,
                 "playoutLinks":[{"name":"patterns","enabled":false,"kind":"patterns","host":"10.0.0.5","port":9697,"token":"b2xk","node":"a1b2c3d4","transport":"tcp","followVt":false,"nextOnEnd":true},
                                 {"name":"stage-left","enabled":true,"kind":"playmap","host":"10.0.0.12","port":9941,"token":"","node":"b2c3d4e5","transport":"wss","followVt":true,"nextOnEnd":false}]}}
                """);
            using var store = new SettingsStore(file.Path);
            var links = store.Settings.Integrations.PlayoutLinks;
            Check(links is [{ Name: "patterns", Enabled: true, Host: "10.0.0.9", Port: 9698, TokenProtected: "bmV3", NextOnEnd: false, Kind: "patterns", Node: "a1b2c3d4", Transport: "tcp", FollowVt: false },
                           { Name: "stage-left", Host: "10.0.0.12", Port: 9941, Kind: "playmap", Node: "b2c3d4e5", Transport: "wss" }],
                  "playout settings: 2.17 keys that differ from the mirror win for link patterns, its own fields and the other links untouched");
        }

        // A file that holds links and no 2.17 key at all: the keys say nothing, and the links stand as written.
        using (var file = Scratch.File("playout-nokeys", ".json"))
        {
            File.WriteAllText(file.Path, """
                {"schemaVersion":2,"integrations":{"playoutLinks":[{"name":"patterns","enabled":true,"kind":"auto","host":"10.0.0.5","port":9697,"token":"","node":"","transport":"auto","followVt":true,"nextOnEnd":true}]}}
                """);
            using var store = new SettingsStore(file.Path);
            Check(store.Settings.Integrations.PlayoutLinks is [{ Name: "patterns", Enabled: true, Host: "10.0.0.5", NextOnEnd: true }],
                  "playout settings: absent 2.17 keys change nothing");
        }

        // A PlayMap link named patterns is never mirrored (2.17 would dial it as a Patterns desk), and keeps its kind on reload.
        var i = new IntegrationSettings { PlayoutLinks = [new PlayoutLinkSettings { Name = "patterns", Kind = "playmap", Host = "10.0.0.12", Port = 9941 }] };
        PlayoutRules.Upgrade(i);
        Check(i.LegacyPatterns == false && i.LegacyPatternsHost is null && i.PlayoutLinks is [{ Kind: "playmap", Host: "10.0.0.12", Port: 9941 }],
              "playout settings: a PlayMap link named patterns has no 2.17 mirror and stays PlayMap");
        PlayoutRules.Upgrade(i);
        Check(i.PlayoutLinks is [{ Kind: "playmap", Enabled: true }], "playout settings: loading its own mirror again changes nothing");

        // Sanitised as they load: names, kinds, transports, ports, hosts and node ids; the first eight, none twice; each loss said.
        var many = Enumerable.Range(1, 10).Select(n => new PlayoutLinkSettings { Name = "link-" + n }).ToList();
        var messy = new List<PlayoutLinkSettings?>
        {
            null,
            new() { Name = " Stage-Left ", Kind = "PLAYMAP", Port = 0, Transport = "udp", Node = "not an id!", TokenProtected = "c2VjcmV0LXRva2Vu" },
            new() { Name = "stage-left", Host = "10.0.0.99" },
            new() { Name = "vmix" }, new() { Name = "go" }, new() { Name = "stage_left" }, new() { Name = "-x\u0007" },
            new() { Name = "fine", Host = "10.0.0.1 /x", TokenProtected = "c2VjcmV0LXRva2Vu" }, new() { Name = "blank", Host = "  ", Transport = " ", Kind = "" },
        };
        var said = new LoadNotes();
        var clean = PlayoutRules.Sanitise(messy, said);
        Check(clean is [{ Name: "stage-left", Enabled: false, Kind: "playmap", Port: 9941, Transport: "wss", Node: "", Host: "127.0.0.1", TokenProtected: "c2VjcmV0LXRva2Vu" },
                        { Name: "fine", Enabled: false, Host: "127.0.0.1", TokenProtected: "c2VjcmV0LXRva2Vu" },
                        { Name: "blank", Enabled: true, Host: "127.0.0.1", Kind: "auto", Transport: "auto" }],
              "playout settings: a name lower-cased and kept once, an unknown transport read as wss, a bad port or host switched off at its default, bad names left out");
        var words = said.Words;
        Check(words.Count == 9
              && words.Any(w => w.Contains("dials wss only", StringComparison.Ordinal) && w.Contains("\"udp\"", StringComparison.Ordinal))
              && words.Any(w => w.Contains("stage-left is switched off at port 9941: 0 isn't a port", StringComparison.Ordinal))
              && words.Any(w => w.Contains("stage-left will find its node again", StringComparison.Ordinal))
              && words.Any(w => w.StartsWith("a second playout link called stage-left left out", StringComparison.Ordinal))
              && words.Any(w => w.StartsWith("playout link \"vmix\" left out", StringComparison.Ordinal) && w.Contains("another integration", StringComparison.Ordinal))
              && words.Any(w => w.StartsWith("playout link \"go\" left out", StringComparison.Ordinal))
              && words.Any(w => w.StartsWith("playout link \"-x\" left out", StringComparison.Ordinal))
              && words.Any(w => w.Contains("fine is switched off at 127.0.0.1: \"10.0.0.1 /x\" isn't an address it can dial", StringComparison.Ordinal))
              && words.All(w => !w.Contains("c2VjcmV0", StringComparison.Ordinal) && !w.Any(char.IsControl)),
              $"playout settings: each link left out or changed is said by name, never its token, with no control character ({string.Join(" | ", words)})");
        var eight = new LoadNotes();
        Check(PlayoutRules.Sanitise(many, eight).Count == PlayoutRules.Max && PlayoutRules.Max == 8
              && eight.Words is [var ninth, var tenth] && ninth.Contains("link-9 left out", StringComparison.Ordinal) && tenth.Contains("link-10", StringComparison.Ordinal),
              "playout settings: at most eight links, the ninth and tenth said");

        // A kind this Stagewatch does not know (a later one's) is never dialled as a guess: kept, switched off, said.
        var later = new LoadNotes();
        Check(PlayoutRules.Sanitise([new PlayoutLinkSettings { Name = "pm", Kind = "agmessage", Transport = "tls", Enabled = true }], later)
                  is [{ Enabled: false, Kind: "auto", Transport: "wss" }]
              && later.Words.Any(w => w.Contains("its kind \"agmessage\" isn't one", StringComparison.Ordinal))
              && later.Words.Any(w => w.Contains("its transport \"tls\" isn't one", StringComparison.Ordinal)),
              "playout settings: an unknown kind switches its link off, and transport tls is never auto");
        foreach (var (word, read) in new[] { ("auto", "auto"), ("TCP", "tcp"), ("wss", "wss"), ("", "auto"), ("tls", "wss"), ("link", "wss") })
            Check(PlayoutRules.Sanitise([new PlayoutLinkSettings { Name = "pm", Transport = word }]) is [{ } t] && t.Transport == read,
                  $"playout settings: transport \"{word}\" is read as {read}");

        // One node, one link: the first link that names a node keeps it, a later one finds its node again.
        var nodes = new LoadNotes();
        var shared = PlayoutRules.Sanitise([new PlayoutLinkSettings { Name = "stage-left", Node = "a1b2c3d4", Kind = "playmap" },
                                            new PlayoutLinkSettings { Name = "stage-left-2", Node = "a1b2c3d4", Kind = "playmap" },
                                            new PlayoutLinkSettings { Name = "stage-right", Node = "b2c3d4e5", Kind = "playmap" }], nodes);
        Check(shared is [{ Name: "stage-left", Node: "a1b2c3d4" }, { Name: "stage-left-2", Node: "" }, { Name: "stage-right", Node: "b2c3d4e5" }]
              && nodes.Words is [var twice] && twice.Contains("node a1b2c3d4 is link stage-left's", StringComparison.Ordinal),
              "playout settings: two links never hold one node");

        // The notes are bounded: past sixteen, one line says how many more.
        var flood = new LoadNotes();
        PlayoutRules.Sanitise(Enumerable.Range(0, 20).Select(n => new PlayoutLinkSettings { Name = "Bad_" + n }), flood);
        Check(flood.Count == 20 && flood.Words.Count == LoadNotes.Max + 1 && LoadNotes.Max == 16 && flood.Words[^1] == "and 4 more",
              "playout settings: at most sixteen notes, then how many more");
        Check(PlayoutRules.ValidHost("10.0.0.5") && PlayoutRules.ValidHost("stage-left.local") && PlayoutRules.ValidHost("fe80::1") && PlayoutRules.ValidHost("[::1]")
              && !PlayoutRules.ValidHost("10.0.0.5:9697") && !PlayoutRules.ValidHost("[::1]:9697") && !PlayoutRules.ValidHost("http://10.0.0.5") && !PlayoutRules.ValidHost("me@host") && !PlayoutRules.ValidHost(""),
              "playout settings: a host is a name or an address, its port a field of its own");
        // The 2.17 keys keep the one address rule: a host with its port in it (2.17 never dialled it) is never turned into this PC
        // and dialled; link patterns is kept switched off and the typed host said.
        using (var file = Scratch.File("playout-hostport", ".json"))
        {
            File.WriteAllText(file.Path, """
                {"schemaVersion":1,"integrations":{"patterns":true,"patternsHost":"10.0.0.5:9697","patternsPort":9697,"patternsToken":"c2VjcmV0LWJsb2I=","patternsNextOnEnd":false}}
                """);
            using (var store = new SettingsStore(file.Path))
            {
                Check(store.Settings.Integrations.PlayoutLinks is [{ Name: "patterns", Enabled: false, Host: "127.0.0.1", Port: 9697, TokenProtected: "c2VjcmV0LWJsb2I=" }]
                      && store.Notes is [var hostNote] && hostNote.Contains("patterns is switched off at 127.0.0.1: \"10.0.0.5:9697\" isn't an address it can dial", StringComparison.Ordinal)
                      && !hostNote.Contains("c2VjcmV0", StringComparison.Ordinal),
                      "playout settings: a 2.17 host with its port in it leaves link patterns switched off, the host said, never dialled at this PC");
                store.SaveNow();
            }
            var saved = Integrations(file.Path);
            Check((bool?)saved["patterns"] == false && (string?)saved["patternsHost"] == "127.0.0.1", "playout settings: that link's mirror says Patterns is off");
            using (var again = new SettingsStore(file.Path))
                Check(again.Notes.Count == 0 && again.Settings.Integrations.PlayoutLinks is [{ Enabled: false }], "playout settings: once saved, the next load has nothing to say");
        }
        var sameRule = new LoadNotes();
        Check(PlayoutRules.Sanitise([new PlayoutLinkSettings { Name = "patterns", Host = "10.0.0.5:9697", Enabled = true }], sameRule) is [{ Enabled: false, Host: "127.0.0.1" }]
              && sameRule.Words is [var sameWords] && sameWords.Contains("\"10.0.0.5:9697\"", StringComparison.Ordinal),
              "playout settings: a link's own host with its port in it follows the same rule as the 2.17 key");
        var port0 = new IntegrationSettings { LegacyPatterns = true, LegacyPatternsPort = 0 };
        var port0Notes = new LoadNotes();
        PlayoutRules.Upgrade(port0, port0Notes);
        Check(port0.PlayoutLinks is [{ Name: "patterns", Enabled: false, Port: 9697 }] && port0Notes.Words.Any(w => w.Contains("0 isn't a port", StringComparison.Ordinal)),
              "playout settings: a 2.17 port out of range switches link patterns off, said");
        var blankKeys = new IntegrationSettings { LegacyPatterns = false, LegacyPatternsHost = " ", LegacyPatternsPort = 9697, LegacyPatternsToken = "", LegacyPatternsNextOnEnd = false };
        PlayoutRules.Upgrade(blankKeys);
        Check(blankKeys.PlayoutLinks.Count == 0, "playout settings: a blank 2.17 host is this PC, as 2.17 dials it, and makes no link");

        var lead = new IntegrationSettings { PlayoutLeadMs = 50 };
        PlayoutRules.Upgrade(lead);
        var lead2 = new IntegrationSettings { PlayoutLeadMs = 5000 };
        PlayoutRules.Upgrade(lead2);
        Check(lead.PlayoutLeadMs == 100 && lead2.PlayoutLeadMs == 2000, "playout settings: the lead stays within 100–2000 ms");

        // A 2.17 key that makes link patterns in a full list: the link comes first and the last gives way, said.
        var full = new IntegrationSettings { PlayoutLinks = many.Take(8).ToList(), LegacyPatterns = true, LegacyPatternsHost = "10.0.0.5" };
        var fullNotes = new LoadNotes();
        PlayoutRules.Upgrade(full, fullNotes);
        Check(full.PlayoutLinks.Count == 8 && full.PlayoutLinks[0] is { Name: "patterns", Enabled: true, Host: "10.0.0.5" } && full.PlayoutLinks[^1].Name == "link-7"
              && fullNotes.Words is [var gaveWay] && gaveWay.StartsWith("playout link link-8 left out to make room for link patterns", StringComparison.Ordinal),
              "playout settings: link patterns made in a full list comes first, the last link gives way and is said");

        // Keys an older Stagewatch changed take the name back from a PlayMap link, said.
        var taken = new IntegrationSettings { PlayoutLinks = [new PlayoutLinkSettings { Name = "patterns", Kind = "playmap", Node = "a1b2c3d4", Transport = "wss" }], LegacyPatterns = true };
        var takenNotes = new LoadNotes();
        PlayoutRules.Upgrade(taken, takenNotes);
        Check(taken.PlayoutLinks is [{ Kind: "auto", Node: "", Transport: "auto", Enabled: true }] && takenNotes.Words is [var back] && back.Contains("a Patterns desk's again", StringComparison.Ordinal),
              "playout settings: 2.17 keys an older Stagewatch changed take link patterns back from a PlayMap node, said");

        // The Settings box writes link patterns: nothing for a box nobody touched, made when ticked, refused with words when full.
        var box = new IntegrationSettings();
        Check(PlayoutRules.EditPatterns(box, false, "", PlayoutRules.PatternsPort, null, false) is null && box.PlayoutLinks.Count == 0,
              "playout settings: an untouched Patterns box makes no link");
        Check(PlayoutRules.EditPatterns(box, true, " 10.0.0.5 ", 9697, "dG9r", true) is null
              && box.PlayoutLinks is [{ Name: "patterns", Enabled: true, Host: "10.0.0.5", TokenProtected: "dG9r", NextOnEnd: true }],
              "playout settings: a ticked Patterns box makes link patterns");
        Check(PlayoutRules.EditPatterns(box, true, "10.0.0.6", 0, null, false) is null && box.PlayoutLinks is [{ Host: "10.0.0.6", Port: 9697, TokenProtected: "dG9r" }],
              "playout settings: a token left as it was is kept, and a port out of range keeps the one it had");
        Check(PlayoutRules.EditPatterns(box, false, "10.0.0.7:9697", 9697, null, false) is { } badHost && badHost.Contains("its own box", StringComparison.Ordinal)
              && box.PlayoutLinks is [{ Enabled: true, Host: "10.0.0.6" }],
              "playout settings: an address with its port in it is said, and nothing changes");
        var crowded = new IntegrationSettings { PlayoutLinks = many.Take(8).Select(l => l.Clone()).ToList() };
        Check(PlayoutRules.EditPatterns(crowded, true, "10.0.0.5", 9697, null, false) is { } full8 && full8.Contains("8 playout links", StringComparison.Ordinal)
              && crowded.PlayoutLinks.Count == 8 && PlayoutRules.PatternsLink(crowded) is null,
              "playout settings: no room for link patterns is said, and nothing changes");

        // The settings JSON names every field.
        var json = JsonSerializer.Serialize(new PlayoutLinkSettings { Name = "stage-left" });
        Check(new[] { "\"name\"", "\"enabled\"", "\"kind\"", "\"host\"", "\"port\"", "\"token\"", "\"node\"", "\"transport\"", "\"followVt\"", "\"nextOnEnd\"" }
              .All(f => json.Contains(f, StringComparison.Ordinal)), "playout settings: a link's fields as the file keeps them");
    }

    static void Names(Action<bool, string> Check)
    {
        foreach (var good in new[] { "patterns", "stage-left", "a", "pm2", new string('x', 24) })
            Check(PlayoutRules.ValidName(good), $"playout names: {good} may name a link");
        foreach (var action in PlayoutRules.Actions)
            Check(PlayoutRules.NameProblem(action) is { } why && why.Contains("one of its commands", StringComparison.Ordinal), $"playout names: {action} is an action word, never a link");
        foreach (var slug in new[] { "powerpoint", "presenter-pcs", "vmix", "speech", "time-server", "ltc", "ltc-in", "cue-lights", "triggers", "rooms", "assistant" })
            Check(PlayoutRules.NameProblem(slug) is { } why && why.Contains("another integration", StringComparison.Ordinal), $"playout names: {slug} is another integration's, never a link's");
        foreach (var bad in new[] { "", "Stage", "stage left", "stage_left", "-x", new string('x', 25), "ü" })
            Check(!PlayoutRules.ValidName(bad), $"playout names: \"{bad}\" is no link's name");
        Check(PlayoutRules.Actions.SequenceEqual(["end", "restart", "play", "pause", "stop", "seek", "cue", "go"]), "playout names: the action words are the design's eight");
        Check(PlayoutRules.Label("stage-left") == "Stage left" && PlayoutRules.Label("patterns") == "Patterns", "playout names: a link's name as people read it");
        Check(PlayoutRules.LicenceWords == "playout links need Stagewatch Production" && !FeatureGate.AllowsPlayout(FeatureTier.Pro) && FeatureGate.AllowsPlayout(FeatureTier.Production),
              "playout names: the licence's words name the tier the links need");
    }

    static void Cues(Action<bool, string> Check)
    {
        PlayoutCue Cue(string link, string list, string item, string label = "Show · Intro") => new() { Link = link, List = list, Item = item, Label = label };
        var item = new RundownItem
        {
            Title = "Intro",
            Playout =
            [
                Cue("Stage-Left", "pl1a2b3c4", "it9f8e7d6"),
                Cue("patterns", "", "c0ffee12", "Cue 12 Walk-in"),
                Cue("stage-left", "pl1a2b3c4", "bad id"),
                Cue("stage-left", "pl1a2b3c4", "it1", new string('x', 81)),
                Cue("stage-left", "pl1a2b3c4", "it2", "line\nbreak"),
                Cue("vmix", "pl1", "it3"),
                Cue("stage-left", "pl 1", "it4"),
                Cue("stage-left", "pl1", "it5", new string('y', 80)),
                Cue("stage-left", "pl1", "it6"),
                Cue("stage-left", "pl1", "it7"),
            ],
        };
        using (var file = Scratch.File("playout-show", ShowFile.Extension))
        {
            var settings = new AppSettings();
            settings.Rundown.Items.Add(item);
            ShowFile.FromSettings(settings, "Gala").Save(file.Path);
            var loaded = ShowFile.Load(file.Path).Rundown.Items[0].Playout;
            Check(loaded.Count == PlayoutRules.MaxCuesPerItem && PlayoutRules.MaxCuesPerItem == 4
                  && loaded.Select(c => c.Item).SequenceEqual(["it9f8e7d6", "c0ffee12", "it5", "it6"]) && loaded[0].Link == "stage-left",
                  "playout cues: a show file keeps four sound cues an item, a bad id, a long or broken label, a reserved link and a fifth left out");
            Check(loaded[1] is { List: "", Item: "c0ffee12", Label: "Cue 12 Walk-in", Standby: false, Together: false },
                  "playout cues: a Patterns cue names one cue, its standby off unless asked");
            Check(ShowFile.Load(file.Path).Notes is [var cueNote] && cueNote.StartsWith("6 playout cues left out of item 1 (Intro) in running order Main", StringComparison.Ordinal),
                  "playout cues: a show file says what it left out");
        }

        // A settings file's running orders are input too.
        using (var file = Scratch.File("playout-cues", ".json"))
        {
            File.WriteAllText(file.Path, """
                {"schemaVersion":2,"rundown":{"items":[{"title":"VT","durationSeconds":60,"playout":[{"link":"stage-left","list":"pl1","item":"it1","label":"Intro","standby":true,"together":true},{"link":"go","list":"","item":"x"},null]}]},
                 "otherRundowns":[{"name":"Day 2","rundown":{"items":[{"title":"VT","durationSeconds":60,"playout":[{"link":"stage-left","list":"","item":"bad id"}]}]}}]}
                """);
            using var store = new SettingsStore(file.Path);
            Check(store.Settings.Rundown.Items[0].Playout is [{ Link: "stage-left", List: "pl1", Item: "it1", Label: "Intro", Standby: true, Together: true }]
                  && store.Settings.OtherRundowns[0].Rundown.Items[0].Playout.Count == 0,
                  "playout cues: a settings file's running orders keep only sound cues");
            Check(store.Notes.Count == 2 && store.Notes[0].StartsWith("1 playout cue left out of item 1 (VT) in running order Main", StringComparison.Ordinal)
                  && store.Notes[1].StartsWith("1 playout cue left out of item 1 (VT) in running order Day 2", StringComparison.Ordinal),
                  $"playout cues: each item that loses a cue is said by its number, title and running order ({string.Join(" | ", store.Notes)})");
        }

        // A clone is a copy: changing it changes nothing in the item it came from.
        var source = new RundownItem { Playout = [Cue("stage-left", "pl1", "it1")] };
        var copy = source.Clone();
        copy.Playout[0].Item = "it2";
        copy.Playout.Add(Cue("patterns", "", "c1"));
        Check(source.Playout is [{ Item: "it1" }] && copy.Playout.Count == 2, "playout cues: an item's clone has cues of its own");

        // A sheet update keeps the item's own cues (a sheet has none).
        var current = new List<RundownItem> { new() { Title = "Intro", Playout = [Cue("stage-left", "pl1", "it1")] } };
        var merged = SheetImport.Update(current, [new RundownItem { Title = "Intro", DurationSeconds = 90 }]);
        Check(merged.Items is [{ DurationSeconds: 90, Playout: [{ Item: "it1" }] }] && !ReferenceEquals(merged.Items[0].Playout[0], current[0].Playout[0]),
              "playout cues: a sheet update keeps the item's cues");
    }

    static ShowCommand? Parse(string line, out string error)
    {
        var t = CloudAdvisor.Tokenize(line);
        if (ShowCommandParser.TryParse(t[0], t.Skip(1).ToList(), out var c, out error)) return c;
        return null;
    }

    static void Grammar(Action<bool, string> Check)
    {
        PlayoutArgs? Args(string line) => Parse(line, out _) is { Kind: ShowCommandKind.Playout } c ? c.Get<PlayoutArgs>() : null;
        double Number(string line) => Parse(line, out _)?.Number ?? -1;

        Check(Args("playout pause") == new PlayoutArgs("", "pause", "", "", false), "playout grammar: playout pause names no link");
        Check(Args("playout stage-left pause") == new PlayoutArgs("stage-left", "pause", "", "", false), "playout grammar: a first word that is no action names the link");
        Check(Args("PLAYOUT Stage-Left PAUSE") == new PlayoutArgs("stage-left", "pause", "", "", false), "playout grammar: words in any case");
        Check(Args("playout end") is { Action: "end" } && Number("playout end") == 10 && Number("playout stage-left end 30") == 30,
              "playout grammar: end takes the seconds before the clip's end, 10 unless said");
        Check(Number("playout seek 1:23") == 83 && Number("playout seek 0:00:07") == 7 && Args("playout seek 45") is { Action: "seek" }, "playout grammar: seek takes seconds, m:ss or h:mm:ss");
        foreach (var t in new[] { "restart", "play", "stop" })
            Check(Args("playout " + t) is { } a && a.Action == t, $"playout grammar: playout {t}");
        Check(Args("playout cue pl1a2b3c4 it9f8e7d6") == new PlayoutArgs("", "cue", "pl1a2b3c4", "it9f8e7d6", false), "playout grammar: cue <playlist> <item>");
        Check(Args("playout patterns cue c0ffee12") == new PlayoutArgs("patterns", "cue", "", "c0ffee12", false), "playout grammar: cue <cue>");
        Check(Args("playout stage-left go show intro together") == new PlayoutArgs("stage-left", "go", "show", "intro", true), "playout grammar: go <playlist> <item> together");
        Check(Args("playout go show") == new PlayoutArgs("", "go", "", "show", false), "playout grammar: go with one word");
        Check(Args("playout go \"Main show\" #4") == new PlayoutArgs("", "go", "Main show", "#4", false), "playout grammar: a quoted name is one word, and PlayMap's #4");
        Check(Parse("playout stage-left go show intro together", out _) is { } packed && packed.Text == "stage-left\u0001go\u0001show\u0001intro\u0001together"
              && new ShowCommand(ShowCommandKind.Playout, Text: packed.Text).Get<PlayoutArgs>() == packed.Get<PlayoutArgs>(),
              "playout grammar: the packed text reads back as the same arguments");

        foreach (var (line, words) in new[]
        {
            ("playout", "playout takes end, restart, play, pause, stop, seek, cue or go"),
            ("playout jump", "playout takes"),
            ("playout stage-left", "playout takes"),
            ("playout stage-left jump", "playout takes"),
            ("playout pause stage-left", "takes nothing after it"),
            ("playout vmix pause", "another integration"),
            ("playout stage_left pause", "a-z, 0-9 and -"),
            ("playout end 0", "1-3600"),
            ("playout end 3601", "1-3600"),
            ("playout end 10 20", "nothing more"),
            ("playout seek", "seek takes"),
            ("playout seek 1:00 2:00", "seek takes"),
            ("playout seek soon", "seek takes"),
            ("playout cue", "cue takes"),
            ("playout cue a b c", "cue takes"),
            ("playout go", "go takes"),
            ("playout go together", "go takes"),
            ("playout go a b c", "go takes"),
            ("playout go " + new string('x', 65), "go takes"),
        })
            Check(Parse(line, out var error) is null && error.Contains(words, StringComparison.Ordinal), $"playout grammar: \"{(line.Length > 40 ? line[..40] + "…" : line)}\" is refused with words ({error})");
        Check(!ShowCommandParser.TryParse("playout", ["cue", "pl1", "it\u0001x"], out _, out _) && !ShowCommandParser.TryParse("playout", ["go", "a\nb"], out _, out _),
              "playout grammar: a word with a control character or a line break is refused");

        Check(Parse("patterns pause", out _) is { Kind: ShowCommandKind.Patterns, Text: "pause" }, "playout grammar: patterns pause stays the Patterns verb");
        Check(ShowCommandParser.Help.Contains("playout [link] go <playlist> [item] [together] | ", StringComparison.Ordinal)
              && ShowCommandParser.Help.Contains("playout [link] restart|play|pause|stop", StringComparison.Ordinal)
              && ShowCommandParser.Help.Split("together").Length == 2,
              "playout grammar: the help names the verb's forms, together once — on a playlist's GO, which the executor times where a clock allows");
        Check((int)ShowCommandKind.Playout == (int)ShowCommandKind.Window + 1, "playout grammar: ShowCommandKind.Playout appended after Window");
        // Each kind appended after the last (old journals keep their numbers): Playout after ClockCheck, then Trigger after it.
        Check((int)JournalKind.Playout == (int)JournalKind.ClockCheck + 1 && (int)JournalKind.Trigger == (int)JournalKind.Playout + 1
              && Enum.GetValues<JournalKind>()[^1] == JournalKind.Trigger,
              "playout grammar: JournalKind.Playout appended after ClockCheck, and JournalKind.Trigger after it");

        // Risk: every action is High, decided here, never by a model; the copilot's check reads the line in the one grammar.
        foreach (var line in new[] { "playout pause", "playout stage-left end 10", "playout seek 1:00", "playout cue pl1 it1", "playout go c1", "playout patterns go show intro together" })
            Check(Copilot.Check(line) is { Kind: ShowCommandKind.Playout } c && Copilot.Classify(c) == CopilotRisk.High, $"playout grammar: {line} is high risk");
        Check(Copilot.ParsePlan("""{"reply":"ok","question":"","steps":[{"command":"playout stage-left go show intro","why":"start the VT"}]}""").Steps is [{ Risk: CopilotRisk.High }],
              "playout grammar: a copilot step that drives playout is never pre-ticked");
        Trust(Check);
    }

    /// <summary>
    /// <c>playout &lt;link&gt; trust &lt;hex&gt;</c> (Show Bus 13.8.3): a kind appended last with its parser verb and help line, High risk,
    /// never among the verbs a peer or an AI may propose, never a step of a plan — and refused at every door and by the executors,
    /// since only a person on the desk trusts a certificate, beside its fingerprint (PlayoutGateTests holds the doors).
    /// </summary>
    static void Trust(Action<bool, string> Check)
    {
        Check(Enum.GetValues<ShowCommandKind>()[^1] == ShowCommandKind.PlayoutTrust && (int)ShowCommandKind.PlayoutTrust == (int)ShowCommandKind.Playout + 1,
              "playout trust: ShowCommandKind.PlayoutTrust appended after Playout");
        Check(Parse("playout stage-left trust 03E9 b152", out _) is { Kind: ShowCommandKind.PlayoutTrust } t
              && t.Get<PlayoutTrustArgs>() == new PlayoutTrustArgs("stage-left", "03e9b152")
              && Parse("playout stage-left trust sha256:" + new string('a', 64), out _)?.Get<PlayoutTrustArgs>() is { Hex.Length: 64 },
              "playout trust: names its link and reads the hex as typed — the short form's spaces, either case, sha256: left off");
        foreach (var (line, words) in new[]
        {
            ("playout trust 03e9b152", "playout trust names its link"),
            ("playout stage-left trust", "8 to 64 hex digits"),
            ("playout stage-left trust 03e9b15", "8 to 64 hex digits"),
            ("playout stage-left trust 03e9b15g", "8 to 64 hex digits"),
            ("playout stage-left trust " + new string('a', 65), "8 to 64 hex digits"),
        })
            Check(Parse(line, out var error) is null && error.Contains(words, StringComparison.Ordinal),
                  $"playout trust: \"{(line.Length > 40 ? line[..40] + "…" : line)}\" is refused with words ({error})");
        Check(PlayoutRules.NameProblem("trust") is { } named && named.Contains("one of its commands", StringComparison.Ordinal)
              && Parse("playout trust pause", out _) is null,
              "playout trust: no link is called trust, so the word never reads as a link's name");
        Check(ShowCommandParser.Help.Contains("playout <link> trust <8+ hex of its certificate> (typed on the desk only, beside the fingerprint)", StringComparison.Ordinal),
              "playout trust: the help names the verb and where it is typed");
        Check(Copilot.Check("playout stage-left trust 03e9b152") is { Kind: ShowCommandKind.PlayoutTrust } risky && Copilot.Classify(risky) == CopilotRisk.High,
              "playout trust: high risk, decided here, never by a model");
        var plan = Copilot.ParsePlan("""{"reply":"ok","question":"","steps":[{"command":"playout stage-left trust 03e9b152","why":"the certificate changed"},{"command":"next","why":"go on"}]}""");
        Check(plan.Steps is [{ Command: "next" }] && plan.Dropped is [{ } dropped] && dropped.Contains(PlayoutRules.TrustDeskOnly, StringComparison.Ordinal),
              $"playout trust: never a step of a plan — dropped, saying why ({string.Join(" | ", plan.Dropped)})");
        var rows = new[] { new PlayoutRow("stage-left", "Stage left", PlayoutRules.PlayMap, Starts: true) };
        Check(!ShowContext.Commands.Any(c => c.Verb.Contains("trust", StringComparison.OrdinalIgnoreCase))
              && !ShowContext.CommandsFor(rows).Any(c => c.Verb.Contains("trust", StringComparison.OrdinalIgnoreCase)),
              "playout trust: never among the verbs the context offers a peer or an AI");
        Check(PlayoutRules.TrustLineWords.StartsWith(PlayoutRules.TrustDeskOnly, StringComparison.Ordinal) && PlayoutRules.TrustLineWords.EndsWith("never a line's — not run", StringComparison.Ordinal)
              && PlayoutRules.TrustLineWords.Contains(PlayoutRules.TrustWhere, StringComparison.Ordinal),
              "playout trust: an executor's refusal says it is a person's choice on the desk, and where");
        Check(PlayoutRules.TrustHex(" 03E9 B152 ") == "03e9b152" && PlayoutRules.TrustHex("sha256:03e9b152") == "03e9b152" && PlayoutRules.TrustHex("03e9b15") is null
              && PlayoutRules.TrustHex(null) is null && PlayoutRules.TrustHex("03e9-b152") is null,
              "playout trust: the hex as compared — at least 8, spaces dropped, nothing but hex");
    }

    static void Targets(Action<bool, string> Check)
    {
        PlayoutLinkSettings L(string name, bool on = true) => new() { Name = name, Enabled = on };
        var none = Array.Empty<PlayoutLinkSettings>();
        Check(PlayoutRules.Target(none, "", "pause", null) is (null, { } why) && why.Contains("no playout link is set up", StringComparison.Ordinal),
              "playout targets: with no link, refused with where to set one up");
        Check(PlayoutRules.Target(none, "stage-left", "go", null) is (null, { } why2) && why2.StartsWith("no playout link called stage-left", StringComparison.Ordinal),
              "playout targets: a named link that does not exist is refused by its name");

        var one = new[] { L("patterns"), L("stage-left", on: false) };
        foreach (var action in PlayoutRules.Actions)
            Check(PlayoutRules.Target(one, "", action, null) is ({ Name: "patterns" }, null), $"playout targets: {action} with one link switched on reaches it");
        Check(PlayoutRules.Target(one, "stage-left", "pause", null) is (null, { } off) && off == "Stage left is switched off (Settings → Integrations)",
              "playout targets: a link switched off is refused by name");

        var two = new[] { L("patterns"), L("stage-left") };
        Check(PlayoutRules.Target(two, "", "pause", "stage-left") is ({ Name: "stage-left" }, null), "playout targets: with two links, transport reaches the one feeding the VT");
        Check(PlayoutRules.Target(two, "", "pause", null) is (null, { } noVt) && noVt == "two playout links and none feeds the VT — name one: playout patterns pause …",
              "playout targets: with two links and no VT, transport is refused naming one");
        Check(PlayoutRules.Target(two, "", "go", "stage-left") is (null, { } goWhy) && goWhy == "two playout links — name one: playout patterns go …"
              && PlayoutRules.Target(two, "", "cue", "patterns") is (null, not null),
              "playout targets: go and cue never guess between two links, the VT's among them");
        Check(PlayoutRules.Target(two, "nope", "go", null) is (null, { } nope) && nope == "no playout link called nope — links: patterns, stage-left",
              "playout targets: a name no link has is refused listing the links");
        Check(PlayoutRules.Target(two, "stage-left", "go", "patterns") is ({ Name: "stage-left" }, null), "playout targets: a named link is the one reached");

        // Which link a command reaches: the licence first, then the link, then whether it runs; what it carries there is the director's.
        PlayoutArgs A(string link, string action) => new(link, action, "", action is "cue" or "go" ? "c1" : "", false);
        var patterns = new PlayoutLinkSettings { Name = "patterns", Kind = "patterns" };
        var stageLeft = new PlayoutLinkSettings { Name = "stage-left", Kind = "playmap" };
        bool All(string _) => true;
        Check(PlayoutRules.Route([patterns], A("", "pause"), licensed: false, All, null) is (null, { } lic) && lic == "playout links need Stagewatch Production. Help → License.",
              "playout route: without the licence, refused naming the tier, before anything else");
        Check(PlayoutRules.Route([], A("", "pause"), true, All, null) is (null, { } noLink) && noLink.Contains("no playout link is set up", StringComparison.Ordinal),
              "playout route: no link, refused with where to set one up");
        Check(PlayoutRules.Route([patterns, stageLeft], A("stage-left", "go"), true, All, null) is ({ Name: "stage-left" }, null),
              "playout route: a PlayMap link is reached by its name, go and cue among what it carries");
        Check(PlayoutRules.Route([new PlayoutLinkSettings { Name = "patterns", Kind = "patterns", Enabled = false }], A("patterns", "pause"), true, All, null) is (null, { } routeOff)
              && routeOff == "Patterns is switched off (Settings → Integrations)",
              "playout route: a link switched off is refused by name");
        Check(PlayoutRules.Route([patterns, stageLeft], A("stage-left", "pause"), true, name => name == "patterns", null) is (null, { } down)
              && down.StartsWith("Stage left isn't running yet", StringComparison.Ordinal),
              "playout route: a link that is not running is refused by name, nothing sent");
        foreach (var action in PlayoutRules.Actions)
            Check(PlayoutRules.Route([new PlayoutLinkSettings { Name = "patterns", Kind = "auto" }], A("", action), true, All, null) is ({ Name: "patterns" }, null),
                  $"playout route: {action} reaches the only link");
        Check(PlayoutRules.PatternsTarget([stageLeft, patterns]) == "patterns" && PlayoutRules.PatternsTarget([stageLeft]) == "",
              "playout route: patterns … means link patterns, else the only link");
        Check(PlayoutRules.Peer("playmap", "stage-left") == "PlayMap Stage left" && PlayoutRules.Peer("patterns", "patterns") == "Patterns"
              && PlayoutRules.Peer(null, "stage-left") == "Stage left" && PlayoutRules.Peer("patterns", "foh") == "Patterns Foh",
              "playout route: a link's peer as people read it, its app named once its kind is known");
    }

    static void Context(Action<bool, string> Check)
    {
        Check(ReferenceEquals(ShowContext.CommandsFor(null), ShowContext.Commands) && ReferenceEquals(ShowContext.CommandsFor([]), ShowContext.Commands),
              "playout context: with no link the commands are the fixed list");

        var transport = ShowContext.CommandsFor([new PlayoutRow("patterns", "Patterns", PlayoutRules.Patterns, Starts: false)]);
        var rows = transport.Where(c => c.Verb.StartsWith("playout ", StringComparison.Ordinal)).ToList();
        Check(rows.Select(r => r.Verb).SequenceEqual(["playout patterns play|pause|stop|restart", "playout patterns end [<seconds>]", "playout patterns seek <m:ss>"])
              && rows.All(r => r.Risk == "high" && r.Air), "playout context: a link that moves its clip lists its transport, high, among the air commands");
        Check(transport.SkipWhile(c => c.Air).All(c => !c.Air) && transport.Count == ShowContext.Commands.Count + 3
              && transport.Where(c => !c.Verb.StartsWith("playout ", StringComparison.Ordinal)).SequenceEqual(ShowContext.Commands.Where(c => c.Air).Concat(ShowContext.Commands.Where(c => !c.Air))),
              "playout context: air first, then the rest, the fixed rows in their order");

        var starts = ShowContext.CommandsFor([new PlayoutRow("stage-left", "Stage left", PlayoutRules.PlayMap, Starts: true), new PlayoutRow("patterns", "Patterns", PlayoutRules.Patterns, Starts: true),
                                              new PlayoutRow("odd", "Odd", "", Starts: true)]);
        Check(starts.Any(c => c.Verb == "playout stage-left go <playlist> [<item>] [together]") && starts.Any(c => c.Verb == "playout stage-left cue <playlist> <item>")
              && starts.Count(c => c.Verb.Contains("together", StringComparison.Ordinal)) == 1
              && starts.Any(c => c.Verb == "playout patterns go <cue>") && starts.Any(c => c.Verb == "playout patterns cue <cue>")
              && !starts.Any(c => c.Verb.StartsWith("playout odd go", StringComparison.Ordinal) || c.Verb.StartsWith("playout odd cue", StringComparison.Ordinal))
              && starts.Where(c => c.Verb.StartsWith("playout ", StringComparison.Ordinal)).All(c => c.Risk == "high"),
              "playout context: a link that can start lists go and cue in its kind's words, none while its kind is not known, together only on a PlayMap node's GO (a Patterns desk takes no AT)");
        Check(ProposalDesk.RiskFromCommands(starts.Select(c => (c.Verb, c.Risk)).ToList(), "playout stage-left go pl1 it1 together") == "high",
              "playout context: a proposal of a GO together reads high");
        Check(ProposalDesk.RiskFromCommands(starts.Select(c => (c.Verb, c.Risk)).ToList(), "playout stage-left cue pl1 it1") == "high"
              && ProposalDesk.RiskFromCommands(transport.Select(c => (c.Verb, c.Risk)).ToList(), "playout patterns pause") == "high",
              "playout context: a peer's proposal of a playout line reads high");

        var input = new ContextInput("abc123abc123", "Hall A", new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc), new ShowSnapshot { Title = "Keynote" }, false,
            Array.Empty<ContextItem>(), Array.Empty<PeerSummary>(), Array.Empty<JournalEntry>(), ShowClock.For("UTC"), 0,
            [new PlayoutRow("patterns", "Patterns", PlayoutRules.Patterns, Starts: false)]);
        using var doc = JsonDocument.Parse(ShowContext.Build(input));
        var verbs = doc.RootElement.GetProperty("commands").EnumerateArray().Select(c => c.GetProperty("verb").GetString()!).ToList();
        Check(verbs.Contains("playout patterns seek <m:ss>") && verbs.IndexOf("playout patterns seek <m:ss>") < verbs.IndexOf("note <text>"),
              "playout context: the built context lists the link's rows before the commands that are not air");
    }

    static void Clock(Action<bool, string> Check)
    {
        var est = new ClockOffsetEstimator();
        // t4 1000, 5000, 9000; rtt 40, 10, 30; offsets 100, 200, 300.
        est.Add(960, 1080, 1080, 1000);
        est.Add(4990, 5195, 5195, 5000);
        est.Add(8970, 9285, 9285, 9000);
        Check(est.Best(TimeSpan.FromSeconds(10), 12_000) == new ClockSample(200, 10, 7000), "playout clock: the best sample of those 10 s old, with its rtt and age");
        Check(est.Best(TimeSpan.FromSeconds(5), 12_000) == new ClockSample(300, 30, 3000), "playout clock: an older better sample is past the age asked");
        Check(est.Best(TimeSpan.FromSeconds(1), 12_000) is null, "playout clock: no sample that recent, none");
        // A sample stamped after now (this machine's clock stepped back) is no sample, even the one with the smallest round trip.
        var stepped = new ClockOffsetEstimator();
        stepped.Add(4990, 5195, 5195, 5000);   // t4 5000, rtt 10, offset 200
        stepped.Add(8998, 9300, 9300, 9001);   // t4 9001, rtt 3, offset 300
        Check(stepped.Best(TimeSpan.FromSeconds(10), 9_001) == new ClockSample(300, 3, 0), "playout clock: a sample taken now is one");
        Check(stepped.Best(TimeSpan.FromSeconds(10), 8_000) == new ClockSample(200, 10, 3000), "playout clock: a sample from after now (the clock stepped back) is no sample");
        Check(est.Offset == TimeSpan.FromMilliseconds(200), "playout clock: the offset over the whole window is unchanged");
        est.Add(10_985, 11_215, 11_215, 11_005);   // rtt 20
        est.Add(11_490, 11_700, 11_700, 11_500);   // rtt 10 again, newer than the one at t4 5000
        Check(est.Best(TimeSpan.FromSeconds(10), 12_000) is { RttMs: 10, AgeMs: 500, OffsetMs: 205 }, "playout clock: of two equal round trips, the newer");
        for (var n = 0; n < ClockOffsetEstimator.Window; n++) est.Add(20_000 + n, 20_000 + n, 20_000 + n, 20_050 + n);
        Check(est.Best(TimeSpan.FromSeconds(30), 21_000) is { RttMs: 50 }, "playout clock: the window keeps the last eight samples");
    }

    // ------------------------------------------------------------------ the wire's primitives

    const string Fingerprint = "sha256:03e9b152ca42518c28128f36def699be8aea5eec11bb9d5afc18a314f8c3eb04";

    static PlayoutPeer? Heard(string json, string address = "10.0.0.12") => Heard(Encoding.UTF8.GetBytes(json), address);

    static PlayoutPeer? Heard(byte[] data, string address = "10.0.0.12") => PlayoutWire.ParseBeacon(data, address);

    static void Finder(Action<bool, string> Check)
    {
        // PlayMap's hello as it says it (§2): its line wire in ports.wire, its https door and its certificate beside it.
        var playmap = Heard("""{"t":"playmap","v":1,"proto":3,"id":"a1b2c3d4","inst":"0a1b2c3d","name":"Stage left","host":"PM-1","ports":{"http":9940,"https":9940,"ws":9940,"wire":9941,"sync":9942},"caps":["video"],"clock":1760101234567,"tls":"FP"}""".Replace("FP", Fingerprint, StringComparison.Ordinal));
        Check(playmap is { Kind: "playmap", Name: "Stage left", Address: "10.0.0.12", WirePort: 9941, Id: "a1b2c3d4", HttpsPort: 9940, Tls: Fingerprint, FromHello: true },
              $"playout finder: PlayMap's hello is heard with its kind, id, line wire, https door and certificate ({playmap})");
        Check(Heard("""{"t":"playmap","v":1,"id":"a1b2c3d4","name":"Stage left","wire":9941}""") is { Kind: "playmap", WirePort: 9941, HttpsPort: 0, Tls: null },
              "playout finder: a hello that gives its line wire only as the older top-level wire is heard too");
        Check(Heard("""{"t":"playmap","v":1,"id":"a1b2c3d4","ports":{"wire":9941},"wire":9999}""")?.WirePort == 9941,
              "playout finder: ports.wire, §2's, wins over the older top-level wire");
        Check(Heard("""{"t":"patterns","v":1,"id":"9f8e7d6c","name":"FOH","ports":{"http":9696,"wire":9697}}""") is { Kind: "patterns", WirePort: 9697, Id: "9f8e7d6c", FromHello: true },
              "playout finder: Patterns' hello naming its wire in ports.wire is heard");
        Check(Heard("""{"patterns":1,"t":"playmap","machine":"FOH desk","kind":"desk","wire":9697,"tls":"FP"}""".Replace("FP", Fingerprint, StringComparison.Ordinal))
                  is { Kind: "patterns", Id: "", Tls: null, FromHello: false, Role: "desk" },
              "playout finder: Patterns' own beacon is a Patterns desk's whatever else it says, and names no id or certificate");
        foreach (var (json, why) in new[]
        {
            ("""{"t":"main","v":1,"id":"3f9c2a1b7d0e","ports":{"wire":9697}}""", "a Stagewatch"),
            ("""{"t":"messenger","v":1,"id":"ag-12345","ports":{"wire":9697}}""", "AGMessage"),
            ("""{"t":"playmap","v":2,"id":"a1b2c3d4","ports":{"wire":9941}}""", "a hello format it does not know"),
            ("""{"t":"playmap","v":1.5,"id":"a1b2c3d4","ports":{"wire":9941}}""", "a format that is not a whole number"),
            ("""{"t":"playmap","v":"1","id":"a1b2c3d4","ports":{"wire":9941}}""", "a format that is not a number"),
            ("""{"t":"playmap","v":1,"id":"a1b2c3d4","ports":{"wire":"9941"},"wire":"x"}""", "a wire that is not a number"),
            ("""{"t":"playmap","v":1,"id":"a1b2c3d4","ports":{"wire":70000}}""", "a wire out of range"),
            ("""{"t":"playmap","v":1,"id":"a1b2c3d4","ports":[9941]}""", "doors that are not an object"),
            ("""{"t":"playmap","v":1,"id":"a1b2c3d4"}""", "no wire"),
            ("""[{"t":"playmap","v":1,"wire":9941}]""", "not an object"),
            ("""{"t":"playmap","v":1,"wire":9941""", "not JSON"),
        })
            Check(Heard(json) is null, "playout finder: not listed — " + why);
        Check(Heard("""{"t":"playmap","v":1,"id":"a1b2c3d4","wire":9941,"name":"PAD"}""".Replace("PAD", new string('x', PlayoutWire.FindReadBytes), StringComparison.Ordinal)) is null,
              "playout finder: a datagram past FindReadBytes is dropped unread");
        // A field of the wrong shape is never read; the node is still listed by its wire.
        var odd = Heard("""{"t":"playmap","v":1,"id":"a1 b2!","name":"Stage\u0007 left","ports":{"wire":9941,"https":0},"tls":"sha256:ABC"}""");
        Check(odd is { Id: "", HttpsPort: 0, Tls: null, Name: "Stage left", WirePort: 9941 },
              $"playout finder: an id, https door or certificate of the wrong shape is not kept, and a name keeps no control character ({odd})");
        Check(!PlayoutWire.IsFingerprint(Fingerprint.ToUpperInvariant()) && !PlayoutWire.IsFingerprint(Fingerprint[..70]) && PlayoutWire.IsFingerprint(Fingerprint),
              "playout finder: a certificate is sha256: and 64 lowercase hex");
        Check(PlayoutWire.FindPorts.SequenceEqual([9928, 9700]) && PlayoutWire.HelloPort == SlidesBeacon.Port,
              "playout finder: it listens on the show network's hello and Patterns' beacon, and nothing else");

        // Text that is not UTF-8, or an escape of half a character, names no peer — and never throws out of Find.
        const string hello = """{"t":"playmap","v":1,"id":"a1b2c3d4","name":"Stage left","ports":{"wire":9941}}""";
        foreach (var (data, why) in new[]
        {
            (WithBytes(hello.Replace("\"playmap\"", "\"play§\"", StringComparison.Ordinal), 0xC3, 0x28), "a t that is not UTF-8"),
            (WithBytes(hello.Replace("Stage left", "Stage§", StringComparison.Ordinal), 0xC3, 0x28), "a name that is not UTF-8"),
            (WithBytes(hello.Replace("{\"t\"", "{\"§\":1,\"t\"", StringComparison.Ordinal), 0xC3, 0x28), "a member name that is not UTF-8"),
            (Encoding.UTF8.GetBytes(hello.Replace("Stage left", "\\ud800", StringComparison.Ordinal)), "a name escaping half a character"),
            (Encoding.UTF8.GetBytes(hello.Replace("\"playmap\"", "\"playmap\\udc00\"", StringComparison.Ordinal)), "a t escaping half a character"),
        })
            Check(Answer(() => Heard(data)) is (null, null), "playout finder: not listed, and no exception — " + why);
        Check(Heard(Encoding.UTF8.GetBytes(hello)) is { Kind: "playmap" }, "playout finder: the same hello whole is listed");
        var thrown = Mutations(Encoding.UTF8.GetBytes(hello)).Select(d => Throws(() => PlayoutWire.ParseBeacon(d, "10.0.0.12"))).Where(t => t is not null).Distinct().ToList();
        Check(thrown.Count == 0, "playout finder: a hello with any byte changed, or half a character escaped anywhere, is read or dropped, never thrown on (" + string.Join("; ", thrown) + ")");

        // One Find's list: of the kinds asked for only, at most FindMax, a hello replacing a beacon and never the other way, even when full.
        PlayoutPeer Peer(int n, string kind, bool hello = false) => new("n" + n.ToString(System.Globalization.CultureInfo.InvariantCulture), "10.0.1." + n.ToString(System.Globalization.CultureInfo.InvariantCulture), 9697, kind) { FromHello = hello };
        var list = new PlayoutFindList([PlayoutRules.Patterns]);
        var others = Enumerable.Range(0, PlayoutWire.FindMax + 5).Count(n => list.Hear(Peer(n, PlayoutRules.PlayMap, hello: true)));
        Check(others == 0 && list.Peers.Count == 0 && list.Hear(Peer(200, PlayoutRules.Patterns)) && list.Peers is [{ Name: "n200" }],
              "playout finder: a node of a kind not asked for takes no place, however many are heard first");
        var full = new PlayoutFindList([PlayoutRules.Patterns]);
        for (var n = 0; n < PlayoutWire.FindMax; n++) full.Hear(Peer(n, PlayoutRules.Patterns));
        Check(!full.Hear(Peer(100, PlayoutRules.Patterns)) && full.Peers.Count == PlayoutWire.FindMax,
              "playout finder: a full list takes no new node");
        Check(full.Hear(Peer(3, PlayoutRules.Patterns, hello: true) with { Id = "9f8e7d6c" }) && full.Peers[3] is { FromHello: true, Id: "9f8e7d6c" }
              && !full.Hear(Peer(3, PlayoutRules.Patterns)) && full.Peers[3].FromHello && full.Peers.Count == PlayoutWire.FindMax,
              "playout finder: a full list still takes a listed node's hello in its place, and a beacon never undoes it");
        var both = new PlayoutFindList([PlayoutRules.Patterns, PlayoutRules.PlayMap]);
        Check(both.Hear(Peer(1, PlayoutRules.PlayMap, hello: true)) && both.Hear(Peer(2, PlayoutRules.Patterns)) && both.Peers.Select(x => x.Kind).SequenceEqual([PlayoutRules.PlayMap, PlayoutRules.Patterns]),
              "playout finder: asked for both kinds, it lists both in the order heard");
    }

    /// <summary><paramref name="json"/>'s UTF-8 with its one § replaced by <paramref name="raw"/>, bytes that need not be text.</summary>
    static byte[] WithBytes(string json, params byte[] raw)
    {
        var at = json.IndexOf('§', StringComparison.Ordinal);
        return [.. Encoding.UTF8.GetBytes(json[..at]), .. raw, .. Encoding.UTF8.GetBytes(json[(at + 1)..])];
    }

    /// <summary>
    /// <paramref name="data"/> with each byte in turn changed to each of a few that break text or JSON (half a UTF-8 character, an
    /// invalid byte, a quote, a backslash, a brace, NUL), and with a half-character escape put in before each byte.
    /// </summary>
    static IEnumerable<byte[]> Mutations(byte[] data)
    {
        byte[] breakers = [0xC3, 0x80, 0xFF, 0x28, (byte)'"', (byte)'\\', (byte)'{', 0x00];
        var escape = "\\ud800"u8.ToArray();
        for (var i = 0; i < data.Length; i++)
        {
            foreach (var b in breakers)
            {
                var changed = (byte[])data.Clone();
                changed[i] = b;
                yield return changed;
            }
            yield return [.. data.AsSpan(0, i), .. escape, .. data.AsSpan(i)];
        }
    }

    /// <summary>What <paramref name="read"/> answered, and what it threw instead (null when it answered), so a reader that throws is a failed check, not an ended run.</summary>
    static (T? Answer, string? Threw) Answer<T>(Func<T?> read) where T : class
    {
        T? answer = null;
        var threw = Throws(() => answer = read());
        return (answer, threw);
    }

    /// <summary>What <paramref name="read"/> threw, by its type and words, or null: a reader handed any bytes answers and never throws.</summary>
    static string? Throws(Action read)
    {
        try
        {
            read();
            return null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or IndexOutOfRangeException or FormatException or OverflowException)
        {
            return e.GetType().Name + ": " + e.Message;
        }
    }

    static void Proofs(Action<bool, string> Check)
    {
        const string nonce = "00112233445566778899aabbccddeeff";
        Check(PlayoutWire.Proof("ab12-cd34", nonce) is { Length: 64 } proof && proof == PeerLink.LegacyProof("ab12-cd34", nonce, "Stagewatch"),
              "playout proof: a link's AUTH-PROOF is the core's 13.6.8 proof over the HELLO name");
        Check(PlayoutWire.Proof("ab12 cd34\t", nonce) == PlayoutWire.Proof("AB12CD34", nonce),
              "playout proof: dashes, spaces and tabs removed and the rest upper-cased, as 1.1 desks compare it");
        Check(PlayoutWire.Proof("ab12\u00a0cd34", nonce) is null && PlayoutWire.TokenProblem("ab12\u00a0cd34") == "this token holds an invisible character — retype it"
              && PlayoutWire.Proof("ab12\ufeffcd34", nonce) is null && PlayoutWire.TokenProblem("ab12\ufeffcd34") == PlayoutWire.InvisibleCharacter
              && PlayoutWire.Proof("ab12\u2028cd34", nonce) is null && PlayoutWire.TokenProblem("ab12\u2028cd34") == PlayoutWire.InvisibleCharacter
              && PlayoutWire.TokenProblem("--") is { } bare && bare.Contains("only dashes", StringComparison.Ordinal)
              && PlayoutWire.TokenProblem("ab12-cd34") is null,
              "playout proof: a token 1.1 desks read apart is never proved, and the link says why in 13.6.8's words");
        Check(PlayoutWire.Proof("ab12-cd34", nonce.ToUpperInvariant()) is null && PlayoutWire.Proof("ab12-cd34", nonce[..31]) is null && !PlayoutWire.IsProvableNonce(nonce + "0"),
              "playout proof: only a NONCE of 32 lowercase hex is answered");
    }

    /// <summary>
    /// A line's words as PlayMap's wire splits them (its WireLine.TrySplit, Show Bus §6's reading): a quoted phrase is one word, a
    /// backslash in it taking the mark after it; a word opening with { or [ runs to the end of the line; any other word runs to the next
    /// white space. Null for a quote that does not close.
    /// </summary>
    static List<string>? Split(string text)
    {
        var words = new List<string>();
        var i = 0;
        while (true)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length) return words;
            var w = new StringBuilder();
            if (text[i] == '"')
            {
                i++;
                var closed = false;
                while (i < text.Length)
                {
                    if (text[i] == '\\' && i + 1 < text.Length) { w.Append(text[i + 1]); i += 2; continue; }
                    if (text[i] == '"') { i++; closed = true; break; }
                    w.Append(text[i++]);
                }
                if (!closed) return null;
            }
            else if (text[i] is '{' or '[') { w.Append(text, i, text.Length - i); i = text.Length; }
            else while (i < text.Length && !char.IsWhiteSpace(text[i])) w.Append(text[i++]);
            words.Add(w.ToString());
        }
    }

    static void Words(Action<bool, string> Check)
    {
        Check(PlayoutWire.Quote("pl1a2b3c4") == "pl1a2b3c4" && PlayoutWire.Quote("#4") == "#4" && PlayoutWire.Quote(@"C:\clips") == @"C:\clips",
              "playout words: a word that reads back as itself goes bare, a backslash in it kept");
        Check(PlayoutWire.Quote("Show · Intro") == "\"Show · Intro\"" && PlayoutWire.Quote("") == "\"\"" && PlayoutWire.Quote("say \"hi\"") == "\"say \\\"hi\\\"\""
              && PlayoutWire.Quote(@"C:\a b\") == "\"C:\\\\a b\\\\\"" && PlayoutWire.Quote("{x}") == "\"{x}\"" && PlayoutWire.Quote("[1]") == "\"[1]\""
              && PlayoutWire.Quote("a\u00a0b") == "\"a\u00a0b\"",
              "playout words: a word with a space, a quote mark, none at all, or opening as JSON goes quoted, its backslashes and quotes escaped");
        Check(PlayoutWire.Quote("a\nb") is null && PlayoutWire.Quote("a\rb") is null && PlayoutWire.Quote("a\tb") is null && PlayoutWire.Quote("a\u0085b") is null
              && PlayoutWire.Quote("\u0000") is null,
              "playout words: a word with a control character, a line break among them, cannot ride a line");
        Check(PlayoutWire.Quote("a\ud800b") is null && PlayoutWire.Quote("\udc00") is null && PlayoutWire.Quote("end\ud83c") is null && PlayoutWire.Quote("\ude00\ud83c") is null
              && PlayoutWire.Quote("clip\ud83c\udfac") == "clip\ud83c\udfac" && PlayoutWire.Line(PlayoutRules.PlayMap, "s1", "PLAYLIST", "GO", "pl1", "a\ud800") is null,
              "playout words: half a surrogate pair cannot ride a line (UTF-8 would carry U+FFFD, another word); a whole pair goes as it is");
        string[] tricky = ["Show · Intro", "", "say \"hi\"", @"C:\a b\", @"C:\clips", "{\"x\":1}", "[1, 2]", "#4", "it's", "a\u00a0b", "\\\"", "end\\", "@s9"];
        foreach (var w in tricky)
            Check(PlayoutWire.Line(PlayoutRules.PlayMap, null, "PLAYLIST", "GO", w, "it9f8e7d6") is { } line && Split(line) is { } back && back.SequenceEqual(["PLAYLIST", "GO", w, "it9f8e7d6"]),
                  $"playout words: «{w}» reads back on PlayMap as the one word it was, in its place");

        // Patterns reads no quotes: it takes a verb's last word raw (CUE GO "Walk in" would look for a cue named with its quote marks).
        // So a word goes to it bare, or the line is not built; the same holds for a peer whose kind is not known yet.
        Check(PlayoutWire.Line(PlayoutRules.Patterns, "s1", "CUE", "GO", "c0ffee12") == "@s1 CUE GO c0ffee12"
              && PlayoutWire.Line(PlayoutRules.Patterns, "s2", "VIDEO", "SEEK", "1:23") == "@s2 VIDEO SEEK 1:23"
              && PlayoutWire.Line(PlayoutRules.Patterns, "pa", "AUTH-PROOF", new string('a', 64)) == "@pa AUTH-PROOF " + new string('a', 64),
              "playout words: a bare word goes to Patterns as it is");
        foreach (var w in new[] { "Walk in", "", "say \"hi\"", "{x}", "[1]", "a\u00a0b", "a\nb", "a\ud800" })
            Check(PlayoutWire.Word(PlayoutRules.Patterns, w) is null && PlayoutWire.Line(PlayoutRules.Patterns, "s1", "CUE", "STANDBY", w) is null
                  && PlayoutWire.Line("", "s1", "CUE", "STANDBY", w) is null && PlayoutWire.Word(PlayoutRules.PlayMap, w) == PlayoutWire.Quote(w),
                  $"playout words: «{w}», a word that needs quotes or cannot ride a line, is never sent to Patterns or to a peer not known yet");
    }

    static void Lines(Action<bool, string> Check)
    {
        var proof = new string('a', 64);
        const string pm = PlayoutRules.PlayMap;
        Check(PlayoutWire.Line(pm, "s1", "PLAYLIST", "GO", "Show · Intro", "#4") == "@s1 PLAYLIST GO \"Show · Intro\" #4" && PlayoutWire.Line(pm, null, "VIDEO", "END", "10") == "VIDEO END 10"
              && PlayoutWire.Line(pm, "pa", "AUTH-PROOF", proof) == "@pa AUTH-PROOF " + proof,
              "playout lines: @id, the verb, then each word as it rides");
        Check(PlayoutWire.Line(pm, "bad id", "VIDEO", "PLAY") is null && PlayoutWire.Line(pm, new string('a', 33), "VIDEO", "PLAY") is null && PlayoutWire.Line(pm, "s1", "video", "PLAY") is null
              && PlayoutWire.Line(pm, "s1", "") is null && PlayoutWire.Line(pm, "s1", "-X") is null && PlayoutWire.Line(pm, "s1", "CUE", "STANDBY", "c0ffee12\nSTOP") is null,
              "playout lines: none with an id that is not one, a verb that is not one, or a word that would break the line");
        var fits = PlayoutWire.CommandBytes - "@s1 X ".Length;
        Check(PlayoutWire.Line(pm, "s1", "X", new string('a', fits)) is { Length: PlayoutWire.CommandBytes } && PlayoutWire.Line(pm, "s1", "X", new string('a', fits + 1)) is null
              && PlayoutWire.Line(pm, "s1", "X", new string('é', fits / 2 + 1)) is null && PlayoutWire.Line(PlayoutRules.Patterns, "s1", "X", new string('a', fits + 1)) is null,
              "playout lines: none longer than link.command.bytes in UTF-8, and never one cut short");

        // The line reader (13.14), fed in the pieces a socket hands it.
        var read = new List<(string Line, long Size, bool Whole)>();
        foreach (var piece in new[] { 1, 3, 4096 })
        {
            read.Clear();
            var reader = new PlayoutLineReader(10);
            var bytes = Encoding.UTF8.GetBytes("STATE {}\r\nabcdefghij\r\nxy\n\ntail");
            for (var at = 0; at < bytes.Length; at += piece)
                reader.Feed(bytes.AsSpan(at, Math.Min(piece, bytes.Length - at)), (line, size, whole) => read.Add((Encoding.UTF8.GetString(line), size, whole)));
            Check(read.SequenceEqual([("STATE {}", 9, true), ("abcdefghij", 11, false), ("xy", 2, true), ("", 0, true)]),
                  $"playout lines: in pieces of {piece}, a line within the bound is read whole without its CR, a longer one keeps the bound and its size once, an unfinished line waits ({string.Join(" | ", read)})");
        }
        Check(PlayoutWire.OversizeWords(300_000) == "STATE 293 KB — over this link's 256 KB" && PlayoutWire.OversizeWords(PlayoutWire.LineBytes + 1) == "STATE 257 KB — over this link's 256 KB",
              "playout lines: a STATE past the bound is said by its whole size, rounded up");
    }

    /// <summary>PlayMap's STATE in its writer's order (v, seq, id, t, …, air, video, playlists, outputs, …, sync, …, project), <paramref name="pad"/> in its outputs.</summary>
    static string PlayMapState(int pad = 0) =>
        "{\"v\":1,\"seq\":42,\"id\":\"a1b2c3d4\",\"t\":\"playmap\",\"name\":\"Stage left\",\"version\":\"3.0.0\",\"at\":1760101234567,\"light\":\"green\",\"headline\":\"\","
        + "\"air\":{\"blackout\":false,\"frozen\":false,\"pattern\":null},"
        + "\"video\":{\"file\":\"intro.mp4\",\"role\":\"playlist\",\"tag\":\"Show\",\"position\":12,\"length\":148,\"remaining\":136,\"playing\":true,\"ended\":false,\"loops\":false,"
        + "\"key\":\"lane-1\",\"positionMs\":12500,\"lengthMs\":148000,\"remainingMs\":135500,\"paused\":false,\"rate\":1,\"seekable\":true,\"seq\":7},"
        + "\"playlists\":[{\"id\":\"pl1a2b3c4\",\"name\":\"Show\",\"focused\":true,\"state\":\"playing\",\"index\":0,\"item\":{\"id\":\"it9f8e7d6\",\"label\":\"Intro\",\"source\":\"m1\"},"
        + "\"positionMs\":12500,\"lengthMs\":148000,\"remainingMs\":135500,\"rate\":1,\"loop\":false,\"syncGroup\":\"main\",\"seq\":7,\"next\":{\"id\":\"it0a0b0c0\",\"label\":\"Walk-in\"}},"
        + "{\"id\":\"pl5e6f7a8\",\"name\":\"Loops\",\"focused\":false,\"state\":\"idle\",\"index\":-1,\"item\":null,\"positionMs\":null,\"lengthMs\":null,\"remainingMs\":null,"
        + "\"rate\":1,\"loop\":true,\"syncGroup\":null,\"seq\":0,\"next\":null}],"
        + "\"outputs\":[{\"id\":\"o1\",\"note\":\"" + new string('x', pad) + "\"}],\"media\":{\"items\":2,\"missing\":0},\"stage\":{},\"scan\":{},"
        + "\"sync\":{\"role\":\"follower\",\"leader\":\"Stage right\"},\"links\":{},\"peers\":[],\"machine\":{\"renderer\":\"gl\"},"
        + "\"project\":{\"id\":\"p1\",\"name\":\"Gala\",\"revision\":3,\"path\":null,\"dirty\":false,\"index\":\"ix-77\"}}";

    /// <summary>Patterns' STATE as its desk writes it: no t, a rev, the cue stack before the clip, the clip's role and its own word on transport.</summary>
    const string PatternsState = """
        {"show":"Gala","rev":9,"version":"2.93","decks":[],"airLabel":"PGM","cuestack":{"armed":true,"hold":false,"seq":3,"listRev":17,"confirm":"","program":{"label":"PGM"},"previous":null,"standby":{"id":"c0ffee12","number":"12","name":"Walk-in","requireConfirm":true,"notes":"","plannedStart":null,"followSeconds":0},"next":[],"last":null,"history":[],"timing":{}},"blackout":false,"video":{"file":"opener.mp4","role":"playlist","tag":"PLAYLIST","position":3,"length":60,"remaining":57,"playing":true,"ended":false,"loops":false,"key":"pl:opener","positionMs":3000,"lengthMs":60000,"remainingMs":57000,"paused":false,"rate":1,"seekable":true,"seq":2,"pausable":false,"stoppable":true}}
        """;

    static void States(Action<bool, string> Check)
    {
        var pm = PlayoutStateReader.Read(PlayMapState());
        Check(pm is { Whole: true, Kind: "playmap", Id: "a1b2c3d4", Seq: 42, At: 1760101234567, HasVideo: true, HasPlaylists: true, HasSync: true, HasProject: true, HasCuestack: false, Cuestack: null }
              && pm.Bytes == Encoding.UTF8.GetByteCount(PlayMapState()),
              $"playout state: PlayMap's STATE read whole — its kind, id, counter, clock and sections ({pm})");
        Check(pm?.Video is { File: "intro.mp4", Role: "playlist", Key: "lane-1", Transport: true, Seq: 7, Pausable: null, Stoppable: null, CountsDown: true } v && Math.Abs(v.PositionSeconds - 12.5) < 0.001,
              $"playout state: PlayMap's clip on air, its role and key, and no word on pause where it gives none ({pm?.Video})");
        Check(pm?.Playlists is [{ Id: "pl1a2b3c4", Name: "Show", State: "playing", ItemId: "it9f8e7d6", ItemLabel: "Intro", PositionMs: 12500, SyncGroup: "main", NextId: "it0a0b0c0", NextLabel: "Walk-in", Seq: 7 },
                                  { Id: "pl5e6f7a8", Name: "Loops", State: "idle", ItemId: "", ItemLabel: "", PositionMs: null, SyncGroup: "", NextId: "", Seq: 0 }],
              "playout state: PlayMap's playlists, ids as it wrote them and none where it gives none");
        Check(pm?.Sync == new PlayoutSync("follower", "Stage right") && pm.ProjectIndex == "ix-77", "playout state: PlayMap's sync and its project's index");

        var pt = PlayoutStateReader.Read(PatternsState);
        Check(pt is { Whole: true, Kind: "patterns", Seq: 9, Id: "", At: null, HasVideo: true, HasCuestack: true, HasPlaylists: false, HasSync: false, HasProject: false, Sync: null }
              && pt.Cuestack == new PlayoutCuestack(true, false, "17", "c0ffee12", "12", "Walk-in", true),
              $"playout state: a Patterns STATE read whole is Patterns' (it sends no t), with its rev and cue stack ({pt})");
        Check(pt?.Video is { File: "opener.mp4", Role: "playlist", Key: "pl:opener", Pausable: false, Stoppable: true, Transport: true },
              $"playout state: Patterns' clip with its role and its own word on pause and stop ({pt?.Video})");
        Check(PlayoutStateReader.Read("""{"t":"main","video":null}""") is { Kind: "", HasVideo: true, Video: null }
              && PlayoutStateReader.Read("""{"t":"patterns"}""") is { Kind: "patterns", HasVideo: false },
              "playout state: a t this link does not drive is no kind it knows; t:patterns is Patterns'");

        // Longer than a link reads: the part kept is read for what lies wholly within it (13.14).
        var big = Encoding.UTF8.GetBytes(PlayMapState(300_000));
        var part = PlayoutStateReader.Read(big.AsSpan(0, PlayoutWire.LineBytes), big.Length);
        Check(part is { Whole: false, Kind: "playmap", Seq: 42, HasVideo: true, HasPlaylists: true, HasSync: false, Sync: null, HasProject: false, ProjectIndex: null }
              && part.Bytes == big.Length && part.Video is { File: "intro.mp4" } && part.Playlists.Count == 2,
              $"playout state: an oversize PlayMap STATE is read for its clip and playlists, before the cut; its sync and project past it are not known ({part})");
        var late = Encoding.UTF8.GetBytes("{\"v\":1,\"t\":\"playmap\",\"air\":{\"note\":\"" + new string('x', 300_000) + "\"}," + PlayMapState()[1..]);
        Check(PlayoutStateReader.Read(late.AsSpan(0, PlayoutWire.LineBytes), late.Length) is { Whole: false, Kind: "playmap", HasVideo: false, Video: null, HasPlaylists: false },
              "playout state: a clip past the part read is not known — never taken as none");
        var whole = Encoding.UTF8.GetBytes(PlayMapState());
        var inVideo = PlayMapState().IndexOf("\"seekable\"", StringComparison.Ordinal);
        Check(PlayoutStateReader.Read(whole.AsSpan(0, inVideo), whole.Length) is { Whole: false, Kind: "playmap", HasVideo: false, Video: null, HasPlaylists: false },
              "playout state: a clip cut inside its own section is not read in part");
        var cutPatterns = Encoding.UTF8.GetBytes(PatternsState);
        Check(PlayoutStateReader.Read(cutPatterns.AsSpan(0, cutPatterns.Length - 40), cutPatterns.Length) is { Whole: false, Kind: null, HasCuestack: true, HasVideo: false },
              "playout state: a Patterns STATE read in part says no kind (no t is proof of nothing until its end)");
        var multi = Encoding.UTF8.GetBytes("{\"video\":null,\"note\":\"" + new string('é', 200_000) + "\"}");
        Check(PlayoutStateReader.Read(multi.AsSpan(0, PlayoutWire.LineBytes - 1), multi.Length) is { Whole: false, HasVideo: true, Video: null },
              "playout state: a part cut inside a character still reads what came before it");

        // Not a STATE: not read at all.
        foreach (var (json, why) in new[]
        {
            ("""{"video":null,"x":}""", "a whole STATE that does not parse to its end"),
            ("""{"video":null} x""", "anything after the STATE"),
            ("""{"video":null}{"video":null}""", "two values"),
            ("[1]", "not an object"),
            ("\"STATE\"", "a string"),
            ("", "nothing"),
            ("{\"video\":null,/*c*/\"t\":\"playmap\"}", "a comment"),
        })
            Check(PlayoutStateReader.Read(json) is null, "playout state: not read — " + why);
        var broken = Encoding.UTF8.GetBytes("{\"video\":null,\"x\":},\"note\":\"" + new string('x', 300_000) + "\"}");
        Check(PlayoutStateReader.Read(broken.AsSpan(0, PlayoutWire.LineBytes), broken.Length) is null,
              "playout state: a part that is not JSON as far as it goes is not read");

        // Text that is not UTF-8, or an escape of half a character, in a name or a string it reads: not read, and never thrown on.
        static PlayoutPeerState? Raw(byte[] data) => PlayoutStateReader.Read(data, data.Length);
        foreach (var (data, why) in new[]
        {
            (WithBytes("{\"§\":1,\"video\":null}", 0xC3, 0x28), "a member name that is not UTF-8"),
            (WithBytes("{\"t\":\"§\"}", 0xC3, 0x28), "a t that is not UTF-8"),
            (WithBytes("{\"video\":{\"file\":\"§\"}}", 0xC3, 0x28), "a clip's file that is not UTF-8"),
            (Encoding.UTF8.GetBytes("{\"video\":{\"file\":\"\\ud800x\"}}"), "a clip's file escaping half a character"),
            (Encoding.UTF8.GetBytes("{\"playlists\":[{\"id\":\"pl1\",\"name\":\"\\udc00\"}]}"), "a playlist's name escaping half a character"),
        })
            Check(Answer(() => Raw(data)) is (null, null), "playout state: not read, and no exception — " + why);
        var cut = WithBytes("{\"video\":{\"file\":\"§\"}," + "\"note\":\"" + new string('x', 300_000) + "\"}", 0xC3, 0x28);
        Check(Answer(() => PlayoutStateReader.Read(cut.AsSpan(0, PlayoutWire.LineBytes), cut.Length)) is (null, null),
              "playout state: a part read whose clip is not UTF-8 is not read either");
        foreach (var (json, name) in new[] { ("{\"video\":{\"file\":\"a.mp4\"}}", "PlayMap's"), (PatternsState, "Patterns'") })
        {
            var thrown = Mutations(Encoding.UTF8.GetBytes(json)).Select(d => Throws(() => { Raw(d); PlayoutStateReader.Read(d.AsSpan(0, d.Length / 2), d.Length); }))
                .Where(t => t is not null).Distinct().ToList();
            Check(thrown.Count == 0, $"playout state: {name} STATE with any byte changed, or half a character escaped anywhere, read whole or in part, is read or not, never thrown on ({string.Join("; ", thrown)})");
        }
        var mapped = Encoding.UTF8.GetBytes(PlayMapState());
        var mappedThrown = Mutations(mapped).Where((_, i) => i % 7 == 0).Select(d => Throws(() => Raw(d))).Where(t => t is not null).Distinct().ToList();
        Check(mappedThrown.Count == 0, $"playout state: PlayMap's whole STATE, changed in every seventh way, is never thrown on ({string.Join("; ", mappedThrown)})");

        // A section of another JSON type than it is written as is not known, never taken as empty, and is named.
        foreach (var (json, section) in new[]
        {
            ("""{"video":"x"}""", "video"), ("""{"playlists":{}}""", "playlists"), ("""{"playlists":null}""", "playlists"), ("""{"sync":3}""", "sync"),
            ("""{"cuestack":[]}""", "cuestack"), ("""{"project":"p1"}""", "project"),
        })
            Check(PlayoutStateReader.Read(json) is { HasVideo: false, Video: null, HasPlaylists: false, HasSync: false, Sync: null, HasCuestack: false, Cuestack: null, HasProject: false, ProjectIndex: null } st
                  && st.Unreadable.SequenceEqual([section]),
                  $"playout state: {json} — its {section} is not known, and named as unreadable");
        Check(PlayoutStateReader.Read("""{"video":null,"sync":null,"cuestack":null,"project":null,"playlists":[]}""")
                  is { HasVideo: true, Video: null, HasSync: true, Sync: null, HasCuestack: true, Cuestack: null, HasProject: true, ProjectIndex: null, HasPlaylists: true, Playlists.Count: 0 } none
              && none.Unreadable.Count == 0,
              "playout state: null (and an empty list) says none, known");

        // A clip's key is sent back to name its source, so it is kept as written when a line can carry it, else not at all.
        string KeyOf(string key) => PlayoutStateReader.Read("{\"video\":{\"file\":\"a.mp4\",\"key\":" + JsonSerializer.Serialize(key) + "}}")?.Video?.Key ?? "(none)";
        Check(KeyOf(new string('k', PlayoutRules.MaxRefChars)) == new string('k', PlayoutRules.MaxRefChars) && KeyOf(new string('k', PlayoutRules.MaxRefChars + 1)) == ""
              && KeyOf("lane\u0001-1") == "" && KeyOf("lane 1") == "lane 1",
              "playout state: a clip's key is kept whole up to 64 characters with no control character, else none — never cut or cleaned into another");

        // Bounds: the first 64 playlists with an id a line can carry, each label cut at 80 with "…".
        var many = "{\"playlists\":[" + string.Join(",", Enumerable.Range(0, 70).Select(n => "{\"id\":\"pl" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\",\"name\":\"" + new string('n', 100) + "\"}"))
                   + ",{\"id\":\"" + new string('i', 65) + "\"}]}";
        var bad = "{\"playlists\":[{\"id\":\"pl\\u0001x\"},{\"id\":\"\"},{\"name\":\"no id\"},7,{\"id\":\"ok-1\",\"item\":{\"id\":\"it\\nx\",\"label\":\"L\\u0007abel\"}}]}";
        Check(PlayoutStateReader.Read(many)?.Playlists is { Count: PlayoutStateReader.MaxPlaylists } lists && lists[0].Name.Length == PlayoutRules.MaxLabelChars && lists[0].Name.EndsWith('…')
              && PlayoutStateReader.Read(bad)?.Playlists is [{ Id: "ok-1", ItemId: "", ItemLabel: "Label" }],
              "playout state: the first 64 playlists kept; an id a line cannot carry drops its playlist or reads as none, never cleaned into another; labels cut and cleaned");
    }
}
