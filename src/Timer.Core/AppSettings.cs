using System.Text.Json.Serialization;

namespace Stagewatch.Core;

/// <summary>
/// Everything that needs to survive across runs. Serialized as JSON in
/// %APPDATA%\Stagewatch\settings.json. The schema is versioned for forward-compat migrations.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Schema 2: playout links are a list (<see cref="IntegrationSettings.PlayoutLinks"/>), and the 2.17 Patterns keys are link
    /// "patterns"'s mirror (<see cref="PlayoutRules.Upgrade"/> as a file loads, <see cref="PlayoutRules.WriteMirror"/> as it saves).
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>This install's Show Bus node id: random, made once, never shown as a secret (it isn't one).</summary>
    [JsonPropertyName("nodeId")]
    public string NodeId { get; set; } = "";

    [JsonPropertyName("lastMode")]
    public TimerMode LastMode { get; set; } = TimerMode.Countdown;

    [JsonPropertyName("lastDurationSeconds")]
    public int LastDurationSeconds { get; set; } = 300;

    /// <summary>The live running order's name, and the show's other running orders (switch between them on the day).</summary>
    [JsonPropertyName("rundownName")]
    public string RundownName { get; set; } = "Main";

    [JsonPropertyName("otherRundowns")]
    public List<NamedRundown> OtherRundowns { get; set; } = new();

    /// <summary>Auxiliary timers (names, direction, durations) — they run alongside the show.</summary>
    [JsonPropertyName("auxTimers")]
    public List<AuxTimerSettings> AuxTimers { get; set; } = new();

    [JsonPropertyName("lastTargetTimeOfDay")]
    public string? LastTargetTimeOfDay { get; set; }

    [JsonPropertyName("presets")]
    public List<TimerPreset> Presets { get; set; } = new(TimerPreset.DefaultDeck());

    [JsonPropertyName("messageBank")]
    public MessageBank MessageBank { get; set; } = MessageBank.Default();

    [JsonPropertyName("messageState")]
    public MessageState MessageState { get; set; } = new();

    [JsonPropertyName("display")]
    public DisplaySettings Display { get; set; } = new();

    [JsonPropertyName("companion")]
    public CompanionSettings Companion { get; set; } = new();

    [JsonPropertyName("mainWindowBounds")]
    public WindowBounds? MainWindowBounds { get; set; }

    /// <summary>The main window's panels: order, torn-out windows and the side column's width.</summary>
    [JsonPropertyName("panels")]
    public PanelLayoutSettings Panels { get; set; } = new();

    [JsonPropertyName("allowOvertime")]
    public bool AllowOvertime { get; set; } = true;

    /// <summary>At zero, hide the time on speaker screens (rather than showing overtime or 00:00).</summary>
    [JsonPropertyName("hideAtZero")]
    public bool HideAtZero { get; set; }

    /// <summary>The show's chime library (WAV files kept beside the settings).</summary>
    [JsonPropertyName("sounds")]
    public List<SoundEntry> Sounds { get; set; } = new();

    /// <summary>End-at pace: false = elastic (stretch the loaded duration), true = count the real time left.</summary>
    [JsonPropertyName("endAtRealTime")]
    public bool EndAtRealTime { get; set; }

    [JsonPropertyName("operatorShortcuts")]
    public bool OperatorShortcuts { get; set; } = true;

    /// <summary>The .stagewatch file currently open (null = not saved yet).</summary>
    [JsonPropertyName("showPath")]
    public string? ShowPath { get; set; }

    [JsonPropertyName("showName")]
    public string ShowName { get; set; } = "";

    [JsonPropertyName("rundown")]
    public Rundown Rundown { get; set; } = new();

    [JsonPropertyName("alerts")]
    public AlertSettings Alerts { get; set; } = new();

    [JsonPropertyName("remote")]
    public RemoteSettings Remote { get; set; } = new();

    [JsonPropertyName("powerpointOverlayBounds")]
    public WindowBounds PowerPointOverlayBounds { get; set; } = new() { X = 16, Y = 16, Width = 360, Height = 120 };

    // ---------------------------------------------------------------- 2.0

    [JsonPropertyName("integrations")]
    public IntegrationSettings Integrations { get; set; } = new();

    [JsonPropertyName("signals")]
    public SignalSettings Signals { get; set; } = new();

    [JsonPropertyName("triggers")]
    public List<TriggerRule> Triggers { get; set; } = new();

    /// <summary>Holding images (2.3): the library list and how they're shown.</summary>
    [JsonPropertyName("holding")]
    public HoldingSettings Holding { get; set; } = new();
}

