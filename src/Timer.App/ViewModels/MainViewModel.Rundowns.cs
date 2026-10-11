using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Stagewatch.Core;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// Several running orders in one show (Day 1 / Day 2, the rehearsal version, a backup plan). One is live; the others
/// can be edited at any time, even while the live one runs. Switching ends the live item cleanly, journals it, and
/// starts the new running order from the top (its own planned start, groups and notes come with it).
/// </summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<string> RundownNames { get; } = new();
    [ObservableProperty] private string _liveRundownName = "Main";

    private void RefreshRundownNames()
    {
        var names = new[] { Settings.RundownName }.Concat(Settings.OtherRundowns.Select(r => r.Name)).ToList();
        if (!names.SequenceEqual(RundownNames))
        {
            RundownNames.Clear();
            foreach (var n in names) RundownNames.Add(n);
        }
        LiveRundownName = Settings.RundownName;
    }

    private NamedRundown? Other(string name)
        => Settings.OtherRundowns.FirstOrDefault(r => string.Equals(r.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private bool NameTaken(string name)
        => string.Equals(Settings.RundownName, name.Trim(), StringComparison.OrdinalIgnoreCase) || Other(name) is not null;

    /// <summary>Make another running order live. Returns false (with a status line) when it can't.</summary>
    public bool SwitchRundown(string name)
    {
        var ok = _show.SwitchRundown(name);
        if (ok)
        {
            OnRundownEdited();
            RefreshRundownNames();
            RefreshUndo();
        }
        return ok;
    }

    public bool NewRundown(string name, bool duplicateLive)
    {
        name = name.Trim();
        if (!NamedRundown.ValidName(name) || NameTaken(name)) { StatusLine = "Pick a new name (up to 40 characters)"; return false; }
        var rundown = duplicateLive ? Settings.Rundown.Clone() : new Rundown { TimeZoneId = Settings.Rundown.TimeZoneId };
        if (duplicateLive) foreach (var i in rundown.Items) { i.Id = Guid.NewGuid().ToString("N")[..10]; i.AdjustSeconds = 0; }
        rundown.Id = Rundown.NewId();   // a copy is a different running order: its history is its own
        _store.Update(s => s.OtherRundowns.Add(new NamedRundown { Name = name, Rundown = rundown }));
        RefreshRundownNames();
        StatusLine = $"Running order \"{name}\" added (not live)";
        return true;
    }

    public bool RenameRundown(string oldName, string newName)
    {
        newName = newName.Trim();
        if (!NamedRundown.ValidName(newName)) return false;
        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase) && NameTaken(newName)) { StatusLine = $"\"{newName}\" is taken"; return false; }
        _store.Update(s =>
        {
            if (string.Equals(s.RundownName, oldName, StringComparison.OrdinalIgnoreCase)) s.RundownName = newName;
            else if (s.OtherRundowns.FirstOrDefault(r => string.Equals(r.Name, oldName, StringComparison.OrdinalIgnoreCase)) is { } r) r.Name = newName;
        });
        _show.RundownRenamed(oldName, newName);
        RefreshRundownNames();
        return true;
    }

    public bool DeleteRundown(string name)
    {
        if (string.Equals(name, Settings.RundownName, StringComparison.OrdinalIgnoreCase)) { Refuse("The live running order can't be deleted — switch to another first"); return false; }
        _store.Update(s => s.OtherRundowns.RemoveAll(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)));
        _show.RundownDeleted(name);
        RefreshRundownNames();
        StatusLine = $"Running order \"{name}\" deleted";
        return true;
    }

    public bool IsLiveRundown(string name) => string.Equals(name, Settings.RundownName, StringComparison.OrdinalIgnoreCase);

    /// <summary>True while something from the live running order is on the clock (switching would interrupt it).</summary>
    public bool RundownBusy => _show.RundownActive && (_engine.IsCounting || _show.ItemStarted);
}
