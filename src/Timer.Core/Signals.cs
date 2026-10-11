namespace Stagewatch.Core;

/// <summary>What a signal asks of its target.</summary>
public enum SignalKind
{
    /// <summary>A message to the speaker / moderator that should be confirmed as seen.</summary>
    Message,
    /// <summary>Department standby ("Lighting, standby").</summary>
    Standby,
    /// <summary>Department GO.</summary>
    Go,
    /// <summary>Green room: bring this speaker to the stage.</summary>
    Call,
    /// <summary>A discreet haptic / visual nudge on a phone or watch (wrap up, look at monitor…).</summary>
    Nudge
}

/// <summary>Where a signal is in its life: sent → seen (acknowledged) → done (in position / complete).</summary>
public enum SignalState { Sent, Seen, Done, Cancelled }

/// <summary>One cue sent from the operator that someone has to see. Immutable; the board swaps whole values.</summary>
public sealed record Signal
{
    public int Id { get; init; }
    public SignalKind Kind { get; init; }
    /// <summary>Who it's for: "speaker", "moderator", "backstage", or a department name ("Lighting").</summary>
    public string Target { get; init; } = "speaker";
    public string Text { get; init; } = "";
    public SignalState State { get; init; }
    public DateTime SentUtc { get; init; }
    public DateTime? SeenUtc { get; init; }
    public DateTime? DoneUtc { get; init; }
    public string By { get; init; } = "";
    /// <summary>Set by the escalation rule when nobody has acknowledged it in time.</summary>
    public bool Escalated { get; init; }

    public bool IsOpen => State is SignalState.Sent or SignalState.Seen;

    /// <summary>
    /// A department standing by: a standby that hasn't been answered with a GO or cleared. Confirmed ("in position")
    /// still counts — that department is waiting for its GO.
    /// </summary>
    public bool IsStandingBy => Kind == SignalKind.Standby && State is SignalState.Sent or SignalState.Seen or SignalState.Done;

    /// <summary>Still in play: waiting for an answer, or a department standing by.</summary>
    public bool IsLive => IsOpen || IsStandingBy;
}

/// <summary>The view of a signal every output gets (value type, compared by content).</summary>
public readonly record struct SignalView(int Id, SignalKind Kind, string Target, string Text, SignalState State, bool Escalated, long AgeSeconds, string By);

/// <summary>
/// The two-way cue channel nobody else ships: cues go out, the target acknowledges, the operator
/// sees the tick (or an escalation when nobody answers). Owner-thread only — the app is the single
/// writer — so no locking: remote acks arrive as commands like everything else.
/// </summary>
public sealed class SignalBoard
{
    private readonly List<Signal> _signals = new();
    private int _nextId = 1;

    /// <summary>Unacknowledged signals escalate (flash fast, alert the operator) after this.</summary>
    public TimeSpan EscalateAfter { get; set; } = TimeSpan.FromSeconds(20);
    /// <summary>Finished signals stay visible this long, then drop off the views.</summary>
    public TimeSpan KeepDoneFor { get; set; } = TimeSpan.FromSeconds(30);

    public IReadOnlyList<Signal> All => _signals;
    public int Version { get; private set; }

    public event Action<Signal>? Changed;

    public Signal Send(SignalKind kind, string target, string text, DateTime nowUtc)
    {
        target = NormaliseTarget(target);
        // A new standby/go/call to the same target supersedes the previous one.
        if (kind is SignalKind.Standby or SignalKind.Go or SignalKind.Call)
            for (int i = 0; i < _signals.Count; i++)
                if (_signals[i].IsLive && _signals[i].Target == target && _signals[i].Kind is SignalKind.Standby or SignalKind.Go or SignalKind.Call)
                    Replace(i, _signals[i] with { State = SignalState.Cancelled, DoneUtc = nowUtc });

        var s = new Signal { Id = _nextId++, Kind = kind, Target = target, Text = text, State = SignalState.Sent, SentUtc = nowUtc };
        _signals.Add(s);
        Version++;
        Changed?.Invoke(s);
        return s;
    }

