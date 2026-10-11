using System.Windows;
using Stagewatch.Core;
using Stagewatch.Licensing;

namespace Stagewatch.App;

public partial class App : Application
{
    public static SettingsStore SettingsStore { get; private set; } = null!;
    public static LicenseManager LicenseManager { get; private set; } = null!;
    public static LicenseStatus License { get; private set; } = LicenseStatus.Locked();

    private const string InstanceName = "Stagewatch.SingleInstance";
    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _activateSignal;

    /// <summary>
    /// Set by a host that drives the app's windows itself (the smoke harness): startup then does nothing — no
    /// single-instance handshake (which would wake, or defer to, a Stagewatch already running on this PC), no settings
    /// or licence of its own, no main window.
    /// </summary>
    public static bool Hosted { get; set; }

    /// <summary>Set when the watchdog restarted this copy after a fault: the show is put back without asking.</summary>
    public static string? RestartNote { get; private set; }

    /// <summary>UI faults contained so far (Diagnostics).</summary>
    public static FaultCounter Faults { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (Hosted) return;

        // Roles: the same Stagewatch.exe runs as the presenter-PC app with --slides (Stagewatch Slides). It has its own
        // single instance and needs no licence, settings store or ports of its own beyond the discovery beacon.
        if (e.Args.Any(a => a.Equals("--slides", StringComparison.OrdinalIgnoreCase) || a.Equals("/slides", StringComparison.OrdinalIgnoreCase)))
        {
            _instanceMutex = new Mutex(initiallyOwned: true, @"Local\Stagewatch.Slides.SingleInstance", out var firstSlides);
            if (!firstSlides) { Shutdown(); return; }
            DispatcherUnhandledException += (_, ex) => LogCrash("slides-ui", ex.Exception);
            TaskScheduler.UnobservedTaskException += (_, ex) => { LogCrash("slides-task", ex.Exception); ex.SetObserved(); };
            var agent = new SlidesAgentWindow();
            MainWindow = agent;
            agent.Show();
            return;
        }

        // One Stagewatch per Windows session: a second copy would fight the first for the
        // network ports and the output screen. Instead, wake the running one and leave.
        var showArg = e.Args.FirstOrDefault(a => a.EndsWith(ShowFile.Extension, StringComparison.OrdinalIgnoreCase));
        var isChild = e.Args.Contains(Watchdog.ChildArg);
        // The supervisor holds the single-instance lock; the copy it watches doesn't take it again.
        var isFirst = true;
        if (!isChild) _instanceMutex = new Mutex(initiallyOwned: true, @"Local\" + InstanceName, out isFirst);
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + InstanceName + ".Activate");
        if (!isFirst)
        {
            // Double-clicked a show file while Stagewatch is running: hand it to the running copy.
            if (showArg is not null)
            {
                try { System.IO.File.WriteAllText(OpenRequestPath, System.IO.Path.GetFullPath(showArg)); }
                catch (System.IO.IOException) { }
            }
            _activateSignal.Set();
            Shutdown();
            return;
        }

        // The first copy becomes the watchdog (no window) and runs the real app as its child — see Watchdog.
        if (!isChild && !e.Args.Contains(Watchdog.NoWatchdogArg) && !System.Diagnostics.Debugger.IsAttached)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var args = e.Args;
            new Thread(() =>
            {
                int code;
                try { code = Watchdog.Supervise(args); }
                catch (Exception ex) { LogCrash("watchdog", ex); code = 1; }
                Dispatcher.BeginInvoke(() => Shutdown(code));
            }) { Name = "watchdog", IsBackground = false }.Start();
            return;
        }
        RestartNote = Watchdog.LastRestartNote(e.Args) ?? Watchdog.TakeGaveUpNote();
        TimerResolution.Begin();
        var listener = new Thread(() =>
        {
            while (_activateSignal.WaitOne())
                Dispatcher.BeginInvoke(() =>
                {
                    if (MainWindow is not MainWindow w) return;
                    w.BringToFront();
                    if (TakeOpenRequest() is { } path) w.OpenShowFile(path);
                });
        }) { IsBackground = true, Name = "single-instance" };
        listener.Start();

        // Crash log: every unhandled exception — UI thread, worker threads, forgotten tasks — goes to
        // %APPDATA%\Stagewatch\crash.log (and into "Copy diagnostics").
        DispatcherUnhandledException += (_, ex) =>
        {
            LogCrash("ui", ex.Exception);
            // A stray fault in one handler must not end the show: contain it, say so, carry on. A fatal one (or a storm
            // of them) still ends the process — and the watchdog brings the show back in seconds.
            if (!Faults.Contain(ex.Exception, DateTime.UtcNow)) return;
            ex.Handled = true;
            (MainWindow as MainWindow)?.ReportFault(ex.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => LogCrash("fatal", ex.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ex) => { LogCrash("task", ex.Exception); ex.SetObserved(); };

        SettingsStore = new SettingsStore();
        LicenseManager = new LicenseManager();
        License = LicenseManager.LoadOnStartup();

        var window = new MainWindow(showArg is null ? null : System.IO.Path.GetFullPath(showArg));
        MainWindow = window;
        window.Show();
        Watchdog.StartHeartbeat(e.Args, () => window.ShowLive);
    }

    private static string OpenRequestPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stagewatch", "open-request.txt");

    private static string? TakeOpenRequest()
    {
        try
        {
            if (!System.IO.File.Exists(OpenRequestPath)) return null;
            var path = System.IO.File.ReadAllText(OpenRequestPath).Trim();
            System.IO.File.Delete(OpenRequestPath);
            return path.Length > 0 ? path : null;
        }
        catch (System.IO.IOException) { return null; }
    }

    private static void LogCrash(string kind, Exception? ex)
    {
        try
        {
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stagewatch", "crash.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var info = new System.IO.FileInfo(path);
            if (info.Exists && info.Length > 1_000_000) info.Delete(); // keep it bounded
            System.IO.File.AppendAllText(path, $"\n{DateTime.Now:yyyy-MM-dd HH:mm:ss zzz} [{kind}] {ex}\n");
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Watchdog.StopHeartbeat();
        TimerResolution.End();
        if (SettingsStore is null) { base.OnExit(e); return; } // second instance, or the --slides role
        SettingsStore.SaveNow();
        SettingsStore.Dispose();
        base.OnExit(e);
    }
}
