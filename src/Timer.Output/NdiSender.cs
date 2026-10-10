using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;

namespace Stagewatch.Output;

/// <summary>
/// Sends the output window to NDI® as a 1920×1080 (by default) BGRA video source.
///
/// Each frame the source visual is drawn — scaled to fill the frame, letterboxed, whatever the
/// window's size — into a reused RenderTargetBitmap and copied into a pinned buffer that is
/// handed to the NDI runtime. Sending is synchronous and un-clocked: pacing comes from our own
/// timer, so the UI thread is never blocked waiting for NDI's clock. When nobody is receiving,
/// rendering is skipped entirely (checked twice a second), so an idle NDI source costs ~nothing.
///
/// Requires the free NDI runtime (NDI Tools); see <see cref="NdiNative"/>.
/// </summary>
public sealed class NdiSender : IDisposable
{
    private readonly FrameworkElement _source;
    private readonly string _name;
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private RenderTargetBitmap? _bmp;
    private DrawingVisual? _canvas;
    // Two pinned buffers, alternated: NDI reads one (async, on its own thread) while we fill the other.
    private readonly byte[]?[] _buffers = new byte[]?[2];
    private readonly GCHandle[] _pins = new GCHandle[2];
    private int _next;
    private IntPtr _nameUtf8;
    private IntPtr _instance;
    private System.Windows.Threading.DispatcherTimer? _tickTimer;
    private bool _running;
    private int _tick;
    private bool _hasReceivers = true;

    public NdiSender(FrameworkElement source, string name, int width = 1920, int height = 1080, int fps = 30, string? runtimePath = null)
    {
        _source = source;
        _name = string.IsNullOrWhiteSpace(name) ? "Stagewatch" : name.Trim();
        _width = width;
        _height = height;
        _fps = Math.Clamp(fps, 1, 60);
        IsAvailable = NdiNative.TryLoad(runtimePath);
    }

    /// <summary>False when the NDI runtime isn't installed (or the CPU isn't supported).</summary>
    public bool IsAvailable { get; }

    /// <summary>The source name NDI receivers will see (NDI prefixes it with the PC name).</summary>
    public string SourceName => _name;

    public int FramesSent { get; private set; }

    public void Start()
    {
        if (_running || !IsAvailable) return;

        _nameUtf8 = Marshal.StringToCoTaskMemUTF8(_name);
        var settings = new NdiNative.SendCreate { NdiName = _nameUtf8, Groups = IntPtr.Zero, ClockVideo = false, ClockAudio = false };
        _instance = NdiNative.SendCreateInstance!(ref settings);
        if (_instance == IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(_nameUtf8);
            _nameUtf8 = IntPtr.Zero;
            throw new InvalidOperationException("The NDI runtime refused to create a sender.");
        }

        _bmp = new RenderTargetBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32);
        _canvas = new DrawingVisual();
        for (var i = 0; i < 2; i++)
        {
            _buffers[i] = new byte[_width * _height * 4];
            _pins[i] = GCHandle.Alloc(_buffers[i], GCHandleType.Pinned);
        }
        _running = true;

        _tickTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromSeconds(1.0 / _fps)
        };
        _tickTimer.Tick += OnTick;
        _tickTimer.Start();
    }

    public void Stop()
    {
        _tickTimer?.Stop();
        _tickTimer = null;
        _running = false;

        if (_instance != IntPtr.Zero)
        {
            // Let NDI finish with the last async frame before its buffer is released.
            try { NdiNative.SendVideoAsyncFlush?.Invoke(_instance, IntPtr.Zero); } catch { }
            try { NdiNative.SendDestroy!(_instance); } catch { }
            _instance = IntPtr.Zero;
        }
        for (var i = 0; i < 2; i++)
        {
            if (_pins[i].IsAllocated) _pins[i].Free();
            _buffers[i] = null;
        }
        if (_nameUtf8 != IntPtr.Zero) { Marshal.FreeCoTaskMem(_nameUtf8); _nameUtf8 = IntPtr.Zero; }
        _bmp = null;
        _canvas = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_running || _bmp is null || _canvas is null || _buffers[_next] is not { } buffer) return;

        // Skip all rendering while nobody is watching; re-check twice a second.
        if (_tick++ % Math.Max(1, _fps / 2) == 0)
        {
            try { _hasReceivers = NdiNative.SendGetNoConnections!(_instance, 0) > 0; }
            catch { _hasReceivers = true; }
        }
        if (!_hasReceivers) return;

        try
        {
            // Scale the whole output into the frame, keeping its aspect ratio.
            var w = Math.Max(1, _source.ActualWidth);
            var h = Math.Max(1, _source.ActualHeight);
            var scale = Math.Min(_width / w, _height / h);
            var rect = new Rect((_width - w * scale) / 2, (_height - h * scale) / 2, w * scale, h * scale);
            // Fill with the output's own background (black, or the chroma-key colour on keyed outputs),
            // then map the element's exact layout rectangle — not its drawn bounds — into the frame.
            var background = Window.GetWindow(_source)?.Background ?? Brushes.Black;
            using (var dc = _canvas.RenderOpen())
            {
                dc.DrawRectangle(background, null, new Rect(0, 0, _width, _height));
                dc.DrawRectangle(new VisualBrush(_source)
                {
                    Stretch = Stretch.Fill,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, w, h)
                }, null, rect);
            }
            _bmp.Clear();
            _bmp.Render(_canvas);

            var stride = _width * 4;
            _bmp.CopyPixels(buffer, stride, 0);

            var frame = new NdiNative.VideoFrameV2
            {
                XRes = _width,
                YRes = _height,
                FourCC = NdiNative.FourCC_BGRA,
                FrameRateN = _fps * 1000,
                FrameRateD = 1000,
                PictureAspectRatio = (float)_width / _height,
                FrameFormatType = NdiNative.FrameFormatProgressive,
                Timecode = NdiNative.TimecodeSynthesize,
                Data = _pins[_next].AddrOfPinnedObject(),
                LineStrideInBytes = stride,
                Metadata = IntPtr.Zero,
                Timestamp = 0
            };
            // Async: compression and sending happen on NDI's thread, not the UI thread. The previous async frame
            // is guaranteed done when this call returns, so the buffer we fill next time is free.
            if (NdiNative.SendVideoAsync is { } sendAsync)
            {
                sendAsync(_instance, ref frame);
                _next ^= 1;
            }
            else NdiNative.SendVideo!(_instance, ref frame);
            FramesSent++;
        }
        catch (Exception)
        {
            // A failed frame must never take the output (or the show) down; try again next tick.
        }
    }

    public void Dispose() => Stop();
}
