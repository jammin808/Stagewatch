using Stagewatch.Core;
using Stagewatch.Licensing;
using Stagewatch.Show;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// The view model as the show controller's host: licensing, words for the operator, and the side effects that belong
/// at the edges — screens, triggers, chimes, the overrun history. The controller owns the show's state; this file is
/// the whole of what it asks of the UI.
/// </summary>
public sealed partial class MainViewModel : IShowHost
{
    bool IShowHost.Allow(ShowFeature feature) => feature switch
    {
        ShowFeature.RunningOrder => Gate(FeatureGate.AllowsRunOfShow(_license.Tier), "The running order", FeatureTier.Pro),
        ShowFeature.Recovery => Gate(FeatureGate.AllowsRunOfShow(_license.Tier), "Schedule recovery", FeatureTier.Pro),
        ShowFeature.Roll => Gate(FeatureGate.AllowsRunOfShow(_license.Tier), "Roll", FeatureTier.Pro),
        _ => false,
    };

    void IShowHost.Refused(string why) => Refuse(why);

    void IShowHost.Status(string line) => StatusLine = line;

    void IShowHost.ItemLoaded(int index, RundownItem item)
    {
        EndAtInput = "";
        if (!string.IsNullOrWhiteSpace(item.Message))
        {
            MessageInput = item.Message;
            SetMessage(item.Message, MessageUrgency.Info, FlashRate.Off);
        }
        RefreshRundownHighlight();
    }

    void IShowHost.ItemStarted(int index, RundownItem item, bool firstOfShow)
    {
        if (firstOfShow) FireTrigger(TriggerEvent.ShowStart, "", item.Title);
        if (FeatureGate.AllowsAlerts(_license.Tier)) PlayChime(ChimeFor("start", Settings.Alerts.OnStart), Settings.Alerts.Device ?? "");
        FireTrigger(TriggerEvent.ItemStart, ItemFilters(index), item.Title);
    }

    void IShowHost.ItemEnded(int index, RundownItem item, TimeSpan ran)
    {
        _history.Add(new HistoryRecord(item.Kind, item.Speaker ?? "", item.EffectiveSeconds, (int)Math.Round(ran.TotalSeconds), DateTime.UtcNow));
        _model = OverrunModel.Learn(_history.Records);
        FireTrigger(TriggerEvent.ItemEnd, ItemFilters(index), item.Title);
    }

    void IShowHost.RundownChanged() => RebuildRundownRows();

    void IShowHost.ConfigChanged() => BumpConfig();

    void IShowHost.DeptNotesChanged() => RefreshNotesCopy();

    void IShowHost.ControlsChanged()
    {
        RefreshUndo();
        RollMode = _show.RollMode;
        RollLine = _show.RollLine;
    }

    void IShowHost.Changed(bool publishNow)
    {
        MarkDirty();
        if (publishNow) Publish();
    }
}
