using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Stagewatch.Core;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Stagewatch.Output;

/// <summary>
/// Lightweight VM for the output window. Subscribes to the engine on the main thread
/// (the App is responsible for marshalling).
/// </summary>
public sealed class OutputViewModel : INotifyPropertyChanged
{
    private string _timerText = "00:00";
    private string _messageText = "";
    private string _timeOfDayText = "";
    private string _endTimeText = "";
    private string _presetName = "";
    private MessageUrgency _urgency;
    private FlashRate _flash;
    private bool _flashTimer;
    private TimerState _state;
    private TimerMode _mode;
    private bool _watermark;
    private DisplayElements _elements = DisplayElements.Timer | DisplayElements.Message;
    private Brush _background = Brushes.Black;
    private Brush _timerColorBrush = Brushes.White;
    private string _nextText = "";
    private string _scheduleText = "";
    private double _progress = -1;
    private bool _blackout;
    private string _colorHex = "";
    private string _cueLabel = "";
    private string _finishBy = "";
    private string _slidesText = "";
    private string _playoutText = "";
    private bool _preview;
    private bool _stableLayout;
    private bool _cueFrame;
    private bool _progressOnly;
    private ImageSource? _holdingImage;
    private Stretch _holdingStretch = Stretch.Uniform;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string TimerText { get => _timerText; set => Set(ref _timerText, value); }
    public string MessageText { get => _messageText; set => Set(ref _messageText, value); }
    public string TimeOfDayText { get => _timeOfDayText; set => Set(ref _timeOfDayText, value); }
    public string EndTimeText { get => _endTimeText; set => Set(ref _endTimeText, value); }
    public string PresetName { get => _presetName; set => Set(ref _presetName, value); }
    public MessageUrgency Urgency { get => _urgency; set => Set(ref _urgency, value); }
    public FlashRate Flash { get => _flash; set => Set(ref _flash, value); }
    public bool FlashTimer { get => _flashTimer; set => Set(ref _flashTimer, value); }
    public TimerState State { get => _state; set => Set(ref _state, value); }
    public TimerMode Mode { get => _mode; set => Set(ref _mode, value); }
    public bool Watermark { get => _watermark; set => Set(ref _watermark, value); }
    public DisplayElements Elements { get => _elements; set => Set(ref _elements, value); }
    public Brush Background { get => _background; set => Set(ref _background, value); }
    public Brush TimerColorBrush { get => _timerColorBrush; set => Set(ref _timerColorBrush, value); }
    public string NextText { get => _nextText; set => Set(ref _nextText, value); }
    public string ScheduleText { get => _scheduleText; set => Set(ref _scheduleText, value); }
    /// <summary>0..1 of the countdown left; -1 hides the bar. Updated every engine tick for smooth motion.</summary>
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public bool Blackout { get => _blackout; set => Set(ref _blackout, value); }
    /// <summary>"5 MIN" / "WRAP UP": a cue that doesn't rely on colour.</summary>
    public string CueLabel { get => _cueLabel; set => Set(ref _cueLabel, value); }
    public string FinishBy { get => _finishBy; set => Set(ref _finishBy, value); }
    public string SlidesText { get => _slidesText; set => Set(ref _slidesText, value); }
    public string PlayoutText { get => _playoutText; set => Set(ref _playoutText, value); }
    private string _auxText = "";
    /// <summary>The auxiliary timer the operator put on stage ("VIDEO  02:13"), or "".</summary>
    public string AuxText { get => _auxText; set => Set(ref _auxText, value); }
    public bool ShowAux => !string.IsNullOrEmpty(AuxText);
    public bool Preview { get => _preview; set => Set(ref _preview, value); }
    public bool StableLayout { get => _stableLayout; set => Set(ref _stableLayout, value); }
    /// <summary>A coloured frame around the whole output while a cue is in force (visible in peripheral vision).</summary>
    public bool CueFrame { get => _cueFrame; set => Set(ref _cueFrame, value); }
    public bool ProgressOnly { get => _progressOnly; set => Set(ref _progressOnly, value); }
    /// <summary>The holding image on air (decoded and frozen off the UI thread), or null.</summary>
    public ImageSource? HoldingImage { get => _holdingImage; set => Set(ref _holdingImage, value); }
    /// <summary>Uniform = fit (black bars), UniformToFill = fill (crop).</summary>
    public Stretch HoldingStretch { get => _holdingStretch; set => Set(ref _holdingStretch, value); }
    /// <summary>Blackout hides the holding image too.</summary>
    public bool ShowHolding => HoldingImage is not null && !Blackout;

