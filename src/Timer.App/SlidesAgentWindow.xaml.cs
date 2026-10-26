using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Stagewatch.App.ViewModels;
using Stagewatch.Integrations;

namespace Stagewatch.App;

/// <summary>Settings of the presenter-PC role, kept in %APPDATA%\Stagewatch\slides.json (PINs encrypted for this Windows user).</summary>
public sealed class SlidesAgentSettings
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = Environment.MachineName;
    [JsonPropertyName("targets")] public List<Target> Targets { get; set; } = new();

    public sealed class Target
    {
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("host")] public string Host { get; set; } = "";
        [JsonPropertyName("port")] public int Port { get; set; } = 9924;
        [JsonPropertyName("pin")] public string PinProtected { get; set; } = "";
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    }

    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stagewatch", "slides.json");

    public static SlidesAgentSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<SlidesAgentSettings>(File.ReadAllText(FilePath)) is { } s)
            {
                if (!Stagewatch.Core.SlideFeed.Valid(s.Id)) s.Id = NewId();
                return s;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new SlidesAgentSettings { Id = NewId() };
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string NewId() => "pc" + Guid.NewGuid().ToString("N")[..10];
}

/// <summary>The presenter-PC role window (Stagewatch.exe --slides): what PowerPoint shows, where it's sent, what's on the network.</summary>
public partial class SlidesAgentWindow : Window
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Stagewatch Slides";

    private readonly SlidesAgentSettings _settings = SlidesAgentSettings.Load();
    private readonly ObservableCollection<TargetRow> _targets = new();
    private readonly ObservableCollection<FoundRow> _found = new();
    private volatile IReadOnlyList<AgentTarget> _active = Array.Empty<AgentTarget>();
    private volatile string _name;
    private readonly SlidesAgent _agent;
    private readonly DispatcherTimer _ui;

    public SlidesAgentWindow()
    {
        InitializeComponent();
        _name = _settings.Name;
        AgentName.Text = _settings.Name;
        foreach (var t in _settings.Targets) _targets.Add(new TargetRow(t));
        TargetList.ItemsSource = _targets;
        FoundList.ItemsSource = _found;
        RebuildActive();
        StartWithWindows.IsChecked = Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(RunValue) is string;
        _agent = new SlidesAgent(_settings.Id, () => _name, () => _active,
            pinsPath: System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stagewatch", "slides-pins.json"));
        _ui = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _ui.Tick += (_, _) => Refresh();
        _ui.Start();
        Closed += (_, _) => { _ui.Stop(); _agent.Dispose(); };
    }

    private void Refresh()
    {
        var d = _agent.Deck;
        DeckLine.Text = d is { Slide: > 0 } ? $"Presenting {d.Presentation} — slide {d.Slide} / {d.Count}" : _agent.PowerPointStatus switch
        {
            var s when s.Contains("isn't running", StringComparison.OrdinalIgnoreCase) => "PowerPoint isn't running",
            _ => "PowerPoint is open — start the slideshow to send slides",
        };
        foreach (var row in _targets) row.Update(row.Enabled ? _agent.StatusOf(row.ToTarget()) : new AgentTargetStatus("", false, "off"));
        NoTargets.Visibility = _targets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var peers = _agent.Peers();
        var mains = peers.Where(p => p.Hello.Kind == "main" && p.AgeSeconds < 10).ToList();
        _found.Clear();
        foreach (var p in mains)
            if (!_targets.Any(t => p.HeardAt(t.Host) && t.Port == p.Hello.HttpPort))
                _found.Add(new FoundRow(p.Conflict is { } c ? $"{p.Hello.Name} — {c.Words}" : p.Hello.Name, p.Address, p.Hello.HttpPort));
        NoneFound.Visibility = mains.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var agents = peers.Where(p => p.Hello.Kind == "agent" && p.AgeSeconds < 10).ToList();
        var others = agents.Count == 0 ? "None." : string.Join("\n", agents.Select(a => a.Conflict is { } c
            ? $"{a.Hello.Name} — {c.Words}" : $"{a.Hello.Name} ({a.Address}){(a.Hello.Presenting ? " — presenting" : "")}"));
        // A copied presenter PC says this one's feed id: Stagewatch cannot tell their slides apart, so say so first.
        OtherAgents.Text = _agent.OwnConflict is { } own ? $"Another presenter PC says it is this one: {own.Other} — give one of them a new id\n{others}" : others;
    }

    private void RebuildActive()
    {
        _active = _targets.Where(t => t.Enabled && t.Host.Length > 0).Select(t => t.ToTarget()).ToList();
        _settings.Targets = _targets.Select(t => t.Settings).ToList();
        _settings.Save();
    }

    private void OnTargetsChanged(object sender, RoutedEventArgs e) => RebuildActive();

    private void OnNameChanged(object sender, RoutedEventArgs e)
    {
        var name = AgentName.Text.Trim();
        _name = name.Length > 0 ? name : Environment.MachineName;
        _settings.Name = _name;
        _settings.Save();
    }

    private void OnAddFound(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FoundRow f }) AddTarget(f.Name, f.Address, f.Port);
    }

    private void OnAddManual(object sender, RoutedEventArgs e)
    {
        var host = ManualHost.Text.Trim();
        if (host.Length == 0 || host.Contains('/') || host.Contains(' ')) { MessageBox.Show(this, "Type the Stagewatch PC's name or IP address.", "Stagewatch Slides"); return; }
        if (!int.TryParse(ManualPort.Text.Trim(), out var port) || port is < 1 or > 65535) port = 9924;
        AddTarget(host, host, port);
        ManualHost.Text = "";
    }

    private void AddTarget(string label, string host, int port)
    {
        if (_targets.Any(t => t.Host.Equals(host, StringComparison.OrdinalIgnoreCase) && t.Port == port)) return;
        _targets.Add(new TargetRow(new SlidesAgentSettings.Target { Label = label, Host = host, Port = port, Enabled = true }));
        RebuildActive();
    }

    private void OnRemoveTarget(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TargetRow r }) { _targets.Remove(r); RebuildActive(); }
    }

    private void OnPinLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox { Tag: TargetRow r } box && box.Password.Length == 0) box.Password = r.Pin;
    }

    private void OnPinChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox { Tag: TargetRow r } box || box.Password == r.Pin) return;
        r.Pin = box.Password;
        RebuildActive();
    }

    private void OnStartWithWindows(object sender, RoutedEventArgs e)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (StartWithWindows.IsChecked == true) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --slides");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
    }

    private void OnOpenTimer(object sender, RoutedEventArgs e)
    {
        var t = _targets.FirstOrDefault(x => x.Enabled);
        if (t is null) { MessageBox.Show(this, "Add a Stagewatch to send to first.", "Stagewatch Slides"); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"http://{t.Host}:{t.Port}/viewer") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    public sealed class TargetRow : INotifyPropertyChanged
    {
        public TargetRow(SlidesAgentSettings.Target s) { Settings = s; Pin = ProtectedKey.Unprotect(s.PinProtected); }
        public SlidesAgentSettings.Target Settings { get; }
        public string Label => Settings.Label.Length > 0 ? Settings.Label : Settings.Host;
        public string Host => Settings.Host;
        public int Port => Settings.Port;
        public string Address => $"{Settings.Host}:{Settings.Port}";
        public bool Enabled { get => Settings.Enabled; set => Settings.Enabled = value; }
        private string _pin = "";
        public string Pin { get => _pin; set { _pin = value; Settings.PinProtected = value.Length > 0 ? ProtectedKey.Protect(value) : ""; } }
        public string Status { get; private set; } = "";
        public bool Ok { get; private set; }
        public bool CertificateChanged => Status.StartsWith("CERTIFICATE CHANGED", StringComparison.Ordinal);
        public AgentTarget ToTarget() => new(Settings.Host, Settings.Port, Pin);

        public void Update(AgentTargetStatus s)
        {
            if (s.Text != Status) { Status = s.Text; PropertyChanged?.Invoke(this, new(nameof(Status))); PropertyChanged?.Invoke(this, new(nameof(CertificateChanged))); }
            if (s.Ok != Ok) { Ok = s.Ok; PropertyChanged?.Invoke(this, new(nameof(Ok))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>After a Stagewatch was reinstalled: trust the certificate it presents now (check its fingerprint first).</summary>
    private void OnTrustCertificate(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TargetRow row) _agent.TrustNewCertificate(row.ToTarget());
    }

    public sealed record FoundRow(string Name, string Address, int Port);
}
