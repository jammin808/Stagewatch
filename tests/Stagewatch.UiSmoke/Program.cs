using System.IO;
using System.Reflection;
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
        store.Update(s => { s.Remote.WebPort = 19924; s.Remote.WebBindAll = false; s.Companion.Enabled = false; s.Remote.OscEnabled = false; });
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
            var vm2 = new MainViewModel(store, license);
            switch (only)
            {
                case "screens": Shot(new ScreensWindow(vm2), "screens"); break;
                case "copilot": Shot(new CopilotWindow(vm2), "copilot"); break;
                case "slides-agent": Shot(new SlidesAgentWindow(), "slides-agent"); break;
                case "rundown-editor": Shot(new RundownEditor(store, _ => Task.FromResult(new Stagewatch.Integrations.AgendaDraft(Array.Empty<Stagewatch.Integrations.AgendaDraftItem>(), Array.Empty<string>()))), "rundown-editor"); break;
                case "settings": Shot(new SettingsDialog(store, license, null, vm2.IntegrationStatus, vm2.PresenterPcs), "settings"); break;
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

    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); frame.Continue = false; };
        t.Start();
        Dispatcher.PushFrame(frame);
    }
}
