using Stagewatch.Core;

namespace Stagewatch.Companion;

/// <summary>
/// The thin surface between the protocol server and the rest of the app.
/// The app implements this; the server calls it.
/// </summary>
public interface ICompanionBridge : IShowControl
{
    TimerEngine Engine { get; }
    AppSettings Settings { get; }
    /// <summary>False in Free mode (trial ended): clients are told why and disconnected.</summary>
    bool CompanionAllowed { get; }
    void LoadPreset(int slot, bool autoStart);
    void SetMessage(string text, MessageUrgency urgency, FlashRate flash);
    void SetFlash(FlashRate rate);
    void SetTimerFlash(bool flashTimer);
    event Action<MessageState>? MessageChanged;
    /// <summary>
    /// The operator did something in Stagewatch itself, as a command line ("nudge 60", "next"…).
    /// Sent to Companion as RECORD lines so a button can be programmed by simply doing it.
    /// </summary>
    event Action<string>? CommandRecorded;
}