/// <summary>Departments, call-to-stage and acknowledgement behaviour.</summary>
public sealed class SignalSettings
{
    /// <summary>Department names that get standby / GO cues (and their own browser view).</summary>
    [JsonPropertyName("departments")]
    public List<string> Departments { get; set; } = new() { "Lighting", "Sound", "Video", "Stage" };

    /// <summary>Messages sent from the main window ask for an acknowledgement by default.</summary>
    [JsonPropertyName("ackByDefault")]
    public bool AckByDefault { get; set; }

    /// <summary>Seconds before an unacknowledged cue escalates (flashes, alerts the operator).</summary>
    [JsonPropertyName("escalateSeconds")]
    public int EscalateSeconds { get; set; } = 20;
}

/// <summary>Everything that talks to other software or hardware. All off by default.</summary>
public sealed class IntegrationSettings
{
    [JsonPropertyName("room")] public string RoomName { get; set; } = "";

    // PowerPoint (COM, no add-in needed)
    [JsonPropertyName("powerPoint")] public bool PowerPoint { get; set; }
    /// <summary>Which slideshow the show follows: "auto" (a presenter PC that's presenting, else this PC), "local", or a presenter PC's id.</summary>
    [JsonPropertyName("slidesSource")] public string SlidesSource { get; set; } = "auto";

    // vMix playout countdowns
    [JsonPropertyName("vmix")] public bool Vmix { get; set; }
    [JsonPropertyName("vmixHost")] public string VmixHost { get; set; } = "127.0.0.1";
    [JsonPropertyName("vmixPort")] public int VmixPort { get; set; } = 8088;

    // The 2.17 Patterns keys: link "patterns"'s mirror, written by PlayoutRules.WriteMirror and read only by PlayoutRules.Upgrade.
    // Every other reader takes the link from PlayoutLinks.
    [JsonPropertyName("patterns")] public bool? LegacyPatterns { get; set; }
    [JsonPropertyName("patternsHost")] public string? LegacyPatternsHost { get; set; }
    [JsonPropertyName("patternsPort")] public int? LegacyPatternsPort { get; set; }
    [JsonPropertyName("patternsToken")] public string? LegacyPatternsToken { get; set; }
    [JsonPropertyName("patternsNextOnEnd")] public bool? LegacyPatternsNextOnEnd { get; set; }

    // Speech-to-script
    [JsonPropertyName("speech")] public bool Speech { get; set; }
    /// <summary>Script file (.txt). Empty = use the PowerPoint speaker notes.</summary>
    [JsonPropertyName("scriptPath")] public string ScriptPath { get; set; } = "";
    [JsonPropertyName("speechCulture")] public string SpeechCulture { get; set; } = "";

    // Physical cue lights
    [JsonPropertyName("artNet")] public bool ArtNet { get; set; }
    [JsonPropertyName("artNetHost")] public string ArtNetHost { get; set; } = "255.255.255.255";
    [JsonPropertyName("sacn")] public bool Sacn { get; set; }
    [JsonPropertyName("dmxUniverse")] public int DmxUniverse { get; set; } = 1;
    /// <summary>First DMX channel (1-based) of the RGB(+dimmer) fixture.</summary>
    [JsonPropertyName("dmxAddress")] public int DmxAddress { get; set; } = 1;
    [JsonPropertyName("dmxMode")] public DmxMode DmxMode { get; set; } = DmxMode.Rgb;
    [JsonPropertyName("hue")] public bool Hue { get; set; }
    [JsonPropertyName("hueBridge")] public string HueBridge { get; set; } = "";
    [JsonPropertyName("hueUser")] public string HueUser { get; set; } = "";
    [JsonPropertyName("hueLights")] public string HueLights { get; set; } = "";

