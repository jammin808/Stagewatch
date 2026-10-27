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
    private PlayoutLink? _patterns;
    private SpeechPacer? _speech;
    private TimeSync? _ntp;
    private LtcOutput? _ltc;
    private LtcInput? _ltcIn;
    private CueLightOutput? _lights;
    private TriggerRunner? _triggers;
    private RoomBeacon? _rooms;
    private CloudAdvisor? _cloud;
    private readonly SpokenCues _voices = new();
    // Starts and stops them as settings change; an old instance retires in the background (the show never waits).
    private readonly IntegrationSupervisor _supervisor;

    /// <summary>(Re)start whatever changed in Settings → Integrations. Owner thread; also re-run when an old instance has retired.</summary>
    public void ApplyIntegrations()
    {
        var i = Settings.Integrations;
        var t = _license.Tier;
        var pro = FeatureGate.AllowsRunOfShow(t);

        _supervisor.Reconcile("ppt", $"{i.PowerPoint && FeatureGate.AllowsPowerPointAddIn(_license.Tier)}", v => _ppt = v,
            () => { var s = new PowerPointSensor(); s.Start(); return s; });
        _supervisor.Reconcile("vmix", $"{i.Vmix && FeatureGate.AllowsPlayout(t)}|{i.VmixHost}|{i.VmixPort}", v => _vmix = v,
            () => { var s = new VmixSource(i.VmixHost, i.VmixPort); s.Start(); return s; });
        // Link "patterns" runs as the Patterns link: a Patterns desk, or a peer whose kind is not set (a PlayMap one never does).
        var pl = PlayoutRules.PatternsLink(i);
        var patternsOn = pl is { Enabled: true } && pl.Kind != PlayoutRules.PlayMap && FeatureGate.AllowsPlayout(t);
        var (patternsHost, patternsPort) = (pl?.Host ?? "", pl?.Port ?? 0);
        var patternsToken = patternsOn ? ProtectedKey.Unprotect(pl!.TokenProtected) : "";
        _supervisor.Reconcile("patterns", $"{patternsOn}|{patternsHost}|{patternsPort}|{patternsToken.GetHashCode()}", v => _patterns = v,
            () =>
            {
                // Its request ids end with this process's hello inst (Show Bus 13.12.1), so a link made again never repeats one.
                var s = new PlayoutLink(new PlayoutLinkOptions { Host = patternsHost, Port = patternsPort, Token = patternsToken, Inst = _inst });
                s.Answered += OnPatternsAnswer;
                s.Logged += OnPatternsLogged;
                s.Start();
                return s;
            });
        _supervisor.Reconcile("ntp", $"{i.Ntp && FeatureGate.AllowsTimecode(t)}|{i.NtpServer}", v => _ntp = v,
            () => { var s = new TimeSync(i.NtpServer); s.Start(); return s; });
        _supervisor.Reconcile("ltc", $"{i.Ltc && FeatureGate.AllowsTimecode(t)}|{i.LtcDevice}|{i.LtcFps}|{i.LtcSource}", v => _ltc = v,
            () => new LtcOutput(i.LtcDevice, i.LtcFps, LtcTimeSource(i.LtcSource)));
        _supervisor.Reconcile("ltcin", $"{i.LtcIn && FeatureGate.AllowsTimecode(t)}|{i.LtcInDevice}", v => _ltcIn = v,
            () => new LtcInput(i.LtcInDevice));
        _supervisor.Reconcile("lights", $"{(i.ArtNet || i.Sacn || i.Hue) && FeatureGate.AllowsCueLights(t)}|{i.ArtNet}|{i.ArtNetHost}|{i.Sacn}|{i.DmxUniverse}|{i.DmxAddress}|{i.DmxMode}|{i.Hue}|{i.HueBridge}|{i.HueUser}|{i.HueLights}", v => _lights = v,
            () =>
            {
                var sinks = new List<ILightSink>();
                if (i.ArtNet) sinks.Add(new ArtNetSink(i.ArtNetHost, Math.Max(0, i.DmxUniverse - 1), i.DmxAddress, i.DmxMode));
                if (i.Sacn) sinks.Add(new SacnSink(i.DmxUniverse, i.DmxAddress, i.DmxMode));
                if (i.Hue && i.HueBridge.Length > 0 && i.HueUser.Length > 0) sinks.Add(new HueSink(i.HueBridge, i.HueUser, i.HueLights));
                return new CueLightOutput(() => _hub.Latest, sinks.ToArray());
            });
        _supervisor.Reconcile("triggers", $"{pro && Settings.Triggers.Count > 0}", v => _triggers = v,
            () => new TriggerRunner(() => _store.Snapshot.Triggers, line => _ui.BeginInvoke(() => RunCommandLine(line))));   // its worker reads a snapshot
        _supervisor.Reconcile("rooms", $"{i.Rooms && FeatureGate.AllowsMultiRoom(t)}|{i.RoomsPort}", v => _rooms = v,
            () => new RoomBeacon(i.RoomsPort, BeatFor));
        var key = i.AssistantCloud ? ProtectedKey.Unprotect(i.AssistantKeyProtected) : "";
        _supervisor.Reconcile("cloud", $"{i.Assistant && i.AssistantCloud && FeatureGate.AllowsAssistant(t) && key.Length > 0}|{key.GetHashCode()}", v => _cloud = v,
            () => new CloudAdvisor(key));
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
            _show.RundownActive ? _show.CurrentItem + 1 : 0, s.Title, s.Phase.ToString().ToLowerInvariant(),
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
        // In parallel, with a cap: closing the app never waits on one slow device for long.
        _supervisor.StopAll(TimeSpan.FromSeconds(3), _speech);
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
