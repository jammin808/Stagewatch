using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Licensing;

namespace Stagewatch.App;

public partial class SettingsDialog : Window
{
    private readonly SettingsStore _store;
    private readonly LicenseStatus _license;

    private readonly Func<string>? _diagnostics;
    private readonly Func<IReadOnlyList<(string Name, LinkHealth Health, string Status)>>? _integrations;
    private readonly ObservableCollection<TriggerRule> _triggers = new();

    private readonly Func<string?>? _fingerprint;

    private void OnCertificatePage(object sender, RoutedEventArgs e)
    {
        var port = int.TryParse(WebPort.Text, out var p) ? p : _store.Settings.Remote.WebPort;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"http://localhost:{port}/tls") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    public SettingsDialog(SettingsStore store, LicenseStatus license, Func<string>? diagnostics = null,
        Func<IReadOnlyList<(string Name, LinkHealth Health, string Status)>>? integrations = null,
        Func<IReadOnlyList<PresenterPc>>? presenters = null, Func<string?>? fingerprint = null)
    {
        InitializeComponent();
        Help.ContextHelp.Attach(this);
        _store = store;
        _license = license;
        _diagnostics = diagnostics;
        _integrations = integrations;
        _presenters = presenters;
        _fingerprint = fingerprint;
        DiagnosticsButton.IsEnabled = diagnostics is not null;
        Load();
        LoadV2();
    }

    private readonly Func<IReadOnlyList<PresenterPc>>? _presenters;

    /// <summary>The chime library lives with the app (files beside the settings): the dialog uses these hooks.</summary>
    public sealed record SoundHooks(Func<string, (SoundEntry? Entry, string? Error)> Add, Action<string> Remove, Action<string> Preview);
    public SoundHooks? Sounds { get; init; }

    private sealed record SoundRow(string Id, string Label);

    /// <summary>
    /// API keys change at once (revoking must not wait for Save): the dialog uses these hooks. <see cref="Binding"/> says
    /// which node a node key speaks for, <see cref="Choices"/> offers the nodes a key may speak for, and <see cref="Rebind"/>
    /// lets it speak for one (the same rebind as a Show network row's "Let a key speak for this node…").
    /// </summary>
    public sealed record KeyHooks(Func<string, ApiKeyScope, string?> Create, Action<string> Revoke, Func<string, DateTime?> LastUsed,
        Func<string, string>? Binding = null, Func<string, IReadOnlyList<NodeChoice>>? Choices = null,
        Func<string, string, string, (bool Ok, string Words)>? Rebind = null);

    private readonly KeyHooks? _keys;

    /// <summary>Set after the dialog is built: the key list is drawn again with what the hooks know (last use, which node each speaks for).</summary>
    public KeyHooks? Keys
    {
        get => _keys;
        init { _keys = value; RefreshApiKeys(); }
    }

    private sealed record KeyRow(string Id, string Name, string Label, bool IsNode);

    private void RefreshApiKeys()
    {
        var selected = (ApiKeyList.SelectedItem as KeyRow)?.Id;
        var rows = _store.Settings.Remote.ApiKeys.Select(k =>
        {
            var used = Keys?.LastUsed(k.Id) is { } t ? "used " + t.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "not used yet";
            var speaks = Keys?.Binding?.Invoke(k.Id) is { Length: > 0 } line ? "  ·  " + line : "";
            return new KeyRow(k.Id, k.Name, $"{k.Name}  ·  {ApiKeys.Describe(k.Scope)}  ·  …{k.Hint}  ·  {used}{speaks}", k.Scope == ApiKeyScope.Node);
        }).ToList();
        ApiKeyList.ItemsSource = rows;
        ApiKeyList.SelectedItem = rows.FirstOrDefault(r => r.Id == selected);
        SyncKeyActions();
    }

    /// <summary>The key buttons and the list's right-click menu are enabled together, by the same rule.</summary>
    private void SyncKeyActions()
    {
        var row = ApiKeyList.SelectedItem as KeyRow;
        var canSpeak = row is { IsNode: true } && Keys?.Rebind is not null;
        ApiKeySpeakButton.IsEnabled = canSpeak;
        ApiKeyRevokeButton.IsEnabled = row is not null && Keys is not null;
        if (ApiKeyList.ContextMenu is { } menu)
            foreach (var item in menu.Items.OfType<MenuItem>())
                item.IsEnabled = item.Tag switch { "speak" => canSpeak, "revoke" => row is not null && Keys is not null, _ => Keys is not null };
    }

    private void OnApiKeySelected(object sender, SelectionChangedEventArgs e) => SyncKeyActions();

    private void OnApiKeyMenuOpened(object sender, RoutedEventArgs e) => SyncKeyActions();

