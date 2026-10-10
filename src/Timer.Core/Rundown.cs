using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Stagewatch.Core;

/// <summary>The running order: an ordered agenda the operator steps through with Next.</summary>
public sealed class Rundown
{
    [JsonPropertyName("items")]
    public List<RundownItem> Items { get; set; } = new();

    /// <summary>Planned wall-clock start of the first item, "HH:mm" (or "yyyy-MM-dd HH:mm"). Null = anchor on first Start.</summary>
    [JsonPropertyName("scheduledStart")]
    public string? ScheduledStart { get; set; }

    /// <summary>
    /// The show's time zone (Windows or IANA id). Null = this PC's zone. Planned times, fixed starts and
    /// hard-outs are read in this zone, so a remote producer or a laptop on home time can't shift the plan.
    /// </summary>
    [JsonPropertyName("timeZone")]
    public string? TimeZoneId { get; set; }

    /// <summary>Target length per group (seconds), e.g. "Morning session" = 2 h. Groups without a target just add up.</summary>
    /// <summary>The shared spreadsheet this running order is imported from (Google Sheets or any CSV link), if any.</summary>
    [JsonPropertyName("sheetUrl")]
    public string? SheetUrl { get; set; }

    [JsonPropertyName("groupTargets")]
    public Dictionary<string, int> GroupTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When the show must end ("HH:mm" or dated). Without a planned start, the plan is back-timed from it; either way it
    /// counts as the last item's curfew, so over/under, the forecast and Recover all aim at it.
    /// </summary>
    [JsonPropertyName("endOfShow")]
    public string? EndOfShow { get; set; }

    /// <summary>The show's labels and their colours ("VT" → "#3B82F6"). Renaming or recolouring here changes every item.</summary>
    [JsonPropertyName("labelColors")]
    public Dictionary<string, string> LabelColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Play the running order again when it ends: 0 = no, n = n more times, -1 = forever (walk-in loops, trade shows).</summary>
    [JsonPropertyName("loop")]
    public int Loop { get; set; }

    /// <summary>A deep copy (items, targets and all) — for show files, duplicates and switching.</summary>
    public Rundown Clone() => new()
    {
        Items = Items.Select(i => i.Clone()).ToList(),
        ScheduledStart = ScheduledStart,
        TimeZoneId = TimeZoneId,
        SheetUrl = SheetUrl,
        EndOfShow = EndOfShow,
        Loop = Loop,
        LabelColors = new Dictionary<string, string>(LabelColors, StringComparer.OrdinalIgnoreCase),
        GroupTargets = new Dictionary<string, int>(GroupTargets, StringComparer.OrdinalIgnoreCase),
    };
}

/// <summary>Labels: a fixed, colour-blind-friendly palette assigned in order, so new labels get distinct colours without fuss.</summary>
public static class Labels
{
    public static readonly string[] Palette =
        { "#3B82F6", "#F59E0B", "#10B981", "#EF4444", "#8B5CF6", "#EC4899", "#14B8A6", "#F97316", "#84CC16", "#06B6D4", "#A855F7", "#EAB308" };

    /// <summary>"VT, gfx ,  CAM 2" → ["VT", "gfx", "CAM 2"] (trimmed, de-duplicated, at most 8, each up to 20 characters).</summary>
    public static List<string> Parse(string? text)
        => (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0)
            .Select(l => l.Length > 20 ? l[..20] : l).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();

    /// <summary>The colour for a label: the show's choice, else a palette colour by position (pure — nothing is stored).</summary>
    public static string ColorFor(Rundown r, string label, int position = 0)
    {
        foreach (var (k, v) in r.LabelColors) if (string.Equals(k, label, StringComparison.OrdinalIgnoreCase)) return v;
        return Palette[Math.Abs(position) % Palette.Length];
    }

    /// <summary>Give every label in use a lasting colour (when the running order is saved), keeping the ones already chosen.</summary>
    public static void AssignColors(Rundown r)
    {
        foreach (var label in InUse(r.Items))
        {
            if (r.LabelColors.Keys.Any(k => string.Equals(k, label, StringComparison.OrdinalIgnoreCase))) continue;
            var used = r.LabelColors.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
            r.LabelColors[label] = Palette.FirstOrDefault(c => !used.Contains(c)) ?? Palette[r.LabelColors.Count % Palette.Length];
        }
    }

