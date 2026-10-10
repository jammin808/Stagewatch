using System.Collections.Concurrent;
using System.Globalization;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>Turns recognised speech into words. Abstracted so a Whisper (or other) engine can replace Windows'.</summary>
public interface ISpeechTranscriber : IDisposable
{
    event Action<string, DateTime>? PhraseRecognized;
    bool Start();
    void Stop();
    string Status { get; }
}

/// <summary>
/// Windows' built-in desktop dictation (System.Speech): fully offline — no audio leaves the PC —
/// and already installed with Windows' speech language packs.
/// </summary>
public sealed class WindowsTranscriber : ISpeechTranscriber
{
    private readonly string _culture;
    private SpeechRecognitionEngine? _engine;

    public WindowsTranscriber(string culture) { _culture = culture; }

    public event Action<string, DateTime>? PhraseRecognized;
    public string Status { get; private set; } = "off";

    public static IReadOnlyList<string> InstalledCultures()
    {
        try { return SpeechRecognitionEngine.InstalledRecognizers().Select(r => r.Culture.Name).Distinct().ToList(); }
        catch (Exception) { return Array.Empty<string>(); }
    }

    public bool Start()
    {
        try
        {
            var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
            if (recognizers.Count == 0) { Status = "no speech recogniser installed (Windows Settings → Time & language → Speech)"; return false; }
            var info = recognizers.FirstOrDefault(r => r.Culture.Name.Equals(_culture, StringComparison.OrdinalIgnoreCase))
                       ?? recognizers.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
                       ?? recognizers[0];
            var engine = new SpeechRecognitionEngine(info);
            engine.LoadGrammar(new DictationGrammar());
            engine.SetInputToDefaultAudioDevice();
            engine.SpeechRecognized += (_, e) =>
            {
                if (e.Result?.Text is { Length: > 0 } text) PhraseRecognized?.Invoke(text, DateTime.UtcNow);
            };
            engine.RecognizeAsync(RecognizeMode.Multiple);
            _engine = engine;
            Status = $"listening ({info.Culture.Name}) on the default microphone";
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            Status = "speech unavailable: " + ex.Message;
            return false;
        }
    }

    public void Stop()
    {
        try { _engine?.RecognizeAsyncCancel(); } catch { }
        try { _engine?.Dispose(); } catch { }
        _engine = null;
        Status = "off";
    }

    public void Dispose() => Stop();
}

/// <summary>
/// Speech-to-script pace tracking: a transcriber feeds a <see cref="ScriptTracker"/>; the show loop
/// reads <see cref="Position"/> (latest-wins). Tracker state lives on the recogniser's thread behind
/// one lock, so the show thread never waits on audio.
/// </summary>
public sealed class SpeechPacer : IIntegration
{
    private readonly ISpeechTranscriber _transcriber;
    private readonly object _lock = new();
    private ScriptTracker? _tracker;
    private string _scriptKey = "";
    private ScriptPosition? _position;

    public SpeechPacer(ISpeechTranscriber transcriber)
    {
        _transcriber = transcriber;
        _transcriber.PhraseRecognized += OnPhrase;
    }

    public string Name => "Speech";
    public LinkHealth Health { get; private set; } = LinkHealth.Off;
    public string Status => _transcriber.Status + (_tracker is null ? " — no script loaded" : $" — script {_tracker.TotalWords} words");
    public bool Listening { get; private set; }

    public ScriptPosition? Position { get { lock (_lock) return _position; } }

    public bool Start()
    {
        Listening = _transcriber.Start();
        Health = Listening ? LinkHealth.Ok : LinkHealth.Error;
        return Listening;
    }

    public void Stop()
    {
        _transcriber.Stop();
        Listening = false;
        Health = LinkHealth.Off;
    }