    public bool ShowTimer => Elements.HasFlag(DisplayElements.Timer) && !ProgressOnly;
    public bool ShowMessage => Elements.HasFlag(DisplayElements.Message) && !string.IsNullOrEmpty(MessageText);
    /// <summary>Message area takes space even when empty (stable layout), so the timer never jumps.</summary>
    public bool ReserveMessage => Elements.HasFlag(DisplayElements.Message) && StableLayout && string.IsNullOrEmpty(MessageText);
    public bool ShowCueLabel => (Elements.HasFlag(DisplayElements.CueLabel) || ProgressOnly) && !string.IsNullOrEmpty(CueLabel);
    public bool ShowFinishBy => Elements.HasFlag(DisplayElements.FinishBy) && !string.IsNullOrEmpty(FinishBy);
    public bool ShowSlides => Elements.HasFlag(DisplayElements.Slides) && !string.IsNullOrEmpty(SlidesText);
    public bool ShowPlayout => Elements.HasFlag(DisplayElements.Playout) && !string.IsNullOrEmpty(PlayoutText);
    public bool ShowTimeOfDay => Elements.HasFlag(DisplayElements.TimeOfDay) && Mode != TimerMode.TimeOfDay;
    public bool ShowEndTime => Elements.HasFlag(DisplayElements.EndTime) && !string.IsNullOrEmpty(EndTimeText) && !ShowFinishBy;
    public bool ShowPresetName => Elements.HasFlag(DisplayElements.PresetName) && !string.IsNullOrEmpty(PresetName);
    public bool ShowNextUp => Elements.HasFlag(DisplayElements.NextUp) && !string.IsNullOrEmpty(NextText);
    public bool ShowSchedule => Elements.HasFlag(DisplayElements.Schedule) && !string.IsNullOrEmpty(ScheduleText);
    public bool ShowProgress => (Elements.HasFlag(DisplayElements.ProgressBar) || ProgressOnly) && Progress >= 0;
    public double ProgressHeight => ProgressOnly ? 160 : 18;
    public bool ShowContent => !Blackout;

    /// <summary>
    /// Take a whole frame from the app. Setters no-op on equal values, so applying an
    /// unchanged frame raises no bindings and costs nothing on the render thread.
    /// </summary>
    public void Apply(ShowSnapshot s)
    {
        // The speaker-facing text: calm / honest formats; empty = progress-only.
        ProgressOnly = s.Mode == TimerMode.Countdown && s.SpeakerText.Length == 0;
        TimerText = s.Mode == TimerMode.Countdown && s.SpeakerText.Length > 0 ? s.SpeakerText : s.Text;
        CueLabel = s.CueLabel;
        FinishBy = s.FinishBy;
        SlidesText = s.Slides is { } sl ? $"Slide {sl.Slide} / {sl.Count}" + (sl.NextTitle.Length > 0 ? $"  ·  Next: {sl.NextTitle}" : "") : "";
        AuxText = s.AuxLine;
        PlayoutText = s.Playout is { } p ? $"VT {TimerTick.FormatSeconds(p.RemainingSeconds)}" + (p.Running ? "" : " (paused)") : "";
        Preview = s.Preview;
        StableLayout = s.StableLayout;
        CueFrame = s.Mode == TimerMode.Countdown && s.State != TimerState.Idle && s.Phase != TimerPhase.Normal;
        State = s.State;
        Mode = s.Mode;
        PresetName = s.Title;
        NextText = string.IsNullOrEmpty(s.NextTitle) ? "" : "Next: " + s.NextTitle;
        ScheduleText = s.ScheduleText;
        EndTimeText = s.EndTimeText;
        TimeOfDayText = s.TimeOfDay;
        MessageText = s.Message;
        Urgency = s.Urgency;
        Flash = s.Flash;
        FlashTimer = s.FlashTimer;
        Blackout = s.Blackout;
        Elements = s.Elements;
        if (s.ColorHex != _colorHex)
        {
            _colorHex = s.ColorHex;
            TimerColorBrush = ParseBrush(s.ColorHex);
        }
    }

    public static SolidColorBrush ParseBrush(string hex)
    {
        SolidColorBrush b;
        try { b = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!); }
        catch { b = new SolidColorBrush(Colors.White); }
        b.Freeze();
        return b;
    }

    public Color UrgencyColor => Urgency switch
    {
        MessageUrgency.Info => Color.FromRgb(0xE2, 0xE8, 0xF0),
        MessageUrgency.Warning => Color.FromRgb(0xF5, 0x9E, 0x0B),
        MessageUrgency.Critical => Color.FromRgb(0xDC, 0x26, 0x26),
        _ => Colors.White
    };

    public Color TimerColor => State switch
    {
        TimerState.Expired => Color.FromRgb(0xDC, 0x26, 0x26),
        _ => Colors.White
    };

    private bool _lastProgressHidden = true;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name is nameof(Elements) or nameof(MessageText) or nameof(EndTimeText) or nameof(PresetName) or nameof(Mode)
            or nameof(NextText) or nameof(ScheduleText) or nameof(Blackout) or nameof(CueLabel) or nameof(FinishBy)
            or nameof(SlidesText) or nameof(PlayoutText) or nameof(StableLayout) or nameof(ProgressOnly) or nameof(AuxText))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowAux)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReserveMessage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowCueLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowFinishBy)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowSlides)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowPlayout)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressHeight)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowNextUp)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowSchedule)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowProgress)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowContent)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowTimer)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowMessage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowTimeOfDay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowEndTime)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowPresetName)));
        }
        if (name is nameof(Progress) && (field is double d) && (d < 0) != (_lastProgressHidden))
        {
            _lastProgressHidden = d < 0;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowProgress)));
        }
        if (name is nameof(HoldingImage) or nameof(Blackout))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowHolding)));
        if (name is nameof(Urgency))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UrgencyColor)));
        if (name is nameof(State))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimerColor)));
    }
}