    /// <summary>
    /// Acknowledge: first ack marks it seen; a second ("done", "in position") completes it.
    /// <paramref name="stage"/> "seen" | "done". Unknown ids are ignored.
    /// </summary>
    public Signal? Ack(int id, string by, string stage, DateTime nowUtc)
    {
        var i = _signals.FindIndex(s => s.Id == id);
        if (i < 0 || !_signals[i].IsOpen) return null;
        var s = _signals[i];
        var done = stage.Equals("done", StringComparison.OrdinalIgnoreCase)
                   || s.State == SignalState.Seen
                   || s.Kind is SignalKind.Message or SignalKind.Nudge or SignalKind.Go;
        var next = s with
        {
            State = done ? SignalState.Done : SignalState.Seen,
            SeenUtc = s.SeenUtc ?? nowUtc,
            DoneUtc = done ? nowUtc : null,
            By = string.IsNullOrWhiteSpace(by) ? s.By : by.Trim()[..Math.Min(by.Trim().Length, 40)],
            Escalated = false
        };
        Replace(i, next);
        return next;
    }

    public void Cancel(int id, DateTime nowUtc)
    {
        var i = _signals.FindIndex(s => s.Id == id);
        if (i >= 0 && _signals[i].IsLive) Replace(i, _signals[i] with { State = SignalState.Cancelled, DoneUtc = nowUtc });
    }

    public void ClearTarget(string target, DateTime nowUtc)
    {
        target = NormaliseTarget(target);
        for (int i = 0; i < _signals.Count; i++)
            if (_signals[i].IsLive && (target == "*" || _signals[i].Target == target))
                Replace(i, _signals[i] with { State = SignalState.Cancelled, DoneUtc = nowUtc });
    }

    /// <summary>Per frame: escalate the unanswered, forget the long finished. Returns newly escalated signals.</summary>
    public List<Signal> Update(DateTime nowUtc)
    {
        var escalated = new List<Signal>();
        for (int i = _signals.Count - 1; i >= 0; i--)
        {
            var s = _signals[i];
            if (s.State == SignalState.Sent && !s.Escalated && nowUtc - s.SentUtc >= EscalateAfter)
            {
                var e = s with { Escalated = true };
                Replace(i, e);
                escalated.Add(e);
            }
            else if (!s.IsLive && s.DoneUtc is { } d && nowUtc - d > KeepDoneFor)
            {
                _signals.RemoveAt(i);
                Version++;
            }
        }
        return escalated;
    }

    public Signal? Find(int id) => _signals.FirstOrDefault(s => s.Id == id);

    /// <summary>The departments standing by, in the order they were stood by (one standby each).</summary>
    public IReadOnlyList<Signal> StandingBy() => _signals.Where(s => s.IsStandingBy).ToList();

    /// <summary>The latest open signal for a target, if any.</summary>
    public Signal? LatestFor(string target, SignalKind? kind = null)
    {
        target = NormaliseTarget(target);
        for (int i = _signals.Count - 1; i >= 0; i--)
            if (_signals[i].Target == target && (kind is null || _signals[i].Kind == kind) && _signals[i].State != SignalState.Cancelled)
                return _signals[i];
        return null;
    }

    public EquatableArray<SignalView> Views(DateTime nowUtc)
        => _signals.Where(s => s.State != SignalState.Cancelled)
                   .Select(s => new SignalView(s.Id, s.Kind, s.Target, s.Text, s.State, s.Escalated,
                       // Age is quantised to 5 s so it doesn't force a new frame every second.
                       (long)(nowUtc - s.SentUtc).TotalSeconds / 5 * 5, s.By))
                   .ToEquatableArray();

    public static string NormaliseTarget(string? t)
    {
        t = (t ?? "").Trim();
        if (t.Length == 0) return "speaker";
        if (t.Length > 32) t = t[..32];
        return t.ToLowerInvariant() is "speaker" or "moderator" or "backstage" or "chair" or "interpreter" or "*"
            ? t.ToLowerInvariant()
            : char.ToUpperInvariant(t[0]) + t[1..];
    }

    private void Replace(int i, Signal s)
    {
        _signals[i] = s;
        Version++;
        Changed?.Invoke(s);
    }
}

public enum QuestionState { New, Approved, Starred, Live, Answered, Dismissed }

public sealed record Question
{
    public int Id { get; init; }
    public string Text { get; init; } = "";
    public string Name { get; init; } = "";
    public QuestionState State { get; init; }
    public DateTime ReceivedUtc { get; init; }
    public DateTime? LiveUtc { get; init; }
    public DateTime? AnsweredUtc { get; init; }
}

public readonly record struct QuestionView(int Id, string Text, string Name, QuestionState State);

/// <summary>
/// Audience questions, moderated. Audience phones submit via the QR page; the moderator approves,
/// stars and takes them live; the timer estimates how many more fit in the time left.
/// </summary>
public sealed class QaBoard
{
    private readonly List<Question> _q = new();
    private int _nextId = 1;
    private readonly List<double> _answerSeconds = new();

