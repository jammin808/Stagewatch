using System.Runtime.InteropServices;

namespace Stagewatch.App;

public enum AlertSound { Warning, WrapUp, Zero, Start }

/// <summary>
/// Operator cue tones, synthesized once at startup — no asset files to lose, nothing to decode at
/// show time — and played on a chosen audio device. At events Windows' default device is often the
/// mixing desk, so the operator can send cues to the laptop speaker or headphones instead. If the
/// chosen device has gone (unplugged), the cue is skipped rather than falling back to the default:
/// a beep in the PA is worse than no beep. Playback is fire-and-forget on the thread pool and a
/// missing/failed audio stack is ignored: a sound must never stop a show.
/// </summary>
public sealed class AlertSounds : IDisposable
{
    private const int SampleRate = 44100;
    private readonly Dictionary<AlertSound, byte[]> _pcm = new();

    public AlertSounds()
    {
        // Warning: two soft high beeps. Wrap-up: three quicker, higher beeps. Zero: a long two-note chime.
        _pcm[AlertSound.Warning] = ToBytes(Beeps(880, count: 2, beepMs: 140, gapMs: 90, volume: 0.30));
        _pcm[AlertSound.WrapUp]  = ToBytes(Beeps(1046, count: 3, beepMs: 110, gapMs: 70, volume: 0.32));
        _pcm[AlertSound.Zero]    = ToBytes(Chime());
        // Start: a short rising pair — "we're on".
        _pcm[AlertSound.Start]   = ToBytes(Beeps(660, count: 1, beepMs: 120, gapMs: 30, volume: 0.28).Concat(Beeps(990, count: 1, beepMs: 160, gapMs: 0, volume: 0.28)).ToArray());
    }

    /// <summary>Output devices Windows offers, by name ("" = Windows default is not listed).</summary>
    public static IReadOnlyList<string> ListDevices()
    {
        var names = new List<string>();
        try
        {
            var n = WinMM.waveOutGetNumDevs();
            for (uint i = 0; i < n; i++)
            {
                var caps = new WinMM.WAVEOUTCAPS();
                if (WinMM.waveOutGetDevCapsW((UIntPtr)i, ref caps, (uint)Marshal.SizeOf<WinMM.WAVEOUTCAPS>()) == 0)
                    names.Add(caps.szPname);
            }
        }
        catch (DllNotFoundException) { }
        return names;
    }

    /// <summary>
    /// Play a cue. <paramref name="deviceName"/> "" = Windows default. Returns false if the named
    /// device isn't present (nothing is played).
    /// </summary>
    public bool Play(AlertSound sound, string deviceName)
    {
        uint deviceId = WinMM.WAVE_MAPPER;
        if (!string.IsNullOrEmpty(deviceName))
        {
            var idx = ListDevices().ToList().IndexOf(deviceName);
            if (idx < 0) return false;
            deviceId = (uint)idx;
        }
        var data = _pcm[sound];
        ThreadPool.QueueUserWorkItem(_ => PlayBlocking(deviceId, data));
        return true;
    }

