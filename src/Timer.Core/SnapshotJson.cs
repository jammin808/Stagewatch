using System.Buffers;
using System.Text.Json;


namespace Stagewatch.Core;

/// <summary>
/// Hand-rolled JSON for the hot path: no reflection, one buffer, serialized once per
/// snapshot no matter how many browsers are watching.
///
/// Two levels: the basic frame (every viewer: speaker, audience, Companion keys) and the
/// detail frame (producer / show-caller pages and the Companion module), which adds the projected
/// running order, the assistant's advice, the Q&amp;A queue and the other rooms.
/// </summary>
/// <summary>Who a frame is for. Each level sees less than the one after it.</summary>
public enum FrameAccess
{
    /// <summary>Anyone on the network (speaker screens, crew pages): the display, no speaker notes, no show internals.</summary>
    Public,
    /// <summary>Public pages that show the agenda (audience, moderator, rooms): + timings and rooms, only moderated questions.</summary>
    PublicDetail,
    /// <summary>The operator (PIN, Companion, MCP, the app itself): everything — notes, assistant advice, every question.</summary>
    Operator
}

public static class SnapshotJson
{
    public static byte[] Serialize(ShowSnapshot s) => Serialize(s, FrameAccess.Public);

    /// <summary>Trusted callers (Companion, MCP): detail = the full operator frame.</summary>
    public static byte[] Serialize(ShowSnapshot s, bool detail) => Serialize(s, detail ? FrameAccess.Operator : FrameAccess.Public);