    /// <summary>
    /// Let the selected node key speak for a node: one it was refused for, one heard on the show network, or a typed id.
    /// The node moves off any key that spoke for it; the pair is saved and journalled at once.
    /// </summary>
    private void OnLetKeySpeak(object sender, RoutedEventArgs e)
    {
        if (ApiKeyList.SelectedItem is not KeyRow { IsNode: true } r || Keys?.Rebind is not { } rebind) return;
        var choices = Keys.Choices?.Invoke(r.Id) ?? Array.Empty<NodeChoice>();
        var now = Keys.Binding?.Invoke(r.Id) is { Length: > 0 } line ? $"Now: {line}. " : "";
        var panel = new StackPanel { Margin = new Thickness(20), MaxWidth = 520 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Which node may key '{r.Name}' speak for? {now}From then on that node's context, proposals and results are taken only with this key. " +
                   "The node this key speaks for now, and any key that spoke for the one you choose, are left with no key.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
        });
        var list = new ListBox { MaxHeight = 200, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var c in choices) list.Items.Add(new ListBoxItem { Content = c.Label, Tag = c });
        if (list.Items.Count > 0) { list.SelectedIndex = 0; panel.Children.Add(list); }
        else panel.Children.Add(new TextBlock { Text = "No node heard or refused yet.", Opacity = 0.6, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(new TextBlock { Text = "…or type its node id, and its kind if you know it (else its first request names it):", Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        var typedId = new TextBox { Width = 220, MaxLength = 40, Margin = new Thickness(0, 0, 8, 0) };
        var typedKind = new ComboBox { Width = 140, IsEditable = true };
        foreach (var k in new[] { "", "patterns", "playmap", "messenger", "main" }) typedKind.Items.Add(k);
        var typed = new StackPanel { Orientation = Orientation.Horizontal };
        typed.Children.Add(typedId);
        typed.Children.Add(typedKind);
        panel.Children.Add(typed);
        if (!AskDialog(panel, "Let it speak for…", "Let it speak")) return;
        string id, kind;
        if (typedId.Text.Trim().Length > 0) { id = typedId.Text.Trim(); kind = typedKind.Text.Trim().ToLowerInvariant(); }
        else if (list.SelectedItem is ListBoxItem { Tag: NodeChoice chosen }) { id = chosen.Id; kind = chosen.Kind; }
        else return;
        var (_, words) = rebind(r.Id, kind, id);
        ApiKeyNote.Text = words;
        ApiKeyNote.Visibility = Visibility.Visible;
        RefreshApiKeys();
    }

    private void OnNewApiKey(object sender, RoutedEventArgs e)
    {
        if (Keys is null) return;
        var name = new TextBox { Width = 260, Text = "Stream Deck", Margin = new Thickness(0, 0, 0, 8) };
        var scope = new ComboBox { Width = 260 };
        foreach (var sc in Enum.GetValues<ApiKeyScope>()) scope.Items.Add(new ComboBoxItem { Content = ApiKeys.Describe(sc), Tag = sc });
        scope.SelectedIndex = 0;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Name (who or what uses it)", Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(name);
        panel.Children.Add(new TextBlock { Text = "What it may do", Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(scope);
        if (!AskDialog(panel, "New API key", "Make key")) return;
        var chosen = (scope.SelectedItem as ComboBoxItem)?.Tag is ApiKeyScope s ? s : ApiKeyScope.Read;
        var secret = Keys.Create(name.Text, chosen);
        RefreshApiKeys();
        if (secret is null) { MessageBox.Show(this, $"There are already {ApiKeys.Max} keys — revoke one first.", "API keys"); return; }
        var shown = new StackPanel { Margin = new Thickness(20), MaxWidth = 460 };
        shown.Children.Add(new TextBlock { Text = "Copy this key now — Stagewatch keeps only a fingerprint of it and can't show it again.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        shown.Children.Add(new TextBox { Text = secret, IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 0, 0, 8) });
        try { Clipboard.SetText(secret); shown.Children.Add(new TextBlock { Text = "(Already copied to the clipboard.)", Opacity = 0.6 }); } catch { }
        AskDialog(shown, "Your new API key", "Done", cancel: false);
    }

    private void OnRevokeApiKey(object sender, RoutedEventArgs e)
    {
        if (ApiKeyList.SelectedItem is not KeyRow r || Keys is null) return;
        Keys.Revoke(r.Id);
        RefreshApiKeys();
    }

    /// <summary>A small modal with OK (and Cancel) around some content.</summary>
    private bool AskDialog(UIElement content, string title, string ok, bool cancel = true)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(20, 0, 20, 16) };
        var win = new Window { Title = title, Owner = this, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        var okButton = new Button { Content = ok, IsDefault = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        okButton.Click += (_, _) => win.DialogResult = true;
        if (cancel) buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 });
        buttons.Children.Add(okButton);
        var root = new StackPanel();
        root.Children.Add(content);
        root.Children.Add(buttons);
        win.Content = root;
        var answered = win.ShowDialog() == true;
        // The content may be asked again (a refused edit keeps what was typed): it leaves this window with it.
        root.Children.Clear();
        return answered;
    }

    private void RefreshSounds()
        => SoundList.ItemsSource = _store.Settings.Sounds.Select(x => new SoundRow(x.Id, $"{x.Name}  ({x.Seconds:0.0} s)")).ToList();

    private void OnAddSound(object sender, RoutedEventArgs e)
    {
        if (Sounds is null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "WAV sound (*.wav)|*.wav", Multiselect = true };
        if (dlg.ShowDialog(this) != true) return;
        var errors = new List<string>();
        foreach (var f in dlg.FileNames)
            if (Sounds.Add(f) is { Error: { } err }) errors.Add($"{System.IO.Path.GetFileName(f)}: {err}");
        RefreshSounds();
        if (errors.Count > 0) MessageBox.Show(this, string.Join("\n", errors) + "\n\n(MP3? Save it as WAV first — e.g. with Audacity.)", "Chime library");
    }

    private void OnPlaySound(object sender, RoutedEventArgs e)
    {
        if (SoundList.SelectedItem is SoundRow r) Sounds?.Preview("sound:" + r.Id);
    }

    private void OnRemoveSound(object sender, RoutedEventArgs e)
    {
        if (SoundList.SelectedItem is not SoundRow r || Sounds is null) return;
        Sounds.Remove(r.Id);
        RefreshSounds();
    }

    private sealed record IntegrationRow(string Name, string Status, System.Windows.Media.Brush Brush);

    private void LoadV2()
    {
        var s = _store.Settings;
        var d = s.Display;
        ShowCueLabel.IsChecked = d.Elements.HasFlag(DisplayElements.CueLabel);
        ShowFinishBy.IsChecked = d.Elements.HasFlag(DisplayElements.FinishBy);
        ShowSlides.IsChecked = d.Elements.HasFlag(DisplayElements.Slides);
        ShowPlayout.IsChecked = d.Elements.HasFlag(DisplayElements.Playout);

        LadderCombo.ItemsSource = d.AllLadders();
        LadderCombo.SelectedValue = string.IsNullOrEmpty(d.CueLadderId) ? "standard" : d.CueLadderId;
        SpeakerFormatCombo.SelectedItem = SpeakerFormatCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == d.SpeakerFormat.ToString())
                                          ?? SpeakerFormatCombo.Items[0];
        HonestClock.IsChecked = d.HonestSpeakerClock;
        StableLayoutBox.IsChecked = d.StableLayout;
        HoloOn.IsChecked = d.Holographic;
        HoloFloorOn.IsChecked = d.HoloFloor;
        SceneEgg.IsChecked = d.EggTimer;
        SceneHolo.IsChecked = !d.EggTimer;
        Anim3DOn.IsChecked = d.Animations3D;
        Particles3DOn.IsChecked = d.Particles3D;
        ColourBlindBox.IsChecked = d.Palette == CuePalette.ColourBlindSafe;
        AckDefault.IsChecked = s.Signals.AckByDefault;
        EscalateSeconds.Text = s.Signals.EscalateSeconds.ToString();
        DepartmentsBox.Text = string.Join(", ", s.Signals.Departments);

        var devices = WavePlayer.Devices();
        void FillDevices(ComboBox box, string current)
        {
            box.Items.Add(new ComboBoxItem { Content = "(same as alert device)", Tag = "" });
            foreach (var n in devices) box.Items.Add(new ComboBoxItem { Content = n, Tag = n });
            if (current.Length > 0 && !devices.Contains(current)) box.Items.Add(new ComboBoxItem { Content = current + "  (not connected)", Tag = current });
            box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == current) ?? box.Items[0];
        }
        FillDevices(WarnDevice, s.Alerts.WarningDevice);
        FillDevices(WrapDevice, s.Alerts.WrapUpDevice);
        FillDevices(ZeroDevice, s.Alerts.ZeroDevice);
        FillDevices(TalentDevice, s.Integrations.TalentDevice);
        VoiceCombo.Items.Add(new ComboBoxItem { Content = "(Windows default voice)", Tag = "" });
        foreach (var v in SpokenCues.Voices()) VoiceCombo.Items.Add(new ComboBoxItem { Content = v, Tag = v });
        VoiceCombo.SelectedItem = VoiceCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.Integrations.Voice) ?? VoiceCombo.Items[0];

        var i = s.Integrations;
        RoomName.Text = i.RoomName;
        RoomsEnabled.IsChecked = i.Rooms;
        PowerPointOn.IsChecked = i.PowerPoint;
        SlidesSource.Items.Add(new ComboBoxItem { Content = "Automatic — a presenter PC that's presenting, else this PC", Tag = "auto" });
        SlidesSource.Items.Add(new ComboBoxItem { Content = "This PC's PowerPoint only", Tag = "local" });
        foreach (var pc in _presenters?.Invoke() ?? Array.Empty<PresenterPc>())
            SlidesSource.Items.Add(new ComboBoxItem { Content = $"Presenter PC: {pc.Name} ({pc.Address})", Tag = pc.Agent });
        if (!SlidesSource.Items.Cast<ComboBoxItem>().Any(x => (string)x.Tag == i.SlidesSource))
            SlidesSource.Items.Add(new ComboBoxItem { Content = $"Presenter PC {i.SlidesSource} (not heard yet)", Tag = i.SlidesSource });
        SlidesSource.SelectedItem = SlidesSource.Items.Cast<ComboBoxItem>().First(x => (string)x.Tag == i.SlidesSource);
        ScriptPath.Text = i.ScriptPath;
        SpeechCulture.Items.Add(new ComboBoxItem { Content = "(automatic)", Tag = "" });
        foreach (var c in WindowsTranscriber.InstalledCultures()) SpeechCulture.Items.Add(new ComboBoxItem { Content = c, Tag = c });
        SpeechCulture.SelectedItem = SpeechCulture.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == i.SpeechCulture) ?? SpeechCulture.Items[0];
        VmixOn.IsChecked = i.Vmix;
        VmixHost.Text = i.VmixHost;
        VmixPort.Text = i.VmixPort.ToString();
        // Settings' own copy of the playout links: Add, Edit and Remove change it, Save writes it whole.
        _links = i.PlayoutLinks.Select(l => l.Clone()).ToList();
        PlayoutLead.Text = i.PlayoutLeadMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        RefreshPlayoutLinks();
        ArtNetOn.IsChecked = i.ArtNet;
        ArtNetHost.Text = i.ArtNetHost;
        SacnOn.IsChecked = i.Sacn;
        DmxUniverse.Text = i.DmxUniverse.ToString();
        DmxAddress.Text = i.DmxAddress.ToString();
        DmxModeCombo.ItemsSource = Enum.GetValues<DmxMode>();
        DmxModeCombo.SelectedItem = i.DmxMode;
        HueOn.IsChecked = i.Hue;
        HueBridge.Text = i.HueBridge;
        HueUser.Text = i.HueUser;
        HueLights.Text = i.HueLights;
        NtpOn.IsChecked = i.Ntp;
        NtpServer.Text = i.NtpServer;
        LtcOn.IsChecked = i.Ltc;
        LtcDevice.Items.Add(new ComboBoxItem { Content = "Windows default device", Tag = "" });
        foreach (var n in devices) LtcDevice.Items.Add(new ComboBoxItem { Content = n, Tag = n });
        LtcDevice.SelectedItem = LtcDevice.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == i.LtcDevice) ?? LtcDevice.Items[0];
        LtcFps.SelectedItem = LtcFps.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Content?.ToString() == i.LtcFps.ToString()) ?? LtcFps.Items[1];
        LtcSource.SelectedItem = LtcSource.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == i.LtcSource) ?? LtcSource.Items[0];
        LtcInOn.IsChecked = i.LtcIn;
        LtcInDevice.Items.Add(new ComboBoxItem { Content = "Windows default input", Tag = "" });
        foreach (var n in WaveRecorder.Devices()) LtcInDevice.Items.Add(new ComboBoxItem { Content = n, Tag = n });
        if (i.LtcInDevice.Length > 0 && !LtcInDevice.Items.Cast<ComboBoxItem>().Any(x => (string)x.Tag == i.LtcInDevice))
            LtcInDevice.Items.Add(new ComboBoxItem { Content = i.LtcInDevice + " (not connected)", Tag = i.LtcInDevice });
        LtcInDevice.SelectedItem = LtcInDevice.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == i.LtcInDevice) ?? LtcInDevice.Items[0];
        LtcChaseOn.IsChecked = i.LtcChase;
        LtcEnd.Text = i.LtcEnd;
        AssistantOn.IsChecked = i.Assistant;
        AssistantCloudOn.IsChecked = i.AssistantCloud;
        AgentApprovalOn.IsChecked = i.AgentsNeedApproval;
        if (i.AssistantKeyProtected.Length > 0) AssistantKey.Password = "••••••••";
        McpOn.IsChecked = i.Mcp;

        EventColumn.ItemsSource = Enum.GetValues<TriggerEvent>();
        ActionColumn.ItemsSource = Enum.GetValues<Stagewatch.Core.TriggerAction>();
        // Each rule whole, its auth profile (and its protected secret) with it: a save never drops one.
        foreach (var t in s.Triggers) _triggers.Add(t.Clone());
        TriggerGrid.ItemsSource = _triggers;

        RefreshIntegrations();
    }

    private void RefreshIntegrations()
    {
        if (_integrations is null) return;
        IntegrationList.ItemsSource = _integrations().Select(x => new IntegrationRow(x.Name, x.Status, x.Health switch
        {
            LinkHealth.Ok => System.Windows.Media.Brushes.LimeGreen,
            LinkHealth.Error => System.Windows.Media.Brushes.Red,
            LinkHealth.NotFound => System.Windows.Media.Brushes.Orange,
            LinkHealth.Starting => System.Windows.Media.Brushes.Gold,
            _ => System.Windows.Media.Brushes.DimGray
        })).ToList();
    }

    private void OnBrowseScript(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Text script (*.txt)|*.txt|All files|*.*" };
        if (dlg.ShowDialog(this) == true) ScriptPath.Text = dlg.FileName;
    }

    private void OnTestVoice(object sender, RoutedEventArgs e)
    {
        var device = (TalentDevice.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        if (device.Length == 0) device = (AlertDevice.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        var voice = (VoiceCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        var pcm = new SpokenCues().Render("two minutes", voice);
        if (pcm is null) { MessageBox.Show(this, "Windows speech isn't available on this PC.", "Spoken cue", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (!WavePlayer.PlayOnce(pcm, SpokenCues.SampleRate, device))
            MessageBox.Show(this, $"\"{device}\" isn't connected.", "Spoken cue", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void SaveV2(AppSettings s)
    {
        var d = s.Display;
        if (ShowCueLabel.IsChecked == true) d.Elements |= DisplayElements.CueLabel;
        if (ShowFinishBy.IsChecked == true) d.Elements |= DisplayElements.FinishBy;
        if (ShowSlides.IsChecked == true) d.Elements |= DisplayElements.Slides;
        if (ShowPlayout.IsChecked == true) d.Elements |= DisplayElements.Playout;

        if (LadderCombo.SelectedValue is string ladder) d.CueLadderId = ladder == "standard" ? "" : ladder;
        if ((SpeakerFormatCombo.SelectedItem as ComboBoxItem)?.Tag is string fmt && Enum.TryParse<SpeakerTimerFormat>(fmt, out var f)) d.SpeakerFormat = f;
        d.HonestSpeakerClock = HonestClock.IsChecked == true;
        d.StableLayout = StableLayoutBox.IsChecked == true;
        d.Holographic = HoloOn.IsChecked == true;
        d.HoloFloor = HoloFloorOn.IsChecked == true;
        d.EggTimer = SceneEgg.IsChecked == true;
        d.Animations3D = Anim3DOn.IsChecked == true;
        d.Particles3D = Particles3DOn.IsChecked == true;
        d.Palette = ColourBlindBox.IsChecked == true ? CuePalette.ColourBlindSafe : CuePalette.Standard;
        s.Signals.AckByDefault = AckDefault.IsChecked == true;
        if (int.TryParse(EscalateSeconds.Text, out var esc) && esc is >= 5 and <= 600) s.Signals.EscalateSeconds = esc;
        var depts = DepartmentsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SignalBoard.NormaliseTarget).Distinct().Take(12).ToList();
        s.Signals.Departments = depts;

        string Tag(ComboBox b) => (b.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        s.Alerts.WarningDevice = Tag(WarnDevice);
        s.Alerts.WrapUpDevice = Tag(WrapDevice);
        s.Alerts.ZeroDevice = Tag(ZeroDevice);

        var i = s.Integrations;
        i.TalentDevice = Tag(TalentDevice);
        i.Voice = Tag(VoiceCombo);
        i.RoomName = RoomName.Text.Trim();
        i.Rooms = RoomsEnabled.IsChecked == true;
        i.PowerPoint = PowerPointOn.IsChecked == true;
        i.SlidesSource = Tag(SlidesSource) is { Length: > 0 } slides ? slides : "auto";
        i.ScriptPath = ScriptPath.Text.Trim();
        i.SpeechCulture = Tag(SpeechCulture);
        i.Vmix = VmixOn.IsChecked == true;
        i.VmixHost = VmixHost.Text.Trim();
        if (int.TryParse(VmixPort.Text, out var vp) && vp is > 0 and < 65536) i.VmixPort = vp;
        // The links as edited here, whole; a renamed link's running-order cues move with it, in every running order of the show.
        i.PlayoutLinks = _links.Select(l => l.Clone()).ToList();
        foreach (var (from, to) in _renames) PlayoutRules.RenameCues(s, from, to);
        i.PlayoutLeadMs = _lead;
        i.ArtNet = ArtNetOn.IsChecked == true;
        i.ArtNetHost = ArtNetHost.Text.Trim();
        i.Sacn = SacnOn.IsChecked == true;
        if (int.TryParse(DmxUniverse.Text, out var u) && u is >= 1 and <= 63999) i.DmxUniverse = u;
        if (int.TryParse(DmxAddress.Text, out var a) && a is >= 1 and <= 512) i.DmxAddress = a;
        if (DmxModeCombo.SelectedItem is DmxMode m) i.DmxMode = m;
        i.Hue = HueOn.IsChecked == true;
        i.HueBridge = HueBridge.Text.Trim();
        i.HueUser = HueUser.Text.Trim();
        i.HueLights = HueLights.Text.Trim();
        i.Ntp = NtpOn.IsChecked == true;
        i.NtpServer = NtpServer.Text.Trim();
        i.Ltc = LtcOn.IsChecked == true;
        i.LtcDevice = Tag(LtcDevice);
        if (int.TryParse((LtcFps.SelectedItem as ComboBoxItem)?.Content?.ToString(), out var fps)) i.LtcFps = fps;
        i.LtcSource = Tag(LtcSource) is { Length: > 0 } src ? src : "tod";
        i.LtcIn = LtcInOn.IsChecked == true;
        i.LtcInDevice = Tag(LtcInDevice);
        i.LtcChase = LtcChaseOn.IsChecked == true;
        i.LtcEnd = Timecode.Parse(LtcEnd.Text, 30) is null ? "" : LtcEnd.Text.Trim();
        i.Assistant = AssistantOn.IsChecked == true;
        i.AssistantCloud = AssistantCloudOn.IsChecked == true;
        i.AgentsNeedApproval = AgentApprovalOn.IsChecked != false;
        if (AssistantKey.Password.Length > 0 && AssistantKey.Password != "••••••••")
            i.AssistantKeyProtected = ViewModels.ProtectedKey.Protect(AssistantKey.Password);
        if (AssistantKey.Password.Length == 0) i.AssistantKeyProtected = "";
        i.Mcp = McpOn.IsChecked == true;

        TriggerGrid.CommitEdit(DataGridEditingUnit.Row, true);
        s.Triggers = _triggers.Where(t => t.Name.Length > 0 || t.Payload.Length > 0).ToList();
    }

    private void Load()
    {
        var s = _store.Settings;

        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var item = new ComboBoxItem
            {
                Content = $"{screen.DeviceName} {(screen.Primary ? "(primary)" : "")}  {screen.Bounds.Width}×{screen.Bounds.Height}",
                Tag = screen.DeviceName
            };
            MonitorCombo.Items.Add(item);
            if (s.Display.TargetMonitorDeviceName == screen.DeviceName) MonitorCombo.SelectedItem = item;
        }
        if (MonitorCombo.SelectedItem is null && MonitorCombo.Items.Count > 0)
            MonitorCombo.SelectedIndex = 0;

        KeyColorCombo.SelectedIndex = (int)s.Display.KeyColor;

        ShowTimer.IsChecked = s.Display.Elements.HasFlag(DisplayElements.Timer);
        ShowMessage.IsChecked = s.Display.Elements.HasFlag(DisplayElements.Message);
        ShowEndTimeOutput.IsChecked = s.Display.Elements.HasFlag(DisplayElements.EndTime);
        ShowTod.IsChecked = s.Display.Elements.HasFlag(DisplayElements.TimeOfDay);
        ShowPreset.IsChecked = s.Display.Elements.HasFlag(DisplayElements.PresetName);
        ShowProgress.IsChecked = s.Display.Elements.HasFlag(DisplayElements.ProgressBar);
        ShowNextUp.IsChecked = s.Display.Elements.HasFlag(DisplayElements.NextUp);
        ShowSchedule.IsChecked = s.Display.Elements.HasFlag(DisplayElements.Schedule);

        NdiName.Text = s.Display.NdiSourceName;
        NdiFps.SelectedItem = NdiFps.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Content?.ToString() == s.Display.NdiFrameRate.ToString());

        AllowOvertime.IsChecked = s.AllowOvertime;
        HideAtZero.IsChecked = s.HideAtZero;
        AlertStart.IsChecked = s.Alerts.OnStart;
        RefreshSounds();
        OperatorShortcuts.IsChecked = s.OperatorShortcuts;
        ShowEndTimeUi.IsChecked = s.Display.ShowEndTime;
        Use12Hour.IsChecked = s.Display.Use12HourClock;
        WarningSeconds.Text = s.Display.WarningSeconds.ToString();
        WarnFlashSeconds.Text = s.MessageState.AutoFlashWarnSeconds.ToString();
        CritFlashSeconds.Text = s.MessageState.AutoFlashCriticalSeconds.ToString();
        NormalColorCombo.SelectedValue = s.Display.NormalColorHex;
        if (NormalColorCombo.SelectedItem is null) NormalColorCombo.SelectedIndex = 0;
        WarningColorCombo.SelectedValue = s.Display.WarningColorHex;
        if (WarningColorCombo.SelectedItem is null) WarningColorCombo.SelectedIndex = 0;
        WrapUpSeconds.Text = s.Display.WrapUpSeconds.ToString();
        WrapUpColorCombo.SelectedValue = s.Display.WrapUpColorHex;
        if (WrapUpColorCombo.SelectedItem is null) WrapUpColorCombo.SelectedIndex = 0;
        AlertWarning.IsChecked = s.Alerts.OnWarning;
        AlertWrapUp.IsChecked = s.Alerts.OnWrapUp;
        AlertZero.IsChecked = s.Alerts.OnZero;
        AlertDevice.Items.Add(new ComboBoxItem { Content = "Windows default device", Tag = "" });
        foreach (var name in AlertSounds.ListDevices())
            AlertDevice.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        if (!string.IsNullOrEmpty(s.Alerts.Device) && !AlertSounds.ListDevices().Contains(s.Alerts.Device))
            AlertDevice.Items.Add(new ComboBoxItem { Content = s.Alerts.Device + "  (not connected)", Tag = s.Alerts.Device });
        AlertDevice.SelectedItem = AlertDevice.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (s.Alerts.Device ?? ""))
                                   ?? AlertDevice.Items[0];

        WebEnabled.IsChecked = s.Remote.WebEnabled;
        WebPort.Text = s.Remote.WebPort.ToString();
        WebBindAll.IsChecked = s.Remote.WebBindAll;
        WebHttps.IsChecked = s.Remote.Https;
        WebRequireHttps.IsChecked = s.Remote.RequireHttps;
        // Grouped as the show log, a presenter PC's row and a pinned peer group it: its first 8 hex are what they compare.
        CertFingerprint.Text = _fingerprint?.Invoke() is { } fp ? "SHA-256 " + TlsPins.Shown(fp, 32) + " …" : "no certificate (the status line and the show log say why)";
        CertFingerprint.ToolTip = _fingerprint?.Invoke();
        RemotePin.Text = s.Remote.Pin ?? "";
        CrewKeysOn.IsChecked = s.Remote.CrewKeysRequired;
        CustomCss.Text = s.Remote.CustomCss;
        ViewCode.Text = s.Remote.ViewCode;
        RefreshApiKeys();
        _crewSecret = s.Remote.CrewSecret;
        OscEnabled.IsChecked = s.Remote.OscEnabled;
        OscPort.Text = s.Remote.OscPort.ToString();
        OscBindAll.IsChecked = s.Remote.OscBindAll;
        OscFeedbackHost.Text = s.Remote.OscFeedbackHost;
        OscFeedbackPort.Text = s.Remote.OscFeedbackPort.ToString();
        PlayoutFromBox.Text = PlayoutGate.Shown(s.Remote.PlayoutFrom);
        RefreshRemoteUrls();

        OvertimeColorCombo.SelectedValue = s.Display.OvertimeColorHex;
        if (OvertimeColorCombo.SelectedItem is null) OvertimeColorCombo.SelectedIndex = 0;
        ExpiredFlashCombo.SelectedValue = s.Display.ExpiredFlashRate.ToString();
        if (ExpiredFlashCombo.SelectedItem is null) ExpiredFlashCombo.SelectedIndex = 0;

        CompanionEnabled.IsChecked = s.Companion.Enabled;
        CompanionPort.Text = s.Companion.Port.ToString();
        CompanionAllInterfaces.IsChecked = s.Companion.BindAllInterfaces;

        VersionLine.Text = "Version " + (typeof(SettingsDialog).Assembly.GetName().Version?.ToString(3) ?? "?");
        LinkedInLink.ToolTip = AppInfo.AuthorLinkedInUrl;
        LicenseLine.Text = $"License: {_license.DisplayName}";
        HwidLine.Text = "Hardware ID: " + HardwareId.Get();
    }

    private void OnOpenLinkedIn(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppInfo.AuthorLinkedInUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't open the browser:\n{ex.Message}", AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshRemoteUrls()
    {
        var port = int.TryParse(WebPort.Text, out var p) ? p : _store.Settings.Remote.WebPort;
        var hosts = WebBindAll.IsChecked == true
            ? ViewModels.MainViewModel.LanAddresses().DefaultIfEmpty("localhost")
            : new[] { "localhost" };
        var urls = hosts.SelectMany(h => new[] { $"http://{h}:{port}/viewer", $"http://{h}:{port}/control", $"http://{h}:{port}/producer" }).ToList();
        // Crew pages with their keys (first address only, to keep the list short).
        if (!string.IsNullOrEmpty(_crewSecret ?? _store.Settings.Remote.CrewSecret))
        {
            var first = hosts.First();
            urls.AddRange(CrewKeys.Links(_crewSecret ?? _store.Settings.Remote.CrewSecret!, _store.Settings.Signals.Departments)
                .Select(l => $"http://{first}:{port}{l.Path}"));
        }
        RemoteUrls.ItemsSource = urls;
    }

    private void OnTestAlert(object sender, RoutedEventArgs e)
    {
        var device = (AlertDevice.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        using var sounds = new AlertSounds();
        if (!sounds.Play(AlertSound.Zero, device))
            MessageBox.Show(this, $"\"{device}\" isn't connected.", "Test alert", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private string? _crewSecret;

    private void OnNewCrewKeys(object sender, RoutedEventArgs e)
    {
        _crewSecret = CrewKeys.NewSecret();
        CrewKeysNote.Text = "New crew links made — every old link stops confirming cues when you press Save.";
        RefreshRemoteUrls();
    }

    private void OnNewPin(object sender, RoutedEventArgs e)
        => RemotePin.Text = System.Security.Cryptography.RandomNumberGenerator.GetInt32(1000, 10000).ToString();

    private void OnCopyUrl(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url })
        {
            try { Clipboard.SetText(url); } catch { }
        }
    }

    private void OnOpenUrl(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url }) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OnCopyDiagnostics(object sender, RoutedEventArgs e)
    {
        if (_diagnostics is null) return;
        var text = _diagnostics();
        var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stagewatch", "diagnostics.txt");
        try { System.IO.File.WriteAllText(path, text); } catch (System.IO.IOException) { path = ""; }
        try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.ExternalException) { }
        DiagnosticsNote.Text = "Copied — paste it into your support email." + (path.Length > 0 ? $" Also saved to {path}" : "");
        DiagnosticsNote.Visibility = Visibility.Visible;
    }

    private void OnCopyHwid(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(HardwareId.Get());
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // A trigger whose secret would go where it is not kept (plain HTTP to another machine, a placeholder in its host) cannot be
        // saved: nothing is, and the dialog stays open with why. A secret in an address's query is a warning, said after the save.
        TriggerGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var rules = _triggers.Where(t => t.Name.Length > 0 || t.Payload.Length > 0).ToList();
        var refused = rules.Select(t => TriggerAuthRules.Problem(t) is { } why ? $"{TriggerAuthRules.Named(t)}: {why}" : null).OfType<string>().ToList();
        if (refused.Count > 0)
        {
            MessageBox.Show(this, "Not saved:\n" + string.Join("\n", refused), "Triggers", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // The playout links' lead and who may drive playout: what can't be kept is said, and nothing is saved until it is fixed.
        if (PlayoutRules.LeadProblem(PlayoutLead.Text, out var lead) is { } leadWhy)
        {
            MessageBox.Show(this, "Not saved: " + leadWhy + ".", "Playout links", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _lead = lead;
        var (from, fromNotes) = PlayoutGate.Typed(PlayoutFromBox.Text);
        if (fromNotes.Count > 0)
        {
            PlayoutFromNote.Text = string.Join("\n", fromNotes);
            PlayoutFromNote.Visibility = Visibility.Visible;
            MessageBox.Show(this, "Not saved:\n" + string.Join("\n", fromNotes), PlayoutGate.SettingWords, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var warned = WarnTriggers(rules);
        _store.Update(s =>
        {
            s.Display.TargetMonitorDeviceName = (MonitorCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            s.Display.KeyColor = (KeyColor)KeyColorCombo.SelectedIndex;

            DisplayElements el = DisplayElements.None;
            if (ShowTimer.IsChecked == true) el |= DisplayElements.Timer;
            if (ShowMessage.IsChecked == true) el |= DisplayElements.Message;
            if (ShowEndTimeOutput.IsChecked == true) el |= DisplayElements.EndTime;
            if (ShowTod.IsChecked == true) el |= DisplayElements.TimeOfDay;
            if (ShowPreset.IsChecked == true) el |= DisplayElements.PresetName;
            if (ShowProgress.IsChecked == true) el |= DisplayElements.ProgressBar;
            if (ShowNextUp.IsChecked == true) el |= DisplayElements.NextUp;
            if (ShowSchedule.IsChecked == true) el |= DisplayElements.Schedule;
            s.Display.Elements = el;

            s.Display.NdiSourceName = NdiName.Text;
            if (int.TryParse((NdiFps.SelectedItem as ComboBoxItem)?.Content?.ToString(), out var fps))
                s.Display.NdiFrameRate = fps;
            s.Display.ShowEndTime = ShowEndTimeUi.IsChecked == true;
            s.Display.Use12HourClock = Use12Hour.IsChecked == true;

            s.AllowOvertime = AllowOvertime.IsChecked == true;
            s.HideAtZero = HideAtZero.IsChecked == true;
            s.Alerts.OnStart = AlertStart.IsChecked == true;
            s.OperatorShortcuts = OperatorShortcuts.IsChecked == true;
            if (int.TryParse(WarningSeconds.Text, out var ws)) s.Display.WarningSeconds = ws;
            if (int.TryParse(WarnFlashSeconds.Text, out var w)) s.MessageState.AutoFlashWarnSeconds = w;
            if (int.TryParse(CritFlashSeconds.Text, out var c)) s.MessageState.AutoFlashCriticalSeconds = c;
            if (NormalColorCombo.SelectedValue is string nc) s.Display.NormalColorHex = nc;
            if (WarningColorCombo.SelectedValue is string wc) s.Display.WarningColorHex = wc;
            if (OvertimeColorCombo.SelectedValue is string oc) s.Display.OvertimeColorHex = oc;
            if (int.TryParse(WrapUpSeconds.Text, out var wu) && wu >= 0) s.Display.WrapUpSeconds = wu;
            if (WrapUpColorCombo.SelectedValue is string wuc) s.Display.WrapUpColorHex = wuc;
            s.Alerts.OnWarning = AlertWarning.IsChecked == true;
            s.Alerts.OnWrapUp = AlertWrapUp.IsChecked == true;
            s.Alerts.OnZero = AlertZero.IsChecked == true;
            s.Alerts.Device = (AlertDevice.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

            s.Remote.WebEnabled = WebEnabled.IsChecked == true;
            if (int.TryParse(WebPort.Text, out var wp) && wp is > 0 and < 65536) s.Remote.WebPort = wp;
            s.Remote.WebBindAll = WebBindAll.IsChecked == true;
            s.Remote.Https = WebHttps.IsChecked == true;
            s.Remote.RequireHttps = WebHttps.IsChecked == true && WebRequireHttps.IsChecked == true;
            s.Remote.Pin = RemotePin.Text.Trim();
            s.Remote.CrewKeysRequired = CrewKeysOn.IsChecked == true;
            s.Remote.ViewCode = ViewCode.Text.Trim();
            s.Remote.CustomCss = CustomCss.Text.Length > RemoteSettings.MaxCustomCss ? CustomCss.Text[..RemoteSettings.MaxCustomCss] : CustomCss.Text;
            if (!string.IsNullOrEmpty(_crewSecret)) s.Remote.CrewSecret = _crewSecret;
            s.Remote.OscEnabled = OscEnabled.IsChecked == true;
            if (int.TryParse(OscPort.Text, out var op) && op is > 0 and < 65536) s.Remote.OscPort = op;
            s.Remote.OscBindAll = OscBindAll.IsChecked == true;
            s.Remote.OscFeedbackHost = OscFeedbackHost.Text.Trim();
            s.Remote.PlayoutFrom = from;
            if (int.TryParse(OscFeedbackPort.Text, out var ofp) && ofp is > 0 and < 65536) s.Remote.OscFeedbackPort = ofp;
            if (ExpiredFlashCombo.SelectedValue is string fr && Enum.TryParse<FlashRate>(fr, out var fra))
                s.Display.ExpiredFlashRate = fra;

            s.Companion.Enabled = CompanionEnabled.IsChecked == true;
            if (int.TryParse(CompanionPort.Text, out var p)) s.Companion.Port = p;
            s.Companion.BindAllInterfaces = CompanionAllInterfaces.IsChecked == true;
            SaveV2(s);
        });
        _store.SaveNow();
        if (warned.Count > 0) MessageBox.Show(this, string.Join("\n", warned), "Triggers", MessageBoxButton.OK, MessageBoxImage.Warning);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnHelp(object sender, RoutedEventArgs e)
        => Help.HelpWindow.ShowTopic(this, (Tabs.SelectedItem as DependencyObject) is { } tab ? Help.ContextHelp.GetTopic(tab) ?? "layout" : "layout");
}