    /// <summary>Load (or keep) the script. Same text = same tracker, so progress isn't lost on every poll.</summary>
    public void SetScript(string? script)
    {
        script ??= "";
        var key = script.Length + ":" + script.GetHashCode();
        lock (_lock)
        {
            if (key == _scriptKey) return;
            _scriptKey = key;
            _tracker = string.IsNullOrWhiteSpace(script) ? null : new ScriptTracker(script);
            _position = null;
        }
    }

    public void Restart()
    {
        lock (_lock)
        {
            _scriptKey = "";
            _position = null;
            _tracker = null;
        }
    }

    private void OnPhrase(string text, DateTime utc)
    {
        lock (_lock)
        {
            if (_tracker is null) return;
            _position = _tracker.Feed(text, utc);
        }
    }

    /// <summary>Refresh rates (words per minute decays when the speaker stops).</summary>
    public ScriptPosition? Sample(DateTime utc)
    {
        lock (_lock) return _tracker is null ? null : _position = _tracker.Current(utc);
    }

    public void Dispose()
    {
        _transcriber.PhraseRecognized -= OnPhrase;
        _transcriber.Dispose();
    }
}

/// <summary>
/// Spoken cues for the talent's in-ear ("two minutes"), rendered once with Windows' offline voices
/// to 16-bit mono PCM and cached — at show time it's just a buffer to play on the talent device.
/// </summary>
public sealed class SpokenCues
{
    public const int SampleRate = 44100;
    private readonly ConcurrentDictionary<string, byte[]> _cache = new();

    public static IReadOnlyList<string> Voices()
    {
        try
        {
            using var s = new SpeechSynthesizer();
            return s.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList();
        }
        catch (Exception) { return Array.Empty<string>(); }
    }

    /// <summary>PCM for <paramref name="text"/>, or null when speech synthesis isn't available.</summary>
    public byte[]? Render(string text, string voice)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var key = voice + "\u0001" + text;
        if (_cache.TryGetValue(key, out var hit)) return hit;
        try
        {
            using var synth = new SpeechSynthesizer();
            if (!string.IsNullOrWhiteSpace(voice))
            {
                try { synth.SelectVoice(voice); } catch (ArgumentException) { }
            }
            synth.Rate = 1;
            using var ms = new MemoryStream();
            synth.SetOutputToAudioStream(ms, new SpeechAudioFormatInfo(SampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            synth.Speak(text);
            synth.SetOutputToNull();
            var pcm = ms.ToArray();
            _cache[key] = pcm;
            return pcm;
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}

/// <summary>
/// Push-to-talk for the copilot: hold a key (or the mic button), speak, let go — the words become a copilot request,
/// which still waits for the operator's approval like any other. Windows dictation, offline: no audio leaves the PC.
/// </summary>
public sealed class PushToTalk : IDisposable
{
    private readonly object _lock = new();
    private readonly List<string> _phrases = new();
    private ISpeechTranscriber? _transcriber;
    private readonly Func<ISpeechTranscriber> _make;

    public PushToTalk(Func<ISpeechTranscriber> make) => _make = make;

    public bool Listening => _transcriber is not null;

    /// <summary>Heard so far (for a live caption while the key is held).</summary>
    public string Heard { get { lock (_lock) return string.Join(" ", _phrases); } }

    /// <summary>Start listening. Null = listening, else why not.</summary>
    public string? Begin()
    {
        if (_transcriber is not null) return null;
        lock (_lock) _phrases.Clear();
        var t = _make();
        t.PhraseRecognized += (text, _) => { lock (_lock) _phrases.Add(text.Trim()); };
        if (!t.Start()) { var why = t.Status; t.Dispose(); return why; }
        _transcriber = t;
        return null;
    }

    /// <summary>Stop listening and return what was said. Waits a moment for the last phrase to finish recognising.</summary>
    public async Task<string> EndAsync(int settleMs = 900)
    {
        var t = _transcriber;
        if (t is null) return "";
        await Task.Delay(settleMs);
        _transcriber = null;
        t.Dispose();
        return Heard.Trim();
    }

    public void Dispose()
    {
        _transcriber?.Dispose();
        _transcriber = null;
    }
}