    // Time
    [JsonPropertyName("ntp")] public bool Ntp { get; set; } = true;
    [JsonPropertyName("ntpServer")] public string NtpServer { get; set; } = "pool.ntp.org";
    [JsonPropertyName("ltc")] public bool Ltc { get; set; }
    [JsonPropertyName("ltcDevice")] public string LtcDevice { get; set; } = "";
    [JsonPropertyName("ltcFps")] public int LtcFps { get; set; } = 25;
    /// <summary>"tod" (time of day) or "countdown" (remaining time as timecode).</summary>
    [JsonPropertyName("ltcSource")] public string LtcSource { get; set; } = "tod";
    /// <summary>LTC in: read timecode from an audio input.</summary>
    [JsonPropertyName("ltcIn")] public bool LtcIn { get; set; }
    [JsonPropertyName("ltcInDevice")] public string LtcInDevice { get; set; } = "";
    /// <summary>The timer follows LTC: starts when the code rolls, pauses when it stops.</summary>
    [JsonPropertyName("ltcChase")] public bool LtcChase { get; set; }
    /// <summary>Optional: count down to this timecode (HH:MM:SS:FF), e.g. the last frame of a video.</summary>
    [JsonPropertyName("ltcEnd")] public string LtcEnd { get; set; } = "";

    // Multi-room
    [JsonPropertyName("rooms")] public bool Rooms { get; set; } = true;
    [JsonPropertyName("roomsPort")] public int RoomsPort { get; set; } = 9927;

    // Talent audio (per-cue device) and spoken cues
    [JsonPropertyName("talentDevice")] public string TalentDevice { get; set; } = "";
    [JsonPropertyName("voice")] public string Voice { get; set; } = "";

    // Producer assistant
    [JsonPropertyName("assistant")] public bool Assistant { get; set; } = true;
    /// <summary>Optional: ask Claude for suggestions too (sends the running order and timings to Anthropic).</summary>
    [JsonPropertyName("assistantCloud")] public bool AssistantCloud { get; set; }
    /// <summary>AI agents on MCP propose; the operator approves in Copilot (on by default). Off = they act directly.</summary>
    [JsonPropertyName("agentsNeedApproval")] public bool AgentsNeedApproval { get; set; } = true;
    /// <summary>DPAPI-protected API key (never written in clear).</summary>
    [JsonPropertyName("assistantKey")] public string AssistantKeyProtected { get; set; } = "";

    // Model Context Protocol endpoint for the Patterns ecosystem / AI tools
    [JsonPropertyName("mcp")] public bool Mcp { get; set; } = true;

    // Playout links (schema 2): Patterns desks and PlayMap nodes, each by a name of its own (PlayoutRules has their rules).
    /// <summary>At most <see cref="PlayoutRules.Max"/>, in the operator's order; link "patterns" is the one a 2.17 Patterns setup became.</summary>
    [JsonPropertyName("playoutLinks")] public List<PlayoutLinkSettings> PlayoutLinks { get; set; } = new();
    /// <summary>How far ahead a timed GO is set, ms (<see cref="PlayoutRules.MinLeadMs"/>–<see cref="PlayoutRules.MaxLeadMs"/>).</summary>
    [JsonPropertyName("playoutLeadMs")] public int PlayoutLeadMs { get; set; } = PlayoutRules.DefaultLeadMs;
}

public enum DmxMode { Rgb, Rgbw, RgbDimmer, Dimmer }

public enum TriggerEvent { ItemStart, ItemEnd, Phase, Overtime, Message, Ack, Escalated, ShowStart, HardOutRisk, Blackout, Timecode, ClipEnd }

public enum TriggerAction { Osc, Http, CompanionPress, Command }

