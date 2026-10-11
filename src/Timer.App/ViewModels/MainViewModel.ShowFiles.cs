using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Stagewatch.Core;

namespace Stagewatch.App.ViewModels;

/// <summary>Show files: one .stagewatch per event (presets, running order, quick messages).</summary>
public sealed partial class MainViewModel
{
    [ObservableProperty] private string _windowTitle = "Stagewatch";

    public string ShowName => string.IsNullOrWhiteSpace(Settings.ShowName) ? "Untitled show" : Settings.ShowName;
    public string? ShowPath => Settings.ShowPath;

    public static string DefaultShowFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Stagewatch Shows");

    private void UpdateWindowTitle() => WindowTitle = $"Stagewatch — {ShowName}";

    /// <summary>Save the current presets, running order and quick messages. Returns an error, or null.</summary>
    public string? SaveShow(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            ShowFile.FromSettings(Settings, name).Save(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
        _store.Update(s => { s.ShowPath = path; s.ShowName = name; });
        UpdateWindowTitle();
        StatusLine = $"Show saved: {name}";
        return null;
    }

    /// <summary>Open a show file, replacing presets, running order and quick messages. The clock is untouched.</summary>
    public string? OpenShow(string path)
    {
        ShowFile show;
        try { show = ShowFile.Load(path); }
        catch (InvalidDataException ex) { return ex.Message; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }

        _store.Update(s =>
        {
            show.ApplyTo(s);
            s.ShowPath = path;
            s.ShowName = show.Name;
        });
        _store.SaveNow();

        MessageBank.Clear();
        foreach (var e in Settings.MessageBank.Entries) MessageBank.Add(e);
        Presets.Clear();
        foreach (var p in Settings.Presets) Presets.Add(p);

        // A new show starts at the top of its running order, with a fresh undo history.
        _show.NewShow();
        OnRundownEdited();   // rebuilds rows + Starts box, bumps remotes/Companion
        UpdateWindowTitle();
        StatusLine = $"Opened show: {show.Name} — {show.Rundown.Items.Count} running-order items";
        return null;
    }
}