    /// <summary>Every label in use, in first-seen order.</summary>
    public static List<string> InUse(IEnumerable<RundownItem> items)
        => items.SelectMany(i => i.Labels).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>A running order that isn't the live one (a second day, a rehearsal version, a different room's plan).</summary>
public sealed class NamedRundown
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("rundown")] public Rundown Rundown { get; set; } = new();

    public static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 40;
}

/// <summary>What kind of segment an item is. Drives trim priorities and the overrun forecaster.</summary>
public enum SegmentKind { Talk, Panel, QA, Break, Award, Video, Buffer, Other }

/// <summary>How an item's start is decided.</summary>
public enum StartMode
{
    /// <summary>Starts when the previous item ends (delays ripple on).</summary>
    Sequential,
    /// <summary>Held to its published time: waits if early, flags a conflict if the show is late.</summary>
    Fixed
}

public sealed class RundownItem
{
    /// <summary>Stable identity across edits and reorders (merging imports, keeping notes made while the editor is open).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("speaker")]
    public string? Speaker { get; set; }

    [JsonPropertyName("durationSeconds")]
    public int DurationSeconds { get; set; }

    /// <summary>Optional message sent to the presenter when this item loads.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>When this item reaches zero, load and start the next one automatically.</summary>
    [JsonPropertyName("autoAdvance")]
    public bool AutoAdvance { get; set; }

    [JsonPropertyName("kind")]
    public SegmentKind Kind { get; set; } = SegmentKind.Talk;

    [JsonPropertyName("startMode")]
    public StartMode StartMode { get; set; } = StartMode.Sequential;

    /// <summary>Published start for <see cref="StartMode.Fixed"/> items: "HH:mm" or "yyyy-MM-dd HH:mm" (show time zone).</summary>
    [JsonPropertyName("fixedStart")]
    public string? FixedStart { get; set; }

    /// <summary>Shortest this item may be cut to. Null = not compressible.</summary>
    [JsonPropertyName("minDurationSeconds")]
    public int? MinDurationSeconds { get; set; }

    /// <summary>A curfew at the end of this item ("HH:mm" or "yyyy-MM-dd HH:mm"): the show must be past it by then.</summary>
    [JsonPropertyName("hardOut")]
    public string? HardOut { get; set; }

    /// <summary>Cue ladder for this item (id), or null for the show default.</summary>
    [JsonPropertyName("cueLadder")]
    public string? CueLadderId { get; set; }

    /// <summary>Live trims (negative) or extensions (positive) applied on the day, kept apart from the plan for the report.</summary>
    [JsonPropertyName("adjustSeconds")]
    public int AdjustSeconds { get; set; }

    /// <summary>Operator / producer notes (never shown to the speaker).</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>The group (session, act, block) this item belongs to. Consecutive items with the same name form a group.</summary>
    [JsonPropertyName("group")]
    public string? Group { get; set; }

    /// <summary>This item's own chimes (start, warning, wrap-up, zero); blank ones use the show's alerts.</summary>
    [JsonPropertyName("chimes")]
    public ItemChimes Chimes { get; set; } = new();

    /// <summary>Labels on this item ("VT", "GFX", "CAM 2"): coloured tags that screens show and triggers can follow.</summary>
    [JsonPropertyName("labels")]
    public List<string> Labels { get; set; } = new();

    /// <summary>Notes per department ("Lighting" → "Blue wash, follow spot on podium"). Each department edits its own.</summary>
    [JsonPropertyName("deptNotes")]
    public Dictionary<string, string> DeptNotes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The duration that runs on the day: plan plus live adjustments, never negative.</summary>
    [JsonIgnore]
    public int EffectiveSeconds => Math.Max(0, DurationSeconds + AdjustSeconds);

