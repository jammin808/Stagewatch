using Stagewatch.Core;
using Stagewatch.Remote;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// Screens: every browser showing Stagewatch, named by the operator, findable ("identify") and movable to another
/// page or a saved screen setup from here — no walking to the lobby TV. The web server reads names and presets from
/// immutable copies swapped in on change, so its threads never touch the live settings.
/// </summary>
public sealed partial class MainViewModel
{
    private volatile IReadOnlyDictionary<string, string> _screenNames = new Dictionary<string, string>();
    private volatile IReadOnlyList<(string Name, string Path)> _screenPresets = Array.Empty<(string, string)>();

    private void RefreshScreenCopies()
    {
        _customCss = Settings.Remote.CustomCss ?? "";
        _viewCode = (Settings.Remote.ViewCode ?? "").Trim();
        _layouts = Settings.Remote.Layouts.ToDictionary(l => l.Name, ScreenLayouts.ToJson, StringComparer.OrdinalIgnoreCase);
        _apiKeys = Settings.Remote.ApiKeys.Select(k => new ApiKey { Id = k.Id, Name = k.Name, Scope = k.Scope, Hash = k.Hash, Hint = k.Hint, CreatedUtc = k.CreatedUtc }).ToArray();
        _screenNames = new Dictionary<string, string>(Settings.Remote.ScreenNames, StringComparer.OrdinalIgnoreCase);
        _screenPresets = Settings.Remote.ScreenPresets
            .Where(p => ScreenPreset.ValidName(p.Name) && ScreenPreset.ValidPath(p.Path))
            .Select(p => (p.Name, p.Path)).ToList();
    }

    private volatile IReadOnlyList<ApiKey> _apiKeys = Array.Empty<ApiKey>();
    public IReadOnlyList<ApiKey> ApiKeys => _apiKeys;

    /// <summary>When a key was last used (this session), for the Settings list.</summary>
    public DateTime? ApiKeyLastUsed(string id) => _web?.KeyLastUsed(id);

    private volatile string _viewCode = "";
    public string? ViewCode => _viewCode;
    private volatile string _customCss = "";
    public string CustomCss => _customCss;

    public string? ScreenName(string screenId) => _screenNames.TryGetValue(screenId, out var n) ? n : null;

    public string? ScreenPresetPath(string name)
        => _screenPresets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Name: not null } hit && hit.Name.Length > 0 ? hit.Path : null;

    IReadOnlyList<(string Name, string Path)> IShowScreens.ScreenPresets => _screenPresets;

    /// <summary>Connected screens (empty when the web server is off).</summary>
    public IReadOnlyList<ScreenInfo> ListScreens() => _web?.ListScreens() ?? Array.Empty<ScreenInfo>();

    public bool ScreensAvailable => _web is not null;

    /// <summary>The address for a saved setup on this PC's network, e.g. http://192.168.1.20:9924/p/lobby.</summary>
    public string? PresetAddress(string name) => _web is null ? null : $"http://{(Settings.Remote.WebBindAll ? LanAddresses().FirstOrDefault() ?? "localhost" : "localhost")}:{_web.Port}/p/{Uri.EscapeDataString(name)}";

    public void RenameScreen(string id, string name)
    {
        name = name.Trim();
        if (!Screens.ValidId(id)) return;
        _store.Update(s => { if (name.Length == 0) s.Remote.ScreenNames.Remove(id); else s.Remote.ScreenNames[id] = name.Length > 40 ? name[..40] : name; });
        RefreshScreenCopies();
    }

    public void SaveScreenPreset(string name, string path)
    {
        if (!ScreenPreset.ValidName(name) || !ScreenPreset.ValidPath(path)) return;
        _store.Update(s =>
        {
            s.Remote.ScreenPresets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            s.Remote.ScreenPresets.Add(new ScreenPreset { Name = name, Path = path });
        });
        RefreshScreenCopies();
    }

    /// <summary>Make an API key; it works at once. Returns the secret (shown once), or null when there are too many.</summary>
    public string? CreateApiKey(string name, ApiKeyScope scope)
    {
        if (Settings.Remote.ApiKeys.Count >= Stagewatch.Core.ApiKeys.Max) return null;
        var (key, secret) = Stagewatch.Core.ApiKeys.Create(name, scope, DateTime.UtcNow);
        _store.Update(s => s.Remote.ApiKeys.Add(key));
        RefreshScreenCopies();
        _journal.Add(DateTime.UtcNow, JournalKind.Note, -1, $"API key made: {key.Name} ({Stagewatch.Core.ApiKeys.Describe(scope)})");
        return secret;
    }

    /// <summary>Revoke an API key: the next request with it is refused.</summary>
    public void RevokeApiKey(string id)
    {
        var name = Settings.Remote.ApiKeys.FirstOrDefault(k => k.Id == id)?.Name;
        _store.Update(s => s.Remote.ApiKeys.RemoveAll(k => k.Id == id));
        RefreshScreenCopies();
        if (name is not null) _journal.Add(DateTime.UtcNow, JournalKind.Note, -1, $"API key revoked: {name}");
    }

    public void DeleteScreenPreset(string name)
    {
        _store.Update(s => s.Remote.ScreenPresets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        RefreshScreenCopies();
    }

    /// <summary>A preset name or a page → the page to open, or null.</summary>
    public string? ResolveScreenTarget(string target)
        => ScreenPreset.ValidPath(target) ? target : ScreenPresetPath(target);

    /// <summary>The <c>screen …</c> command (from here, Companion, HTTP, OSC, triggers or the AI copilot).</summary>
    private void ApplyScreenCommand(ScreenArgs args)
    {
        var (action, arg, who) = (args.Action, args.Argument, args.Who);
        if (_web is null) { Refuse("Screens: turn on Settings → Remote first"); return; }
        var screens = _web.ListScreens();

        if (action == "name")
        {
            var target = screens.FirstOrDefault(s => s.Id.Equals(who, StringComparison.OrdinalIgnoreCase));
            if (target is null && !Screens.ValidId(who)) { Refuse($"Screens: no screen with id {who}"); return; }
            RenameScreen(target?.Id ?? who, arg);
            StatusLine = arg.Length > 0 ? $"Screen {who} is now \"{arg}\"" : $"Screen {who}: name cleared";
            return;
        }

        var hits = Screens.Match(screens, who);
        if (hits.Count == 0) { Refuse($"Screens: nothing matches \"{who}\""); return; }
        switch (action)
        {
            case "identify":
                _web.Identify(hits.Select(h => h.Id));
                StatusLine = $"Identifying {Count(hits.Count)}";
                break;
            case "reload":
                _web.Reload(hits.Select(h => h.Id));
                StatusLine = $"Reloading {Count(hits.Count)}";
                break;
            case "disconnect":
                _web.Disconnect(hits.Select(h => h.Id));
                StatusLine = $"Disconnected {Count(hits.Count)}";
                break;
            case "go":
                if (ResolveScreenTarget(arg) is not { } path) { Refuse($"Screens: no saved setup called \"{arg}\""); return; }
                _web.Redirect(hits.Select(h => h.Id), path);
                StatusLine = $"Sent {Count(hits.Count)} to {arg}";
                break;
            case "save":
                SaveScreenPreset(arg, hits[0].Page);
                StatusLine = $"Saved \"{arg}\" = {hits[0].Page}";
                break;
        }

        static string Count(int n) => n == 1 ? "1 screen" : $"{n} screens";
    }
}
