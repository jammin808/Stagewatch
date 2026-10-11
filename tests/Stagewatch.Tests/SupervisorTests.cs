using Stagewatch.Integrations;

/// <summary>
/// The integration supervisor, headless: integrations start, restart and stop as their settings change, a slow one
/// retires in the background without blocking the owner, and its replacement waits until it has let go.
/// </summary>
static class SupervisorTests
{
    sealed class Device : IDisposable
    {
        public static readonly List<string> Log = new();
        public readonly string Tag;
        public readonly ManualResetEventSlim Release = new(true);
        public volatile bool Disposed;
        public Device(string tag) { Tag = tag; lock (Log) Log.Add("start " + tag); }
        public void Dispose() { Release.Wait(5000); Disposed = true; lock (Log) Log.Add("stop " + Tag); }
    }

    public static void Run(Action<bool, string> Check)
    {
        // The owner thread's queue: posted work runs only when the test drains it, like a dispatcher.
        var queue = new Queue<Action>();
        var reports = new List<string>();
        Device? live = null;
        var signature = "True|port 1";
        var created = 0;
        IntegrationSupervisor sup = null!;
        void Apply() => sup.Reconcile("dev", signature, (Device? d) => live = d, () => new Device($"#{++created} {signature}"));
        sup = new IntegrationSupervisor(work => { lock (queue) queue.Enqueue(work); }, Apply, reports.Add);
        bool Drain(int ms = 3000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                Action? next = null;
                lock (queue) if (queue.Count > 0) next = queue.Dequeue();
                if (next is not null) { next(); return true; }
                Thread.Sleep(5);
            }
            return false;
        }

        Apply();
        Check(live is not null && created == 1, "supervisor: an enabled integration starts");
        Apply();
        Check(created == 1, "supervisor: the same settings leave it running");

        // Settings change while the old one is slow to stop: the owner isn't blocked, and nothing new starts yet.
        var old = live!;
        old.Release.Reset();
        signature = "True|port 2";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Apply();
        Check(sw.ElapsedMilliseconds < 200 && live is null && sup.Retiring.Contains("dev"), $"supervisor: a slow stop runs in the background ({sw.ElapsedMilliseconds} ms)");
        signature = "True|port 3";
        Apply();
        Check(created == 1, "supervisor: nothing replaces it while the old one still holds its device");
        old.Release.Set();
        Check(Drain() && live is not null && old.Disposed && created == 2 && live.Tag.EndsWith("port 3"),
              $"supervisor: once it has let go, the latest settings start ({live?.Tag})");

        // Turning it off stops it; a failing start is reported, not thrown.
        signature = "False|port 3";
        Apply();
        Drain();
        Check(live is null && sup.Retiring.Count == 0 && created == 2, "supervisor: disabled stops it and doesn't restart it");
        sup.Reconcile<Device>("broken", "True", _ => { }, () => throw new InvalidOperationException("no device"));
        Check(reports.Count == 1 && reports[0].Contains("no device"), "supervisor: a failed start is reported to the operator");

        // Closing: everything stops in parallel, capped, including integrations the owner runs itself.
        signature = "True|port 4";
        Apply();
        var stuck = live!;
        stuck.Release.Reset();
        var other = new Device("own");
        sw.Restart();
        sup.StopAll(TimeSpan.FromMilliseconds(300), other, null);
        Check(sw.ElapsedMilliseconds < 1500 && other.Disposed && live is null, $"supervisor: closing never waits long on a stuck device ({sw.ElapsedMilliseconds} ms)");
        stuck.Release.Set();
        Apply();
        Check(live is null, "supervisor: nothing starts after closing");
    }

    /// <summary>Holding images: what "holding n|name|off|toggle" puts on air.</summary>
    public static void Holding(Action<bool, string> Check)
    {
        var images = new List<Stagewatch.Core.HoldingImage>
        {
            new() { Id = "aaaaaaaaaaaa", Name = "Sponsors" },
            new() { Id = "bbbbbbbbbbbb", Name = "Back soon" },
        };
        var sel = new Stagewatch.Core.HoldingSelection();
        var first = sel.Choose(images, "toggle", out var err);
        Check(first?.Name == "Sponsors" && err is null && sel.Set(first) && sel.OnAir && sel.NameIn(images) == "Sponsors", "holding: toggle with nothing on air brings up the first image");
        Check(!sel.Set(first), "holding: the same image again changes nothing");
        Check(sel.Choose(images, "toggle", out _) is null && sel.Set(null) && !sel.OnAir && sel.NameIn(images) == "", "holding: toggle takes it off");
        sel.Set(sel.Choose(images, "2", out _));
        sel.Set(null);
        Check(sel.Choose(images, "toggle", out _)?.Name == "Back soon", "holding: toggle brings back the last one");
        sel.Forget("bbbbbbbbbbbb");
        Check(sel.Choose(images, "toggle", out _)?.Name == "Sponsors", "holding: a deleted image isn't brought back");
        Check(sel.Choose(images, "nope", out var missing) is null && missing!.Contains("nope") && sel.Choose(new List<Stagewatch.Core.HoldingImage>(), "toggle", out var none) is null && none is not null,
              "holding: an unknown name or an empty library says why");
        Check(sel.Choose(images, "OFF", out var offErr) is null && offErr is null, "holding: off is not an error");
    }
}
