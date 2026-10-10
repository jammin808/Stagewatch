using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stagewatch.Integrations;

/// <summary>
/// Opt-in (dotnet run … -- --powerpoint): drives real PowerPoint over COM. Builds a 3-slide deck,
/// runs it as a windowed slideshow and checks the sensor follows it — and that closing PowerPoint
/// isn't blocked by Stagewatch holding references. Skipped when PowerPoint is already running, so it
/// can never touch someone's open presentations.
/// </summary>
static class PowerPointLiveTest
{
    public static async Task Run(Action<bool, string> Check)
    {
        if (Process.GetProcessesByName("POWERPNT").Length > 0) { Console.WriteLine("note: PowerPoint is open — live test skipped"); return; }
        var type = Type.GetTypeFromProgID("PowerPoint.Application");
        if (type is null) { Console.WriteLine("note: PowerPoint not installed — live test skipped"); return; }

        object app = Activator.CreateInstance(type)!;
        using var sensor = new PowerPointSensor();
        try
        {
            BuildAndRun(app);
            sensor.Start();
            DeckState? d = null;
            for (int i = 0; i < 40 && d is null; i++) { await Task.Delay(100); d = sensor.Value; }
            Check(d is { Slide: 1, Count: 3, NextTitle: "The plan" }, $"powerpoint: slide 1/3, next title ({d?.Slide}/{d?.Count} '{d?.NextTitle}')");
            Check(d is not null && d.Notes.StartsWith("Notes for slide 1") && d.Script.Contains("hello number 3"), "powerpoint: notes and script");
            Check(d is not null && d.RehearsalBudgets.SequenceEqual(new double[] { 30, 60, 90 }), $"powerpoint: rehearsal timings ({string.Join(",", d?.RehearsalBudgets ?? Array.Empty<double>())})");

            GoToSlide(app, 3);
            DeckState? d3 = null;
            for (int i = 0; i < 30 && d3?.Slide != 3; i++) { await Task.Delay(100); d3 = sensor.Value; }
            Check(d3 is { Slide: 3, NextTitle: "" } && d3.SlideSinceUtc > d!.SlideSinceUtc, $"powerpoint: follows slide changes ({d3?.Slide})");

            ExitShow(app);
            DeckState? gone = d3;
            for (int i = 0; i < 30 && gone is not null; i++) { await Task.Delay(100); gone = sensor.Value; }
            Check(gone is null, "powerpoint: slideshow ended → no deck");

            // The operator closes PowerPoint while Stagewatch is still watching: it must actually exit.
            Quit(app);
            app = null!;
            Collect();
            var exited = false;
            for (int i = 0; i < 80 && !exited; i++) { await Task.Delay(100); exited = Process.GetProcessesByName("POWERPNT").Length == 0; }
            Check(exited, "powerpoint: closing PowerPoint isn't blocked by Stagewatch's references");
        }
        catch (Exception ex)
        {
            Check(false, "powerpoint: " + ex.Message);
            if (app is not null) { try { Quit(app); } catch { } }
        }
    }

    // Each step lives in its own method so its COM wrappers die with it (Debug builds keep locals alive to
    // the end of a method) and are released explicitly.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void BuildAndRun(dynamic app)
    {
        var made = new List<object>();
        dynamic K(object o) { made.Add(o); return o; }
        try
        {
            dynamic presentations = K(app.Presentations);
            dynamic pres = K(presentations.Add(0)); // msoFalse: no window
            dynamic slides = K(pres.Slides);
            string[] titles = { "Welcome", "The plan", "Thank you" };
            for (int i = 1; i <= 3; i++)
            {
                dynamic slide = K(slides.Add(i, 2)); // ppLayoutText
                dynamic shapes = K(slide.Shapes);
                dynamic title = K(shapes.Title);
                dynamic tf = K(title.TextFrame);
                dynamic tr = K(tf.TextRange);
                tr.Text = titles[i - 1];
                dynamic page = K(slide.NotesPage);
                dynamic nshapes = K(page.Shapes);
                dynamic ph = K(nshapes.Placeholders);
                dynamic body = K(ph.Item(2));
                dynamic btf = K(body.TextFrame);
                dynamic btr = K(btf.TextRange);
                btr.Text = $"Notes for slide {i}. Say hello number {i}.";
                dynamic trans = K(slide.SlideShowTransition);
                trans.AdvanceOnTime = -1; // msoTrue
                trans.AdvanceTime = 30f * i;
            }
            dynamic settings = K(pres.SlideShowSettings);
            settings.ShowType = 2;    // ppShowTypeWindow: no full-screen takeover
            settings.AdvanceMode = 1; // manual advance
            K(settings.Run());
        }
        finally { foreach (var o in made.AsEnumerable().Reverse()) try { Marshal.ReleaseComObject(o); } catch { } }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void GoToSlide(dynamic app, int n)
    {
        dynamic wins = app.SlideShowWindows;
        dynamic w = wins.Item(1);
        dynamic view = w.View;
        view.GotoSlide(n);
        Marshal.ReleaseComObject(view); Marshal.ReleaseComObject(w); Marshal.ReleaseComObject(wins);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void ExitShow(dynamic app)
    {
        dynamic wins = app.SlideShowWindows;
        dynamic w = wins.Item(1);
        dynamic view = w.View;
        view.Exit();
        Marshal.ReleaseComObject(view); Marshal.ReleaseComObject(w); Marshal.ReleaseComObject(wins);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Quit(dynamic app)
    {
        dynamic presentations = app.Presentations;
        while ((int)presentations.Count > 0)
        {
            dynamic p = presentations.Item(1);
            p.Saved = -1;
            p.Close();
            Marshal.ReleaseComObject(p);
        }
        Marshal.ReleaseComObject(presentations);
        app.Quit();
        Marshal.FinalReleaseComObject(app);
    }

    static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