    /// <summary>Title plus speaker, for one-line displays.</summary>
    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Speaker) ? Title : $"{Title} — {Speaker}";

    /// <summary>A department's note on this item ("" = none). Department names match case-insensitively.</summary>
    public string DeptNote(string dept)
    {
        foreach (var (k, v) in DeptNotes)
            if (string.Equals(k.Trim(), dept.Trim(), StringComparison.OrdinalIgnoreCase)) return v;
        return "";
    }

    /// <summary>Set (or with empty text, remove) a department's note.</summary>
    public void SetDeptNote(string dept, string text)
    {
        foreach (var k in DeptNotes.Keys.Where(k => string.Equals(k.Trim(), dept.Trim(), StringComparison.OrdinalIgnoreCase)).ToList())
            DeptNotes.Remove(k);
        if (!string.IsNullOrWhiteSpace(text)) DeptNotes[dept.Trim()] = text.Trim();
    }

    public RundownItem Clone() => new()
    {
        Id = Id,
        Labels = Labels.ToList(),
        Chimes = Chimes.Clone(),
        Title = Title, Speaker = Speaker, DurationSeconds = DurationSeconds,
        Message = Message, AutoAdvance = AutoAdvance, Kind = Kind, StartMode = StartMode,
        FixedStart = FixedStart, MinDurationSeconds = MinDurationSeconds, HardOut = HardOut,
        CueLadderId = CueLadderId, AdjustSeconds = AdjustSeconds, Notes = Notes, Group = Group,
        DeptNotes = new Dictionary<string, string>(DeptNotes, StringComparer.OrdinalIgnoreCase)
    };
}

/// <summary>Where the show stands against its plan. All times are local wall-clock.</summary>
/// Offset: positive = running late, negative = ahead.
public readonly record struct ScheduleStatus(
    TimeSpan Offset,
    DateTime ProjectedEnd,
    DateTime PlannedEnd);

/// <summary>Pure schedule maths — no clocks, no state, so it's trivially testable.</summary>
public static class Schedule
{
    /// <summary>Planned start of every item, given the anchor (planned start of item 0).</summary>
    public static DateTime[] PlannedStarts(IReadOnlyList<RundownItem> items, DateTime anchor)
    {
        var starts = new DateTime[items.Count];
        var t = anchor;
        for (int i = 0; i < items.Count; i++)
        {
            starts[i] = t;
            t = t.AddSeconds(Math.Max(0, items[i].DurationSeconds));
        }
        return starts;
    }

    /// <param name="currentRemaining">What the current item's countdown shows now (may be negative).</param>
    /// <param name="started">False while the current item is loaded but has never run.</param>
    public static ScheduleStatus Compute(
        IReadOnlyList<RundownItem> items, int current, DateTime anchor,
        DateTime now, TimeSpan currentRemaining, bool started)
    {
        var starts = PlannedStarts(items, anchor);
        var plannedEndCurrent = starts[current].AddSeconds(Math.Max(0, items[current].DurationSeconds));
        var rem = currentRemaining < TimeSpan.Zero ? TimeSpan.Zero : currentRemaining;

        // Not started yet: it can't start before its planned slot, so a pre-show "now"
        // doesn't read as hours ahead of schedule.
        var effectiveNow = !started && now < starts[current] ? starts[current] : now;
        var expectedEndCurrent = effectiveNow + rem;

        var after = 0L;
        for (int i = current + 1; i < items.Count; i++) after += Math.Max(0, items[i].DurationSeconds);
        var plannedEnd = starts.Length == 0 ? anchor : starts[^1].AddSeconds(Math.Max(0, items[^1].DurationSeconds));

        return new ScheduleStatus(
            expectedEndCurrent - plannedEndCurrent,
            expectedEndCurrent.AddSeconds(after),
            plannedEnd);
    }

    /// <summary>"+04:12 behind", "-01:00 ahead", "on time". Under 30 s reads as on time.</summary>
    public static string DescribeOffset(TimeSpan offset)
    {
        var secs = (long)Math.Round(offset.TotalSeconds);
        if (Math.Abs(secs) < 30) return "on time";
        var text = TimerTick.FormatSeconds(Math.Abs(secs));
        return secs > 0 ? $"+{text} behind" : $"-{text} ahead";
    }

    /// <summary>How far either side of "now" a bare HH:MM is assumed to mean.</summary>
    public static readonly TimeSpan ClockWindow = TimeSpan.FromHours(12);

