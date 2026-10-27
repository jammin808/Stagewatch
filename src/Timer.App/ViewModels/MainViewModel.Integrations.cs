using System.Globalization;
using System.IO;
using System.Windows.Threading;
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
    /// <summary>What each playout link keeps across a restart: a hold and its end (Show Bus 13.15.3); never a token, proof or nonce.</summary>
    private readonly PlayoutKeptStore _playoutKept = new(Path.Combine(DataFolder, "playout-links.json"));
    private SpeechPacer? _speech;
    private TimeSync? _ntp;
    private LtcOutput? _ltc;
    private LtcInput? _ltcIn;
    private CueLightOutput? _lights;
    private TriggerRunner? _triggers;
    /// <summary>
    /// What HTTP triggers keep across a restart and across a runner rebuilt by the supervisor: the certificates their HTTPS targets showed,
    /// pinned first or by a person, and their held secrets' ends in wall time; never a secret, nor a hash of one.
    /// </summary>
    private readonly TriggerPinStore _triggerPins = new(Path.Combine(DataFolder, "trigger-pins.json"));
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
        ReconcilePlayout(i, FeatureGate.AllowsPlayout(t));
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
            () => new TriggerRunner(() => _store.Snapshot.Triggers, line => _ui.BeginInvoke(() => RunCommandLine(line)), TriggerOptions()));   // its worker reads a snapshot
        _supervisor.Reconcile("rooms", $"{i.Rooms && FeatureGate.AllowsMultiRoom(t)}|{i.RoomsPort}", v => _rooms = v,
            () => new RoomBeacon(i.RoomsPort, BeatFor));
        var key = i.AssistantCloud ? ProtectedKey.Unprotect(i.AssistantKeyProtected) : "";
        _supervisor.Reconcile("cloud", $"{i.Assistant && i.AssistantCloud && FeatureGate.AllowsAssistant(t) && key.Length > 0}|{key.GetHashCode()}", v => _cloud = v,
            () => new CloudAdvisor(key));
    }

    /// <summary>
    /// What the trigger worker is set to: an auth profile's secret resolved here (DPAPI, this Windows user) as the trigger goes, request
    /// ids ending with this process's hello inst, its HTTPS targets' pins kept beside the settings, a target's node by the hello heard at
    /// its host and port, and each authenticated fire's outcome a row in the show log on the show thread — never its secret.
    /// </summary>
    private TriggerRunnerOptions TriggerOptions() => new()
    {
        Unprotect = ProtectedKey.Unprotect,
        Inst = _inst,
        Pins = _triggerPins,
        NodeAt = (host, port) => Volatile.Read(ref _slidesBeacon)?.Peers() is { } peers ? TriggerPinStore.NodeAt(peers, host, port) : null,
        Sent = sent =>
        {
            if (sent.Authenticated) _ui.BeginInvoke(() => _journal.Add(sent.At.UtcDateTime, JournalKind.Trigger, _show.RundownActive ? _show.CurrentItem : -1, sent.Row));
        },
    };

    /// <summary>
    /// Each playout link Settings has, started, restarted or retired as its settings change: a link runs while it is switched on and
    /// the licence runs playout links, on the transport Settings set (<c>auto</c>: a PlayMap node over TLS where its hello names that
    /// door; <c>tcp</c>; <c>wss</c>: TLS alone, never dialled plain), for the node Settings set — its certificate's pin is kept for
    /// that id. Its request ids end with this process's hello inst (Show Bus 13.12.1), so a link made again never repeats one; it starts
    /// from what it kept across a restart and keeps a hold before it writes or dials again (13.15.3), the hold kept with the tag of the
    /// token as Settings stores it, so a token changed in Settings is proved at once — the tag is in the signature (it is no secret), so
    /// the same token typed again remakes the link with the tag Settings now holds. A link no longer in Settings is retired by its name.
    /// Its listeners are the director's, wired before it starts.
    /// </summary>
    private void ReconcilePlayout(IntegrationSettings i, bool licensed)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in i.PlayoutLinks)
        {
            var link = s.Clone();
            names.Add(link.Name);
            var on = link.Enabled && licensed;
            var token = on ? ProtectedKey.Unprotect(link.TokenProtected) : "";
            var tag = PlayoutKeptStore.Tag(link.TokenProtected);
            _supervisor.Reconcile("playout:" + link.Name, $"{on}|{link.Kind}|{link.Host}|{link.Port}|{link.Transport}|{link.Node}|{token.GetHashCode()}|{tag}",
                v => _playout.Set(link.Name, v),
                () => PlayoutLinks.Make(link, token, _inst, TimeProvider.System, _playoutKept, tag, Director.Wire, PlayoutHello(link)));
        }
        foreach (var gone in _playoutNames.Where(n => !names.Contains(n)))
            _supervisor.Reconcile<PlayoutLink>("playout:" + gone, "False", v => _playout.Set(gone, v), () => throw new InvalidOperationException("a retired link is never made"));
        _playoutNames = names;
    }

    /// <summary>
    /// The hello the show network hears for a playout link's node (Show Bus §2), asked on the link's own threads and never waiting
    /// (<see cref="PlayoutHelloSource"/>, <see cref="PlayoutHellos.Choose"/>): the node Settings set by its id, else a PlayMap node heard
    /// at the link's host — its name resolved off those threads — within <see cref="SlidesBeacon.StaleAfter"/>; one two running nodes
    /// say comes with its conflict mark and holds the link (13.4.4). Its <c>https</c> door is one to try TLS at, never a pin (13.4.2).
    /// </summary>
    private Func<PlayoutHeard?> PlayoutHello(PlayoutLinkSettings s)
        => new PlayoutHelloSource(() => Volatile.Read(ref _slidesBeacon)?.Peers(), s.Node, s.Host).Heard;

    /// <summary>Why a playout link Settings has is not running, in words, or null when it runs.</summary>
    private string? PlayoutNotRunning(PlayoutLinkSettings s)
    {
        if (!s.Enabled) return "off";
        if (!FeatureGate.AllowsPlayout(_license.Tier)) return FeatureGate.Needs(FeatureTier.Production);
        return _playout[s.Name] is null ? "starting" : null;
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

    /// <summary>One line per integration for Settings and diagnostics, each playout link among them by its peer's name.</summary>
    public IReadOnlyList<(string Name, LinkHealth Health, string Status)> IntegrationStatus() => IntegrationStatus(playout: true);

    /// <summary>One line per integration; the playout links' only with <paramref name="playout"/> (their context items are their records').</summary>
    private IReadOnlyList<(string Name, LinkHealth Health, string Status)> IntegrationStatus(bool playout)
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
        if (playout)
            foreach (var s in Settings.Integrations.PlayoutLinks)
            {
                if (_playout[s.Name] is { } link) list.Add((link.Peer, link.Health, link.Status));
                else list.Add((PlayoutRules.Peer(s.Kind == PlayoutRules.Auto ? null : s.Kind, s.Name), LinkHealth.Off, PlayoutNotRunning(s) ?? "off"));
            }
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
        _ppt = null; _vmix = null; _speech = null; _ntp = null; _ltc = null; _ltcIn = null; _lights = null; _triggers = null; _rooms = null; _cloud = null;
        // The stopped links posted their last rows to this thread — outcomes their stop settled, "→ off" — and the journal takes them
        // here, before its file closes (Show Bus 13.12.10): the queue is run up to them, for a second at most. A hold a link kept as it
        // stopped reaches its file within a second too.
        try
        {
            _ui.Invoke(static () => { }, DispatcherPriority.Background, CancellationToken.None, TimeSpan.FromSeconds(1));
        }
        catch (InvalidOperationException)
        {
            // The dispatcher is past running its queue (the app is shutting down): rows posted after this point are the journal's in
            // memory alone.
        }
        _playoutKept.Flushed.Wait(TimeSpan.FromSeconds(1));
        _triggerPins.Flushed.Wait(TimeSpan.FromSeconds(1));
        _ = _journal.DisposeAsync();
    }
}
