using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Show;

/// <summary>
/// The copilot plans steps for other apps, and a person decides (Show Bus §11, release B): a step names its app by id and is kept only
/// when that app's verified table lists the line and it fills no secret, at that table's risk, every other one dropped in words that
/// name the app and never quote the line; nothing is cut; the AI's grammar leaves a person's own verbs out; the model sees an app's
/// verb heads, and its whole verbs only when the request names it or a plan asks once more, within budgets; an approved plan runs in
/// order, an app's step through this desk as the operator's line, waited for and stopping the run when it does not report done.
/// </summary>
static class CopilotPlanTests
{
    public static async Task Run(Action<bool, string> Check)
    {
        Parse(Check);
        Named(Check);
        await Passes(Check);
        Budgets(Check);
        HelpSplit(Check);
        await Runner(Check);
        await RunnerThroughDesk(Check);
        PlanSends(Check);
    }

    /// <summary>The apps a plan may name, as the app builds them (the same call): the desk's apps, each rated by the desk's own risk.</summary>
    internal static IReadOnlyList<CopilotApp> Apps(DeskService desk) => CopilotApp.From(desk.Apps(), desk.Risk);

    /// <summary>PlayMap Hall as the usual room's desk lists it: the app a runner's step names.</summary>
    static readonly NodeRef Pm = new("playmap", "pm1", "PlayMap Hall");

    static string Plan(params object[] steps) => JsonSerializer.Serialize(new { reply = "ok", question = "", steps });

    static object Step(string command, string target = "", string why = "because") => new { command, why, target };

