using System.Globalization;
using Stagewatch.Core;
using Stagewatch.Integrations;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// Sensors (PowerPoint, vMix, speech, time server) and outputs (cue lights, LTC, triggers, room
/// beacon, assistant) — started and stopped from Settings → Integrations. Each runs on its own
/// thread or timer; the frame loop only ever reads their latest values.
/// </summary>
public sealed partial class MainViewModel
{
    private PowerPointSensor? _ppt;
    private VmixSource? _vmix;
    private PatternsLink? _patterns;
    private SpeechPacer? _speech;
    private TimeSync? _ntp;
    private LtcOutput? _ltc;
    private LtcInput? _ltcIn;
    private CueLightOutput? _lights;
    private TriggerRunner? _triggers;
    private RoomBeacon? _rooms;
    private CloudAdvisor? _cloud;
    private readonly SpokenCues _voices = new();
    private readonly Dictionary<string, string> _integrationSignatures = new();

    /// <summary>(Re)start whatever changed in Settings → Integrations. Owner thread.</summary>
    public void ApplyIntegrations()
    {
        var i = Settings.Integrations;
        var t = _license.Tier;
        var pro = FeatureGate.AllowsRunOfShow(t);

        Reconcile("ppt", $"{i.PowerPoint && FeatureGate.AllowsPowerPointAddIn(_license.Tier)}", ref _ppt,
            () => { var s = new PowerPointSensor(); s.Start(); return s; });
        Reconcile("vmix", $"{i.Vmix && FeatureGate.AllowsPlayout(t)}|{i.VmixHost}|{i.VmixPort}", ref _vmix,
            () => { var s = new VmixSource(i.VmixHost, i.VmixPort); s.Start(); return s; });
        var patternsToken = ProtectedKey.Unprotect(i.PatternsTokenProtected);
        Reconcile("patterns", $"{i.Patterns && FeatureGate.AllowsPlayout(t)}|{i.PatternsHost}|{i.PatternsPort}|{patternsToken.GetHashCode()}", ref _patterns,
            () => { var s = new PatternsLink(i.PatternsHost, i.PatternsPort, patternsToken); s.Start(); return s; });
        Reconcile("ntp", $"{i.Ntp && FeatureGate.AllowsTimecode(t)}|{i.NtpServer}", ref _ntp,
            () => { var s = new TimeSync(i.NtpServer); s.Start(); return s; });
        Reconcile("ltc", $"{i.Ltc && FeatureGate.AllowsTimecode(t)}|{i.LtcDevice}|{i.LtcFps}|{i.LtcSource}", ref _ltc,
            () => new LtcOutput(i.LtcDevice, i.LtcFps, LtcTimeSource(i.LtcSource)));
        Reconcile("ltcin", $"{i.LtcIn && FeatureGate.AllowsTimecode(t)}|{i.LtcInDevice}", ref _ltcIn,
            () => new LtcInput(i.LtcInDevice));
        Reconcile("lights", $"{(i.ArtNet || i.Sacn || i.Hue) && FeatureGate.AllowsCueLights(t)}|{i.ArtNet}|{i.ArtNetHost}|{i.Sacn}|{i.DmxUniverse}|{i.DmxAddress}|{i.DmxMode}|{i.Hue}|{i.HueBridge}|{i.HueUser}|{i.HueLights}", ref _lights,
            () =>
            {
                var sinks = new List<ILightSink>();
                if (i.ArtNet) sinks.Add(new ArtNetSink(i.ArtNetHost, Math.Max(0, i.DmxUniverse - 1), i.DmxAddress, i.DmxMode));
                if (i.Sacn) sinks.Add(new SacnSink(i.DmxUniverse, i.DmxAddress, i.DmxMode));
                if (i.Hue && i.HueBridge.Length > 0 && i.HueUser.Length > 0) sinks.Add(new HueSink(i.HueBridge, i.HueUser, i.HueLights));
                return new CueLightOutput(() => _hub.Latest, sinks.ToArray());
            });
        Reconcile("triggers", $"{pro && Settings.Triggers.Count > 0}", ref _triggers,
            () => new TriggerRunner(() => Settings.Triggers, line => _ui.BeginInvoke(() => RunCommandLine(line))));
        Reconcile("rooms", $"{i.Rooms && FeatureGate.AllowsMultiRoom(t)}|{i.RoomsPort}", ref _rooms,
            () => new RoomBeacon(i.RoomsPort, BeatFor));
        var key = i.AssistantCloud ? ProtectedKey.Unprotect(i.AssistantKeyProtected) : "";
        Reconcile("cloud", $"{i.Assistant && i.AssistantCloud && FeatureGate.AllowsAssistant(t) && key.Length > 0}|{key.GetHashCode()}", ref _cloud,
            () => new CloudAdvisor(key));
    }

    private void Reconcile<T>(string name, string signature, ref T? current, Func<T> create) where T : class, IDisposable
    {
        if (_integrationSignatures.TryGetValue(name, out var old) && old == signature && (current is not null) == signature.StartsWith("True")) return;
        _integrationSignatures[name] = signature;
        var previous = current;
        current = null;
        try { previous?.Dispose(); } catch (Exception) { }
        if (!signature.StartsWith("True", StringComparison.Ordinal)) return;
        try { current = create(); }
        catch (Exception ex) { StatusLine = $"{name}: couldn't start — {ex.Message}"; }
    }