/// <summary>"When X happens, do Y" — Stagewatch driving the rest of the show (the Patterns direction).</summary>
public sealed class TriggerRule
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("event")] public TriggerEvent Event { get; set; }
    /// <summary>Optional filter: phase name for Phase, item number for ItemStart/ItemEnd, target for Ack.</summary>
    [JsonPropertyName("filter")] public string Filter { get; set; } = "";
    [JsonPropertyName("action")] public TriggerAction Action { get; set; }
    /// <summary>OSC: host:port; HTTP: URL; Companion: host:port; Command: unused.</summary>
    [JsonPropertyName("target")] public string Target { get; set; } = "";
    /// <summary>OSC address + args; HTTP body; Companion "page/row/column"; Command: a command line.</summary>
    [JsonPropertyName("payload")] public string Payload { get; set; } = "";
    /// <summary>
    /// HTTP only: the secret it sends and how (<see cref="TriggerAuthRules"/>), its secret DPAPI-protected; null (left out of the file)
    /// for none. Only to this PC or a pinned HTTPS server, the address fixed, no redirect followed.
    /// </summary>
    [JsonPropertyName("auth")] public TriggerAuth? Auth { get; set; }

    /// <summary>A copy that shares nothing with this rule, its profile included.</summary>
    public TriggerRule Clone() => new()
    {
        Enabled = Enabled, Name = Name, Event = Event, Filter = Filter, Action = Action, Target = Target, Payload = Payload,
        Auth = Auth is null ? null : new TriggerAuth { Kind = Auth.Kind, SecretProtected = Auth.SecretProtected },
    };
}

/// <summary>
/// Audible cues for the operator. Off by default on new installs: at events the PC's default
/// audio device is often the mixing desk. Pick the laptop speaker / headphones in Settings first.
/// </summary>
public sealed class AlertSettings
{
    [JsonPropertyName("onWarning")] public bool OnWarning { get; set; }
    [JsonPropertyName("onWrapUp")]  public bool OnWrapUp { get; set; }
    [JsonPropertyName("onZero")]    public bool OnZero { get; set; }
    /// <summary>A short rising chime when a running-order item starts.</summary>
    [JsonPropertyName("onStart")]   public bool OnStart { get; set; }
    /// <summary>Output device name; "" = Windows default. If the named device is missing, cues are skipped.</summary>
    [JsonPropertyName("device")]    public string Device { get; set; } = "";
    /// <summary>Optional per-cue devices (blank = the device above). E.g. wrap-up to the talent's in-ear feed.</summary>
    [JsonPropertyName("warningDevice")] public string WarningDevice { get; set; } = "";
    [JsonPropertyName("wrapUpDevice")]  public string WrapUpDevice { get; set; } = "";
    [JsonPropertyName("zeroDevice")]    public string ZeroDevice { get; set; } = "";
}

/// <summary>Browser viewer / remote control (HTTP + WebSocket) and OSC.</summary>
public sealed class RemoteSettings
{
    [JsonPropertyName("webEnabled")] public bool WebEnabled { get; set; } = true;
    [JsonPropertyName("webPort")]    public int WebPort { get; set; } = 9924;
    /// <summary>Listen on the LAN (all interfaces). Off = this PC only.</summary>
    [JsonPropertyName("webBindAll")] public bool WebBindAll { get; set; } = true;
    /// <summary>Also speak HTTPS on the web port, with this install's certificate (Show Bus §10). On by default: plain HTTP keeps working.</summary>
    [JsonPropertyName("https")]      public bool Https { get; set; } = true;
    /// <summary>Refuse plain HTTP / WebSockets from other machines (they're sent to https://).</summary>
    [JsonPropertyName("requireHttps")] public bool RequireHttps { get; set; }
    /// <summary>Required for control (not viewing). Empty = no PIN. Generated on first run.</summary>
    [JsonPropertyName("pin")]        public string? Pin { get; set; }
    /// <summary>Secret behind the crew link keys (see <see cref="CrewKeys"/>). A new one revokes every crew link.</summary>
    [JsonPropertyName("crewSecret")] public string? CrewSecret { get; set; }
    /// <summary>Crew pages may confirm cues only when opened with their crew link (recommended).</summary>
    [JsonPropertyName("crewKeys")]   public bool CrewKeysRequired { get; set; } = true;

