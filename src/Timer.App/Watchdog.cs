using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Windows.Threading;
using Stagewatch.Core;

namespace Stagewatch.App;

/// <summary>
/// Stagewatch runs as a child of a small supervisor (the same exe, no window). The child beats once a second from its
/// UI thread over an anonymous pipe; the supervisor restarts it when it exits with a fault or stops beating — quickly
/// while a show is live — and the new copy puts the show back by itself. The rules are <see cref="WatchdogPolicy"/>
/// (Timer.Core, tested); this class only runs processes. <c>--no-watchdog</c> runs the app on its own.
/// </summary>
public static class Watchdog
{
    public const string ChildArg = "--child";
    public const string BeatArg = "--beat";
    public const string RecoveredArg = "--recovered";
    public const string NoWatchdogArg = "--no-watchdog";

    private const byte BeatIdle = 1, BeatLive = 2;

    public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stagewatch");
    private static string LogPath => Path.Combine(DataFolder, "watchdog.log");

    /// <summary>The supervisor's last word about this app's previous run (for the status line), or null.</summary>
    public static string? LastRestartNote(string[] args)
    {
        var i = Array.IndexOf(args, RecoveredArg);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // ------------------------------------------------------------------ the supervisor

    /// <summary>Run the app as a watched child until it closes normally or the watchdog gives up. Returns the exit code.</summary>
    public static int Supervise(string[] args)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("no process path");
        var policy = new WatchdogPolicy();
        // A show file is opened by the first run only: a restart puts the live show back instead.
        var passOn = args.Where(a => a != ChildArg && a != NoWatchdogArg).ToList();
        string? note = null;
        CrashDumps.Sweep(DataFolder);
        while (true)
        {
            var started = Stopwatch.StartNew();
            var (code, hung) = RunOnce(exe, passOn, note);
            var decision = policy.OnExit(code, started.Elapsed, DateTime.UtcNow, hung);
            Log($"exit {code}{(hung ? " (hung, killed)" : "")} after {started.Elapsed:hh\\:mm\\:ss}: {decision.Action} — {decision.Reason}");
            if (decision.Action == WatchdogAction.Stop) return code;
            if (decision.Action == WatchdogAction.GiveUp)
            {
                MarkGaveUp(decision.Reason);
                return code == 0 ? 1 : code;
            }
            passOn = passOn.Where(a => !a.EndsWith(ShowFile.Extension, StringComparison.OrdinalIgnoreCase)).ToList();
            note = $"Stagewatch {decision.Reason} at {DateTime.Now:HH:mm:ss} and was restarted";
            CrashDumps.Sweep(DataFolder);
            #pragma warning disable RS0030 // the supervisor's own thread waits out its restart backoff; it has no window
            if (decision.Delay > TimeSpan.Zero) Thread.Sleep(decision.Delay);
            #pragma warning restore RS0030
        }
    }

    private static (int Code, bool Hung) RunOnce(string exe, IReadOnlyList<string> args, string? note)
    {
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add(ChildArg);
        psi.ArgumentList.Add(BeatArg);
        psi.ArgumentList.Add(pipe.GetClientHandleAsString());
        if (note is not null) { psi.ArgumentList.Add(RecoveredArg); psi.ArgumentList.Add(note); }
        foreach (var (k, v) in CrashDumps.Environment(DataFolder)) psi.Environment[k] = v;
        try { Directory.CreateDirectory(CrashDumps.Folder(DataFolder)); } catch (IOException) { }

        using var child = Process.Start(psi) ?? throw new InvalidOperationException("couldn't start Stagewatch");
        pipe.DisposeLocalCopyOfClientHandle();

        var lastBeat = Stopwatch.StartNew();
        var hadBeat = false;
        var live = false;
        var reader = new Thread(() =>
        {
            var buf = new byte[64];
            try
            {
                int n;
                while ((n = pipe.Read(buf, 0, buf.Length)) > 0)
                {
                    lock (lastBeat) { lastBeat.Restart(); hadBeat = true; live = buf[n - 1] == BeatLive; }
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }) { IsBackground = true, Name = "watchdog beat" };
        reader.Start();

        while (!child.WaitForExit(250))
        {
            bool hung;
            lock (lastBeat) hung = WatchdogPolicy.IsHung(lastBeat.Elapsed, live, hadBeat);
            if (!hung) continue;
            try { child.Kill(entireProcessTree: true); child.WaitForExit(5000); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            return (-1, true);
        }
        return (child.ExitCode, false);
    }

    private static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > 512 * 1024) File.Move(LogPath, LogPath + ".old", overwrite: true);   // bounded, never truncated in place
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss zzz} {line}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void MarkGaveUp(string reason)
    {
        try { File.WriteAllText(Path.Combine(DataFolder, "watchdog-gave-up.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {reason}"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>If the watchdog gave up last time, say so once (and clear it).</summary>
    public static string? TakeGaveUpNote()
    {
        var path = Path.Combine(DataFolder, "watchdog-gave-up.txt");
        try
        {
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path).Trim();
            File.Delete(path);
            return text.Length > 0 ? "Last time the watchdog stopped restarting Stagewatch: " + text : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    // ------------------------------------------------------------------ the child's heartbeat

    private static AnonymousPipeClientStream? _beatPipe;
    private static DispatcherTimer? _beatTimer;

    /// <summary>In the watched app: beat once a second from the UI thread (a hung UI stops the beat — that's the point).</summary>
    public static void StartHeartbeat(string[] args, Func<bool> showLive)
    {
        var i = Array.IndexOf(args, BeatArg);
        if (i < 0 || i + 1 >= args.Length) return;
        try { _beatPipe = new AnonymousPipeClientStream(PipeDirection.Out, args[i + 1]); }
        catch (IOException) { return; }
        catch (ArgumentException) { return; }
        var beat = new byte[1];
        _beatTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
        _beatTimer.Tick += (_, _) =>
        {
            if (_beatPipe is null) return;
            beat[0] = showLive() ? BeatLive : BeatIdle;
            try { _beatPipe.Write(beat, 0, 1); _beatPipe.Flush(); }
            catch (IOException) { StopHeartbeat(); }   // the supervisor is gone: carry on unwatched
        };
        _beatTimer.Start();
    }

    public static void StopHeartbeat()
    {
        _beatTimer?.Stop();
        try { _beatPipe?.Dispose(); } catch (IOException) { }
        _beatPipe = null;
    }
}