    /// <summary>
    /// "Finish at HH:MM" → the next time the clock shows HH:MM. If that time passed less than
    /// 12 h ago it's a slip (22:00 typed at 22:05), not "tomorrow night": returns null.
    /// So 00:30 typed at 23:30 → in an hour (tomorrow's date); 22:00 typed at 22:05 → rejected.
    /// </summary>
    public static DateTime? ResolveFinishAt(TimeSpan timeOfDay, DateTime nowLocal)
    {
        var candidate = nowLocal.Date + timeOfDay;
        if (candidate > nowLocal) return candidate;
        if (nowLocal - candidate < ClockWindow) return null;
        return candidate.AddDays(1);
    }

    /// <summary>
    /// "Show starts HH:MM" → the occurrence nearest to now (within ±12 h), so a 19:00 start seen at
    /// 00:30 is last night, not tonight. Callers pin the result once the show is running so a long
    /// day can never flip it to the next day.
    /// </summary>
    public static DateTime ResolveShowStart(TimeSpan timeOfDay, DateTime nowLocal)
    {
        var candidate = nowLocal.Date + timeOfDay;
        if (candidate - nowLocal > ClockWindow) return candidate.AddDays(-1);
        if (nowLocal - candidate > ClockWindow) return candidate.AddDays(1);
        return candidate;
    }

    /// <summary>
    /// A time of day as people type it: "14:30", "9:05", "14:30:15", and 12-hour forms
    /// "2:30 pm", "2:30PM", "7pm", "12:15 am".
    /// </summary>
    public static bool TryParseClock(string? s, out TimeSpan timeOfDay)
    {
        timeOfDay = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim().ToLowerInvariant().Replace(".", "");

        int? meridiem = null; // 0 = am, 12 = pm
        foreach (var (suffix, add) in new[] { ("am", 0), ("pm", 12), ("a", 0), ("p", 12) })
        {
            if (!t.EndsWith(suffix, StringComparison.Ordinal)) continue;
            meridiem = add;
            t = t[..^suffix.Length].TrimEnd();
            break;
        }
        if (meridiem is not null && !t.Contains(':') && int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var hourOnly))
            t = hourOnly + ":00";

        var formats = new[] { @"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss" };
        if (!TimeSpan.TryParseExact(t, formats, CultureInfo.InvariantCulture, out timeOfDay)) return false;
        if (timeOfDay < TimeSpan.Zero || timeOfDay >= TimeSpan.FromDays(1)) return false;

        if (meridiem is { } m)
        {
            if (timeOfDay.Hours is < 1 or > 12) return false;          // "13:00 pm" is nonsense
            var h = timeOfDay.Hours % 12 + m;                           // 12 am → 0, 12 pm → 12
            timeOfDay = new TimeSpan(h, timeOfDay.Minutes, timeOfDay.Seconds);
        }
        return true;
    }
}

/// <summary>
/// Import/export the running order as CSV — or tab-separated text pasted straight
/// out of Excel / Google Sheets. Columns are matched by header name when there is a
/// header row, otherwise read in order: Title, Duration, Speaker, Message, Auto.
/// </summary>
public static class RundownCsv
{
    private static readonly string[] TitleNames    = { "title", "segment", "item", "name", "session", "cue", "event" };
    private static readonly string[] DurationNames = { "duration", "length", "time", "mins", "minutes", "dur" };
    private static readonly string[] SpeakerNames  = { "speaker", "presenter", "host", "who", "talent" };
    private static readonly string[] MessageNames  = { "message", "note", "notes", "msg", "cue text" };
    private static readonly string[] AutoNames     = { "auto", "autoadvance", "auto advance", "auto-advance", "link", "linked", "follow" };
    private static readonly string[] KindNames     = { "kind", "type", "segment type" };
    private static readonly string[] FixedNames    = { "fixed start", "fixed", "starts at", "start time", "published" };
    private static readonly string[] MinNames      = { "min", "minimum", "min duration" };
    private static readonly string[] CurfewNames   = { "curfew", "hard out", "hard-out", "hardout" };
    private static readonly string[] GroupNames    = { "group", "block", "act", "part", "section", "segment group" };
    private static readonly string[] LabelNames    = { "labels", "label", "tags", "tag" };

    public sealed record Result(List<RundownItem> Items, List<string> Warnings);

