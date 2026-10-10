using System.IO;
using System.Text;
using Stagewatch.Core;
using Stagewatch.Output;
using Stagewatch.Remote;

namespace Stagewatch.App.ViewModels;

// Holographic display style: a three.js renderer in the output window (WebView2) and on browser screens.
// The show thread owns the setting; the output window's HoloHost owns the 3D layer and its watchdog;
// frames reach it as the same JSON browser screens get, only when something visible changed.
public partial class MainViewModel
{
    private static readonly string LocalData =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Stagewatch");

    public bool Holographic => Settings.Display.Holographic && FeatureGate.AllowsHolographic(_license.Tier);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleHolographic() => SetHolographic(!Holographic);

    public bool EggTimer => Settings.Display.EggTimer;

    /// <summary>Switch the 3D display on with the hologram or the egg timer.</summary>
    public void SetThreeDScene(bool egg)
    {
        if (!Gate(FeatureGate.AllowsHolographic(_license.Tier), "The 3D display", FeatureTier.Pro)) return;
        if (Settings.Display.EggTimer != egg)
        {
            RecordUi(egg ? "style egg" : "style holo");
            _store.Update(s => s.Display.EggTimer = egg);
            OnPropertyChanged(nameof(EggTimer));
            MarkDirty();
        }
        SetHolographic(true);
        StatusLine = egg ? "3D egg timer on the output and browser screens" : "Holographic display on the output and browser screens";
    }

    /// <summary>Flip one of the 3D options (animations, particles, projector base) from a menu.</summary>
    public void Set3DOption(string option, bool on)
    {
        _store.Update(s =>
        {
            if (option == "animations") s.Display.Animations3D = on;
            else if (option == "particles") s.Display.Particles3D = on;
            else if (option == "floor") s.Display.HoloFloor = on;
        });
        MarkDirty();   // every 3D screen picks it up from the next frame
        if (option == "floor") ConfigureHolo();
    }

    /// <summary>This PC's own web address (http://127.0.0.1:port), or null when the web server is off.</summary>
    public string? RemoteBaseUrl => Settings.Remote.WebEnabled && FeatureGate.AllowsRemote(_license.Tier) ? $"http://127.0.0.1:{Settings.Remote.WebPort}" : null;

    /// <summary>Switch the holographic style on or off (setting, output window, browser screens).</summary>
    public void SetHolographic(bool on)
    {
        if (on && !Gate(FeatureGate.AllowsHolographic(_license.Tier), "The holographic display", FeatureTier.Pro)) return;
        if (Settings.Display.Holographic == on) { ApplyHolo(); return; }
        RecordUi(on ? "style holo" : "style standard");
        _store.Update(s => s.Display.Holographic = on);
        ApplyHolo();
        MarkDirty();   // browser screens switch with the next frame
        OnPropertyChanged(nameof(Holographic));
        StatusLine = on ? "Holographic display on — outputs and browser screens switch to 3D" : "Standard display";
    }

    /// <summary>Bring the output window's 3D layer in line with the setting. Owner thread.</summary>
    private void ApplyHolo()
    {
        if (_outputWindow is null) return;
        var want = Holographic;
        if (want && !_outputWindow.HolographicOn)
        {
            string assets;
            try { assets = WebAssets.ExtractHolo(Path.Combine(LocalData, "holo")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                StatusLine = "Holographic display unavailable (couldn't prepare its files: " + ex.Message + ")";
                return;
            }
            _outputWindow.HoloStatus -= OnHoloStatus;
            _outputWindow.HoloStatus += OnHoloStatus;
            _outputWindow.SetHolographic(true, assets, Path.Combine(LocalData, "WebView2"));
            _lastHoloFrame = null;
        }
        else if (!want && _outputWindow.HolographicOn)
        {
            _outputWindow.SetHolographic(false, null, null);
        }
        if (want) ConfigureHolo();
        OnPropertyChanged(nameof(Holographic));
    }

    private void ConfigureHolo()
    {
        var disp = Settings.Display;
        var bg = SelectedOutputMode is OutputMode.KeyedFullScreen or OutputMode.FloatingMinimal
            ? "#" + ChromaKey.ToColor(disp.KeyColor).ToString().Substring(3)   // #AARRGGBB → #RRGGBB
            : "#000000";
        _outputWindow?.ConfigureHolo(bg, disp.HoloFloor);
    }

    private string? _lastHoloFrame;

    /// <summary>Called with every published frame: forward it to the 3D layer when it's on and the frame changed.</summary>
    private void PublishHolo(ShowSnapshot s)
    {
        if (_outputWindow is not { HolographicOn: true }) return;
        var json = Encoding.UTF8.GetString(SnapshotJson.Serialize(s));
        if (json == _lastHoloFrame) return;
        _lastHoloFrame = json;
        _outputWindow.PostHoloFrame(json);
    }

    private void OnHoloStatus(string message) => StatusLine = message;

    /// <summary>IShowControl: crew link keys (read from network threads; strings and a bool, so a plain read is safe).</summary>
    public (string? Secret, bool Required) CrewKeyPolicy => (Settings.Remote.CrewSecret, Settings.Remote.CrewKeysRequired);
}