    static void Parse(Action<bool, string> Check)
    {
        var b = DeskTests.Room();
        var apps = Apps(b.Desk);
        var window = "w7kq2m9xw7kq2m9xw7kq2m9xw7";
        var plan = Copilot.ParsePlan(Plan(
            Step("trim 3 -60"),
            Step("PLAYLIST CUE show #3", "pm1", "cue the intro"),
            Step("PANIC", "pm1"),
            Step("notice Doors open in 5", "ag1"),
            Step("OUTPUTS OFF", "pm1"),
            Step("LINK STAGEWATCH host hunter2", "pm1"),
            Step("LINK STAGEWATCH host swk_abcdefghijkmnpqrstuv", "pm1"),
            Step("MESSAGE see " + window, "pm1"),
            Step("MESSAGE " + new string('x', LineFence.MaxChars), "pm1"),
            Step("STATUS", "pmX"),
            Step("start", "stagewatch"),
            Step("STATUS", "swk_abcdefghijkmnpqrstuv")), apps, [window]);
        Check(plan.Steps.Count == 4 && plan.Steps[0] is { Command: "trim 3 -60", Target: null }
              && plan.Steps[1] is { Command: "PLAYLIST CUE show #3", Risk: CopilotRisk.Review, Why: "cue the intro" } cue && cue.Target == new NodeRef("playmap", "pm1", "PlayMap Hall")
              && plan.Steps[2] is { Command: "PANIC", Risk: CopilotRisk.High, Target.Id: "pm1" }
              && plan.Steps[3] is { Command: "notice Doors open in 5", Risk: CopilotRisk.Review, Target.Id: "ag1" },
            $"copilot plan: a step for an app is kept when its verified table lists the line, at that table's risk, aimed at the app by id ({plan.Steps.Count})");
        Check(plan.Dropped.Count == 0 && plan.AppDropped.SequenceEqual([
                "a step for PlayMap Hall (not a verb PlayMap Hall lists)",
                "a step for PlayMap Hall (" + LineFence.SecretWords("PlayMap Hall") + ")",
                "a step for PlayMap Hall (" + LineFence.Key + ")",
                "a step for PlayMap Hall (" + LineFence.Window + ")",
                "a step for PlayMap Hall (" + LineFence.TooLong + ")",
                "a step for 'pmX' (no app on the show network has that id — a step for another app names it by its id)",
                "a step for 'stagewatch' (no app on the show network has that id — a step for another app names it by its id)",
                "a step for an app (no app on the show network has that id — a step for another app names it by its id)"]),
            $"copilot plan: an unlisted, secret, keyed, windowed, too long or unaimed app step is dropped in words of its own, per app ({string.Join(" | ", plan.AppDropped)})");
        var words = string.Join(" ", plan.AppDropped);
        Check(!words.Contains("OUTPUTS", StringComparison.Ordinal) && !words.Contains("hunter2", StringComparison.Ordinal) && !words.Contains("swk_", StringComparison.Ordinal)
              && !words.Contains(window, StringComparison.Ordinal) && !words.Contains("xxxx", StringComparison.Ordinal),
            "copilot plan: a dropped app step never quotes its line, nor a target that holds a key");

        // Unverified: posted without its key, its verbs prove nothing, so no line of it is proposed — not even one its table lists.
        var unverified = new CopilotApp("pm7", "PlayMap Side", "playmap", false, _ => new RiskVerdict("low", "STATUS", "", true, false));
        var side = Copilot.ParsePlan(Plan(Step("STATUS", "pm7")), [unverified], null);
        Check(side.Steps.Count == 0 && side.AppDropped is ["a step for PlayMap Side (its verbs are not verified — Stagewatch proposes only lines it can read)"],
            $"copilot plan: an app whose verbs are not verified is proposed nothing ({string.Join("|", side.AppDropped)})");
        Check(Copilot.ParsePlan(Plan(Step("PLAYLIST CUE show #3", "pm1"))) is { Steps.Count: 0, AppDropped: [{ } none] } && none.StartsWith("a step for 'pm1' (no app", StringComparison.Ordinal),
            "copilot plan: with no apps given, a step for an app is dropped by its target");

        // Refused, never cut: a Stagewatch line longer than the old 400-character cut is kept whole; one over 2048 is dropped whole.
        var note = "note " + new string('n', 600);
        var huge = "note " + new string('h', LineFence.MaxChars);
        var lengths = Copilot.ParsePlan(Plan(Step(note), Step(huge)), apps, null);
        Check(lengths.Steps is [{ } kept] && kept.Command == note && lengths.Dropped is [{ } cut] && cut.EndsWith("… (" + LineFence.TooLong + ")", StringComparison.Ordinal) && cut.Length < 300,
            $"copilot plan: a line is kept whole or refused whole, never cut ({lengths.Dropped.FirstOrDefault()?.Length})");

        // Twelve steps in all, Stagewatch's and other apps' alike.
        var many = Copilot.ParsePlan(Plan(Enumerable.Range(1, 14).Select(i => i % 2 == 0 ? Step("STATUS", "pm1") : Step("note n" + i)).ToArray()), apps, null);
        Check(many.Steps.Count == 12 && many.Dropped.Count + many.AppDropped.Count == 2 && many.AppDropped.All(d => d == "a step for PlayMap Hall (too many steps)"),
            "copilot plan: at most twelve steps, apps' counted with Stagewatch's");

        // A person's own verbs stay a person's: a plan's peer or desk line is dropped by its verb, as before.
        var person = Copilot.ParsePlan(Plan(Step("peer playmap UNDO"), Step("desk approve next")), apps, null);
        Check(person.Steps.Count == 0 && person.Dropped.Count == 2 && person.Dropped.All(d => d.StartsWith("peer (", StringComparison.Ordinal) || d.StartsWith("desk (", StringComparison.Ordinal)),
            "copilot plan: peer and desk lines are never a step — a step for an app names its target instead");

        // need_verbs: verified apps' ids, each once; anything else is not honoured.
        var asks = Copilot.ParsePlan("""{"reply":"","question":"","steps":[],"need_verbs":["pm1","nope","pm1","ag1",7]}""", [.. apps, unverified], null);
        Check(asks.NeedVerbs.SequenceEqual(["pm1", "ag1"]) && Copilot.ParsePlan("""{"reply":"","question":"","steps":[],"need_verbs":["pm7"]}""", [unverified], null).NeedVerbs.Count == 0,
            $"copilot plan: need_verbs keeps verified apps' ids once each ({string.Join(",", asks.NeedVerbs)})");
    }

