using System.Text.Json;
using System.Text.Json.Nodes;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Show;

/// <summary>
/// Playout links' model and grammar: settings schema 2 and the 2.17 Patterns keys kept as link "patterns"'s mirror, the names a
/// link may take, running-order cues as files keep them, the <c>playout</c> verb in the one grammar, which link it reaches, its
/// risk, its rows in the context, its journal kind and the clock's sample times. Fields first, words beside them.
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
        Check(ShowCommandParser.Help.Contains("playout [link] go <playlist> [item] [together]", StringComparison.Ordinal)
              && ShowCommandParser.Help.Contains("playout [link] restart|play|pause|stop", StringComparison.Ordinal),
              "playout grammar: the help names the verb's forms");
        Check(Enum.GetValues<ShowCommandKind>()[^1] == ShowCommandKind.Playout && (int)ShowCommandKind.Playout == (int)ShowCommandKind.Window + 1,
              "playout grammar: ShowCommandKind.Playout appended after Window");
        Check(Enum.GetValues<JournalKind>()[^1] == JournalKind.Playout && (int)JournalKind.Playout == (int)JournalKind.ClockCheck + 1,
              "playout grammar: JournalKind.Playout appended after ClockCheck");

        // Risk: every action is High, decided here, never by a model; the copilot's check reads the line in the one grammar.
        foreach (var line in new[] { "playout pause", "playout stage-left end 10", "playout seek 1:00", "playout cue pl1 it1", "playout go c1", "playout patterns go show intro together" })
            Check(Copilot.Check(line) is { Kind: ShowCommandKind.Playout } c && Copilot.Classify(c) == CopilotRisk.High, $"playout grammar: {line} is high risk");
        Check(Copilot.ParsePlan("""{"reply":"ok","question":"","steps":[{"command":"playout stage-left go show intro","why":"start the VT"}]}""").Steps is [{ Risk: CopilotRisk.High }],
              "playout grammar: a copilot step that drives playout is never pre-ticked");
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

        // What this Stagewatch does with the command: every refusal in its order, and the one thing it runs.
        PlayoutArgs A(string link, string action) => new(link, action, "", action is "cue" or "go" ? "c1" : "", false);
        var patterns = new PlayoutLinkSettings { Name = "patterns", Kind = "patterns" };
        var playmap = new PlayoutLinkSettings { Name = "patterns", Kind = "playmap" };
        var stageLeft = new PlayoutLinkSettings { Name = "stage-left", Kind = "playmap" };
        Check(PlayoutRules.Route([patterns], A("", "pause"), licensed: false, patternsRuns: true, null) is (null, { } lic) && lic == "playout links need Stagewatch Production. Help → License.",
              "playout route: without the licence, refused naming the tier, before anything else");
        Check(PlayoutRules.Route([], A("", "pause"), true, false, null) is (null, { } noLink) && noLink.Contains("no playout link is set up", StringComparison.Ordinal),
              "playout route: no link, refused with where to set one up");
        Check(PlayoutRules.Route([patterns, stageLeft], A("stage-left", "pause"), true, true, null) is (null, { } other)
              && other == "Stage left isn't running: this Stagewatch runs one playout link, patterns, as its Patterns link",
              "playout route: a link that is not link patterns is refused, nothing sent");
        Check(PlayoutRules.Route([new PlayoutLinkSettings { Name = "patterns", Kind = "patterns", Enabled = false }], A("patterns", "pause"), true, false, null) is (null, { } routeOff)
              && routeOff == "Patterns is switched off (Settings → Integrations)",
              "playout route: link patterns switched off is refused by name");
        Check(PlayoutRules.Route([playmap], A("", "pause"), true, true, null) is (null, { } pm) && pm.Contains("set to a PlayMap node", StringComparison.Ordinal),
              "playout route: link patterns set to a PlayMap node is refused, even were a Patterns link running");
        Check(PlayoutRules.Route([patterns], A("", "pause"), true, patternsRuns: false, null) is (null, { } down) && down == "Patterns isn't running (Settings → Integrations → Patterns)",
              "playout route: link patterns whose Patterns link is not running is refused");
        foreach (var action in new[] { "cue", "go" })
            Check(PlayoutRules.Route([patterns], A("patterns", action), true, true, null) is (null, { } notCarried)
                  && notCarried == $"playout {action} isn't carried to Patterns by this Stagewatch: it sends end, restart, play, pause, stop and seek",
                  $"playout route: {action} is refused on link patterns until a link carries it");
        foreach (var action in PlayoutRules.TransportActions)
            Check(PlayoutRules.Route([new PlayoutLinkSettings { Name = "patterns", Kind = "auto" }], A("", action), true, true, null) is ({ Name: "patterns" }, null),
                  $"playout route: {action} on link patterns runs");
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
              && starts.Any(c => c.Verb == "playout patterns go <cue>") && starts.Any(c => c.Verb == "playout patterns cue <cue>")
              && !starts.Any(c => c.Verb.StartsWith("playout odd go", StringComparison.Ordinal) || c.Verb.StartsWith("playout odd cue", StringComparison.Ordinal))
              && starts.Where(c => c.Verb.StartsWith("playout ", StringComparison.Ordinal)).All(c => c.Risk == "high"),
              "playout context: a link that can start lists go and cue in its kind's words, none while its kind is not known");
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
}
