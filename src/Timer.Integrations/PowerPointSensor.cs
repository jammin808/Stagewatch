using System.Runtime.InteropServices;

namespace Stagewatch.Integrations;

/// <summary>What PowerPoint is showing right now.</summary>
public sealed record DeckState(
    string Presentation,
    int Slide,
    int Count,
    string NextTitle,
    string Notes,
    /// <summary>Saved per-slide timings (PowerPoint "Rehearse Timings"), seconds; empty when there are none.</summary>
    double[] RehearsalBudgets,
    /// <summary>When the current slide came up (UTC), measured by us.</summary>
    DateTime SlideSinceUtc,
    /// <summary>When the slideshow started (UTC), measured by us.</summary>
    DateTime ShowSinceUtc,
    /// <summary>All speaker notes, in order: the script for speech tracking.</summary>
    string Script);

/// <summary>
/// Reads the running slideshow from PowerPoint over COM automation (late bound — works with any
/// Office version, no add-in, no Office interop assemblies). Polls at 4 Hz on its own STA thread;
/// when PowerPoint isn't running it checks every 2 s and costs nothing.
///
/// Every COM object touched in a poll is released at the end of that poll. Otherwise the wrappers
/// would keep PowerPoint alive in the background after the operator closes it.
///
/// Deck-level data (titles, notes, rehearsal timings) is read once per presentation and cached;
/// per-poll work is just "which slide is up".
/// </summary>
public sealed class PowerPointSensor : PollingSensor<DeckState>
{
    private object? _app;
    private string _deckKey = "";
    private string[] _titles = Array.Empty<string>();
    private string[] _notes = Array.Empty<string>();
    private double[] _budgets = Array.Empty<double>();
    private string _script = "";
    private int _lastSlide;
    private DateTime _slideSince;
    private DateTime _showSince;
    private readonly Func<DateTime> _utcNow;
    private readonly List<object> _touched = new();

    public PowerPointSensor(Func<DateTime>? utcNow = null)
        : base("PowerPoint", TimeSpan.FromMilliseconds(250), ApartmentState.STA)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>Remember a COM object so it is released at the end of this poll.</summary>
    private dynamic K(object o)
    {
        if (o is not null && Marshal.IsComObject(o)) _touched.Add(o);
        return o!;
    }

    private void ReleaseTouched()
    {
        for (int i = _touched.Count - 1; i >= 0; i--)
        {
            try { Marshal.ReleaseComObject(_touched[i]); } catch { }
        }
        _touched.Clear();
    }

    protected override DeckState? Poll(CancellationToken ct)
    {
        _app ??= Attach();
        if (_app is null)
        {
            Report(LinkHealth.NotFound, "PowerPoint isn't running");
            _lastSlide = 0;
            return null;
        }

        try
        {
            dynamic app = _app;
            dynamic windows = K(app.SlideShowWindows);
            if ((int)windows.Count == 0)
            {
                Report(LinkHealth.Ok, "PowerPoint open — no slideshow running");
                _lastSlide = 0;
                return null;
            }
            dynamic w = K(windows.Item(1));
            dynamic pres = K(w.Presentation);
            string full = pres.FullName;
            dynamic slides = K(pres.Slides);
            int count = slides.Count;
            int slide;
            try
            {
                dynamic view = K(w.View);
                slide = view.CurrentShowPosition;
            }
            catch (COMException) { slide = 0; }
            if (slide <= 0) { Report(LinkHealth.Ok, "slideshow paused / black screen"); return Value; }

            var key = full + "|" + count;
            if (key != _deckKey) ReadDeck(slides, count, key);

            var now = _utcNow();
            if (_lastSlide == 0) _showSince = now;
            if (slide != _lastSlide) { _lastSlide = slide; _slideSince = now; }

            Report(LinkHealth.Ok, $"slide {slide} of {count} — {System.IO.Path.GetFileName(full)}");
            return new DeckState(
                System.IO.Path.GetFileName(full), slide, count,
                slide < _titles.Length ? _titles[slide] : "",            // titles[] is 0-based, so [slide] is the next one
                slide - 1 < _notes.Length ? FirstLine(_notes[slide - 1]) : "",
                _budgets, _slideSince, _showSince, _script);
        }
        catch (COMException ex) when ((uint)ex.HResult is 0x80010001 or 0x8001010A) // RPC_E_CALL_REJECTED / SERVERCALL_RETRYLATER
        {
            return Value; // PowerPoint is busy (a dialog, saving): keep the last state, try again next poll.
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            ReleaseTouched();
            Release(); // PowerPoint closed or restarted: re-attach next time.
            Report(LinkHealth.NotFound, "lost PowerPoint: " + ex.Message);
            return null;
        }
        finally
        {
            ReleaseTouched();
        }
    }

    private void ReadDeck(dynamic slides, int count, string key)
    {
        var titles = new string[count];
        var notes = new string[count];
        var budgets = new double[count];
        var anyTimings = false;
        for (int i = 1; i <= count; i++)
        {
            dynamic s = K(slides.Item(i));
            titles[i - 1] = SafeText(() =>
            {
                dynamic shapes = K(s.Shapes);
                if ((int)shapes.HasTitle == 0) return "";
                dynamic title = K(shapes.Title);
                dynamic frame = K(title.TextFrame);
                dynamic range = K(frame.TextRange);
                return (string)range.Text;
            });
            notes[i - 1] = SafeText(() =>
            {
                dynamic page = K(s.NotesPage);
                dynamic shapes = K(page.Shapes);
                dynamic placeholders = K(shapes.Placeholders);
                dynamic body = K(placeholders.Item(2));
                dynamic frame = K(body.TextFrame);
                dynamic range = K(frame.TextRange);
                return (string)range.Text;
            });
            try
            {
                dynamic tr = K(s.SlideShowTransition);
                if ((int)tr.AdvanceOnTime != 0)
                {
                    budgets[i - 1] = (float)tr.AdvanceTime;
                    if (budgets[i - 1] > 0) anyTimings = true;
                }
            }
            catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
        }
        _titles = titles.Select(Clean).ToArray();
        _notes = notes;
        _budgets = anyTimings ? budgets : Array.Empty<double>();
        _script = string.Join("\n", notes.Where(n => !string.IsNullOrWhiteSpace(n)));
        _deckKey = key;
    }

    private static string SafeText(Func<string> read)
    {
        try { return read() ?? ""; }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or ArgumentException) { return ""; }
    }

    private static string Clean(string s) => s.Replace('\r', ' ').Replace('\v', ' ').Replace('\n', ' ').Trim();

    private static string FirstLine(string s)
    {
        var line = s.Split('\r', '\n', '\v').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return line.Length > 160 ? line[..160] + "…" : line;
    }

    private static object? Attach()
    {
        if (CLSIDFromProgID("PowerPoint.Application", out var clsid) != 0) return null;
        return GetActiveObject(ref clsid, IntPtr.Zero, out var obj) == 0 ? obj : null;
    }

    private void Release()
    {
        try { if (_app is not null) Marshal.FinalReleaseComObject(_app); } catch { }
        _app = null;
        _deckKey = "";
        _lastSlide = 0;
    }

    protected override void OnStopped()
    {
        ReleaseTouched();
        Release();
        // Wrappers created on this STA thread must be cleaned up before it exits.
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
}