    public static byte[] Serialize(ShowSnapshot s, FrameAccess access)
    {
        var detail = access != FrameAccess.Public;
        var buffer = new ArrayBufferWriter<byte>(detail ? 4096 : 1024);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("type", "state");
            w.WriteNumber("v", ShowSnapshot.FormatVersion);
            w.WriteNumber("seq", s.Seq.Value);
            w.WriteString("mode", ModeName(s.Mode));
            w.WriteString("state", s.State.ToString().ToLowerInvariant());
            w.WriteNumber("seconds", s.Seconds);
            w.WriteString("text", s.Text);
            w.WriteNumber("total", s.TotalSeconds);
            w.WriteString("phase", s.Phase.ToString().ToLowerInvariant());
            w.WriteString("color", s.ColorHex);
            w.WriteString("title", s.Title);
            w.WriteString("next", s.NextTitle);
            w.WriteNumber("item", s.ItemIndex);
            w.WriteNumber("items", s.ItemCount);
            w.WriteNumber("preset", s.PresetSlot);
            w.WriteString("endTime", s.EndTimeText);
            w.WriteString("timeOfDay", s.TimeOfDay);
            w.WriteNumber("clock", s.ClockSeconds);
            w.WriteNumber("utc", s.UtcSeconds);
            w.WriteString("date", s.DateText);
            w.WriteNumber("notesVersion", s.NotesVersion);
            w.WriteNumber("chimeSeq", s.ChimeSeq);
            w.WriteString("chime", s.Chime);
            if (s.OffsetSeconds is { } off) w.WriteNumber("offset", off);
            else w.WriteNull("offset");
            w.WriteString("schedule", s.ScheduleText);
            w.WriteString("message", s.Message);
            w.WriteString("urgency", s.Urgency.ToString().ToLowerInvariant());
            w.WriteString("flash", s.Flash.ToString().ToLowerInvariant());
            w.WriteBoolean("flashTimer", s.FlashTimer);
            w.WriteBoolean("blackout", s.Blackout);
            w.WriteString("output", OutputName(s.Output));
            w.WriteBoolean("ndi", s.Ndi);
            w.WriteString("holding", s.HoldingId);
            w.WriteString("holdingName", s.HoldingName);
            w.WriteBoolean("holdingFill", s.HoldingFill);
            w.WriteString("ltc", s.LtcText);
            w.WriteBoolean("ltcRunning", s.LtcRunning);
            w.WriteBoolean("ltcChase", s.LtcChase);
            w.WriteBoolean("roll", s.Roll);
            w.WriteString("rundown", s.RundownName);
            w.WriteStartArray("rundowns");
            foreach (var n in s.RundownNames) w.WriteStringValue(n);
            w.WriteEndArray();
            w.WriteString("auxLine", s.AuxLine);
            w.WriteStartArray("aux");
            foreach (var a in s.Aux)
            {
                w.WriteStartObject();
                w.WriteNumber("n", a.Slot);
                w.WriteString("name", a.Name);
                w.WriteString("text", a.Text);
                w.WriteNumber("seconds", a.Seconds);
                w.WriteNumber("duration", a.DurationSeconds);
                w.WriteBoolean("running", a.Running);
                w.WriteBoolean("up", a.CountUp);
                w.WriteBoolean("stage", a.OnStage);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("watermark", s.Watermark);
            w.WriteStartObject("show");
            w.WriteBoolean("timer", s.Elements.HasFlag(DisplayElements.Timer));
            w.WriteBoolean("message", s.Elements.HasFlag(DisplayElements.Message));
            w.WriteBoolean("timeOfDay", s.Elements.HasFlag(DisplayElements.TimeOfDay));
            w.WriteBoolean("endTime", s.Elements.HasFlag(DisplayElements.EndTime));
            w.WriteBoolean("title", s.Elements.HasFlag(DisplayElements.PresetName));
            w.WriteBoolean("next", s.Elements.HasFlag(DisplayElements.NextUp));
            w.WriteBoolean("progress", s.Elements.HasFlag(DisplayElements.ProgressBar));
            w.WriteBoolean("schedule", s.Elements.HasFlag(DisplayElements.Schedule));
            w.WriteBoolean("cue", s.Elements.HasFlag(DisplayElements.CueLabel));
            w.WriteBoolean("finishBy", s.Elements.HasFlag(DisplayElements.FinishBy));
            w.WriteBoolean("slides", s.Elements.HasFlag(DisplayElements.Slides));
            w.WriteBoolean("playout", s.Elements.HasFlag(DisplayElements.Playout));
            w.WriteEndObject();
            w.WriteNumber("configVersion", s.ConfigVersion);

            // ---- 2.0: speaker face
            w.WriteString("speakerText", s.SpeakerText);
            w.WriteString("cue", s.CueLabel);
            w.WriteString("finishBy", s.FinishBy);
            w.WriteBoolean("preview", s.Preview);
            w.WriteBoolean("stretching", s.Stretching);
            w.WriteBoolean("stable", s.StableLayout);
            w.WriteString("style", s.Holographic ? "holo" : "standard");
            w.WriteBoolean("holoFloor", s.HoloFloor);
            w.WriteString("scene", s.EggTimer ? "egg" : "holo");
            w.WriteBoolean("animate", s.Animations3D);
            w.WriteBoolean("particles", s.Particles3D);
            w.WriteString("room", s.Room);

            // ---- acknowledged cues
            w.WriteNumber("messageId", s.MessageSignalId);
            w.WriteString("messageAck", s.MessageAck);
            w.WriteString("messageAckBy", s.MessageAckBy);
            w.WriteStartArray("signals");
            foreach (var g in s.Signals)
            {
                w.WriteStartObject();
                w.WriteNumber("id", g.Id);
                w.WriteString("kind", g.Kind.ToString().ToLowerInvariant());
                w.WriteString("target", g.Target);
                w.WriteString("text", g.Text);
                w.WriteString("state", g.State.ToString().ToLowerInvariant());
                w.WriteBoolean("escalated", g.Escalated);
                w.WriteNumber("age", g.AgeSeconds);
                w.WriteString("by", g.By);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            // ---- presentation awareness
            if (s.Slides is { } sl)
            {
                w.WriteStartObject("slides");
                w.WriteNumber("slide", sl.Slide);
                w.WriteNumber("count", sl.Count);
                w.WriteString("next", sl.NextTitle);
                // Speaker notes are the presenter's private script: operator frames only.
                w.WriteString("notes", access == FrameAccess.Operator ? sl.Notes : "");
                w.WriteNumber("ghost", sl.GhostDeltaSeconds);
                w.WriteNumber("projected", sl.ProjectedSeconds);
                w.WriteString("pace", sl.Pace.ToString().ToLowerInvariant());
                w.WriteBoolean("rehearsing", sl.Rehearsing);
                w.WriteEndObject();
            }
            else w.WriteNull("slides");
            if (s.Speech is { } sp)
            {
                w.WriteStartObject("speech");
                w.WriteNumber("percent", sp.Percent);
                w.WriteNumber("wpm", sp.WordsPerMinute);
                if (sp.ProjectedRemainingSeconds is { } pr) w.WriteNumber("projected", pr); else w.WriteNull("projected");
                w.WriteBoolean("listening", sp.Listening);
                w.WriteEndObject();
            }
            else w.WriteNull("speech");
            if (s.Playout is { } po)
            {
                w.WriteStartObject("playout");
                w.WriteString("title", po.Title);
                w.WriteNumber("remaining", po.RemainingSeconds);
                w.WriteNumber("duration", po.DurationSeconds);
                w.WriteBoolean("running", po.Running);
                w.WriteString("source", po.Source);
                w.WriteEndObject();
            }
            else w.WriteNull("playout");

            // ---- Q&A summary (everyone may see what's live)
            w.WriteString("qaLive", s.QaLive);
            w.WriteNumber("qaPending", s.QaPending);
            w.WriteNumber("qaQueued", s.QaQueued);
            w.WriteNumber("qaFit", s.QaFit);

            // ---- schedule summary
            w.WriteString("hardOut", s.HardOutText);
            if (s.HardOutMarginSeconds is { } hm) w.WriteNumber("hardOutMargin", hm); else w.WriteNull("hardOutMargin");
            w.WriteString("forecast", s.ForecastText);
            w.WriteNumber("delay", s.PendingDelaySeconds);
            if (s.ClockOffsetMs is { } co) w.WriteNumber("clockOffsetMs", co); else w.WriteNull("clockOffsetMs");

            if (detail) WriteDetail(w, s, access == FrameAccess.Operator);
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteDetail(Utf8JsonWriter w, ShowSnapshot s, bool operatorView)
    {
        w.WriteBoolean("detail", true);
        w.WriteStartArray("timings");
        foreach (var t in s.Timings)
        {
            w.WriteStartObject();
            w.WriteNumber("i", t.Index);
            w.WriteString("title", t.Title);
            w.WriteString("speaker", t.Speaker);
            w.WriteString("kind", t.Kind.ToString().ToLowerInvariant());
            w.WriteNumber("seconds", t.Seconds);
            w.WriteNumber("adjust", t.AdjustSeconds);
            w.WriteString("planned", t.Planned);
            w.WriteString("expected", t.Expected);
            w.WriteString("expectedEnd", t.ExpectedEnd);
            w.WriteString("actual", t.Actual);
            w.WriteString("actualEnd", t.ActualEnd);
            w.WriteString("status", t.Status.ToString().ToLowerInvariant());
            w.WriteNumber("delay", t.DelaySeconds);
            w.WriteBoolean("conflict", t.Conflict);
            w.WriteBoolean("fixed", t.Fixed);
            w.WriteString("hardOut", t.HardOut);
            w.WriteString("group", t.Group);
            w.WriteStartArray("labels");
            foreach (var l in t.Labels.Split(',', StringSplitOptions.RemoveEmptyEntries)) w.WriteStringValue(l);
            w.WriteEndArray();
            if (t.StartIn is { } si) w.WriteNumber("in", si); else w.WriteNull("in");
            if (t.EndIn is { } ei) w.WriteNumber("endIn", ei); else w.WriteNull("endIn");
            if (t.PlannedIn is { } pi) w.WriteNumber("plannedIn", pi); else w.WriteNull("plannedIn");
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartObject("labelColors");
        foreach (var pair in s.LabelColors.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0) w.WriteString(pair[..eq], pair[(eq + 1)..]);
        }
        w.WriteEndObject();
        w.WriteStartArray("groups");
        foreach (var g in s.Groups)
        {
            w.WriteStartObject();
            w.WriteString("name", g.Name);
            w.WriteNumber("first", g.First);
            w.WriteNumber("last", g.Last);
            w.WriteNumber("planned", g.PlannedSeconds);
            if (g.TargetSeconds is { } gt) w.WriteNumber("target", gt); else w.WriteNull("target");
            if (g.StartIn is { } gs) w.WriteNumber("in", gs); else w.WriteNull("in");
            if (g.EndIn is { } ge) w.WriteNumber("endIn", ge); else w.WriteNull("endIn");
            w.WriteString("plannedStart", g.Planned);
            w.WriteString("expectedEnd", g.ExpectedEnd);
            w.WriteString("status", g.Status.ToString().ToLowerInvariant());
            w.WriteString("targetText", RundownGroups.TargetText(g));
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("advice");
        // The assistant's suggestions ("cut the panel by 4 min") are for the operator only.
        foreach (var a in operatorView ? s.Advice : EquatableArray<Advice>.Empty)
        {
            w.WriteStartObject();
            w.WriteString("key", a.Key);
            w.WriteString("level", a.Level.ToString().ToLowerInvariant());
            w.WriteString("text", a.Text);
            if (a.Command is null) w.WriteNull("command"); else w.WriteString("command", a.Command);
            if (a.CommandLabel is null) w.WriteNull("label"); else w.WriteString("label", a.CommandLabel);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("questions");
        // Public pages see only questions a moderator has let through — never raw audience submissions.
        foreach (var q in s.Questions.Where(q => operatorView || q.State is QuestionState.Approved or QuestionState.Starred or QuestionState.Live))
        {
            w.WriteStartObject();
            w.WriteNumber("id", q.Id);
            w.WriteString("text", q.Text);
            w.WriteString("name", q.Name);
            w.WriteString("state", q.State.ToString().ToLowerInvariant());
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("rooms");
        foreach (var r in s.Rooms)
        {
            w.WriteStartObject();
            w.WriteString("room", r.Room);
            w.WriteString("host", r.Host);
            w.WriteNumber("webPort", r.WebPort);
            w.WriteString("title", r.Title);
            w.WriteString("text", r.Text);
            w.WriteString("state", r.State);
            w.WriteString("phase", r.Phase);
            if (r.OffsetSeconds is { } o) w.WriteNumber("offset", o); else w.WriteNull("offset");
            w.WriteString("hardOut", r.HardOut);
            if (r.HardOutMarginSeconds is { } m) w.WriteNumber("hardOutMargin", m); else w.WriteNull("hardOutMargin");
            w.WriteNumber("openSignals", r.OpenSignals);
            w.WriteBoolean("stale", r.Stale);
            w.WriteBoolean("self", r.Self);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    public static byte[] SerializeConfig(RemoteConfig c)
    {
        var buffer = new ArrayBufferWriter<byte>(2048);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteStartArray("presets");
            foreach (var p in c.Presets)
            {
                w.WriteStartObject();
                w.WriteNumber("slot", p.Slot);
                w.WriteString("label", p.Label);
                w.WriteNumber("seconds", p.DurationSeconds);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("messages");
            foreach (var m in c.Messages)
            {
                w.WriteStartObject();
                w.WriteString("label", m.Label);
                w.WriteString("text", m.Text);
                w.WriteString("urgency", m.Urgency.ToString().ToLowerInvariant());
                w.WriteString("flash", m.Flash.ToString().ToLowerInvariant());
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("rundown");
            foreach (var r in c.Rundown)
            {
                w.WriteStartObject();
                w.WriteString("title", r.Title);
                if (r.Speaker is null) w.WriteNull("speaker"); else w.WriteString("speaker", r.Speaker);
                w.WriteNumber("seconds", r.DurationSeconds);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("ladders");
            foreach (var l in c.Ladders ?? Array.Empty<(string, string)>())
            {
                w.WriteStartObject();
                w.WriteString("id", l.Id);
                w.WriteString("name", l.Name);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("departments");
            foreach (var d in c.Departments ?? Array.Empty<string>()) w.WriteStringValue(d);
            w.WriteEndArray();
            w.WriteString("room", c.Room ?? "");
            w.WriteString("ladder", c.ActiveLadder ?? "");
            if (c.Tier is not null) w.WriteString("tier", c.Tier);
            if (c.Holding is not null)
            {
                w.WriteStartArray("holding");
                foreach (var h in c.Holding)
                {
                    w.WriteStartObject();
                    w.WriteString("id", h.Id);
                    w.WriteString("name", h.Name);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            if (c.Features is not null)
            {
                w.WriteStartArray("features");
                foreach (var f in c.Features) w.WriteStringValue(f);
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    public static string OutputName(OutputMode m) => m switch
    {
        OutputMode.FullScreen => "full",
        OutputMode.KeyedFullScreen => "keyed",
        OutputMode.FloatingMinimal => "mini",
        _ => "closed"
    };

    public static string ModeName(TimerMode m) => m switch
    {
        TimerMode.Countdown => "countdown",
        TimerMode.CountUp => "countup",
        TimerMode.TimeOfDay => "tod",
        _ => m.ToString().ToLowerInvariant()
    };
}
