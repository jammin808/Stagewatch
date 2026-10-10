using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Stagewatch.Core;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Stagewatch.Output;

public partial class OutputWindow : Window
{
    private readonly DispatcherTimer _flashTimer;
    private bool _flashOn = true;
    private OutputViewModel? _vm;
    private HoloHost? _holo;

    /// <summary>News from the holographic layer, for the operator's status line.</summary>
    public event Action<string>? HoloStatus;

    public OutputWindow()
    {
        InitializeComponent();
        _flashTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(500) };
        _flashTimer.Tick += OnFlashTick;
        _flashTimer.Start();
    }

    public void Bind(OutputViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(OutputViewModel.Blackout) or nameof(OutputViewModel.HoldingImage)) SuppressHolo();
        };
        Closed += (_, _) => SetHolographic(false, null, null);
    }

    // ------------------------------------------------------------------ holographic layer (three.js in WebView2)

    /// <summary>Turn the holographic layer on (with the extracted page folder) or off. The WPF display stays underneath.</summary>
    public void SetHolographic(bool on, string? assetFolder, string? userDataFolder)
    {
        if (on && _holo is null && assetFolder is not null && userDataFolder is not null)
        {
            _holo = new HoloHost(RootGrid, assetFolder, userDataFolder);
            _holo.Status += s => HoloStatus?.Invoke(s);
            SuppressHolo();
            _ = _holo.StartAsync();
        }
        else if (!on && _holo is not null)
        {
            _holo.Dispose();
            _holo = null;
        }
    }

    public bool HolographicOn => _holo is not null;

    public void PostHoloFrame(string json) => _holo?.PostFrame(json);

    public void ConfigureHolo(string background, bool floor) => _holo?.Configure(background, floor);

    private void SuppressHolo() => _holo?.Suppress(_vm is { } v && (v.Blackout || v.HoldingImage is not null));

    public void ApplyMode(OutputMode mode, KeyColor keyColor)
    {
        switch (mode)
        {
            case OutputMode.FullScreen:
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
                Topmost = false;
                Background = Brushes.Black;
                break;
            case OutputMode.KeyedFullScreen:
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
                Topmost = false;
                Background = ChromaKey.ToBrush(keyColor);
                break;
            case OutputMode.FloatingMinimal:
                WindowStyle = WindowStyle.ToolWindow;
                ResizeMode = ResizeMode.CanResizeWithGrip;
                WindowState = WindowState.Normal;
                Topmost = true;
                Background = ChromaKey.ToBrush(keyColor);
                break;
        }
    }

    public void MoveToMonitor(string? monitorDeviceName)
    {
        // Match the monitor by device name and place fullscreen there.
        // Falls back to the primary screen if not found.
        var screens = System.Windows.Forms.Screen.AllScreens;
        var target = screens.FirstOrDefault(s => s.DeviceName == monitorDeviceName) ?? screens.First(s => s.Primary);
        var bounds = target.Bounds;

        // WPF uses DIPs; convert pixels using PresentationSource.
        var src = PresentationSource.FromVisual(this);
        var dpi = src?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var scaleX = dpi.M11 == 0 ? 1.0 : dpi.M11;
        var scaleY = dpi.M22 == 0 ? 1.0 : dpi.M22;
        Left = bounds.Left / scaleX;
        Top = bounds.Top / scaleY;
        Width = bounds.Width / scaleX;
        Height = bounds.Height / scaleY;
    }

    private void OnFlashTick(object? sender, EventArgs e)
    {
        if (_vm is null) return;

        var rate = _vm.Flash;
        var interval = rate switch
        {
            FlashRate.Slow => TimeSpan.FromMilliseconds(500),
            FlashRate.Fast => TimeSpan.FromMilliseconds(166),
            _ => TimeSpan.FromMilliseconds(500)
        };
        if (_flashTimer.Interval != interval) _flashTimer.Interval = interval;

        if (rate == FlashRate.Off && !_vm.FlashTimer)
        {
            // No flashing — reset opacity once and bail.
            if (MessageText.Opacity != 1.0) MessageText.Opacity = 1.0;
            if (TimerText.Opacity != 1.0) TimerText.Opacity = 1.0;
            return;
        }

        _flashOn = !_flashOn;
        var visible = _flashOn ? 1.0 : 0.15;
        if (rate != FlashRate.Off) MessageText.Opacity = visible;
        if (_vm.FlashTimer) TimerText.Opacity = visible;
        else if (TimerText.Opacity != 1.0) TimerText.Opacity = 1.0;
    }
}