    public const int MaxQuestions = 500;
    public const int MaxLength = 280;
    public int Version { get; private set; }
    public IReadOnlyList<Question> All => _q;

    /// <summary>Default answer length until we have measured some (seconds).</summary>
    public double DefaultAnswerSeconds { get; set; } = 90;

    public Question? Submit(string text, string name, DateTime nowUtc)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return null;
        if (text.Length > MaxLength) text = text[..MaxLength];
        name = (name ?? "").Trim();
        if (name.Length > 40) name = name[..40];
        if (_q.Count >= MaxQuestions) return null;
        // Drop exact duplicates (double taps, repeated submissions).
        if (_q.Any(x => x.Text.Equals(text, StringComparison.OrdinalIgnoreCase) && x.State != QuestionState.Dismissed)) return null;
        var q = new Question { Id = _nextId++, Text = text, Name = name, State = QuestionState.New, ReceivedUtc = nowUtc };
        _q.Add(q);
        Version++;
        return q;
    }

    public Question? Set(int id, QuestionState state, DateTime nowUtc)
    {
        var i = _q.FindIndex(x => x.Id == id);
        if (i < 0) return null;
        var q = _q[i];
        if (state == QuestionState.Live)
        {
            // One question live at a time: the previous live one is answered.
            for (int j = 0; j < _q.Count; j++)
                if (j != i && _q[j].State == QuestionState.Live) Finish(j, nowUtc);
            q = q with { State = QuestionState.Live, LiveUtc = nowUtc };
        }
        else if (state == QuestionState.Answered && q.State == QuestionState.Live)
        {
            _q[i] = q;
            Finish(i, nowUtc);
            Version++;
            return _q[i];
        }
        else q = q with { State = state };
        _q[i] = q;
        Version++;
        return q;
    }

    /// <summary>Take the next approved (starred first) question live. Returns it, or null when the queue is empty.</summary>
    public Question? Next(DateTime nowUtc)
    {
        var next = _q.Where(x => x.State == QuestionState.Starred).OrderBy(x => x.Id).FirstOrDefault()
                   ?? _q.Where(x => x.State == QuestionState.Approved).OrderBy(x => x.Id).FirstOrDefault();
        return next is null ? EndLive(nowUtc) : Set(next.Id, QuestionState.Live, nowUtc);
    }

    private Question? EndLive(DateTime nowUtc)
    {
        var i = _q.FindIndex(x => x.State == QuestionState.Live);
        if (i >= 0) { Finish(i, nowUtc); Version++; }
        return null;
    }

    public void Clear()
    {
        _q.Clear();
        _answerSeconds.Clear();
        Version++;
    }

    private void Finish(int i, DateTime nowUtc)
    {
        var q = _q[i];
        if (q.LiveUtc is { } live)
        {
            var secs = (nowUtc - live).TotalSeconds;
            if (secs is > 5 and < 1800) _answerSeconds.Add(secs);
        }
        _q[i] = q with { State = QuestionState.Answered, AnsweredUtc = nowUtc };
    }

    public double AverageAnswerSeconds
        => _answerSeconds.Count == 0 ? DefaultAnswerSeconds : _answerSeconds.TakeLast(8).Average();

    /// <summary>How many more answers fit in <paramref name="remaining"/>.</summary>
    public int FitIn(TimeSpan remaining)
        => remaining <= TimeSpan.Zero ? 0 : (int)Math.Floor(remaining.TotalSeconds / Math.Max(10, AverageAnswerSeconds));

    public int Pending => _q.Count(x => x.State == QuestionState.New);
    public int Queued => _q.Count(x => x.State is QuestionState.Approved or QuestionState.Starred);
    public Question? Live => _q.FirstOrDefault(x => x.State == QuestionState.Live);

    /// <summary>What the moderator and producer see: live, then starred, approved, new (oldest first).</summary>
    public EquatableArray<QuestionView> Views(int max = 30)
        => _q.Where(x => x.State is not (QuestionState.Answered or QuestionState.Dismissed))
             .OrderBy(x => x.State switch { QuestionState.Live => 0, QuestionState.Starred => 1, QuestionState.Approved => 2, _ => 3 })
             .ThenBy(x => x.Id)
             .Take(max)
             .Select(x => new QuestionView(x.Id, x.Text, x.Name, x.State))
             .ToEquatableArray();
}
