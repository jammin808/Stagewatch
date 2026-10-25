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
        _apiKeys = Settings.Remote.ApiKeys.Select(k => new ApiKey { Id = k.Id, Name = k.Name, Scope = k.Scope, Hash = k.Hash, Hint = k.Hint, CreatedUtc = k.CreatedUtc, Node = k.Node, NodeKind = k.NodeKind }).ToArray();
        _nodes.Keep(_apiKeys);   // a revoked key speaks for nothing; a pair saved on a key is put back
        _screenNames = new Dictionary<string, string>(Settings.Remote.ScreenNames, StringComparer.OrdinalIgnoreCase);
        _screenPresets = Settings.Remote.ScreenPresets
            .Where(p => ScreenPreset.ValidName(p.Name) && ScreenPreset.ValidPath(p.Path))
            .Select(p => (p.Name, p.Path)).ToList();
        _web?.CredentialsChanged();   // a revoked key, a moved binding or a new PIN reaches the sockets already open, at once
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

    /// <summary>Connected screens: browsers and claims (empty when the web server is off).</summary>
    public IReadOnlyList<ScreenInfo> ListScreens() => _web?.ListScreens() ?? Array.Empty<ScreenInfo>();

    /// <summary>Show apps following over /ws, never screens (empty when the web server is off).</summary>
    public IReadOnlyList<NodeLinkFact> ListNodes() => _web?.ListNodes() ?? Array.Empty<NodeLinkFact>();

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

    /// <summary>Revoke an API key: the next request with it is refused, and a socket it opened drops to the public frame at once.</summary>
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

    /// <summary>
    /// The <c>screen …</c> command (from here, Companion, HTTP, OSC, triggers or the AI copilot): browsers only, in
    /// <see cref="ScreenDirector"/>'s fixed order; a show app named by its id or its name is refused as one.
    /// </summary>
    private void ApplyScreenCommand(ScreenArgs args)
    {
        var (ok, words) = ScreenDirector.Run(args, _web, PeerNameOf, RenameScreen, SaveScreenPreset, ResolveScreenTarget);
        if (ok) StatusLine = words;
        else Refuse(words);
    }
}
