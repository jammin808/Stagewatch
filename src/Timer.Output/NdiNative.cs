using System.IO;
using System.Runtime.InteropServices;

namespace Stagewatch.Output;

/// <summary>
/// Minimal binding to the NDI® runtime (Processing.NDI.Lib.x64.dll), loaded at run time.
/// Nothing NDI ships inside Stagewatch: the runtime comes from the free NDI Tools / NDI Runtime
/// installer, which also sets NDI_RUNTIME_DIR_V6 (or _V5). If it isn't installed, NDI output
/// simply reports itself unavailable — the rest of the app is unaffected.
///
/// NDI® is a registered trademark of Vizrt NDI AB.
/// </summary>
internal static class NdiNative
{
    public const string RuntimeDownloadUrl = "https://ndi.video/tools/";
    private const string DllName = "Processing.NDI.Lib.x64.dll";

    // FourCC 'BGRA' — matches WPF's Pbgra32 byte order. Our frames are opaque, so pre-multiplication is moot.
    public const int FourCC_BGRA = 'B' | ('G' << 8) | ('R' << 16) | ('A' << 24);
    public const int FrameFormatProgressive = 1;
    public const long TimecodeSynthesize = long.MaxValue;

    [StructLayout(LayoutKind.Sequential)]
    public struct SendCreate
    {
        public IntPtr NdiName;    // const char* (UTF-8)
        public IntPtr Groups;     // const char* or null
        [MarshalAs(UnmanagedType.U1)] public bool ClockVideo;
        [MarshalAs(UnmanagedType.U1)] public bool ClockAudio;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VideoFrameV2
    {
        public int XRes;
        public int YRes;
        public int FourCC;
        public int FrameRateN;
        public int FrameRateD;
        public float PictureAspectRatio;
        public int FrameFormatType;
        public long Timecode;
        public IntPtr Data;
        public int LineStrideInBytes;
        public IntPtr Metadata;
        public long Timestamp;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    public delegate bool InitializeFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr SendCreateFn(ref SendCreate settings);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void SendDestroyFn(IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void SendVideoV2Fn(IntPtr instance, ref VideoFrameV2 frame);

    /// <summary>Async send: returns at once, NDI compresses on its own thread. The buffer must stay valid until the next send.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void SendVideoAsyncV2Fn(IntPtr instance, ref VideoFrameV2 frame);

    /// <summary>The same export called with NULL: waits until NDI has finished with the last async frame.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void SendVideoAsyncFlushFn(IntPtr instance, IntPtr nullFrame);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int SendGetNoConnectionsFn(IntPtr instance, uint timeoutMs);

    public static InitializeFn? Initialize { get; private set; }
    public static SendCreateFn? SendCreateInstance { get; private set; }
    public static SendDestroyFn? SendDestroy { get; private set; }
    public static SendVideoV2Fn? SendVideo { get; private set; }
    public static SendVideoAsyncV2Fn? SendVideoAsync { get; private set; }
    public static SendVideoAsyncFlushFn? SendVideoAsyncFlush { get; private set; }
    public static SendGetNoConnectionsFn? SendGetNoConnections { get; private set; }

    /// <summary>Where the runtime was loaded from, or null when unavailable.</summary>
    public static string? LoadedFrom { get; private set; }

    private static readonly object Gate = new();
    private static bool _attempted;
    private static bool _ok;

    /// <summary>Load the runtime once. Optional <paramref name="explicitPath"/> wins (tests, custom installs).</summary>
    public static bool TryLoad(string? explicitPath = null)
    {
        lock (Gate)
        {
            if (_attempted && explicitPath is null) return _ok;
            _attempted = true;
            foreach (var candidate in Candidates(explicitPath))
            {
                try
                {
                    if (!NativeLibrary.TryLoad(candidate, out var lib)) continue;
                    Initialize = Get<InitializeFn>(lib, "NDIlib_initialize");
                    SendCreateInstance = Get<SendCreateFn>(lib, "NDIlib_send_create");
                    SendDestroy = Get<SendDestroyFn>(lib, "NDIlib_send_destroy");
                    SendVideo = Get<SendVideoV2Fn>(lib, "NDIlib_send_send_video_v2");
                    // Async send (every NDI 4+ runtime has it); without it we fall back to the synchronous call.
                    if (NativeLibrary.TryGetExport(lib, "NDIlib_send_send_video_async_v2", out var asyncPtr))
                    {
                        SendVideoAsync = Marshal.GetDelegateForFunctionPointer<SendVideoAsyncV2Fn>(asyncPtr);
                        SendVideoAsyncFlush = Marshal.GetDelegateForFunctionPointer<SendVideoAsyncFlushFn>(asyncPtr);
                    }
                    SendGetNoConnections = Get<SendGetNoConnectionsFn>(lib, "NDIlib_send_get_no_connections");
                    // initialize() is false on CPUs NDI doesn't support (no SSE4.2).
                    if (!Initialize()) continue;
                    LoadedFrom = candidate;
                    return _ok = true;
                }
                catch (EntryPointNotFoundException) { }
                catch (BadImageFormatException) { }
            }
            return _ok = false;
        }
    }

    private static IEnumerable<string> Candidates(string? explicitPath)
    {
        if (!string.IsNullOrEmpty(explicitPath)) yield return explicitPath;
        foreach (var v in new[] { "NDI_RUNTIME_DIR_V6", "NDI_RUNTIME_DIR_V5", "NDI_RUNTIME_DIR_V4" })
            if (Environment.GetEnvironmentVariable(v) is { Length: > 0 } dir)
                yield return Path.Combine(dir, DllName);
        yield return Path.Combine(AppContext.BaseDirectory, DllName);
        yield return DllName; // normal DLL search path
    }

    private static T Get<T>(IntPtr lib, string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));
}