    static void Named(Action<bool, string> Check)
    {
        var apps = Apps(DeskTests.Room().Desk);
        Check(Copilot.Named("Cue the intro on PlayMap and tell everyone", apps).SequenceEqual(["pm1"])
              && Copilot.Named("ask patterns foh to focus screen 2", apps).SequenceEqual(["pat1"])
              && Copilot.Named("send an AGMessage notice", apps).SequenceEqual(["ag1"]) && Copilot.Named("the messenger", apps).SequenceEqual(["ag1"])
              && Copilot.Named("playmapper and patternsy", apps).Count == 0 && Copilot.Named("we're 5 minutes late", apps).Count == 0,
            "copilot plan: a request names an app by its name or kind word, whole and whatever its case");
        Check(Copilot.Named("cue PlayMap", [new CopilotApp("pm7", "PlayMap Side", "playmap", false, _ => RiskVerdict.Unlisted)]).Count == 0,
            "copilot plan: an unverified app's verbs are never given, named or not");
    }

    static async Task Passes(Action<bool, string> Check)
    {
        var apps = Apps(DeskTests.Room().Desk);
        var asked = new List<(string Context, bool Second)>();
        string Context(IReadOnlyCollection<string> full) => string.Join(",", full.Order(StringComparer.Ordinal));
        Func<string, bool, Task<(string, string?)>> Answer(params string[] answers)
            => (context, second) =>
            {
                asked.Add((context, second));
                return Task.FromResult<(string, string?)>((answers[Math.Min(asked.Count - 1, answers.Length - 1)], null));
            };

        var needs = """{"reply":"","question":"","steps":[],"need_verbs":["pm1","pat1"]}""";
        var then = """{"reply":"done","question":"","steps":[{"command":"PLAYLIST CUE show #3","why":"","target":"pm1"}],"need_verbs":["ag1"]}""";
        var plan = await Copilot.Passes(Answer(needs, then), Context, apps, ["pat1"]);
        Check(asked is [("pat1", false), ("pat1,pm1", true)] && plan is { Reply: "done", Steps: [{ Target.Id: "pm1" }] },
            $"copilot plan: need_verbs asks once more with those verbs, beside the named app's, and that plan is the answer — never a third pass ({string.Join(" | ", asked)})");
        asked.Clear();
        var once = await Copilot.Passes(Answer("""{"reply":"x","question":"","steps":[],"need_verbs":["pat1"]}"""), Context, apps, ["pat1"]);
        Check(asked is [("pat1", false)] && once.Reply == "x", "copilot plan: verbs already given are not asked for again");
        asked.Clear();
        var failed = await Copilot.Passes((c, s) => { asked.Add((c, s)); return Task.FromResult<(string, string?)>(("", "The copilot declined this request.")); }, Context, apps, []);
        Check(asked.Count == 1 && failed is { Reply: "The copilot declined this request.", Steps.Count: 0 }, "copilot plan: a failed pass is the plan's failure");
    }