    /// <summary>LTC follows the time of day (default) or the countdown itself as timecode.</summary>
    private Func<TimeSpan> LtcTimeSource(string source)
    {
        if (source == "countdown") return () => TimeSpan.FromSeconds(Math.Max(0, _hub.Latest.Seconds));
        // Time of day, corrected by the time server when we have a reading.
        return () => (DateTime.Now + (_ntp?.Value?.Offset ?? TimeSpan.Zero)).TimeOfDay;
    }

    private void EnsureSpeech()
    {
        if (_speech is not null) return;
        _speech = new SpeechPacer(new WindowsTranscriber(Settings.Integrations.SpeechCulture));
    }

    /// <summary>What this room tells the others (beacon thread; reads the immutable latest frame only).</summary>
    private RoomBeat? BeatFor(string id)
    {
        var s = _hub.Latest;
        var room = Settings.Integrations.RoomName;
        return new RoomBeat(1, id, room.Length > 0 ? room : Environment.MachineName, Environment.MachineName, _web?.Port ?? 0,
            s.Title, s.Mode == TimerMode.TimeOfDay ? s.TimeOfDay : s.Text, s.State.ToString().ToLowerInvariant(), s.Phase.ToString().ToLowerInvariant(),
            s.OffsetSeconds, s.HardOutText, s.HardOutMarginSeconds, s.Signals.Count(x => x.State is SignalState.Sent or SignalState.Seen));
    }

    private void FireTrigger(TriggerEvent ev, string filter, string detail)
    {
        if (_triggers is null) return;
        var s = _hub.Latest;
        _triggers.Fire(ev, filter, new TriggerContext(
            _rundownActive ? _currentItem + 1 : 0, s.Title, s.Phase.ToString().ToLowerInvariant(),
            TimerTick.FormatSeconds(Math.Max(0, s.Seconds)), detail, Settings.Integrations.RoomName));
    }

    /// <summary>One line per integration for Settings and diagnostics.</summary>
    public IReadOnlyList<(string Name, LinkHealth Health, string Status)> IntegrationStatus()
    {
        var list = new List<(string, LinkHealth, string)>();
        var t = _license.Tier;
        void Add(IIntegration? x, string name, bool licensed, FeatureTier needs)
        {
            if (x is not null) list.Add((x.Name, x.Health, x.Status));
            else list.Add((name, LinkHealth.Off, licensed ? "off" : FeatureGate.Needs(needs)));
        }
        Add(_ppt, "PowerPoint", FeatureGate.AllowsPowerPointAddIn(t), FeatureTier.Pro);
        if (FeatureGate.AllowsPowerPointAddIn(t)) { var (h, st) = PresenterStatus(); list.Add(("Presenter PCs", h, st)); }
        Add(_vmix, "vMix", FeatureGate.AllowsPlayout(t), FeatureTier.Production);
        Add(_patterns, "Patterns", FeatureGate.AllowsPlayout(t), FeatureTier.Production);
        Add(_speech, "Speech", FeatureGate.AllowsSpeechPace(t), FeatureTier.Production);
        Add(_ntp, "Time server", FeatureGate.AllowsTimecode(t), FeatureTier.Production);
        Add(_ltc, "LTC", FeatureGate.AllowsTimecode(t), FeatureTier.Production);
        Add(_ltcIn, "LTC in", FeatureGate.AllowsTimecode(t), FeatureTier.Production);
        Add(_lights, "Cue lights", FeatureGate.AllowsCueLights(t), FeatureTier.Production);
        Add(_triggers, "Triggers", FeatureGate.AllowsRunOfShow(t), FeatureTier.Pro);
        Add(_rooms, "Rooms", FeatureGate.AllowsMultiRoom(t), FeatureTier.Production);
        Add(_cloud, "Assistant (Claude)", FeatureGate.AllowsAssistant(t), FeatureTier.Production);
        return list;
    }

    private void DisposeIntegrations()
    {
        _slidesBeacon?.Dispose();
        _slidesBeacon = null;
        foreach (var d in new IDisposable?[] { _ppt, _vmix, _patterns, _speech, _ntp, _ltc, _ltcIn, _lights, _triggers, _rooms, _cloud })
        {
            try { d?.Dispose(); } catch (Exception) { }
        }
        _ppt = null; _vmix = null; _patterns = null; _speech = null; _ntp = null; _ltc = null; _ltcIn = null; _lights = null; _triggers = null; _rooms = null; _cloud = null;
        _ = _journal.DisposeAsync();
    }
}

/// <summary>API keys are stored encrypted for this Windows user (DPAPI), never in clear text.</summary>
public static class ProtectedKey
{
    private static readonly byte[] Entropy = "Stagewatch assistant key"u8.ToArray();

    public static string Protect(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";
        var bytes = System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(key.Trim()), Entropy,
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return "";
        try
        {
            var bytes = System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException) { return ""; }
    }
}
