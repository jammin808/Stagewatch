using System.Buffers.Binary;
using System.Text.Json.Serialization;

namespace Stagewatch.Core;

/// <summary>
/// Minimal WAV codec for chimes: reads PCM WAV (8/16/24/32-bit integer or 32-bit float, mono or multi-channel, any rate)
/// into 16-bit mono, and writes 16-bit mono WAV. Untrusted input: every size is checked; anything odd is refused.
/// </summary>
public static class Wav
{
    public const int MaxSeconds = 15;

    public sealed record Sound(short[] Samples, int SampleRate)
    {
        public double Seconds => SampleRate > 0 ? (double)Samples.Length / SampleRate : 0;
    }

    /// <summary>Decode, or null with a reason in <paramref name="error"/>.</summary>
    public static Sound? Decode(ReadOnlySpan<byte> data, out string error)
    {
        error = "";
        if (data.Length < 44 || !data[..4].SequenceEqual("RIFF"u8) || !data[8..12].SequenceEqual("WAVE"u8)) { error = "not a WAV file"; return null; }
        int pos = 12, format = 0, channels = 0, rate = 0, bits = 0;
        ReadOnlySpan<byte> pcm = default;
        while (pos + 8 <= data.Length)
        {
            var id = data.Slice(pos, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos + 4, 4));
            if (size < 0 || pos + 8 + (long)size > data.Length) size = data.Length - pos - 8;   // tolerate a short last chunk
            var body = data.Slice(pos + 8, size);
            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(body);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
                if (format == 0xFFFE && size >= 26) format = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]);   // WAVE_FORMAT_EXTENSIBLE
            }
            else if (id.SequenceEqual("data"u8)) pcm = body;
            pos += 8 + size + (size & 1);
        }
        if (format is not (1 or 3) || channels is < 1 or > 8 || rate is < 8000 or > 192000 || pcm.IsEmpty) { error = "only PCM WAV is supported (save it as WAV, 16-bit)"; return null; }
        var bytesPer = bits / 8;
        if (bits is not (8 or 16 or 24 or 32) || (format == 3 && bits != 32)) { error = "unsupported sample format"; return null; }
        var frames = pcm.Length / (bytesPer * channels);
        if (frames > rate * MaxSeconds) { error = $"too long (chimes are at most {MaxSeconds} seconds)"; return null; }
        var outSamples = new short[frames];
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            for (int c = 0; c < channels; c++)
            {
                var s = pcm.Slice((f * channels + c) * bytesPer, bytesPer);
                sum += bits switch
                {
                    8 => (s[0] - 128) / 128.0,
                    16 => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768.0,
                    24 => ((s[0] | (s[1] << 8) | (s[2] << 16)) << 8 >> 8) / 8388608.0,
                    _ => format == 3 ? BinaryPrimitives.ReadSingleLittleEndian(s) : BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648.0,
                };
            }
            var v = Math.Clamp(sum / channels, -1, 1);
            outSamples[f] = (short)Math.Round(v * 32767);
        }
        return new Sound(outSamples, rate);
    }

    public static byte[] Encode(Sound s)
    {
        var data = new byte[44 + s.Samples.Length * 2];
        var span = data.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], data.Length - 8);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], s.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], s.SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], s.Samples.Length * 2);
        for (int i = 0; i < s.Samples.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(span[(44 + i * 2)..], s.Samples[i]);
        return data;
    }

    public static byte[] PcmBytes(Sound s)
    {
        var bytes = new byte[s.Samples.Length * 2];
        Buffer.BlockCopy(s.Samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}

/// <summary>A sound in the show's chime library (stored as 16-bit mono WAV beside the settings).</summary>
public sealed class SoundEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("seconds")] public double Seconds { get; set; }
}

/// <summary>
/// Which chime plays at each moment of an item. Each is "" (the show's usual alert), "off", a built-in
/// ("builtin:warning", "builtin:wrapup", "builtin:zero", "builtin:start") or a library sound ("sound:&lt;id&gt;").
/// </summary>
public sealed class ItemChimes
{
    [JsonPropertyName("start")] public string Start { get; set; } = "";
    [JsonPropertyName("warning")] public string Warning { get; set; } = "";
    [JsonPropertyName("wrapUp")] public string WrapUp { get; set; } = "";
    [JsonPropertyName("zero")] public string Zero { get; set; } = "";

    [JsonIgnore] public bool IsDefault => Start.Length == 0 && Warning.Length == 0 && WrapUp.Length == 0 && Zero.Length == 0;

    public ItemChimes Clone() => new() { Start = Start, Warning = Warning, WrapUp = WrapUp, Zero = Zero };

    public static bool ValidSpec(string? s)
        => string.IsNullOrEmpty(s) || s is "off" or "builtin:warning" or "builtin:wrapup" or "builtin:zero" or "builtin:start"
           || (s.StartsWith("sound:", StringComparison.Ordinal) && s.Length is > 6 and <= 40 && s[6..].All(char.IsAsciiLetterOrDigit));
}
