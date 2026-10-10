using System.Collections.Concurrent;
using System.IO;
using Stagewatch.Core;
using Stagewatch.Integrations;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// Chimes: the show's sound library (WAV, stored 16-bit mono beside the settings), per-item overrides for the start,
/// warning, wrap-up and zero moments, and the same chime on browser screens that ask for sound (?sound=1) — a tablet
/// by the lectern or an in-ear feed plays exactly what the operator hears.
/// </summary>
public sealed partial class MainViewModel
{
    private static string SoundsDir => Path.Combine(LocalData!, "sounds");   // a property: static fields in partial files initialise in no fixed order
    private readonly ConcurrentDictionary<string, Wav.Sound> _soundCache = new();
    private int _chimeSeq;
    private string _chimeSpec = "";

    /// <summary>Add a WAV to the library. Returns the new entry, or an error for the operator.</summary>
    public (SoundEntry? Entry, string? Error) AddSound(string path)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return (null, "The file isn't there.");
            if (info.Length > 20 * 1024 * 1024) return (null, "That file is too big for a chime.");
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (null, ex.Message); }
        if (Wav.Decode(bytes, out var error) is not { } sound) return (null, $"Can't use it: {error}.");
        var entry = new SoundEntry { Id = Guid.NewGuid().ToString("N")[..12], Name = Path.GetFileNameWithoutExtension(path), Seconds = Math.Round(sound.Seconds, 1) };
        try
        {
            Directory.CreateDirectory(SoundsDir);
            File.WriteAllBytes(Path.Combine(SoundsDir, entry.Id + ".wav"), Wav.Encode(sound));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (null, ex.Message); }
        _store.Update(s => s.Sounds.Add(entry));
        _soundCache[entry.Id] = sound;
        return (entry, null);
    }

    public void RemoveSound(string id)
    {
        _store.Update(s => s.Sounds.RemoveAll(x => x.Id == id));
        _soundCache.TryRemove(id, out _);
        try { File.Delete(Path.Combine(SoundsDir, id + ".wav")); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private Wav.Sound? LoadSound(string id)
    {
        if (_soundCache.TryGetValue(id, out var s)) return s;
        if (!id.All(char.IsAsciiLetterOrDigit)) return null;
        try
        {
            var path = Path.Combine(SoundsDir, id + ".wav");
            if (!File.Exists(path) || Wav.Decode(File.ReadAllBytes(path), out _) is not { } sound) return null;
            _soundCache[id] = sound;
            return sound;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Try a chime on the operator's device only (Settings and the editor's Test buttons).</summary>
    public void PreviewChime(string spec)
    {
        var device = Settings.Alerts.Device ?? "";
        var ok = spec switch
        {
            "builtin:warning" => _sounds.Play(AlertSound.Warning, device),
            "builtin:wrapup" => _sounds.Play(AlertSound.WrapUp, device),
            "builtin:zero" => _sounds.Play(AlertSound.Zero, device),
            "builtin:start" => _sounds.Play(AlertSound.Start, device),
            _ when spec.StartsWith("sound:", StringComparison.Ordinal) && LoadSound(spec[6..]) is { } s => WavePlayer.PlayOnce(Wav.PcmBytes(s), s.SampleRate, device),
            _ => true,
        };
        if (!ok) AudioMissing(device);
    }

    /// <summary>A library sound as WAV, for browser screens (thread-safe).</summary>
    public byte[]? SoundData(string id) => LoadSound(id) is { } s ? Wav.Encode(s) : null;

    /// <summary>Play a chime spec ("builtin:zero", "sound:abc…") on the operator's device, and tell screens that play sound.</summary>
    private void PlayChime(string spec, string device)
    {
        if (spec is "" or "off") return;
        var played = spec switch
        {
            "builtin:warning" => _sounds.Play(AlertSound.Warning, device),
            "builtin:wrapup" => _sounds.Play(AlertSound.WrapUp, device),
            "builtin:zero" => _sounds.Play(AlertSound.Zero, device),
            "builtin:start" => _sounds.Play(AlertSound.Start, device),
            _ when spec.StartsWith("sound:", StringComparison.Ordinal) && LoadSound(spec[6..]) is { } s => WavePlayer.PlayOnce(Wav.PcmBytes(s), s.SampleRate, device),
            _ => true,
        };
        if (!played) AudioMissing(device);
        _chimeSpec = spec;
        Interlocked.Increment(ref _chimeSeq);
        MarkDirty();
    }

    /// <summary>The chime for a moment of the live item: its own override, else the show's alert (when that alert is on).</summary>
    private string ChimeFor(string moment, bool showAlertOn)
    {
        var item = _rundownActive && _currentItem >= 0 && _currentItem < Settings.Rundown.Items.Count ? Settings.Rundown.Items[_currentItem] : null;
        var own = moment switch
        {
            "start" => item?.Chimes.Start,
            "warning" => item?.Chimes.Warning,
            "wrapup" => item?.Chimes.WrapUp,
            _ => item?.Chimes.Zero,
        } ?? "";
        if (own.Length > 0) return own;
        return showAlertOn ? "builtin:" + moment : "off";
    }
}