    [JsonPropertyName("oscEnabled")] public bool OscEnabled { get; set; }
    [JsonPropertyName("oscPort")]    public int OscPort { get; set; } = 9925;
    [JsonPropertyName("oscBindAll")] public bool OscBindAll { get; set; } = true;
    /// <summary>Where to send OSC state feedback. Empty = don't send.</summary>
    [JsonPropertyName("oscFeedbackHost")] public string OscFeedbackHost { get; set; } = "";
    [JsonPropertyName("oscFeedbackPort")] public int OscFeedbackPort { get; set; } = 9926;

    /// <summary>Names the operator gave browser screens, by the screen's id (kept in that browser).</summary>
    [JsonPropertyName("screenNames")] public Dictionary<string, string> ScreenNames { get; set; } = new();
    /// <summary>Saved screen setups: a page and its options under a short name, opened at /p/&lt;name&gt;.</summary>
    [JsonPropertyName("screenPresets")] public List<ScreenPreset> ScreenPresets { get; set; } = new();
    /// <summary>Extra CSS for every browser screen (served at /custom.css). Each page's &lt;html&gt; has class sw-page-&lt;name&gt;.</summary>
    [JsonPropertyName("customCss")] public string CustomCss { get; set; } = "";
    /// <summary>Optional code every browser screen must know to see the show (public or shared networks). Empty = open.</summary>
    [JsonPropertyName("viewCode")] public string ViewCode { get; set; } = "";
    /// <summary>Scoped, revocable API keys (read / control / MCP) for scripts and agents, so the PIN needn't be shared.</summary>
    [JsonPropertyName("apiKeys")] public List<ApiKey> ApiKeys { get; set; } = new();
    /// <summary>Designed screens, opened at /layout?name=… and edited at /designer.</summary>
    [JsonPropertyName("layouts")] public List<ScreenLayout> Layouts { get; set; } = new();
    /// <summary>Browser windows Stagewatch controls, each at an address of its own (/w/&lt;token&gt;). They stay with this PC
    /// (the addresses are on the screens in the venue), never in a show file.</summary>
    [JsonPropertyName("windows")] public List<ScreenWindow> Windows { get; set; } = new();
    /// <summary>
    /// Settings → Remote → May drive playout: the addresses (at most <see cref="PlayoutGate.MaxFrom"/>) whose <c>playout</c> and
    /// <c>patterns</c> lines run without a credential (<see cref="PlayoutGate"/>). Empty by default: only this PC, the PIN or a key.
    /// They stay with this PC, never in a show file.
    /// </summary>
    [JsonPropertyName("playoutFrom")] public List<string> PlayoutFrom { get; set; } = new();
    public const int MaxCustomCss = 64 * 1024;
}

/// <summary>A saved screen setup, e.g. "lobby" → "/audience?holding=1".</summary>
public sealed class ScreenPreset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "/viewer";

    /// <summary>Preset names go in addresses (/p/lobby): letters, digits, - and _.</summary>
    public static bool ValidName(string? name)
        => !string.IsNullOrEmpty(name) && name.Length <= 40 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>A page on this Stagewatch: "/viewer?…". Never another site, never a PIN in the address.</summary>
    public static bool ValidPath(string? path)
        => !string.IsNullOrEmpty(path) && path.Length <= 400 && path[0] == '/' && !path.StartsWith("//", StringComparison.Ordinal)
           && !path.Contains('\\') && !path.Any(char.IsControl) && !path.Contains("://", StringComparison.Ordinal)
           && !System.Text.RegularExpressions.Regex.IsMatch(path, @"[?&]pin=", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}

public sealed class CompanionSettings
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("port")]    public int Port { get; set; } = 9923;
    [JsonPropertyName("bindAll")] public bool BindAllInterfaces { get; set; }
}