    private static void PlayBlocking(uint deviceId, byte[] data)
    {
        var format = new WinMM.WAVEFORMATEX
        {
            wFormatTag = 1, nChannels = 1, nSamplesPerSec = SampleRate,
            nAvgBytesPerSec = SampleRate * 2, nBlockAlign = 2, wBitsPerSample = 16, cbSize = 0
        };
        IntPtr wave = IntPtr.Zero, header = IntPtr.Zero, buffer = IntPtr.Zero;
        var headerSize = (uint)Marshal.SizeOf<WinMM.WAVEHDR>();
        try
        {
            if (WinMM.waveOutOpen(out wave, deviceId, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0) return;
            buffer = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, buffer, data.Length);
            header = Marshal.AllocHGlobal((int)headerSize);
            Marshal.StructureToPtr(new WinMM.WAVEHDR { lpData = buffer, dwBufferLength = (uint)data.Length }, header, false);
            if (WinMM.waveOutPrepareHeader(wave, header, headerSize) != 0) return;
            if (WinMM.waveOutWrite(wave, header, headerSize) == 0)
            {
                var deadline = Environment.TickCount64 + data.Length * 1000L / (SampleRate * 2) + 2000;
                while (Environment.TickCount64 < deadline &&
                       (Marshal.PtrToStructure<WinMM.WAVEHDR>(header).dwFlags & WinMM.WHDR_DONE) == 0)
                    Thread.Sleep(20);
            }
            WinMM.waveOutReset(wave);
            WinMM.waveOutUnprepareHeader(wave, header, headerSize);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        finally
        {
            if (wave != IntPtr.Zero) WinMM.waveOutClose(wave);
            if (header != IntPtr.Zero) Marshal.FreeHGlobal(header);
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose() => _pcm.Clear();

    private static byte[] ToBytes(short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static short[] Beeps(double freq, int count, int beepMs, int gapMs, double volume)
    {
        var beep = SampleRate * beepMs / 1000;
        var gap = SampleRate * gapMs / 1000;
        var data = new short[count * (beep + gap)];
        for (int b = 0; b < count; b++)
        {
            var offset = b * (beep + gap);
            for (int i = 0; i < beep; i++)
            {
                var t = (double)i / SampleRate;
                data[offset + i] = (short)(Math.Sin(2 * Math.PI * freq * t) * Envelope(i, beep, 0.006) * volume * short.MaxValue);
            }
        }
        return data;
    }

    private static short[] Chime()
    {
        // Two notes (E5 then C5 + octave shimmer) with an exponential decay — reads as "time's up", not "error".
        var noteLen = SampleRate * 650 / 1000;
        var data = new short[noteLen * 2];
        void Note(int offset, double f)
        {
            for (int i = 0; i < noteLen; i++)
            {
                var t = (double)i / SampleRate;
                var decay = Math.Exp(-3.2 * t);
                var v = (Math.Sin(2 * Math.PI * f * t) + 0.35 * Math.Sin(2 * Math.PI * f * 2 * t)) / 1.35;
                data[offset + i] = (short)(v * decay * Envelope(i, noteLen, 0.004) * 0.40 * short.MaxValue);
            }
        }
        Note(0, 659.25);
        Note(noteLen, 523.25);
        return data;
    }

    /// <summary>Short linear attack/release so tones start and stop without clicks.</summary>
    private static double Envelope(int i, int length, double rampSeconds)
    {
        var ramp = Math.Max(1, (int)(SampleRate * rampSeconds));
        if (i < ramp) return (double)i / ramp;
        if (i > length - ramp) return Math.Max(0, (double)(length - i) / ramp);
        return 1.0;
    }

    /// <summary>Just enough of winmm's waveOut API to play one PCM buffer on a chosen device.</summary>
    private static class WinMM
    {
        public const uint WAVE_MAPPER = 0xFFFFFFFF;
        public const uint WHDR_DONE = 0x00000001;

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        public struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public int nSamplesPerSec;
            public int nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WAVEOUTCAPS
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwFormats;
            public ushort wChannels;
            public ushort wReserved1;
            public uint dwSupport;
        }

        [DllImport("winmm.dll")] public static extern uint waveOutGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] public static extern int waveOutGetDevCapsW(UIntPtr uDeviceID, ref WAVEOUTCAPS pwoc, uint cbwoc);
        [DllImport("winmm.dll")] public static extern int waveOutOpen(out IntPtr phwo, uint uDeviceID, ref WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);
        [DllImport("winmm.dll")] public static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll")] public static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll")] public static extern int waveOutWrite(IntPtr hwo, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll")] public static extern int waveOutReset(IntPtr hwo);
        [DllImport("winmm.dll")] public static extern int waveOutClose(IntPtr hwo);
    }
}
