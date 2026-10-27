using System.Diagnostics;
using Stagewatch.Core;
using Stagewatch.Remote;

// Serves a realistic frame and screenshots the given pages with headless Edge.
var outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "sw-pages");
var pages = args.Length > 1 ? args[1..] : new[] { "/viewer", "/clock", "/countdown?to=next", "/timeline", "/audience", "/layout?name=lobby", "/layout?name=foyer", "/designer?name=foyer" };
Directory.CreateDirectory(outDir);
var control = new Ctl();
var port = 19931;
await using var web = new WebServer(control, () => "4321");
web.Start(port, bindAll: false);

var clock = 14 * 3600 + 7 * 60 + 23;
ItemTimingView T(int i, string title, string spk, SegmentKind k, int secs, string planned, string exp, string end, ItemStatus st, string group, long inS, long endIn, string actual = "")
    => new(i, title, spk, k, secs, 0, planned, exp, end, actual, "", st, 0, false, false, "", group, inS, endIn, inS);
control.Snapshots.Publish(new ShowSnapshot
{
    Mode = TimerMode.Countdown, State = TimerState.Running, Seconds = 754, Text = "12:34", TotalSeconds = 1800, Title = "Keynote — Ana Ruiz",
    ColorHex = "#FFFFFF", TimeOfDay = "14:07:23", ClockSeconds = clock, DateText = "Saturday 10 October", Message = "Five minutes please", Room = "Hall A", OffsetSeconds = 240, ItemIndex = 1,
    Timings = new[]
    {
        T(0, "Welcome", "Host", SegmentKind.Talk, 600, "13:30", "13:30", "13:40", ItemStatus.Done, "Opening", -2240, -1640, "13:30:10"),
        T(1, "Keynote", "Ana Ruiz", SegmentKind.Talk, 1800, "13:45", "13:49", "14:19", ItemStatus.Live, "Opening", -1046, 754),
        T(2, "Panel: The next decade", "Moderated by Sam", SegmentKind.Panel, 2700, "14:15", "14:19", "15:04", ItemStatus.Upcoming, "Opening", 754, 3454),
        T(3, "Coffee break", "", SegmentKind.Break, 1200, "15:00", "15:04", "15:24", ItemStatus.Upcoming, "", 3454, 4654),
        T(4, "Product demo", "Lee", SegmentKind.Talk, 1500, "15:20", "15:24", "15:49", ItemStatus.Upcoming, "Afternoon", 4654, 6154),
        T(5, "Awards", "", SegmentKind.Award, 1800, "15:45", "15:49", "16:19", ItemStatus.Upcoming, "Afternoon", 6154, 7954),
    }.ToEquatableArray(),
    Groups = new[]
    {
        new GroupView("Opening", 0, 2, 5100, 4800, -2240, 3454, "13:30", "15:04", ItemStatus.Live),
        new GroupView("Afternoon", 4, 5, 3300, null, 4654, 7954, "15:20", "16:19", ItemStatus.Upcoming),
    }.ToEquatableArray(),
});
control.Snapshots.MarkAlive();
var keep = new Timer(_ => control.Snapshots.MarkAlive(), null, 0, 500);

// The browser: STAGEWATCH_BROWSER (or the older STAGEWATCH_EDGE), else Edge on Windows, else Playwright's Chromium or a
// Chromium / Chrome on the path (the Linux bench and CI job). Every one of them takes the same headless switches.
var edge = new[]
{
    Environment.GetEnvironmentVariable("STAGEWATCH_BROWSER") ?? "",
    Environment.GetEnvironmentVariable("STAGEWATCH_EDGE") ?? "",
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
}.Concat(LinuxChromium()).FirstOrDefault(File.Exists);
if (edge is null) { Console.WriteLine("No Edge or Chromium found (set STAGEWATCH_BROWSER)"); return 2; }
// Off Windows the sandbox is off: Chromium refuses it to root (containers, the cloud bench), and Ubuntu 24.04 (CI's Linux
// runner) blocks the user namespaces it needs. The pages it loads are this harness's own, on 127.0.0.1.
var noSandbox = !OperatingSystem.IsWindows() ? "--no-sandbox " : "";
// The headless shell is headless already and takes no mode.
var headless = Path.GetFileName(edge).StartsWith("chrome-headless-shell", StringComparison.Ordinal) ? "" : "--headless=new ";
var missing = 0;
foreach (var page in pages)
{
    // A readable, unique file name per page and its options: "layout_name-lobby.png".
    var name = string.Concat(page.Trim('/').Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
    var file = Path.Combine(outDir, (name.Length == 0 ? "index" : name) + ".png");
    if (File.Exists(file)) File.Delete(file);
    var profile = Path.Combine(Path.GetTempPath(), "sw-pageshot-" + Guid.NewGuid().ToString("N")[..8]);
    var psi = new ProcessStartInfo(edge, $"{headless}{noSandbox}--disable-gpu --hide-scrollbars --no-first-run --user-data-dir=\"{profile}\" --window-size=1600,900 --virtual-time-budget=4000 --screenshot=\"{file}\" \"http://127.0.0.1:{port}{page}\"")
    { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
    using var p = Process.Start(psi)!;
    await p.WaitForExitAsync();
    var ok = File.Exists(file) && new FileInfo(file).Length > 2000;
    if (!ok) missing++;
    Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {page} -> {file}");
    if (RemoveProfile(profile) is { } why) Console.WriteLine($"NOTE {profile} could not be removed: {why}");
}
Console.WriteLine(missing == 0 ? "ALL OK" : $"{missing} page(s) didn't render");
return missing;

// The browser's profile folder, made for one page and gone with it: a removal its last process still holds is retried, each
// wait longer, and said if it stands (null once it is gone).
static string? RemoveProfile(string profile)
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (attempt == 5) return e.Message;
            Thread.Sleep(TimeSpan.FromMilliseconds(100 * attempt));
        }
    }
}