    /// <param name="departments">Columns headed with a department ("Lighting" or "Lighting notes") become that department's notes.</param>
    public static Result Parse(string text, IReadOnlyCollection<string>? departments = null)
    {
        var warnings = new List<string>();
        var items = new List<RundownItem>();
        if (string.IsNullOrWhiteSpace(text)) return new Result(items, warnings);

        var delimiter = DetectDelimiter(text);
        var rows = ReadRows(text, delimiter).Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        if (rows.Count == 0) return new Result(items, warnings);

        // Column map: title, duration, speaker, message, auto, kind, fixed start, min, curfew. -1 = absent.
        int[] map = { 0, 1, 2, 3, 4, 5, 6, 7, 8, -1, -1 };
        var first = rows[0].Select(c => c.Trim().ToLowerInvariant()).ToList();
        int Find(string[] names) => first.FindIndex(c => names.Contains(c));
        var headerTitle = Find(TitleNames);
        var headerDuration = Find(DurationNames);
        var startRow = 0;
        var deptColumns = new List<(int Index, string Dept)>();
        if (headerTitle >= 0 || headerDuration >= 0)
        {
            map = new[] { headerTitle, headerDuration, Find(SpeakerNames), Find(MessageNames), Find(AutoNames),
                          Find(KindNames), Find(FixedNames), Find(MinNames), Find(CurfewNames), Find(GroupNames), Find(LabelNames) };
            startRow = 1;
            foreach (var d in departments ?? Array.Empty<string>())
            {
                var name = d.Trim().ToLowerInvariant();
                if (name.Length == 0) continue;
                var col = first.FindIndex(c => c == name || c == name + " notes" || c == name + " note");
                if (col >= 0 && !map.Contains(col)) deptColumns.Add((col, d.Trim()));
            }
        }

        for (int r = startRow; r < rows.Count; r++)
        {
            var row = rows[r];
            string Cell(int idx) => idx >= 0 && idx < row.Count ? row[idx].Trim() : "";

            var title = Cell(map[0]);
            var durText = Cell(map[1]);
            if (!TryParseDuration(durText, out var dur))
            {
                warnings.Add($"Row {r + 1}: couldn't read duration \"{durText}\" — set to 0.");
                dur = TimeSpan.Zero;
            }
            var auto = Cell(map[4]).ToLowerInvariant() is "1" or "y" or "yes" or "true" or "x" or "auto";
            var kind = Enum.TryParse<SegmentKind>(Cell(map[5]).Replace("&", "").Replace(" ", ""), ignoreCase: true, out var k) ? k : SegmentKind.Talk;
            var fixedStart = NullIfEmpty(Cell(map[6]));
            if (fixedStart is not null && !ShowClock.IsValidTimeText(fixedStart))
            {
                warnings.Add($"Row {r + 1}: fixed start \"{fixedStart}\" isn't a time — ignored.");
                fixedStart = null;
            }
            int? min = TryParseDuration(Cell(map[7]), out var m) ? (int)m.TotalSeconds : null;
            var curfew = NullIfEmpty(Cell(map[8]));
            if (curfew is not null && !ShowClock.IsValidTimeText(curfew))
            {
                warnings.Add($"Row {r + 1}: curfew \"{curfew}\" isn't a time — ignored.");
                curfew = null;
            }

            var item = new RundownItem
            {
                Group = NullIfEmpty(Cell(map[9])),
                Labels = Labels.Parse(Cell(map[10])),
                Title = string.IsNullOrEmpty(title) ? $"Item {items.Count + 1}" : title,
                DurationSeconds = (int)Math.Min(dur.TotalSeconds, int.MaxValue),
                Speaker = NullIfEmpty(Cell(map[2])),
                Message = NullIfEmpty(Cell(map[3])),
                AutoAdvance = auto,
                Kind = kind,
                StartMode = fixedStart is null ? StartMode.Sequential : StartMode.Fixed,
                FixedStart = fixedStart,
                MinDurationSeconds = min,
                HardOut = curfew
            };
            foreach (var (col, dept) in deptColumns)
                if (Cell(col) is { Length: > 0 } note) item.SetDeptNote(dept, note.Length > 1000 ? note[..1000] : note);
            items.Add(item);
        }
        return new Result(items, warnings);
    }

