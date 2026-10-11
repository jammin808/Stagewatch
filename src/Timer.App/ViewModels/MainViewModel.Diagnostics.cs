using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Stagewatch.Core;
using Stagewatch.Licensing;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// "Copy diagnostics": one paste that tells support what they need — without secrets.
/// Never includes the remote PIN, master serial or licence file contents.
/// </summary>
public sealed partial class MainViewModel
{
    private readonly Queue<string> _statusHistory = new();
    private readonly DateTime _startedAt = DateTime.Now;

    partial void OnStatusLineChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        _statusHistory.Enqueue($"{DateTime.Now:HH:mm:ss}  {value}");
        while (_statusHistory.Count > 40) _statusHistory.Dequeue();
    }

    public string BuildDiagnostics()
    {
        var sb = new StringBuilder();
        void Line(string k, object? v) => sb.Append(k.PadRight(22)).Append(": ").AppendLine(v?.ToString() ?? "—");
        void Section(string title) => sb.AppendLine().AppendLine("== " + title);

        var asm = typeof(MainViewModel).Assembly;
        sb.AppendLine("Stagewatch diagnostics — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));

        Section("App");
        Line("Version", asm.GetName().Version);
        Line("Exe", Environment.ProcessPath);
        Line("Running since", $"{_startedAt:HH:mm:ss} ({(DateTime.Now - _startedAt):hh\\:mm\\:ss})");
        Line("Slow frame consumer", SlowSubscriberNote ?? "none (no consumer has delayed a frame)");
        Line("Watchdog", Environment.GetCommandLineArgs().Contains(Watchdog.ChildArg) ? "watching (restarts a crashed or hung Stagewatch in seconds; watchdog.log)" : "off (run with --no-watchdog or under a debugger)");
        Line("Faults contained", App.Faults.Total);
        Line("GO latency", GoLatencyLine);
        Line("Garbage collector", System.Runtime.GCSettings.LatencyMode + (System.Runtime.GCSettings.IsServerGC ? ", server" : ", workstation"));
        if (SettingsStore.LoadedFromBackup) Line("Settings", "the settings file was damaged at start — the last good copy (.bak) was loaded");
        Line("Memory", $"{Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024)} MB");
        Line(".NET", RuntimeInformation.FrameworkDescription);
        Line("Windows", RuntimeInformation.OSDescription + " " + RuntimeInformation.OSArchitecture);
        Line("Time zone", TimeZoneInfo.Local.DisplayName);
        Line("Culture", System.Globalization.CultureInfo.CurrentCulture.Name);

        Section("Licence");
        Line("Status", _license.DisplayName);
        Line("Tier", _license.Tier);
        Line("Hardware ID", HardwareId.Get());
        if (_license.LastError is { } le) Line("Last error", le);

        Section("Displays");
        foreach (var s in System.Windows.Forms.Screen.AllScreens)
            sb.AppendLine($"  {s.DeviceName}{(s.Primary ? " (primary)" : "")}  {s.Bounds.Width}x{s.Bounds.Height} at {s.Bounds.X},{s.Bounds.Y}");

        Section("Outputs");
        Line("Output window", _outputWindow is null ? "closed" : Settings.Display.OutputMode.ToString());
        Line("Target monitor", Settings.Display.TargetMonitorDeviceName);
        Line("NDI", _ndi is null ? "off" : $"sending as {_ndi.SourceName}, {_ndi.FramesSent} frames");
        Line("NDI runtime", NdiRuntimeLocation());
        Line("Blackout", Blackout);

        Section("Connections");
        Line("Companion server", _companion.IsRunning ? $"listening on {_companion.Port}" : "off");
        Line("Web remote", _web is null ? "off" : $"listening on {_web.Port}, {_web.ConnectedClients} browser(s)");
        Line("OSC", _osc is null ? "off" : $"listening on {_osc.Port}, {_osc.MessagesReceived} received, {_osc.MessagesRejected} rejected");
        Line("LAN addresses", string.Join(", ", LanAddresses()));

        Section("Audio");
        Line("Alerts", $"warning={Settings.Alerts.OnWarning} wrap-up={Settings.Alerts.OnWrapUp} zero={Settings.Alerts.OnZero}");
        Line("Alert device", string.IsNullOrEmpty(Settings.Alerts.Device) ? "Windows default" : Settings.Alerts.Device);
        Line("Devices", string.Join(" | ", AlertSounds.ListDevices()));

        Section("Show");
        Line("Show file", Settings.ShowPath ?? "(not saved)");
        Line("Timer", $"{_engine.Mode} {_engine.State} {TimerText}");
        Line("Title", CurrentTitle);
        Line("Running order", $"{Settings.Rundown.Items.Count} items, current {(_show.RundownActive ? _show.CurrentItem + 1 : 0)}, starts {Settings.Rundown.ScheduledStart ?? "—"}");
        Line("Settings", $"overtime={Settings.AllowOvertime} warn={Settings.Display.WarningSeconds}s wrap={Settings.Display.WrapUpSeconds}s 12h={Settings.Display.Use12HourClock} elements={Settings.Display.Elements}");

        Section("Recent activity");
        foreach (var s in _statusHistory) sb.AppendLine("  " + s);

        Section("Crash log (last 40 lines)");
        try
        {
            var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stagewatch", "crash.log");
            sb.AppendLine(File.Exists(log) ? string.Join(Environment.NewLine, File.ReadLines(log).TakeLast(40)) : "  (none — no crashes recorded)");
        }
        catch (IOException ex) { sb.AppendLine("  (unreadable: " + ex.Message + ")"); }

        return sb.ToString();
    }

    private static string NdiRuntimeLocation()
    {
        foreach (var v in new[] { "NDI_RUNTIME_DIR_V6", "NDI_RUNTIME_DIR_V5", "NDI_RUNTIME_DIR_V4" })
            if (Environment.GetEnvironmentVariable(v) is { Length: > 0 } dir)
                return File.Exists(Path.Combine(dir, "Processing.NDI.Lib.x64.dll")) ? dir : dir + " (DLL missing)";
        return "not installed (NDI Tools from ndi.video/tools)";
    }
}
