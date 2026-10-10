using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stagewatch.Core;
using Stagewatch.Integrations;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// Voice and words for the copilot: push-to-talk (hold F9 or the mic button, speak, let go — it becomes a request that
/// still waits for approval) and message drafts that fit the moment (picked and edited by the operator, never sent by AI).
/// </summary>
public sealed partial class MainViewModel
{
    private PushToTalk? _ptt;
    private System.Windows.Threading.DispatcherTimer? _captionTimer;

    [ObservableProperty] private bool _talking;
    [ObservableProperty] private string _talkCaption = "";

    /// <summary>Start listening for a spoken copilot request.</summary>
    public void BeginTalk()
    {
        if (Talking) return;
        if (CopilotUnavailable is { } why) { CopilotStatus = why; StatusLine = why; return; }
        _ptt ??= new PushToTalk(() => new WindowsTranscriber(Settings.Integrations.SpeechCulture));
        if (_ptt.Begin() is { } error) { CopilotStatus = "Can't listen: " + error; StatusLine = CopilotStatus; return; }
        Talking = true;
        TalkCaption = "Listening… let go when you've finished";
        _captionTimer ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(250), System.Windows.Threading.DispatcherPriority.Background,
            (_, _) => { if (Talking && _ptt?.Heard is { Length: > 0 } h) TalkCaption = "“" + h + "”"; }, _ui);
        _captionTimer.Start();
    }

    /// <summary>Stop listening; what was said goes to the copilot as a request (a plan for approval, never an action).</summary>
    public async Task EndTalk()
    {
        if (!Talking || _ptt is null) return;
        TalkCaption = "…";
        var said = await _ptt.EndAsync();
        _captionTimer?.Stop();
        Talking = false;
        if (said.Length < 3) { TalkCaption = "Didn't catch that — hold F9 and speak a little longer."; return; }
        TalkCaption = "“" + said + "”";
        await RunCopilot(said, CopilotMode.Ask, "You said");
    }

    [ObservableProperty] private bool _draftingMessages;

    /// <summary>Three short speaker messages for this moment (the operator picks one into the message box).</summary>
    public async Task<IReadOnlyList<string>> DraftMessagesAsync()
    {
        if (CopilotClient() is not { } copilot) { StatusLine = CopilotUnavailable ?? "Copilot unavailable"; return Array.Empty<string>(); }
        DraftingMessages = true;
        try
        {
            var context = CopilotContext();
            var typed = MessageInput ?? "";
            var drafts = await Task.Run(() => copilot.DraftMessagesAsync(context, typed));
            _journal.Add(DateTime.UtcNow, JournalKind.Note, _rundownActive ? _currentItem : -1, $"Copilot suggested messages: {string.Join(" | ", drafts)}");
            if (drafts.Count == 0) StatusLine = "The copilot had no suggestions — try again";
            return drafts;
        }
        finally { DraftingMessages = false; }
    }
}
