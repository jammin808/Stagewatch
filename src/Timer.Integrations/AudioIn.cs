using System.Runtime.InteropServices;

namespace Stagewatch.Integrations;

/// <summary>Audio inputs (winmm), for LTC in.</summary>
public static class WaveRecorder
{
    public static IReadOnlyList<string> Devices()
    {
        var list = new List<string>();
        try
        {
            var n = WinIn.waveInGetNumDevs();
            for (uint i = 0; i < n; i++)
            {
                var caps = new WinIn.WAVEINCAPS();
                if (WinIn.waveInGetDevCapsW((UIntPtr)i, ref caps, (uint)Marshal.SizeOf<WinIn.WAVEINCAPS>()) == 0) list.Add(caps.szPname);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        return list;
    }

    /// <summary>The device id for a name ("" = the Windows default input); null when it isn't connected.</summary>
    public static uint? DeviceId(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return WinIn.WAVE_MAPPER;
        var devices = Devices();
        for (int i = 0; i < devices.Count; i++) if (devices[i] == name) return (uint)i;
        return null;
    }

    internal static class WinIn
    {
        public const uint WAVE_MAPPER = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WAVEINCAPS
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwFormats;
            public ushort wChannels;
            public ushort wReserved1;
        }

        [DllImport("winmm.dll")] public static extern uint waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] public static extern int waveInGetDevCapsW(UIntPtr uDeviceID, ref WAVEINCAPS pwic, uint cbwic);
        [DllImport("winmm.dll")] public static extern int waveInOpen(out IntPtr phwi, uint uDeviceID, ref WavePlayer.WinMM.WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);
        [DllImport("winmm.dll")] public static extern int waveInPrepareHeader(IntPtr hwi, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll")] public static extern int waveInUnprepareHeader(IntPtr hwi, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll")] public static extern int waveInAddBuffer(IntPtr hwi, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll")] public static extern int waveInStart(IntPtr hwi);
        [DllImport("winmm.dll")] public static extern int waveInReset(IntPtr hwi);
        [DllImport("winmm.dll")] public static extern int waveInClose(IntPtr hwi);
    }
}

/// <summary>
/// Continuous capture from an audio input on its own thread: a ring of small buffers (10 ms each), handed to
/// <c>onSamples</c> as they fill. The callback runs on the capture thread and must be quick.
/// </summary>
public sealed class WaveCapture : IDisposable
{
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();
    private readonly uint _deviceId;
    private readonly int _rate;
    private readonly int _samplesPerBuffer;
    private readonly int _buffers;
    private readonly Action<ReadOnlySpan<short>> _onSamples;

    public WaveCapture(uint deviceId, int sampleRate, Action<ReadOnlySpan<short>> onSamples, int samplesPerBuffer = 480, int buffers = 16)
    {
        _deviceId = deviceId;
        _rate = sampleRate;
        _samplesPerBuffer = samplesPerBuffer;
        _buffers = buffers;
        _onSamples = onSamples;
        _thread = new Thread(Run) { IsBackground = true, Name = "Stagewatch audio capture", Priority = ThreadPriority.AboveNormal };
    }

    public string Status { get; private set; } = "starting";
    public bool Running { get; private set; }

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
            if (WaveRecorder.WinIn.waveInOpen(out wave, _deviceId, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
            {
                Status = "couldn't open the audio input";
                return;
            }
            for (int i = 0; i < _buffers; i++)
            {
                datas[i] = Marshal.AllocHGlobal(_samplesPerBuffer * 2);
                headers[i] = Marshal.AllocHGlobal((int)headerSize);
                Marshal.StructureToPtr(new WavePlayer.WinMM.WAVEHDR { lpData = datas[i], dwBufferLength = (uint)(_samplesPerBuffer * 2) }, headers[i], false);
                WaveRecorder.WinIn.waveInPrepareHeader(wave, headers[i], headerSize);
                WaveRecorder.WinIn.waveInAddBuffer(wave, headers[i], headerSize);
            }
            WaveRecorder.WinIn.waveInStart(wave);
            Running = true;
            Status = "listening";
            var next = 0;
            while (!_cts.IsCancellationRequested)
            {
                var h = Marshal.PtrToStructure<WavePlayer.WinMM.WAVEHDR>(headers[next]);
                #pragma warning disable RS0030 // the capture worker thread polls the device buffer it owns; never the show thread
                if ((h.dwFlags & WavePlayer.WinMM.WHDR_DONE) == 0) { Thread.Sleep(2); continue; }
                #pragma warning restore RS0030
                var count = (int)Math.Min(h.dwBytesRecorded / 2, (uint)_samplesPerBuffer);
                if (count > 0)
                {
                    Marshal.Copy(datas[next], samples, 0, count);
                    try { _onSamples(samples.AsSpan(0, count)); } catch (Exception) { /* a bad buffer must not stop capture */ }
                }
                h.dwFlags &= ~WavePlayer.WinMM.WHDR_DONE;
                h.dwBytesRecorded = 0;
                Marshal.StructureToPtr(h, headers[next], false);
                WaveRecorder.WinIn.waveInAddBuffer(wave, headers[next], headerSize);
                next = (next + 1) % _buffers;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { Status = "no audio input"; }
        finally
        {
            Running = false;
            if (wave != IntPtr.Zero)
            {
                WaveRecorder.WinIn.waveInReset(wave);
                for (int i = 0; i < _buffers; i++)
                    if (headers[i] != IntPtr.Zero) WaveRecorder.WinIn.waveInUnprepareHeader(wave, headers[i], headerSize);
                WaveRecorder.WinIn.waveInClose(wave);
            }
            foreach (var p in headers) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
            foreach (var p in datas) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(2));
    }
}