    public static string Export(IEnumerable<RundownItem> source)
    {
        var items = source.ToList();
        var depts = items.SelectMany(i => i.DeptNotes.Keys).Select(k => k.Trim()).Where(k => k.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var sb = new StringBuilder();
        sb.Append("Title,Duration,Speaker,Message,Auto,Kind,Fixed start,Min,Curfew,Group,Labels");
        foreach (var d in depts) sb.Append(',').Append(Quote(d + " notes"));
        sb.Append("\r\n");
        foreach (var i in items)
        {
            sb.Append(Quote(i.Title)).Append(',')
              .Append(TimerTick.FormatSeconds(i.DurationSeconds, forceHours: true)).Append(',')
              .Append(Quote(i.Speaker ?? "")).Append(',')
              .Append(Quote(i.Message ?? "")).Append(',')
              .Append(i.AutoAdvance ? "yes" : "").Append(',')
              .Append(i.Kind).Append(',')
              .Append(Quote(i.StartMode == StartMode.Fixed ? i.FixedStart ?? "" : "")).Append(',')
              .Append(i.MinDurationSeconds is { } min ? TimerTick.FormatSeconds(min, forceHours: true) : "").Append(',')
              .Append(Quote(i.HardOut ?? "")).Append(',')
              .Append(Quote(i.Group ?? "")).Append(',')
              .Append(Quote(string.Join(", ", i.Labels)));
            foreach (var d in depts) sb.Append(',').Append(Quote(i.DeptNote(d)));
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Durations the way people type them in run sheets:
    /// "1:30:00", "45:00", "15" (minutes), "15m", "90s", "1h30m", "1h 30m 15s", "12.5" (minutes).
    /// </summary>
    public static bool TryParseDuration(string? s, out TimeSpan ts)
    {
        ts = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim().ToLowerInvariant();
        var inv = CultureInfo.InvariantCulture;

        if (s.Contains(':'))
        {
            var parts = s.Split(':');
            if (parts.Any(p => !int.TryParse(p, NumberStyles.None, inv, out _))) return false;
            var n = parts.Select(p => int.Parse(p, inv)).ToArray();
            ts = n.Length switch
            {
                2 => new TimeSpan(0, n[0], n[1]),
                3 => new TimeSpan(n[0], n[1], n[2]),
                _ => TimeSpan.Zero
            };
            return n.Length is 2 or 3;
        }

        if (double.TryParse(s, NumberStyles.Float, inv, out var minutes))
        {
            if (minutes < 0) return false;
            ts = TimeSpan.FromMinutes(minutes);
            return true;
        }

        // Unit form: 1h30m, 90s, 1h 30m 15s, 20 min
        var total = TimeSpan.Zero;
        var num = new StringBuilder();
        var any = false;
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsDigit(c) || c == '.') { num.Append(c); i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            var unitStart = i;
            while (i < s.Length && char.IsLetter(s[i])) i++;
            var unit = s[unitStart..i];
            if (num.Length == 0 || !double.TryParse(num.ToString(), NumberStyles.Float, inv, out var v)) return false;
            num.Clear();
            switch (unit)
            {
                case "h": case "hr": case "hrs": case "hour": case "hours": total += TimeSpan.FromHours(v); break;
                case "m": case "min": case "mins": case "minute": case "minutes": total += TimeSpan.FromMinutes(v); break;
                case "s": case "sec": case "secs": case "second": case "seconds": total += TimeSpan.FromSeconds(v); break;
                default: return false;
            }
            any = true;
        }
        if (!any || num.Length > 0) return false;
        ts = total;
        return true;
    }

    private static char DetectDelimiter(string text)
    {
        var firstLine = text.Split('\n')[0];
        if (firstLine.Contains('\t')) return '\t';
        return firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';
    }

    /// <summary>RFC 4180-ish reader: quoted fields, doubled quotes, embedded newlines.</summary>
    private static IEnumerable<List<string>> ReadRows(string text, char delimiter)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }
            if (c == '"' && field.Length == 0) { inQuotes = true; continue; }
            if (c == delimiter) { row.Add(field.ToString()); field.Clear(); continue; }
            if (c == '\r') continue;
            if (c == '\n')
            {
                row.Add(field.ToString()); field.Clear();
                yield return row;
                row = new List<string>();
                continue;
            }
            field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }

    private static string Quote(string s)
        => s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
