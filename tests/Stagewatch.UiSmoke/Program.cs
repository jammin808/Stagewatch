using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Stagewatch.App;
using Stagewatch.App.ViewModels;
using Stagewatch.Core;
using Stagewatch.Licensing;

namespace UiSmoke;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "sw-uismoke");
        Directory.CreateDirectory(outDir);
        App.Hosted = true;   // the harness owns startup: never touch (or wake) a Stagewatch running on this PC
        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var code = Body(outDir, args.Length > 1 ? args[1] : "");
        Environment.Exit(code);
        return code;
    }

    private static int Body(string outDir, string only)
    {
        var failures = 0;

        var settingsPath = Path.Combine(outDir, "settings.json");
        if (File.Exists(settingsPath)) File.Delete(settingsPath);
        var store = new SettingsStore(settingsPath);
        store.Update(s => { s.Remote.WebPort = 19924; s.Remote.WebBindAll = false; s.Remote.Https = false; /* no certificate in the real AppData */ s.Companion.Enabled = false; s.Remote.OscEnabled = false; });
        var license = LicenseStatus.Trial(TimeSpan.FromDays(10));
        typeof(App).GetProperty("SettingsStore")!.SetValue(null, store);
        typeof(App).GetProperty("License")!.SetValue(null, license);

        void Shot(Window w, string name, Action<Window>? before = null)
        {
            try
            {
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Left = -30000; w.Top = 0; w.ShowInTaskbar = false; w.ShowActivated = false;
                w.Show();
                before?.Invoke(w);
                Pump(600);
                var content = (FrameworkElement)w;
                var width = (int)Math.Max(1, content.ActualWidth); var height = (int)Math.Max(1, content.ActualHeight);
                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                var bg = new DrawingVisual();
                using (var dc = bg.RenderOpen()) dc.DrawRectangle(w.Background, null, new Rect(0, 0, width, height));
                rtb.Render(bg);
                rtb.Render((Visual)VisualTreeHelper.GetChild(w, 0));
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(Path.Combine(outDir, name + ".png"));
                enc.Save(fs);
                Console.WriteLine($"OK   {name} ({width}x{height})");
            }
            catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex}"); }
            finally { try { w.Hide(); } catch { } }
        }

        // A window rendered to a PNG as it is (already shown, off-screen).
        void Save(Window w, string name)
        {
            Pump(300);
            var width = (int)Math.Max(1, w.ActualWidth); var height = (int)Math.Max(1, w.ActualHeight);
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle(w.Background, null, new Rect(0, 0, width, height));
            rtb.Render(bg);
            rtb.Render((Visual)VisualTreeHelper.GetChild(w, 0));
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(Path.Combine(outDir, name + ".png"));
            enc.Save(fs);
            Console.WriteLine($"OK   {name} ({width}x{height})");
        }
        void Expect(bool ok, string what) { Console.WriteLine((ok ? "OK   " : "FAIL ") + what); if (!ok) failures++; }

        if (only == "panels")
        {
            // Draggable panels and ALL GO, on the real main window (off-screen).
            try
            {
                var main = new MainWindow();
                var vm = (MainViewModel)main.DataContext;
                Shot(main, "panels-main", w =>
                {
                    w.Width = 1500; w.Height = 950;
                    Pump(400);
                    var dock = (PanelDock)typeof(MainWindow).GetField("_dock", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
                    var tabs = (TabControl)w.FindName("SideTabs");
                    var sideCol = (ColumnDefinition)w.FindName("SideCol");
                    var cuesTab = (TabItem)w.FindName("TabCues");
                    Expect(tabs.Items.Count == 6, $"six panels docked to start ({tabs.Items.Count})");

                    // Stand two departments by, then tear the Cues panel out into its own window.
                    Run(vm, "signal standby Lighting LX_12");
                    Run(vm, "signal standby Sound SQ_4");
                    Pump(500);
                    var lx = vm.DepartmentRows.FirstOrDefault(r => r.Name == "Lighting");
                    Expect(vm.StandingByCount == 2 && lx is { StandingBy: true, State: "STANDBY" } && vm.AllGoText.Contains("(2)"),
                           $"two departments standing by, shown on their rows ({vm.StandingByCount}, {lx?.State}, {vm.AllGoText})");
                    dock.PopOut("cues");
                    Pump(500);
                    var panel = Application.Current.Windows.OfType<PanelWindow>().SingleOrDefault();
                    Expect(panel is { PanelId: "cues" } && !tabs.Items.Contains(cuesTab) && tabs.Items.Count == 5 && vm.Settings.Panels.Floating.Any(f => f.Id == "cues"),
                           "Cues & Q&A pops out into its own window (and the layout remembers)");
                    if (panel is not null)
                    {
                        panel.Width = 440; panel.Height = 760;
                        Save(panel, "panel-cues-standing-by");
                    }
                    Run(vm, "signal go all");
                    Pump(500);
                    var gos = vm.Snapshots.Latest.Signals.Where(g => g.Kind == SignalKind.Go).Select(g => g.Target + " " + g.Text).ToList();
                    Expect(vm.StandingByCount == 0 && gos.Count == 2 && gos.Contains("Lighting LX_12") && gos.Contains("Sound SQ_4") && lx!.Gone,
                           $"ALL GO fires every department standing by with its cue ({string.Join(", ", gos)})");
                    if (panel is not null) Save(panel, "panel-cues-all-go");

                    // Reorder, dock back where it was, fold the column away with everything out.
                    dock.Nudge("timers", -10);
                    Expect(((tabs.Items[0] as TabItem)?.Name) == "TabAux" && vm.Settings.Panels.Order[0] == "timers", "a panel moves along the strip, and the order is saved");
                    dock.Dock("cues");
                    Pump(300);
                    Expect(tabs.Items.Count == 6 && tabs.SelectedItem == cuesTab && !Application.Current.Windows.OfType<PanelWindow>().Any() && vm.Settings.Panels.Floating.Count == 0,
                           "docking puts the panel back in the strip and closes its window");
                    foreach (var id in PanelLayout.Default) dock.PopOut(id);
                    Pump(500);
                    Expect(tabs.Items.Count == 0 && sideCol.ActualWidth < 1 && Application.Current.Windows.OfType<PanelWindow>().Count() == 6,
                           $"with every panel out, the timer gets the whole window (side column {sideCol.ActualWidth:0})");
                    Save(w, "panels-all-out");
                    dock.Reset();
                    Pump(300);
                    Expect(tabs.Items.Count == 6 && sideCol.ActualWidth > 299 && !Application.Current.Windows.OfType<PanelWindow>().Any(), "reset docks everything at the standard width");

                    // A saved layout comes back as it was left (what a restart does).
                    App.SettingsStore.Update(s => { PanelLayout.Move(s.Panels, "running", 0); PanelLayout.Float(s.Panels, "producer", -30000, 0, 420, 700); s.Panels.SideWidth = 480; });
                    dock.Restore();
                    Pump(400);
                    Expect((tabs.Items[0] as TabItem)?.Name == "TabRunning" && Application.Current.Windows.OfType<PanelWindow>().Any(p => p.PanelId == "producer")
                           && Math.Abs(sideCol.ActualWidth - 480) < 2, $"a saved layout is restored (first {(tabs.Items[0] as TabItem)?.Name}, width {sideCol.ActualWidth:0})");
                    dock.Reset();

                    // Show network: another app posts its context; Producer lists it with its light, headline and problems.
                    var agm = "{\"v\":1,\"node\":{\"t\":\"messenger\",\"id\":\"agm01\",\"name\":\"AGM backchannel\"},\"light\":\"amber\",\"headline\":\"Board room live · 2 action prompts open\"," +
                              "\"items\":[{\"id\":\"prompts\",\"kind\":\"prompts\",\"label\":\"Action prompts\",\"light\":\"amber\",\"state\":\"2 waiting for the chair\"}]}";
                    var pat = "{\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"9f8e7d6c\",\"name\":\"Patterns FOH\"},\"light\":\"red\",\"headline\":\"14 things · 1 red — Projector 1 MISMATCH\"," +
                              "\"items\":[{\"id\":\"screen:1\",\"kind\":\"screen\",\"label\":\"Projector 1\",\"light\":\"red\",\"state\":\"MISMATCH: carries 1080p60 against a 50 contract\"}]}";
                    Expect(vm.AcceptPeerContext(agm, "10.0.0.7", NodeCredential.None) is (200, null, NodeProof.None) && vm.AcceptPeerContext(pat, "10.0.0.5", NodeCredential.None) is (200, null, NodeProof.None),
                           "two show apps post their context (held unverified: no credential names them)");
                    Pump(1600);
                    Expect(vm.PeerRows.Any(r => r is { Name: "Patterns FOH", Kind: "Patterns", Light: "red" }) && vm.PeerRows.Any(r => r is { Name: "AGM backchannel", Light: "amber" }) && vm.ContextJson() is { } ctx && ctx.Contains("AGM backchannel"),
                           $"Producer lists them, and this Stagewatch's context names them ({vm.PeerRows.Count})");
                    Expect(vm.PeerRows.Where(r => r.Name is "Patterns FOH" or "AGM backchannel").ToList() is { Count: 2 } posted
                           && posted.All(r => r is { Mark: "○", LinkLine: "posts its context unverified, so it sets no risk · not linked on /ws", KeyId: "", NodeId.Length: > 0 })
                           && vm.ContextJson() is { } ctxRows && ctxRows.Contains("\"link\":\"\",\"verified\":\"none\"}", StringComparison.Ordinal),
                           "each row says what proves it (○ unverified, a shape beside the light) and how it reaches this Stagewatch; the context says the same");

                    // A key of its own: ✓, and "Revoke its key…" has the key to revoke. The same key pasted into a second install, and
                    // a node key on a socket that names no id, are rows apart: ○, no key to revoke, and the refused one can be given a key.
                    var patSecret = vm.CreateApiKey("Patterns FOH", ApiKeyScope.Node);
                    var patKey = vm.ApiKeys.SingleOrDefault(k => k.Name == "Patterns FOH")?.Id ?? "";
                    Expect(patSecret is not null && vm.AcceptPeerContext(pat, "10.0.0.5", NodeCredential.Key(patKey)) is (200, null, NodeProof.Key),
                           "Patterns posts again with a node key of its own");
                    Pump(1600);
                    Expect(vm.PeerRows.SingleOrDefault(r => r.Id == "9f8e7d6c") is { Mark: "✓", Apart: false, RefusalOnly: false } keyedRow && keyedRow.KeyId == patKey
                           && keyedRow.LinkLine == "posts its context with key 'Patterns FOH' · not linked on /ws" && keyedRow.MarkTip == ShowNetwork.MarkWords("key"),
                           "a key of its own: ✓, what proves it in words, and the key Revoke would revoke");
                    var oldSecret = vm.CreateApiKey("Old AGMessage", ApiKeyScope.Node);
                    var port = vm.WebPort ?? 19924;
                    using var twin = new ClientWebSocket();
                    twin.Options.SetRequestHeader("Authorization", "Bearer " + patSecret);
                    using var legacy = new ClientWebSocket();
                    legacy.Options.SetRequestHeader("Authorization", "Bearer " + oldSecret);
                    var linked = Await(twin.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?node=9f8e7d6d&t=patterns"), CancellationToken.None), 5000)
                                 && Await(legacy.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?cid=legacy01&page=/patterns"), CancellationToken.None), 5000);
                    Pump(1600);
                    var twinRow = vm.PeerRows.SingleOrDefault(r => r.Id == "9f8e7d6d");
                    var keyRow = vm.PeerRows.SingleOrDefault(r => r.Name == "Key 'Old AGMessage'");
                    Expect(linked && twinRow is { Apart: true, Mark: "○", Name: "Patterns 9f8e7d6d", NodeId: "9f8e7d6d", KeyId: "", MakeKeyLabel: "Make a key for Patterns" }
                           && twinRow.Headline == ShowNetwork.ApartUnproven && twinRow.LinkLine.Contains("unverified: this key speaks for patterns 9f8e7d6c", StringComparison.Ordinal),
                           $"a key two installs share: the second is a row apart, ○, and can be given a key of its own ({twinRow?.LinkLine})");
                    Expect(keyRow is { Apart: true, Mark: "○", NodeId: "", KeyId: "", MakeKeyLabel: "" } && keyRow.Headline == ShowNetwork.ApartNoId
                           && vm.ShowNetworkLine.Contains("· 2 unverified on /ws", StringComparison.Ordinal) && vm.ContextJson() is { } apartCtx
                           && PeerIds(apartCtx) is var peerIds && peerIds.Contains("9f8e7d6c") && !peerIds.Contains("9f8e7d6d") && !peerIds.Any(id => id.StartsWith("key:", StringComparison.Ordinal)),
                           $"a node key that names no id is a row of its own with no node id to copy; the headline counts both, and the context lists neither ({vm.ShowNetworkLine})");

                    // The bindings are kept: Patterns' first use is saved on its key and journalled; Settings' row says what it
                    // speaks for and what it was refused for. Letting the old key speak for the second install (the row's
                    // right-click, the same rebind as Settings) saves both, journals the operator's choice, and the row's
                    // right-click offers what the buttons do.
                    var patSaved = vm.Settings.Remote.ApiKeys.SingleOrDefault(k => k.Id == patKey);
                    Expect(patSaved is { Node: "9f8e7d6c", NodeKind: "patterns" } && vm.ContextJson() is { } boundCtx
                           && RecentLines(boundCtx).Any(l => l.EndsWith("API key 'Patterns FOH' now speaks for patterns 9f8e7d6c (first use)", StringComparison.Ordinal)),
                           $"a key's first use is saved on the key (a restart keeps it) and journalled ({patSaved?.Node} {patSaved?.NodeKind})");
                    Expect(vm.ApiKeyBindingLine(patKey).StartsWith("speaks for patterns 9f8e7d6c · refused for patterns 9f8e7d6d ", StringComparison.Ordinal),
                           $"Settings: the key's row says what it speaks for and what it was refused for ({vm.ApiKeyBindingLine(patKey)})");
                    var oldKey = vm.ApiKeys.SingleOrDefault(k => k.Name == "Old AGMessage")?.Id ?? "";
                    var menu = twinRow is null ? new List<string>() : main.ActionsFor("show-network", new Border { DataContext = twinRow }).OfType<MenuItem>().Select(m => m.Header as string ?? "").ToList();
                    Expect(menu.SequenceEqual(new[] { "Copy node id", "Make a key for Patterns", "Let a key speak for this node…", "Revoke its key…" }),
                           $"the row's right-click offers its button's action and the rebind ({string.Join(", ", menu)})");
                    var keyChoices = twinRow is null ? Array.Empty<KeyChoice>() : vm.KeyChoicesFor(twinRow);
                    Expect(keyChoices.Count == 2 && keyChoices[0].KeyId == patKey && keyChoices[0].Label.Contains("refused for this node", StringComparison.Ordinal)
                           && keyChoices[0].Label.EndsWith("speaks for patterns 9f8e7d6c now, and would stop", StringComparison.Ordinal)
                           && keyChoices[1].KeyId == oldKey && keyChoices[1].Label.EndsWith("speaks for no node yet", StringComparison.Ordinal),
                           $"Let a key speak for this node… lists the key refused for it first, saying what each speaks for now ({string.Join(" | ", keyChoices.Select(c => c.Label))})");
                    var (rebound, rebindWords) = twinRow is null ? (false, "") : vm.LetKeySpeakForPeer(twinRow, oldKey);
                    Pump(1600);
                    var oldSaved = vm.Settings.Remote.ApiKeys.SingleOrDefault(k => k.Id == oldKey);
                    Expect(rebound && rebindWords == "API key 'Old AGMessage' now speaks for patterns 9f8e7d6d (the operator's choice; it spoke for no node)"
                           && oldSaved is { Node: "9f8e7d6d", NodeKind: "patterns" } && vm.ApiKeyBindingLine(oldKey) == "speaks for patterns 9f8e7d6d"
                           && vm.ContextJson() is { } reboundCtx && RecentLines(reboundCtx).Any(l => l.EndsWith(rebindWords, StringComparison.Ordinal)),
                           $"the operator's choice is saved and journalled ({rebindWords})");
                    Expect(vm.RebindApiKey(oldKey, "patterns", "9f8e7d6d") is (false, var same) && same.StartsWith("Not changed: it already speaks for patterns 9f8e7d6d", StringComparison.Ordinal)
                           && vm.ApiKeyNodeChoices(patKey).FirstOrDefault()?.Id == "9f8e7d6d",
                           "a rebind that changes nothing is refused with the reason; Settings offers the node a key was refused for first");
                    tabs.SelectedItem = (TabItem)w.FindName("TabProducer");
                    Pump(400);
                    Save(w, "producer-show-network");
                });
                vm.Dispose();
            }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL panels: " + ex); }
            Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
            return failures;
        }

        if (only == "review")
        {
            // The reviewer's scenarios, on a real view model.
            void Report(bool ok, string what) { Console.WriteLine((ok ? "OK   " : "FAIL ") + what); if (!ok) failures++; }
            store.Update(s =>
            {
                s.RundownName = "Day 1";
                s.Rundown.ScheduledStart = DateTime.Now.AddHours(-3).ToString("HH:mm");
                s.Rundown.Items = new List<RundownItem> { new() { Title = "Day 1 talk", DurationSeconds = 300 }, new() { Title = "Day 1 panel", DurationSeconds = 900 } };
                s.OtherRundowns.Add(new NamedRundown { Name = "Day2", Rundown = new Rundown { Items = { new RundownItem { Title = "Day 2 opening", DurationSeconds = 300 } } } });
            });
            var vm = new MainViewModel(store, license);
            Pump(400);

            // 1 + 5: trim in Day 1, start its talk, switch, undo.
            Run(vm, "trim 1 -60");
            Run(vm, "goto 1 start");
            Pump(1200);
            Run(vm, "rundown Day2");
            Pump(400);
            var f = vm.Snapshots.Latest;
            Report(f.State != TimerState.Running && f.Title.Contains("Day 2 opening") && !f.Title.Contains("Day 1"),
                $"switching stops the old timer and takes its title off screens (state={f.State}, title={f.Title})");
            Run(vm, "undo");
            Pump(300);
            Report(vm.Settings.Rundown.Items[0].AdjustSeconds == 0 && vm.Settings.Rundown.Items[0].EffectiveSeconds == 300,
                $"undo in Day 2 doesn't touch Day 2's first item ({vm.Settings.Rundown.Items[0].EffectiveSeconds} s)");
            Run(vm, "rundown Day 1");
            Pump(300);
            Run(vm, "undo");
            Pump(300);
            Report(vm.Settings.Rundown.Items[0].AdjustSeconds == 0, $"back in Day 1, undo reverses Day 1's trim ({vm.Settings.Rundown.Items[0].AdjustSeconds})");

            // 6: Roll steps aside when the operator adds a minute.
            store.Update(s => { s.Rundown.ScheduledStart = DateTime.Now.AddMinutes(-1).ToString("HH:mm"); s.Rundown.Items[0].AdjustSeconds = 0; });
            vm.ApplySettings();
            Run(vm, "roll on");
            Pump(1200);
            var rolling = vm.RollMode && vm.Snapshots.Latest.State == TimerState.Running;
            Run(vm, "nudge 60");
            Pump(1000);
            Report(rolling && !vm.RollMode, $"adding a minute hands over from Roll (was rolling={rolling}, roll now={vm.RollMode})");

            // 3: a one-item looping running order restarts on each pass.
            var secIntoMinute = DateTime.Now.Second;
            store.Update(s =>
            {
                s.Rundown.ScheduledStart = DateTime.Now.ToString("HH:mm");
                s.Rundown.Items = new List<RundownItem> { new() { Title = "Loop clip", DurationSeconds = 10 } };
                s.Rundown.Loop = -1;
            });
            vm.ApplySettings();
            Run(vm, "roll on");
            Pump(800);
            var before = vm.Snapshots.Latest;
            Pump(12_500);                    // past at least one pass boundary
            var after = vm.Snapshots.Latest;
            Report(vm.RollMode && after.State == TimerState.Running && after.Seconds > 0,
                $"one-item loop restarts on the next pass (before: {before.State} {before.Text}; after: {after.State} {after.Text}; roll={vm.RollMode})");

            vm.Dispose();
            Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
            return failures;
        }
        if (only == "vm")
        {
            // Roll, department notes and switching running orders on a real view model with a live dispatcher.
            var start = DateTime.Now.AddMinutes(-2);
            store.Update(s =>
            {
                s.Rundown.ScheduledStart = start.ToString("HH:mm");
                s.Rundown.Items = new List<RundownItem>
                {
                    new() { Title = "Walk-in", DurationSeconds = 60 },
                    new() { Title = "Keynote", DurationSeconds = 1800 },
                    new() { Title = "Panel", DurationSeconds = 1800 },
                };
                s.OtherRundowns.Add(new NamedRundown { Name = "Day 2", Rundown = new Rundown { Items = { new RundownItem { Title = "Day 2 opening", DurationSeconds = 600 } } } });
            });
            var vm = new MainViewModel(store, license);
            Pump(400);
            Run(vm, "roll on");
            Pump(1200);
            var f = vm.Snapshots.Latest;
            var minuteOffset = (DateTime.Now - DateTime.Now.Date.AddHours(start.Hour).AddMinutes(start.Minute)).TotalSeconds;
            Console.WriteLine($"roll: item={f.ItemIndex} state={f.State} title={f.Title} text={f.Text} roll={f.Roll} line={vm.RollLine}");
            // Started 2 min (+ seconds) ago: Walk-in (1 min) is over, so Keynote should be running with ~28 min left.
            var ok1 = f.Roll && f.ItemIndex == 1 && f.State == TimerState.Running && f.Seconds is > 1500 and <= 1740;
            Console.WriteLine((ok1 ? "OK   " : "FAIL ") + "roll runs the item whose planned window is now");
            if (!ok1) failures++;
            Run(vm, "pause");
            Pump(600);
            Console.WriteLine((!vm.RollMode ? "OK   " : "FAIL ") + "roll steps aside when the operator pauses");
            if (vm.RollMode) failures++;
            Run(vm, "deptnote 2 Lighting Blue wash");
            Pump(300);
            var notes = vm.DeptNotesFor("lighting");
            var ok3 = notes is { Count: 3 } && notes[1].Note == "Blue wash" && vm.Snapshots.Latest.NotesVersion > 0;
            Console.WriteLine((ok3 ? "OK   " : "FAIL ") + $"department note stored and versioned ({notes?[1].Note})");
            if (!ok3) failures++;
            Run(vm, "trim 3 -120");
            Pump(200);
            var trimmed = vm.Settings.Rundown.Items[2].AdjustSeconds;
            Run(vm, "undo");
            Pump(200);
            var undone = vm.Settings.Rundown.Items[2].AdjustSeconds;
            Run(vm, "redo");
            Pump(200);
            var redone = vm.Settings.Rundown.Items[2].AdjustSeconds;
            var okU = trimmed == -120 && undone == 0 && redone == -120;
            Console.WriteLine((okU ? "OK   " : "FAIL ") + $"undo/redo a trim ({trimmed} → {undone} → {redone})");
            if (!okU) failures++;
            Run(vm, "undo");
            Pump(200);
            var plan = vm.PlanRecovery(300, RecoverMethod.Proportional, "rest");
            var sim = vm.Simulate(plan.Trims);
            Console.WriteLine($"sim: {sim?.Summary} rows={sim?.Rows.Count} plan={string.Join(",", plan.Trims)}");
            vm.ApplyRecovery(plan, "Recovered 5:00");
            Pump(200);
            var after = vm.Settings.Rundown.Items.Sum(i => i.AdjustSeconds);
            Run(vm, "undo");
            Pump(200);
            var back = vm.Settings.Rundown.Items.Sum(i => i.AdjustSeconds);
            var okR = sim is not null && after == -plan.Applied && back == 0;
            Console.WriteLine((okR ? "OK   " : "FAIL ") + $"recovery applies as one undoable step ({after} → {back})");
            if (!okR) failures++;
            // Receipts tell the truth: a refused action is ok:false with why, not "applied" with the failure as its effect.
            ShowCommand Cmd(string line) { var tk = line.Split(' '); ShowCommandParser.TryParse(tk[0], tk.Skip(1).ToList(), out var cc, out _); return cc; }
            var missing = vm.ExecuteWithReceiptAsync(Cmd("rundown Nowhere"), "r1", TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            var noPatterns = vm.ExecuteWithReceiptAsync(Cmd("patterns end 10"), "r2", TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            var fine = vm.ExecuteWithReceiptAsync(Cmd("note hello"), "r3", TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            // No playout link here (or no licence for one): refused with the reason, and nothing sent.
            var noPlayout = vm.ExecuteWithReceiptAsync(Cmd("playout pause"), "r4", TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            var okR2 = !missing.Ok && missing.Error!.Contains("Nowhere") && !noPatterns.Ok && noPatterns.Error!.Contains("Patterns") && fine is { Ok: true, Stage: "applied" }
                       && !noPlayout.Ok && noPlayout.Error!.Contains("playout");
            Console.WriteLine((okR2 ? "OK   " : "FAIL ") + $"receipts: refusals are refusals ({missing.Ok}/{missing.Error} · {noPatterns.Ok}/{noPatterns.Error} · {noPlayout.Ok}/{noPlayout.Error} · {fine.Stage})");
            if (!okR2) failures++;
            // A speaker message that asks for its acknowledgement, as a show app sends one: the receipt names its signal, the
            // first ACK applies and the frame says done by whom, and an ACK that would change nothing is refused with the board's
            // words, never applied (the rig's tests/e2e/message.e2e.cjs pins the same steps' bytes).
            var asked = vm.ExecuteWithReceiptAsync(Cmd("message Wrap info slow ack"), "m1", TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            var signalId = asked.Refs is { } askedRefs && askedRefs.TryGetValue("messageId", out var named) ? named : 0;
            var ackLine = "ack " + signalId.ToString(System.Globalization.CultureInfo.InvariantCulture) + " seen Patterns";
            var firstAck = vm.ExecuteWithReceiptAsync(Cmd(ackLine), "m2", TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            var secondAck = vm.ExecuteWithReceiptAsync(Cmd(ackLine), "m3", TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            Pump(200);
            var answered = vm.Snapshots.Latest;
            var okM = asked is { Ok: true, Stage: "applied" } && signalId > 0 && firstAck is { Ok: true, Stage: "applied" }
                      && secondAck is { Ok: false } && secondAck.Error == SignalBoard.GoneWords
                      && answered.MessageSignalId == signalId && answered.MessageAck == "done" && answered.MessageAckBy == "Patterns";
            Console.WriteLine((okM ? "OK   " : "FAIL ") + $"message ack: refs.messageId {signalId}, first ACK {firstAck.Stage}, second refused ({secondAck.Ok}/{secondAck.Error}), frame {answered.MessageAck} by {answered.MessageAckBy}");
            if (!okM) failures++;

            // The shared approval queue: another app's proposal lands in the Copilot list; approving it here lets that app
            // collect it; one aimed at this Stagewatch runs here with a receipt.
            var forPatterns = vm.SubmitProposal("{\"from\":{\"t\":\"patterns\",\"id\":\"pat1\",\"name\":\"Patterns FOH\"},\"target\":{\"t\":\"patterns\",\"id\":\"pat1\",\"name\":\"Patterns FOH\"},\"line\":\"EYE FOCUS screen 2\",\"reason\":\"Projector 1 is on the wrong mode\"}", NodeCredential.Key("keyP"));
            var forUs = vm.SubmitProposal("{\"from\":{\"t\":\"messenger\",\"id\":\"ag1\",\"name\":\"AGM\"},\"target\":{\"t\":\"main\"},\"line\":\"note From the board\",\"reason\":\"log it\"}", NodeCredential.Key("keyA"));
            Pump(300);
            var pPat = vm.Proposals.FirstOrDefault(x => x.Source.StartsWith("Patterns FOH"));
            var pUs = vm.Proposals.FirstOrDefault(x => x.Source.StartsWith("AGM"));
            var queued = forPatterns.Status == 201 && forUs.Status == 201 && pPat is { IsPending: true } && pUs is { IsPending: true } && pPat.Steps[0].Risk == Stagewatch.Integrations.CopilotRisk.High;
            vm.ApproveProposalCommand.Execute(pPat);
            vm.ApproveProposalCommand.Execute(pUs);
            Pump(300);
            var collected = vm.CollectApproved("pat1", NodeCredential.Key("keyP"));
            var stolen = vm.CollectApproved("pat1", NodeCredential.Key("keyA"));
            var okD = queued && collected.Json.Contains("EYE FOCUS screen 2") && stolen.Status == 403 && pUs!.Steps[0].Result.StartsWith("done");
            Console.WriteLine((okD ? "OK   " : "FAIL ") + $"approval desk: queued, approved, collected by its own app only; ours ran here ({forPatterns.Status}/{forUs.Status}, {pUs?.Steps[0].Result})");
            if (!okD) failures++;

            Run(vm, "rundown Day 2");
            Pump(400);
            var g = vm.Snapshots.Latest;
            var ok4 = g.RundownName == "Day 2" && g.RundownNames.Contains("Main") && g.ItemCount == 1 && vm.Settings.OtherRundowns.Any(r => r.Name == "Main" && r.Rundown.Items[1].DeptNote("Lighting") == "Blue wash");
            Console.WriteLine((ok4 ? "OK   " : "FAIL ") + $"switched running order ({g.RundownName}; others: {string.Join(",", g.RundownNames)})");
            if (!ok4) failures++;
            vm.Dispose();
            Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
            return failures;
        }
        if (only != "main" && only != "main-ro")
        {
            if (only == "screens-windows")
                store.Update(s => s.Remote.Windows = new List<ScreenWindow>
                {
                    new() { Id = "wlobby1", Name = "lobby", Token = ScreenWindows.NewToken(), Content = "/clock", Revision = 2 },
                    new() { Id = "wstage1", Name = "stage-left", Token = ScreenWindows.NewToken(), Content = "/viewer", Hold = true, HoldText = "Back at 2pm", Revision = 3 },
                });
            var vm2 = new MainViewModel(store, license);
            switch (only)
            {
                case "screens": Shot(new ScreensWindow(vm2), "screens"); break;
                case "screens-windows":
                {
                    // The Windows tab, through the view model's own commands: a made window, a refused no-op, the rows.
                    static ShowCommand Line(string line) { var tk = line.Split(' '); ShowCommandParser.TryParse(tk[0], tk.Skip(1).ToList(), out var cc, out _); return cc; }
                    var made = vm2.ExecuteWithReceiptAsync(Line("window new foyer /timeline"), "w1", TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                    var again = vm2.ExecuteWithReceiptAsync(Line("window show lobby /clock"), "w2", TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                    var okW = made is { Ok: true, Stage: "applied" } && !again.Ok && again.Error!.Contains("already shows") && vm2.ControlledWindows.Count == 3
                              && vm2.Settings.Remote.Windows.Count == 3;
                    Console.WriteLine((okW ? "OK   " : "FAIL ") + $"windows: made through the command, a no-op refused with words, saved ({made.Effect} · {again.Error})");
                    if (!okW) failures++;
                    var sw = new ScreensWindow(vm2);
                    sw.ShowWindowsTab();
                    Shot(sw, "screens-windows");
                    break;
                }
                case "copilot": Shot(new CopilotWindow(vm2), "copilot"); break;
                case "slides-agent": Shot(new SlidesAgentWindow(), "slides-agent"); break;
                case "rundown-editor": Shot(new RundownEditor(store, _ => Task.FromResult(new Stagewatch.Integrations.AgendaDraft(Array.Empty<Stagewatch.Integrations.AgendaDraftItem>(), Array.Empty<string>()))), "rundown-editor"); break;
                case "settings":
                {
                    // Settings on the view model's own key hooks (as the desk opens it): a node key's row says what it speaks
                    // for, a typed id that is this Stagewatch's own is refused, and Remote's key buttons and the list's
                    // right-click menu are enabled together for a node key, a read key and nothing.
                    var nodeSecret = vm2.CreateApiKey("Patterns FOH", ApiKeyScope.Node);
                    var readSecret = vm2.CreateApiKey("Dashboard", ApiKeyScope.Read);
                    var nodeKey = vm2.ApiKeys.SingleOrDefault(k => k.Name == "Patterns FOH")?.Id ?? "";
                    var readKey = vm2.ApiKeys.SingleOrDefault(k => k.Name == "Dashboard")?.Id ?? "";
                    var patJson = "{\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"9f8e7d6c\",\"name\":\"Patterns FOH\"},\"light\":\"green\",\"headline\":\"14 things\"}";
                    Expect(nodeSecret is not null && readSecret is not null && vm2.AcceptPeerContext(patJson, "10.0.0.5", NodeCredential.Key(nodeKey)) is (200, null, NodeProof.Key),
                           "settings: Patterns posts with a node key of its own, and its first use binds the key");
                    Pump(600);
                    var own = vm2.Settings.NodeId;
                    Expect(vm2.RebindApiKey(nodeKey, "", own) is (false, var ownWords) && ownWords == $"Not changed: {own} is this Stagewatch's own id."
                           && vm2.ApiKeyBindingLine(nodeKey) == "speaks for patterns 9f8e7d6c" && vm2.ApiKeyNodeChoices(nodeKey).All(c => c.Id != own),
                           "settings: this Stagewatch's own id, typed, is refused with the reason and never offered; the key keeps its node");
                    var dlg = new SettingsDialog(store, license, null, vm2.IntegrationStatus, vm2.PresenterPcs)
                    {
                        Keys = new SettingsDialog.KeyHooks(vm2.CreateApiKey, vm2.RevokeApiKey, vm2.ApiKeyLastUsed, vm2.ApiKeyBindingLine, vm2.ApiKeyNodeChoices, vm2.RebindApiKey),
                    };
                    Shot(dlg, "settings", w =>
                    {
                        var tabs = (TabControl)w.FindName("Tabs");
                        var first = tabs.SelectedIndex;
                        tabs.SelectedItem = tabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Header as string == "Remote");
                        Pump(300);
                        var list = (ListBox)w.FindName("ApiKeyList");
                        var speak = (Button)w.FindName("ApiKeySpeakButton");
                        var revoke = (Button)w.FindName("ApiKeyRevokeButton");
                        var menu = list.ContextMenu?.Items.OfType<MenuItem>().ToDictionary(m => m.Tag as string ?? "", m => m) ?? new Dictionary<string, MenuItem>();
                        object? RowOf(string id) => list.Items.Cast<object>().FirstOrDefault(r => r.GetType().GetProperty("Id")?.GetValue(r) as string == id);
                        static string LabelOf(object? row) => row?.GetType().GetProperty("Label")?.GetValue(row) as string ?? "";
                        bool InStep(bool speakOn, bool revokeOn)
                            => menu.TryGetValue("speak", out var sp) && menu.TryGetValue("revoke", out var rv) && menu.TryGetValue("new", out var nw) && nw.IsEnabled
                               && speak.IsEnabled == speakOn && sp.IsEnabled == speakOn && revoke.IsEnabled == revokeOn && rv.IsEnabled == revokeOn
                               && sp.Header as string == speak.Content as string && rv.Header as string == revoke.Content as string;
                        string State() => $"buttons {speak.IsEnabled}/{revoke.IsEnabled}, menu {string.Join("/", menu.Select(m => m.Key + "=" + m.Value.IsEnabled))}";
                        var nodeRow = RowOf(nodeKey);
                        Expect(LabelOf(nodeRow).Contains("speaks for patterns 9f8e7d6c", StringComparison.Ordinal) && !LabelOf(RowOf(readKey)).Contains("speaks for", StringComparison.Ordinal),
                               $"settings: the node key's row says what it speaks for, a read key's says nothing of a node ({LabelOf(nodeRow)})");
                        list.SelectedItem = nodeRow;
                        Pump(100);
                        Expect(InStep(speakOn: true, revokeOn: true), $"settings: a node key — Let it speak for… and Revoke on, the buttons and the right-click alike ({State()})");
                        list.SelectedItem = RowOf(readKey);
                        Pump(100);
                        Expect(InStep(speakOn: false, revokeOn: true), $"settings: a read key — Revoke on, Let it speak for… off, the buttons and the right-click alike ({State()})");
                        list.SelectedItem = null;
                        Pump(100);
                        Expect(InStep(speakOn: false, revokeOn: false), $"settings: nothing selected — both off, the buttons and the right-click alike ({State()})");
                        list.SelectedItem = nodeRow;
                        Save(w, "settings-remote-keys");
                        tabs.SelectedIndex = first;
                    });
                    break;
                }
            }
            vm2.Dispose();
            Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
            return failures;
        }
        if (only == "main-ro")
        {
            store.Update(s =>
            {
                s.RundownName = "Day 1";
                s.Rundown.ScheduledStart = "09:00";
                s.Rundown.Items = new List<RundownItem>
                {
                    new() { Title = "Welcome", Speaker = "Host", DurationSeconds = 600, Group = "Morning" },
                    new() { Title = "Keynote", Speaker = "Ana Ruiz", DurationSeconds = 2700, Group = "Morning" },
                    new() { Title = "Coffee", DurationSeconds = 900, Kind = SegmentKind.Break },
                    new() { Title = "Panel", DurationSeconds = 2700, Group = "Panels" },
                };
                s.Rundown.GroupTargets["Morning"] = 3000;
                s.OtherRundowns.Add(new NamedRundown { Name = "Day 2" });
            });
        }
        try
        {
            var main = new MainWindow();
            if (only == "main-ro") ((TabControl)main.FindName("SideTabs")).SelectedItem = main.FindName("TabRunning");
            var vm = (MainViewModel)main.DataContext;
            if (only != "main-ro") ((TabControl)main.FindName("SideTabs")).SelectedIndex = 5;
            Run(vm, "aux 1 name VT"); Run(vm, "aux 1 set 2:30"); Run(vm, "aux 1 start"); Run(vm, "aux 1 stage"); Run(vm, "aux 2 up"); Run(vm, "aux 3 name Changeover");
            Shot(main, only == "main-ro" ? "main-running-order" : "main-timers", w =>
            {
                w.Width = 1500; w.Height = 950;
                var tabs = (TabControl)w.FindName("SideTabs");
                tabs.SelectedIndex = only == "main-ro" ? 1 : tabs.Items.Count - 1;
                Pump(300);
                Run(vm, "aux 1 name VT");
                Run(vm, "aux 1 set 2:30");
                Run(vm, "aux 1 start");
                Run(vm, "aux 1 stage");
                Run(vm, "aux 2 up");
                Pump(800);
                tabs.UpdateLayout();
                var tw = (int)tabs.ActualWidth; var th = (int)tabs.ActualHeight;
                var trtb = new RenderTargetBitmap(tw, th, 96, 96, PixelFormats.Pbgra32);
                var tbg = new DrawingVisual();
                using (var dc = tbg.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(11, 13, 18)), null, new Rect(0, 0, tw, th));
                trtb.Render(tbg);
                var vb = new VisualBrush(tabs);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen()) dc.DrawRectangle(vb, null, new Rect(0, 0, tw, th));
                trtb.Render(dv);
                var tenc = new PngBitmapEncoder();
                tenc.Frames.Add(BitmapFrame.Create(trtb));
                using (var tfs = File.Create(Path.Combine(outDir, "timers-tab.png"))) tenc.Save(tfs);
                Console.WriteLine($"tab={tabs.SelectedIndex}/{tabs.Items.Count} header={(tabs.SelectedItem as TabItem)?.Header} aux0={vm.AuxItems[0].Name} {vm.AuxItems[0].Text} running={vm.AuxItems[0].Running} stage={vm.AuxItems[0].OnStage} aux1Up={vm.AuxItems[1].CountUp}");
            });
        }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL MainWindow: " + ex); }
        Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
        return failures;
    }

    private static void Run(MainViewModel vm, string line)
    {
        var t = line.Split(' ');
        if (ShowCommandParser.TryParse(t[0], t.Skip(1).ToList(), out var c, out var e)) vm.Execute(c); else Console.WriteLine("bad: " + e);
    }

    /// <summary>The ids of a context's peers rows (Show Bus §6).</summary>
    private static List<string> PeerIds(string contextJson)
    {
        using var doc = JsonDocument.Parse(contextJson);
        return doc.RootElement.GetProperty("peers").EnumerateArray().Select(p => p.GetProperty("id").GetString() ?? "").ToList();
    }

    /// <summary>A context's recent journal lines (Show Bus §6), as text.</summary>
    private static List<string> RecentLines(string contextJson)
    {
        using var doc = JsonDocument.Parse(contextJson);
        return doc.RootElement.GetProperty("recent").EnumerateArray().Select(r => r.GetProperty("text").GetString() ?? "").ToList();
    }

    /// <summary>Pumps the dispatcher until <paramref name="task"/> ends or <paramref name="ms"/> pass; true when it ran to completion.</summary>
    private static bool Await(Task task, int ms)
    {
        for (var waited = 0; !task.IsCompleted && waited < ms; waited += 50) Pump(50);
        return task.IsCompletedSuccessfully;
    }

    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); frame.Continue = false; };
        t.Start();
        Dispatcher.PushFrame(frame);
    }
}