    static void Budgets(Action<bool, string> Check)
    {
        var b = DeskTests.Room();
        b.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
        var apps = b.Desk.Apps().Append(new DeskApp("pm7", "PlayMap Side", "playmap", true, false)).ToList();
        PeerFact Fact(string kind, string id, string name) => new(kind, name, Light.Green, "", [], [], Id: id);
        var facts = new[] { Fact("playmap", "pm1", "PlayMap Hall"), Fact("patterns", "pat1", "Patterns FOH"), Fact("messenger", "ag1", "AGMessage"),
            Fact("playmap", "pm7", "PlayMap Side"), Fact("agent", "ppt1", "Slides PC") };
        // Three tables of 300 rows, about 200 bytes each: more than one app's budget each, more than the total for any two.
        IReadOnlyList<PeerCommand> Big(string id) => Enumerable.Range(0, 300)
            .Select(i => new PeerCommand($"{id.ToUpperInvariant()} VERB{i % 40} <thing> [<more>]", i % 3 == 0 ? "high" : "review", new string('w', 140))).ToList();
        var heads = AiContext.Apps(facts, apps, Big, []);
        Check(heads.Single(f => f.Id == "pm1") is { Collects: true, VerbsVerified: true, Verbs: null, MoreVerbs: 0, VerbHeads.Count: 40 }
              && heads.Single(f => f.Id == "ag1") is { Collects: false, VerbsVerified: true, Verbs: null }
              && heads.Single(f => f.Id == "pm7") is { VerbsVerified: false, Verbs: null, VerbHeads.Count: 0 }
              && heads.Single(f => f.Id == "ppt1") is { VerbsVerified: false, Collects: false, VerbHeads.Count: 0 },
            "copilot context: each app's collecting and verified flags and its verb heads; an unverified table or what is no app gives none");
        var full = AiContext.Apps(facts, apps, Big, ["pm1", "pat1", "ag1", "pm7"]);
        static int Bytes(PeerFact f) => (f.Verbs ?? []).Sum(v => JsonSerializer.SerializeToUtf8Bytes(new { verb = v.Verb, risk = v.Risk, words = v.Words }).Length + 1);
        var given = full.Where(f => f.Verbs is not null).ToList();
        Check(given.Select(f => f.Id).SequenceEqual(["pm1", "pat1", "ag1"]) && given.All(f => Bytes(f) <= AiContext.VerbsPerAppBytes && f.Verbs!.Count + f.MoreVerbs == 300)
              && given.Sum(Bytes) <= AiContext.VerbsTotalBytes && given[0].Verbs!.Count > 0 && given[0].MoreVerbs > 0
              && given[2].Verbs!.Count < given[0].Verbs!.Count && full.Single(f => f.Id == "pm7").Verbs is null
              && given[0].Verbs!.SequenceEqual(Big("pm1").Take(given[0].Verbs!.Count)),
            $"copilot context: whole verbs only for the apps asked, the table's first rows within 32 KB an app and 64 KB in all, the rest counted ({string.Join(", ", given.Select(f => $"{f.Id} {f.Verbs!.Count}+{f.MoreVerbs} {Bytes(f)} B"))})");

        var json = AiContext.Copilot(new CopilotFacts(DeskTests.T0, ShowClock.For("UTC"), "{}"u8.ToArray(), "{}"u8.ToArray(), [], [], [], 0, OverrunModel.Learn([]), [], [],
            AiContext.Apps(facts, apps, Big, ["pat1"])));
        using (var doc = JsonDocument.Parse(json))
        {
            var rows = doc.RootElement.GetProperty("other_apps").EnumerateArray().ToDictionary(a => a.GetProperty("id").GetString()!);
            var names = rows["pat1"].EnumerateObject().Select(p => p.Name).ToList();
            Check(rows["pm1"].GetProperty("collects").GetBoolean() && rows["pm1"].GetProperty("verbs_verified").GetBoolean() && rows["pm1"].GetProperty("verb_heads").GetArrayLength() == 40
                  && !rows["pm1"].TryGetProperty("verbs", out _) && rows["pat1"].GetProperty("verbs")[0].GetProperty("verb").GetString() == "PAT1 VERB0 <thing> [<more>]"
                  && rows["pat1"].GetProperty("more_verbs").GetInt32() > 0 && names.IndexOf("can") < names.IndexOf("collects") && names[^1] == "more_verbs"
                  && rows["pm7"].GetProperty("verb_heads").GetArrayLength() == 0 && !rows["pm7"].TryGetProperty("verbs", out _),
                "copilot context: other_apps rows end with collects, verbs_verified, verb_heads and — for an app given them — verbs and more_verbs");
        }

        // PlayMap's real busy context: its heads are a few kilobytes, its whole table many more.
        var path = ShowBusSpecTests.Master() is { } master ? Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(master)!)!, "tests", "Stagewatch.Tests", "fixtures", "playmap-context-busy.json") : null;
        Check(path is not null && File.Exists(path), "copilot context: PlayMap's busy context is in the suite's fixtures");
        if (path is null || !File.Exists(path)) return;
        var entry = PeerContexts.Parse(File.ReadAllText(path), "10.0.0.9", DeskTests.T0)!;
        var pm = new[] { new PeerFact("playmap", entry.Name, Light.Green, "", [], [], Id: entry.Id) };
        var desk = new[] { new DeskApp(entry.Id, entry.Name, "playmap", true, true) };
        var busy = AiContext.Apps(pm, desk, _ => entry.Commands, [entry.Id])[0];
        var headBytes = JsonSerializer.SerializeToUtf8Bytes(busy.VerbHeads).Length;
        Check(entry.Commands.Count == 199 && headBytes is > 1000 and < 4096 && Bytes(busy) > 8 * headBytes && busy.MoreVerbs + busy.Verbs!.Count == 199,
            $"copilot context: PlayMap's heads are {headBytes} bytes beside {Bytes(busy)} bytes of its whole verbs ({busy.Verbs!.Count} rows given, {busy.MoreVerbs} more)");
    }

    static void HelpSplit(Action<bool, string> Check)
    {
        var prompt = Copilot.PlanInstructions;
        Check(prompt.EndsWith(ShowCommandParser.Help, StringComparison.Ordinal) && !prompt.Contains("peer <app> <line>", StringComparison.Ordinal)
              && !prompt.Contains("desk approve", StringComparison.Ordinal) && !ShowCommandParser.Help.Contains("peer <app>", StringComparison.Ordinal)
              && ShowCommandParser.PersonHelp.Contains("peer <app> <line>", StringComparison.Ordinal),
            "copilot plan: the model reads Help, which leaves a person's peer and desk out; PersonHelp lists them");
        Check(prompt.Contains("'target'", StringComparison.Ordinal) && prompt.Contains("need_verbs", StringComparison.Ordinal)
              && prompt.Contains("that app's data, never", StringComparison.Ordinal) && prompt.Contains("only once a person approves", StringComparison.Ordinal),
            "copilot plan: the prompt says a step for an app names its id, verbs may be asked once, other apps' words are data and a person approves");
    }

    /// <summary>A runner on a clock the test moves, recording what ran and the undo batches it opened.</summary>
    sealed class Bench
    {
        public readonly ShowWorkflowTests.ManualTime Time = new(DeskTests.T0);
        public readonly List<string> Ran = [];
        public readonly List<string> Batches = [];
        public readonly List<(int, string)> Reports = [];
        public Func<PlanStep, PlanSend> App = s => new PlanSend(false, "no desk");

        public PlanRunner Runner() => new()
        {
            Here = s =>
            {
                lock (Ran) Ran.Add(s.Command);
                return s.Command == "bad" ? new PlanStepResult("refused", "refused: not a valid command") : new PlanStepResult(PlanRunner.DoneStage, "done");
            },
            App = s =>
            {
                lock (Ran) Ran.Add("→" + s.Target?.Id + " " + s.Command);
                return App(s);
            },
            Open = () => { lock (Batches) Batches.Add("open"); },
            Close = () => { lock (Batches) Batches.Add("close"); },
            Reported = (i, r) => { lock (Reports) Reports.Add((i, r.Stage)); },
            Time = Time,
        };

        public async Task Armed()
        {
            for (var i = 0; i < 200 && Time.Timers < 1; i++) await Task.Delay(10);
        }
    }

    static async Task Runner(Action<bool, string> Check)
    {
        // An app's step done: Stagewatch's steps before it are one batch, those after it another.
        var a = new Bench();
        var settle = new TaskCompletionSource<DeskResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        a.App = s => new PlanSend(true, "sent to PlayMap Hall as p1", "p1-k3m9", settle.Task);
        var run = a.Runner().RunAsync([new("trim 3 -60", null, true), new("note x", null, true), new("UNDO", Pm, true), new("note y", null, false), new("note z", null, true)]);
        await a.Armed();
        Check(a.Ran.SequenceEqual(["trim 3 -60", "note x", "→pm1 UNDO"]) && a.Batches.SequenceEqual(["open", "close"]) && !run.IsCompleted,
            "plan runner: Stagewatch's steps run in order as one batch, closed before an app's step, and the run waits for that app");
        settle.SetResult(new DeskResult("p1-k3m9", "done", true, "PlayMap Hall ran UNDO (p1)", true));
        var results = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Check(results.Select(r => r.Stage).SequenceEqual(["done", "done", "done", PlanRunner.Skipped, "done"]) && results[2].Words == "PlayMap Hall ran UNDO (p1)"
              && a.Ran.SequenceEqual(["trim 3 -60", "note x", "→pm1 UNDO", "note z"]) && a.Batches.SequenceEqual(["open", "close", "open", "close"])
              && a.Reports.Select(r => r.Item1).SequenceEqual([0, 1, 2, 3, 4]),
            "plan runner: once the app says done the run goes on — an unticked step skipped, the next run of Stagewatch's steps a batch of its own, each step reported in order");

        // No result within 10 s: the run stops, and the steps after it say why.
        var b = new Bench();
        b.App = s => new PlanSend(true, "sent to PlayMap Hall as p2 — it collects it and reports back", "p2-k3m9", new TaskCompletionSource<DeskResult>().Task);
        var slow = b.Runner().RunAsync([new("PLAYLIST CUE show #3", Pm, true), new("note after", null, true), new("note off", null, false)]);
        await b.Armed();
        b.Time.Advance(PlanRunner.AppWait - TimeSpan.FromMilliseconds(1));
        Check(!slow.IsCompleted, "plan runner: an app's step is waited for up to 10 s");
        b.Time.Advance(TimeSpan.FromMilliseconds(1));
        var timedOut = await slow.WaitAsync(TimeSpan.FromSeconds(5));
        Check(timedOut[0] is { Stage: "no result", Done: false } nr && nr.Words.StartsWith("no result in 10 s — sent to PlayMap Hall as p2", StringComparison.Ordinal)
              && timedOut[1] is { Stage: "not run", Words: PlanRunner.NotRun } && timedOut[2].Stage == PlanRunner.Skipped && !b.Ran.Contains("note after") && b.Batches.Count == 0,
            $"plan runner: no result in 10 s stops the run; the ticked steps after it are not run, in words ({timedOut[0].Words})");

        // The desk refuses the line, or the app refuses it: the run stops at once.
        var c = new Bench();
        c.App = s => new PlanSend(false, "PlayMap Hall has not asked this desk for approved lines in 16 s — it would not collect this");
        var refused = await c.Runner().RunAsync([new("note before", null, true), new("UNDO", Pm, true), new("note after", null, true)]).WaitAsync(TimeSpan.FromSeconds(5));
        Check(refused.Select(r => r.Stage).SequenceEqual(["done", "refused", "not run"]) && refused[1].Words.StartsWith("PlayMap Hall has not asked", StringComparison.Ordinal)
              && c.Batches.SequenceEqual(["open", "close"]) && !c.Ran.Contains("note after"),
            "plan runner: a step the desk refuses stops the run, the batch before it closed");
        var d = new Bench();
        d.App = s => new PlanSend(true, "sent", "p3-k3m9", Task.FromResult(new DeskResult("p3-k3m9", "refused", false, "PlayMap Hall refused PANIC: Show Mode is on (p3)", true)));
        var no = await d.Runner().RunAsync([new("PANIC", Pm, true), new("UNDO", Pm, true)]).WaitAsync(TimeSpan.FromSeconds(5));
        Check(no is [{ Stage: "refused", Words: "PlayMap Hall refused PANIC: Show Mode is on (p3)" }, { Stage: "not run" }] && d.Ran.Count == 1,
            "plan runner: an app's refusal stops the run in its words");

        // A Stagewatch step refused here stops it too; nothing ticked runs nothing.
        var e = new Bench();
        var bad = await e.Runner().RunAsync([new("bad", null, true), new("note after", null, true)]);
        var none = await new Bench().Runner().RunAsync([new("note", null, false), new("UNDO", Pm, false)]);
        Check(bad is [{ Stage: "refused" }, { Stage: "not run" }] && e.Batches.SequenceEqual(["open", "close"]) && none.All(r => r.Stage == PlanRunner.Skipped),
            "plan runner: a refused Stagewatch step stops the run; unticked steps never run");
    }

    /// <summary>Every app step goes to this Stagewatch's desk: the operator's line, born approved, waited for until its app answers.</summary>
    static async Task RunnerThroughDesk(Action<bool, string> Check)
    {
        var b = DeskTests.Room();
        var pm = NodeCredential.Key("kM");
        b.Desk.CollectApproved("pm1", pm);
        var time = new ShowWorkflowTests.ManualTime(DeskTests.T0);
        // The app's own wiring: its steps go through the desk as the operator who approved the plan.
        var runner = new PlanRunner
        {
            Here = _ => new PlanStepResult(PlanRunner.DoneStage, "done"),
            App = PlanRunner.ThroughDesk(b.Desk, PlanRunner.Origin("You asked")),
            Time = time,
        };
        var run = runner.RunAsync([new("PLAYLIST CUE show #3", Pm, true, LineRisk.Review), new("OUTPUTS OFF", Pm, true, LineRisk.Low)]);
        for (var i = 0; i < 200 && time.Timers < 1; i++) await Task.Delay(10);
        var sent = b.Desk.Queue.All().Single();
        Check(sent is { Line: "PLAYLIST CUE show #3", DecidedBy: "operator", State: SharedProposalState.Approved, Reason: PlanRunner.Reason, Risk: "review" }
              && sent.From.Id == DeskTests.DeskId && sent.Target.Id == "pm1"
              && b.Rows.Any(r => r.Words == "Sent p1 to PlayMap Hall (operator): PLAYLIST CUE show #3" && r.Who == "the copilot's plan (You asked)"),
            "plan runner: an app's step goes to this desk as the operator's line, born approved, fixed words for its reason, the plan named beside the journal row");
        b.Desk.CollectApproved("pm1", pm);
        b.Desk.ProposalResult(sent.Id, """{"ok":true,"words":"cued Intro"}""", pm);
        var results = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Check(results[0] is { Done: true, Words: "PlayMap Hall ran PLAYLIST CUE show #3: cued Intro (p1)" }
              && results[1] is { Stage: "refused", Words: "PlayMap Hall does not list that line — see its verbs" } && b.Desk.Queue.All().Count == 1,
            $"plan runner: the app's answer goes on to the next step, and a line it does not list is refused by the desk, unquoted, with nothing queued ({results[1].Words})");
        var json = b.Desk.GetProposal("p1").Json;
        Check(json.Contains("\"decidedBy\":\"operator\"", StringComparison.Ordinal) && !json.Contains("copilot's plan", StringComparison.Ordinal),
            "plan runner: who sent it stays in the desk's journal, never in the proposal other apps read");
    }

    /// <summary>
    /// A plan's step for another app goes only as the operator was shown it: to the app the plan named, by its id alone, at no higher a
    /// rating than its table gave when the plan was made — and a Run press asks every ticked step first, so nothing runs halfway.
    /// </summary>
    static void PlanSends(Action<bool, string> Check)
    {
        var b = DeskTests.Room();
        var pm = NodeCredential.Key("kM");
        b.Desk.CollectApproved("pm1", pm);
        var send = PlanRunner.ThroughDesk(b.Desk, PlanRunner.Origin("You asked"));
        var cue = new PlanStep("PLAYLIST CUE show #3", Pm, true, LineRisk.Review);
        Check(PlanRunner.Blocked(b.Desk, [new("note x", null, true), cue]) is null && b.Desk.WhyNotPlanned(Pm, cue.Command, LineRisk.Review) is null,
            "plan sends: a ticked step its app takes now, at the rating the operator was shown, blocks nothing");

        // Between the plan and Run its app's table changes: the cue is high now. Shown REVIEW and ticked, it never goes as high.
        b.Post("playmap", "pm1", "PlayMap Hall", pm, [.. DeskTests.PlayMapVerbs.Select(v => v.Item1.StartsWith("PLAYLIST CUE", StringComparison.Ordinal) ? (v.Item1, "high") : v)]);
        var rows = b.Rows.Count;
        const string rose = "PlayMap Hall now rates this line high — tick it again";
        Check(PlanRunner.Blocked(b.Desk, [new("note x", null, true), new("STATUS", Pm, false), cue]) is { Index: 2, Why: { Risen: LineRisk.High, Shown: rose } }
              && b.Rows.Count == rows && b.Desk.Queue.All().Count == 0,
            "plan sends: a Run press finds the step its app now rates above what was shown, by its index, with nothing sent or journalled");
        var refused = send(cue);
        Check(refused is { Ok: false, Risen: LineRisk.High, Proposal: null, Id: null, Words: rose } && b.Desk.Queue.All().Count == 0
              && !b.Rows.Any(r => r.Words.StartsWith("Sent", StringComparison.Ordinal)) && b.Last == "Refused to send to PlayMap Hall (operator): " + rose,
            $"plan sends: a step its app now rates higher is refused as it goes — nothing queued, no row says Sent ({b.Last})");
        var again = send(cue with { Risk = LineRisk.High });
        Check(again is { Ok: true, Proposal: { Risk: "high", DecidedBy: "operator", State: SharedProposalState.Approved } } && b.Desk.Queue.All().Count == 1,
            "plan sends: ticked again at the rating it has now, it goes");
        Check(PlanRunner.Blocked(b.Desk, [cue with { Ticked = false }]) is null,
            "plan sends: an unticked step never blocks a run");

        // By id alone: an id the desk would resolve by a kind word to another app is that app's no more.
        var old = new NodeRef("playmap", "playmap", "Old PlayMap");
        var queued = b.Desk.Queue.All().Count;
        Check(b.Desk.Resolve("playmap").App?.Id == "pm1"
              && send(new("STATUS", old, true)) is { Ok: false, Words: "Old PlayMap is no longer on the show network" } && b.Desk.Queue.All().Count == queued
              && PlanRunner.Blocked(b.Desk, [new("STATUS", old, true)]) is { Index: 0, Why.Shown: "Old PlayMap is no longer on the show network" },
            "plan sends: the app a plan named is found by its id alone — gone, the step is refused, never sent to another app by its name or kind");

        // Its app is not collecting: refused at the press, in the words its door says.
        var quiet = DeskTests.Room();
        Check(PlanRunner.Blocked(quiet.Desk, [new("STATUS", Pm, true)]) is { Index: 0, Why: { Risen: null } why } && why.Shown == quiet.Desk.Door("pm1").NoSend,
            $"plan sends: a step whose app is not collecting blocks the run in its door's words ({quiet.Desk.Door("pm1").NoSend})");
        Check(b.Desk.SendPlanned(Pm, "STATUS", LineRisk.Low, DeskOrigin.Copilot, PlanRunner.Reason) is { Ok: false, Words: DeskService.PersonSends },
            "plan sends: a plan's step goes as a person's line only — never as the copilot's");
    }
}