static IEnumerable<string> LinuxChromium()
{
    if (OperatingSystem.IsWindows()) yield break;
    var pw = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH") is { Length: > 0 } root ? root
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "ms-playwright");
    // Playwright's headless shell first: the full Chromium's new headless mode can stall in a container.
    foreach (var dir in new[] { pw, "/opt/pw-browsers" }.Where(Directory.Exists))
    {
        foreach (var shell in Directory.GetDirectories(dir, "chromium_headless_shell-*").OrderDescending())
            yield return Path.Combine(shell, "chrome-headless-shell-linux64", "chrome-headless-shell");
        foreach (var chromium in Directory.GetDirectories(dir, "chromium-*").OrderDescending())
            yield return Path.Combine(chromium, "chrome-linux64", "chrome");
    }
    foreach (var name in new[] { "chromium", "chromium-browser", "google-chrome", "microsoft-edge" })
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
            yield return Path.Combine(folder, name);
}

sealed class Ctl : IShowControl
{
    public SnapshotHub Snapshots { get; } = new();
    public void Execute(ShowCommand command) { }
    public RemoteConfig GetRemoteConfig() => new(Array.Empty<(int, string, int)>(), Array.Empty<(string, string, MessageUrgency, FlashRate)>(),
        Array.Empty<(string, string?, int)>(), Array.Empty<(string, string)>(), new[] { "Lighting" }, "Hall A", "");
    public string? LayoutJson(string name) => name switch
    {
        "lobby" => ScreenLayouts.ToJson(ScreenLayouts.Starter("lobby")),
        "foyer" => ScreenLayouts.ToJson(ScreenLayouts.Sanitise(new ScreenLayout { Name = "foyer", Background = "#0B1B3A", Font = "Bahnschrift", Elements =
        {
            new() { Kind = "box", X = 0, Y = 0, W = 100, H = 14, Background = "#1D4ED8" },
            new() { Kind = "text", X = 3, Y = 2, W = 60, H = 10, Text = "{room}  ·  {date}", Size = 5, Align = "left", Bold = true },
            new() { Kind = "text", X = 62, Y = 2, W = 35, H = 10, Text = "{clock}", Size = 6, Align = "right", Bold = true },
            new() { Kind = "text", X = 3, Y = 22, W = 94, H = 8, Text = "NOW", Size = 4, Color = "#93C5FD", Align = "left" },
            new() { Kind = "text", X = 3, Y = 30, W = 94, H = 14, Text = "{title}", Size = 9, Align = "left", Bold = true },
            new() { Kind = "text", X = 3, Y = 54, W = 94, H = 8, Text = "NEXT — starts in {nextIn}", Size = 4, Color = "#93C5FD", Align = "left" },
            new() { Kind = "text", X = 3, Y = 62, W = 94, H = 12, Text = "{next}  at {nextStart}", Size = 7, Align = "left" },
            new() { Kind = "message", X = 3, Y = 82, W = 94, H = 12, Size = 6, Bold = true },
        } }, out _)!),
        _ => null,
    };
    public IReadOnlyList<string> LayoutNames => new[] { "foyer", "lobby" };
    public IReadOnlyList<(int Index, string Title, string Note)>? DeptNotesFor(string dept) => new[]
    {
        (0, "Welcome", "House to half, podium special"), (1, "Keynote", "Blue wash; follow spot on the podium; Q12 at the video"),
        (2, "Panel: The next decade", ""), (3, "Coffee break", "Walk-in state"), (4, "Product demo", "Stage left special"), (5, "Awards", "Gobo rotate on winners"),
    };
}
