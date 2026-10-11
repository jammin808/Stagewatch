using System.Runtime.InteropServices;

namespace Stagewatch.Integrations;

/// <summary>
/// Minimal winmm waveOut: device list by name, fire-and-forget PCM playback, and a continuous
/// stream fed by a callback (for LTC). No NAudio dependency, nothing to install.
/// </summary>
public static class WavePlayer
{
    public static IReadOnlyList<string> Devices()
    {
        var names = new List<string>();
        try
        {
            var n = WinMM.waveOutGetNumDevs();
            for (uint i = 0; i < n; i++)
            {
                var caps = new WinMM.WAVEOUTCAPS();
                if (WinMM.waveOutGetDevCapsW((UIntPtr)i, ref caps, (uint)Marshal.SizeOf<WinMM.WAVEOUTCAPS>()) == 0) names.Add(caps.szPname);
            }
        }
        catch (DllNotFoundException) { }
        return names;
    }

    /// <summary>"" = Windows default. Null when a named device isn't present (unplugged): play nothing rather than the wrong output.</summary>
    public static uint? DeviceId(string name)
    {
        if (string.IsNullOrEmpty(name)) return WinMM.WAVE_MAPPER;
        var idx = Devices().ToList().IndexOf(name);
        return idx < 0 ? null : (uint)idx;
    }

    /// <summary>Play 16-bit mono PCM once, on a worker. Returns false when the device isn't there.</summary>
    public static bool PlayOnce(byte[] pcm, int sampleRate, string device)
    {
        if (DeviceId(device) is not { } id) return false;
        ThreadPool.QueueUserWorkItem(_ => PlayBlocking(id, pcm, sampleRate));
        return true;
    }

    private static void PlayBlocking(uint deviceId, byte[] data, int sampleRate)
    {
        var format = WinMM.Mono16(sampleRate);
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
                var deadline = Environment.TickCount64 + data.Length * 1000L / (sampleRate * 2) + 2000;
                while (Environment.TickCount64 < deadline && (Marshal.PtrToStructure<WinMM.WAVEHDR>(header).dwFlags & WinMM.WHDR_DONE) == 0)
                    #pragma warning disable RS0030 // the audio worker thread waits for the device to finish a one-shot sound; never the show thread
                    Thread.Sleep(20);
                    #pragma warning restore RS0030
            }
            WinMM.waveOutReset(wave);
            WinMM.waveOutUnprepareHeader(wave, header, headerSize);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        finally
        {
            if (wave != IntPtr.Zero) WinMM.waveOutClose(wave);
            if (header != IntPtr.Zero) Marshal.FreeHGlobal(header);
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    internal static class WinMM
    {
        public const uint WAVE_MAPPER = 0xFFFFFFFF;
        public const uint WHDR_DONE = 0x00000001;

        public static WAVEFORMATEX Mono16(int rate) => new()
        {
            wFormatTag = 1, nChannels = 1, nSamplesPerSec = rate, nAvgBytesPerSec = rate * 2, nBlockAlign = 2, wBitsPerSample = 16
        };

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

/// <summary>
/// A continuous waveOut stream: N buffers in rotation on a dedicated high-priority thread, each
/// refilled by a callback as soon as the card has played it. The audio equivalent of a game's
/// swap chain — the producer never blocks the device and the device never starves.
/// </summary>
public sealed class WaveStream : IDisposable
{
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();
    private readonly Action<short[]> _fill;
    private readonly uint _deviceId;
    private readonly int _rate;
    private readonly int _samplesPerBuffer;
    private readonly int _buffers;

    public WaveStream(uint deviceId, int sampleRate, int samplesPerBuffer, int buffers, Action<short[]> fill)
    {
        _deviceId = deviceId;
        _rate = sampleRate;
        _samplesPerBuffer = samplesPerBuffer;
        _buffers = buffers;
        _fill = fill;
        _thread = new Thread(Run) { IsBackground = true, Name = "Stagewatch audio stream", Priority = ThreadPriority.AboveNormal };
    }

    public string Status { get; private set; } = "starting";
    public bool Running { get; private set; }

    /// <summary>Audio already queued ahead of the speaker (what a timecode generator must add to "now").</summary>
    public TimeSpan Latency => TimeSpan.FromSeconds((double)_samplesPerBuffer * (_buffers - 1) / _rate);

    public void Start() => _thread.Start();

    private void Run()
    {
        var format = WavePlayer.WinMM.Mono16(_rate);
        var headerSize = (uint)Marshal.SizeOf<WavePlayer.WinMM.WAVEHDR>();
        var headers = new IntPtr[_buffers];
        var datas = new IntPtr[_buffers];
        var samples = new short[_samplesPerBuffer];
        IntPtr wave = IntPtr.Zero;
        try
        {
            if (WavePlayer.WinMM.waveOutOpen(out wave, _deviceId, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
            {
                Status = "couldn't open the audio device";
                return;
            }
            for (int i = 0; i < _buffers; i++)
            {
                datas[i] = Marshal.AllocHGlobal(_samplesPerBuffer * 2);
                headers[i] = Marshal.AllocHGlobal((int)headerSize);
                Marshal.StructureToPtr(new WavePlayer.WinMM.WAVEHDR { lpData = datas[i], dwBufferLength = (uint)(_samplesPerBuffer * 2) }, headers[i], false);
                WavePlayer.WinMM.waveOutPrepareHeader(wave, headers[i], headerSize);
                Submit(i);
            }
            Running = true;
            Status = "running";
            var next = 0;
            while (!_cts.IsCancellationRequested)
            {
                var h = Marshal.PtrToStructure<WavePlayer.WinMM.WAVEHDR>(headers[next]);
                #pragma warning disable RS0030 // the audio worker thread polls the device buffer it owns; never the show thread
                if ((h.dwFlags & WavePlayer.WinMM.WHDR_DONE) == 0) { Thread.Sleep(2); continue; }
                #pragma warning restore RS0030
                Submit(next);
                next = (next + 1) % _buffers;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { Status = "no audio"; }
        finally
        {
            Running = false;
            if (wave != IntPtr.Zero)
            {
                WavePlayer.WinMM.waveOutReset(wave);
                for (int i = 0; i < _buffers; i++)
                    if (headers[i] != IntPtr.Zero) WavePlayer.WinMM.waveOutUnprepareHeader(wave, headers[i], headerSize);
                WavePlayer.WinMM.waveOutClose(wave);
            }
            foreach (var p in headers) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
            foreach (var p in datas) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
        }

        void Submit(int i)
        {
            try { _fill(samples); }
            catch (Exception) { Array.Clear(samples); }
            Marshal.Copy(samples, 0, datas[i], samples.Length);
            var hdr = Marshal.PtrToStructure<WavePlayer.WinMM.WAVEHDR>(headers[i]);
            hdr.dwFlags &= ~WavePlayer.WinMM.WHDR_DONE;
            Marshal.StructureToPtr(hdr, headers[i], false);
            WavePlayer.WinMM.waveOutWrite(wave, headers[i], headerSize);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(2));
    }
}
