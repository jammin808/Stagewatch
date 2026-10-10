using System.IO;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Stagewatch.Output;

/// <summary>
/// The holographic (three.js) layer of an output window, hosted in WebView2.
///
/// Safety model: the normal WPF display keeps rendering underneath at all times. This layer only covers it once
/// the page reports it is rendering (a heartbeat every second), and steps aside again the moment heartbeats stop
/// for <see cref="StallAfter"/>, the page reports a failure, the browser process dies, or the host asks for it
/// (blackout, a holding image). The show can never be left on a blank or frozen 3D view.
///
/// "Stepping aside" shrinks the layer to one pixel rather than hiding it: a hidden WebView2 tells the page it is
/// invisible, which pauses requestAnimationFrame — the heartbeat would stop and the layer could never come back.
/// Frames are pushed as web messages (latest wins); nothing here blocks the show thread.
/// </summary>
public sealed class HoloHost : IDisposable
{
    public static readonly TimeSpan StallAfter = TimeSpan.FromSeconds(3);
    private const string Host = "stagewatch-holo.local";

    private readonly Grid _parent;
    private readonly string _assets;
    private readonly string _userData;
    private readonly DispatcherTimer _watchdog;
    private WebView2? _web;
    private bool _ready;           // page loaded and listening
    private bool _healthy;         // heartbeats arriving
    private bool _failed;          // gave up (no WebGL, crash): stay on the WPF display
    private bool _suppressed;      // host says hide (blackout / holding image)
    private DateTime _lastBeat = DateTime.MinValue;
    private int _beatsSinceStall;
    private string? _lastFrame;
    private string? _lastConfig;
    private bool _disposed;

    /// <summary>Human-readable news for the operator's status line ("holographic display running", "fell back…").</summary>
    public event Action<string>? Status;

    public HoloHost(Grid parent, string assetFolder, string userDataFolder)
    {
        _parent = parent;
        _assets = assetFolder;
        _userData = userDataFolder;
        _watchdog = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _watchdog.Tick += (_, _) => Check();
    }

    public bool IsShowing => _web is { Width: double.NaN };

    public async Task StartAsync()
    {
        try
        {
            Directory.CreateDirectory(_userData);
            var env = await CoreWebView2Environment.CreateAsync(null, _userData);
            if (_disposed) return;
            _web = new WebView2
            {
                DefaultBackgroundColor = System.Drawing.Color.Black,
                Focusable = false,
                IsHitTestVisible = false,
            };
            Collapse(true);                             // covers the display only after the first heartbeats
            // Above the normal display, below nothing else that matters: holding images and blackout hide this layer.
            _parent.Children.Insert(Math.Min(2, _parent.Children.Count), _web);
            await _web.EnsureCoreWebView2Async(env);
            if (_disposed) return;
            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsPinchZoomEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.SetVirtualHostNameToFolderMapping(Host, _assets, CoreWebView2HostResourceAccessKind.DenyCors);
            core.WebMessageReceived += OnMessage;
            core.ProcessFailed += (_, e) => Fail($"the display process stopped ({e.ProcessFailedKind})");
            core.NavigationCompleted += (_, e) => { if (!e.IsSuccess) Fail($"couldn't load ({e.WebErrorStatus})"); };
            // Only our own page, ever.
            core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true; };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.Navigate($"https://{Host}/holo.html");
            _watchdog.Start();
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or COMException or IOException or UnauthorizedAccessException)
        {
            Fail(ex is WebView2RuntimeNotFoundException
                ? "the Microsoft WebView2 Runtime isn't installed"
                : "the 3D view couldn't start: " + ex.Message);
        }
    }

    /// <summary>A display frame (the same JSON browser screens receive). Latest wins; dropped until the page is ready.</summary>
    public void PostFrame(string json)
    {
        _lastFrame = json;
        if (_ready && !_failed) Post(json);
    }

    /// <summary>Page options: { "type":"config", "background":"#0000FF", "floor":true }.</summary>
    public void Configure(string background, bool floor)
    {
        _lastConfig = JsonSerializer.Serialize(new { type = "config", background, floor });
        if (_ready && !_failed) Post(_lastConfig);
    }

    /// <summary>Hide the layer for blackout or a holding image (the WPF display shows those).</summary>
    public void Suppress(bool hide)
    {
        _suppressed = hide;
        UpdateVisibility();
    }

    private void Post(string json)
    {
        try { _web?.CoreWebView2?.PostWebMessageAsJson(json); }
        catch (Exception ex) when (ex is InvalidOperationException or COMException) { Fail("lost the display page"); }
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? type;
        string? reason = null;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (doc.RootElement.TryGetProperty("reason", out var r)) reason = r.GetString();
        }
        catch (JsonException) { return; }

        switch (type)
        {
            case "ready":
                _ready = true;
                if (_lastConfig is not null) Post(_lastConfig);
                if (_lastFrame is not null) Post(_lastFrame);
                break;
            case "beat":
                _lastBeat = DateTime.UtcNow;
                if (!_healthy && ++_beatsSinceStall >= 2)
                {
                    _healthy = true;
                    UpdateVisibility();
                    Status?.Invoke("Holographic display running");
                }
                break;
            case "fail":
                Fail(reason ?? "the 3D view stopped");
                break;
        }
    }

    private void Check()
    {
        if (_failed || !_healthy) return;
        if (DateTime.UtcNow - _lastBeat > StallAfter)
        {
            _healthy = false;
            _beatsSinceStall = 0;
            UpdateVisibility();
            Status?.Invoke("Holographic display stalled — showing the standard display until it recovers");
        }
    }

    private void Fail(string reason)
    {
        if (_failed) return;
        _failed = true;
        _healthy = false;
        UpdateVisibility();
        _watchdog.Stop();
        Status?.Invoke("Holographic display unavailable (" + reason + ") — showing the standard display");
    }

    private void UpdateVisibility()
    {
        if (_web is null) return;
        if (_failed) { _web.Visibility = Visibility.Collapsed; return; } // gone for good: let it sleep
        Collapse(!(_healthy && !_suppressed));
    }

    /// <summary>Cover the output, or shrink to one pixel in the corner (still rendering, still sending heartbeats).</summary>
    private void Collapse(bool small)
    {
        if (_web is null) return;
        _web.HorizontalAlignment = small ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        _web.VerticalAlignment = small ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        _web.Width = small ? 1 : double.NaN;
        _web.Height = small ? 1 : double.NaN;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watchdog.Stop();
        if (_web is not null)
        {
            _parent.Children.Remove(_web);
            try { _web.Dispose(); } catch (Exception) { }
            _web = null;
        }
    }
}
