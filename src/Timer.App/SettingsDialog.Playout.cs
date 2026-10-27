using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Stagewatch.App.ViewModels;
using Stagewatch.Core;
using Stagewatch.Integrations;

namespace Stagewatch.App;

/// <summary>
/// Settings' playout links (Integrations), its May drive playout list (Remote) and its HTTP triggers' secrets (Triggers). The links are
/// edited on a copy that Save writes whole, each change refused in words by the rules a settings file keeps (PlayoutRules.EditLink),
/// never mended; a renamed link's running-order cues move with it at Save. The buttons and the list's right-click menu are enabled
/// together, by one rule.
/// </summary>
public partial class SettingsDialog
{
    /// <summary>
    /// What Settings asks the desk about its running links: a link's light and status, the certificate it has waiting for a person, and
    /// the one door that trusts it (the same as the Show network row's and the status line's).
    /// </summary>
    public sealed record PlayoutHooks(Func<string, (LinkHealth Health, string Status)?> State, Func<string, CertificateWaiting?> Waiting,
        Func<CertificateWaiting, string, bool, (bool Trusted, string Words)> Trust);

    private readonly PlayoutHooks? _playoutHooks;

    /// <summary>Set after the dialog is built: the list is drawn again with what the desk knows of each running link.</summary>
    public PlayoutHooks? Playout
    {
        get => _playoutHooks;
        init { _playoutHooks = value; RefreshPlayoutLinks(); }
    }

    private List<PlayoutLinkSettings> _links = new();

    /// <summary>The renames made here, in order: Save moves each one's running-order cues.</summary>
    private readonly List<(string From, string To)> _renames = new();

    /// <summary>The links added or changed here: they take their new settings at Save.</summary>
    private readonly HashSet<string> _changed = new(StringComparer.Ordinal);

    /// <summary>The lead Save writes, read from its box when Save is pressed.</summary>
    private int _lead = PlayoutRules.DefaultLeadMs;

    /// <summary>
    /// A link's row as the list drew it: its name, line and status, and the certificate waiting for a person that its status showed
    /// (<see cref="Waiting"/>, null for none) — the one Trust its certificate… offers and is bound to, never one that came since.
    /// </summary>
    private sealed record PlayoutRow(string Name, string Line, string Status, CertificateWaiting? Waiting);

    private void RefreshPlayoutLinks()
    {
        if (PlayoutLinkList is null) return;
        var selected = (PlayoutLinkList.SelectedItem as PlayoutRow)?.Name;
        var rows = _links.Select(Row).ToList();
        PlayoutLinkList.ItemsSource = rows;
        PlayoutLinkList.SelectedItem = rows.FirstOrDefault(r => r.Name == selected);
        SyncPlayoutActions();

        PlayoutRow Row(PlayoutLinkSettings l)
        {
            var line = PlayoutRules.Describe(l);
            if (_changed.Contains(l.Name)) return new(l.Name, line, "changed here — it takes these settings when you press Save", null);
            if (Playout?.State(l.Name) is { } state)
            {
                // The waiting certificate is read once: the row shows its short form, and the row's Trust is bound to this one.
                var waiting = Playout.Waiting(l.Name);
                return new(l.Name, line, state.Status + (waiting is not null ? " · " + waiting.Note : ""), waiting);
            }
            return new(l.Name, line, l.Enabled ? "not running (it starts when Settings are saved, under Production)" : "switched off", null);
        }
    }

    /// <summary>
    /// How many of the show's running-order cues will name link <paramref name="name"/> once Save has moved them through the renames
    /// made here, in their order (<c>PlayoutRules.CuesNaming</c> with the renames).
    /// </summary>
    private int CuesNaming(string name) => PlayoutRules.CuesNaming(_store.Settings, name, _renames);

    /// <summary>
    /// The selected row's certificate waiting for a person, as that row drew it with its short form, or null: a certificate that came
    /// to wait since the list was drawn is not offered until the row shows it.
    /// </summary>
    private CertificateWaiting? SelectedWaiting() => (PlayoutLinkList.SelectedItem as PlayoutRow)?.Waiting;

    /// <summary>The link buttons and the list's right-click menu, enabled together by the same rule.</summary>
    private void SyncPlayoutActions()
    {
        var row = PlayoutLinkList.SelectedItem as PlayoutRow;
        var canAdd = _links.Count < PlayoutRules.Max;
        var canTrust = SelectedWaiting() is not null;
        PlayoutAddButton.IsEnabled = canAdd;
        PlayoutEditButton.IsEnabled = row is not null;
        PlayoutRemoveButton.IsEnabled = row is not null;
        PlayoutTrustButton.IsEnabled = canTrust;
        if (PlayoutLinkList.ContextMenu is { } menu)
            foreach (var item in menu.Items.OfType<MenuItem>())
                item.IsEnabled = item.Tag switch { "add" => canAdd, "edit" or "remove" => row is not null, "trust" => canTrust, _ => true };
    }

    private void OnPlayoutLinkSelected(object sender, SelectionChangedEventArgs e) => SyncPlayoutActions();

    private void OnPlayoutLinkMenuOpened(object sender, RoutedEventArgs e) => SyncPlayoutActions();

    private void SayPlayout(string words)
    {
        PlayoutNote.Text = words;
        PlayoutNote.Visibility = words.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAddPlayoutLink(object sender, RoutedEventArgs e)
    {
        var name = PlayoutRules.SuggestName("", PlayoutRules.Patterns, _links.Select(l => l.Name));
        EditPlayoutLink(null, new PlayoutLinkEdit(name, true, PlayoutRules.Auto, PlayoutRules.DefaultHost, PlayoutRules.PatternsPort, null, "", PlayoutRules.Auto, true, false));
    }

    private void OnEditPlayoutLink(object sender, RoutedEventArgs e)
    {
        if (PlayoutLinkList.SelectedItem is PlayoutRow row && PlayoutRules.Find(_links, row.Name) is { } link) EditPlayoutLink(link.Name, link.Edit());
    }

    private void OnRemovePlayoutLink(object sender, RoutedEventArgs e)
    {
        if (PlayoutLinkList.SelectedItem is not PlayoutRow row || PlayoutRules.Find(_links, row.Name) is not { } link) return;
        var cues = CuesNaming(link.Name);
        var words = $"Remove playout link {link.Name}? It stops when you press Save." + (cues > 0
            ? $" {cues.ToString(CultureInfo.InvariantCulture)} running-order cue{(cues == 1 ? " names" : "s name")} it: they are kept, and skipped with advice until a link is called {link.Name} again."
            : "");
        if (MessageBox.Show(this, words, "Playout links", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        _links.Remove(link);
        _changed.Remove(link.Name);
        SayPlayout($"Link {link.Name} removed — Save to stop it.");
        RefreshPlayoutLinks();
    }

    /// <summary>
    /// The link's dialog: every field a person sets, the token in a box that shows only whether one is kept. OK writes the copy through
    /// <see cref="PlayoutRules.EditLink"/>; a refusal keeps the dialog open with why, so nothing typed is lost.
    /// </summary>
    private void EditPlayoutLink(string? was, PlayoutLinkEdit start)
    {
        const string Kept = "••••••••";
        var own = was is null ? null : PlayoutRules.Find(_links, was);
        var panel = new StackPanel { Margin = new Thickness(20), Width = 520 };
        TextBox Box(string label, string text, string tip)
        {
            panel.Children.Add(new TextBlock { Text = label, Opacity = 0.75, Margin = new Thickness(0, 6, 0, 2) });
            var box = new TextBox { Text = text, ToolTip = tip };
            panel.Children.Add(box);
            return box;
        }
        ComboBox Combo(string label, (string Tag, string Text)[] items, string current)
        {
            panel.Children.Add(new TextBlock { Text = label, Opacity = 0.75, Margin = new Thickness(0, 6, 0, 2) });
            var combo = new ComboBox();
            foreach (var (tag, text) in items) combo.Items.Add(new ComboBoxItem { Content = text, Tag = tag });
            combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == current) ?? combo.Items[0];
            panel.Children.Add(combo);
            return combo;
        }
        var name = Box("Name (how commands, the context and running-order cues name it)", start.Name, "1 to 24 of a-z, 0-9 and -: playout stage-left go …");
        var kind = Combo("Peer", [(PlayoutRules.Auto, "Found out from its hello and STATE"), (PlayoutRules.Patterns, "A Patterns desk"), (PlayoutRules.PlayMap, "A PlayMap node")], start.Kind);
        var host = Box("Address", start.Host, "A name or an address (blank = this PC); the port has its own box");
        var port = Box("Port (its line wire: Patterns 9697, PlayMap 9941)", start.Port.ToString(CultureInfo.InvariantCulture), "1-65535");
        panel.Children.Add(new TextBlock { Text = "Pairing token (the peer's Remote page; empty if it asks for none)", Opacity = 0.75, Margin = new Thickness(0, 6, 0, 2) });
        var token = new PasswordBox { ToolTip = "Kept encrypted for this Windows user. Stagewatch proves it — it sends it in clear only to a desk or node on this PC (a loopback socket)." };
        if (own is { TokenProtected.Length: > 0 }) token.Password = Kept;
        panel.Children.Add(token);
        var node = Box("Node id (optional: the Show Bus id its certificate's pin is kept under; Find fills it in)", start.Node, "Empty: the link finds its node");
        var transport = Combo("Transport", [(PlayoutRules.Auto, "Auto — TLS where a PlayMap node offers it; never plain once its certificate is pinned"),
            (PlayoutRules.Tcp, "TCP — its line wire"), (PlayoutRules.Wss, "TLS only (wss) — a PlayMap node")], start.Transport);
        var on = new CheckBox { Content = "Switched on", IsChecked = start.Enabled, Margin = new Thickness(0, 10, 0, 2) };
        var vt = new CheckBox { Content = "Its clip on air may feed the VT countdown", IsChecked = start.FollowVt, Margin = new Thickness(0, 2, 0, 2) };
        var next = new CheckBox { Content = "When its clip ends on a video item, go to the next item (once per item)", IsChecked = start.NextOnEnd, Margin = new Thickness(0, 2, 0, 2) };
        panel.Children.Add(on);
        panel.Children.Add(vt);
        panel.Children.Add(next);
        var why = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        panel.Children.Add(why);
        var title = was is null ? "Add a playout link" : $"Playout link {was}";
        while (AskDialog(panel, title, was is null ? "Add" : "Keep"))
        {
            string? tokenProtected = null;
            if (token.Password != Kept)
            {
                if (token.Password.Length > 0 && PlayoutWire.TokenProblem(token.Password) is { } bad)
                {
                    why.Text = "Pairing token: " + bad + ".";
                    why.Visibility = Visibility.Visible;
                    continue;
                }
                tokenProtected = token.Password.Length == 0 ? "" : ProtectedKey.Protect(token.Password);
            }
            var portNumber = int.TryParse(port.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? p : 0;
            string Tag(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as string ?? PlayoutRules.Auto;
            var edit = new PlayoutLinkEdit(name.Text, on.IsChecked == true, Tag(kind), host.Text, portNumber, tokenProtected, node.Text, Tag(transport),
                vt.IsChecked == true, next.IsChecked == true);
            if (PlayoutRules.EditLink(_links, was, edit) is { } refused)
            {
                why.Text = refused + ".";
                why.Visibility = Visibility.Visible;
                continue;
            }
            var now = edit.Name.Trim().ToLowerInvariant();
            var moved = 0;
            // Cues that already name the new name (a link removed or renamed away before) are this link's from Save on: said beside
            // the ones the rename moves.
            var adopted = was != now ? CuesNaming(now) : 0;
            if (was is not null && was != now)
            {
                moved = CuesNaming(was);
                _renames.Add((was, now));
                _changed.Remove(was);
            }
            _changed.Add(now);
            SayPlayout((was is null ? $"Link {now} added" : $"Link {now} changed") + " — Save to start it with these settings."
                       + (moved > 0 ? $" Its {moved.ToString(CultureInfo.InvariantCulture)} running-order cue{(moved == 1 ? "" : "s")} move to the new name at Save." : "")
                       + (adopted > 0 ? $" {adopted.ToString(CultureInfo.InvariantCulture)} running-order cue{(adopted == 1 ? " already names" : "s already name")} {now}: {(adopted == 1 ? "it goes" : "they go")} to this link too." : ""));
            RefreshPlayoutLinks();
            PlayoutLinkList.SelectedItem = PlayoutLinkList.Items.OfType<PlayoutRow>().FirstOrDefault(r => r.Name == now);
            return;
        }
    }

    /// <summary>
    /// Find: listen for Patterns desks and PlayMap nodes, then add the one chosen as a link of its kind, its address, port and node
    /// filled in — one a link already reaches is said so. A miss names every port Find listens on.
    /// </summary>
    private async void OnFindPlayout(object sender, RoutedEventArgs e)
    {
        SayPlayout("Listening for Patterns desks and PlayMap nodes on this network…");
        PlayoutFindButton.IsEnabled = false;
        IReadOnlyList<PlayoutPeer> found;
        try { found = await PlayoutWire.DiscoverAsync(PlayoutWire.FindListen, [PlayoutRules.Patterns, PlayoutRules.PlayMap]); }
        finally { PlayoutFindButton.IsEnabled = true; }
        if (found.Count == 0)
        {
            SayPlayout("Nothing heard. Check its remote control is on, it is on the same network, and the firewall allows UDP "
                       + string.Join(" and ", PlayoutWire.FindPorts.Select(p => p.ToString(CultureInfo.InvariantCulture))) + " — or Add it by its address.");
            return;
        }
        SayPlayout("Heard: " + string.Join(", ", found.Select(Heard)));
        var chosen = Help.Pick.One(this, "Find", "Add a link to which?", found, Heard, "Add…");
        if (chosen is null) return;
        // One node has one link: a peer a link already reaches (its node, or its address and port) is that link's — selected, never
        // added again beside it.
        if (Linked(chosen) is { } already)
        {
            PlayoutLinkList.SelectedItem = PlayoutLinkList.Items.OfType<PlayoutRow>().FirstOrDefault(r => r.Name == already.Name);
            SayPlayout($"Not added: link {already.Name} reaches {Who(chosen)} already, and one node has one link — Edit that link to change it.");
            return;
        }
        var kind = chosen.Kind is PlayoutRules.Patterns or PlayoutRules.PlayMap ? chosen.Kind : PlayoutRules.Auto;
        var nodeId = NodeBindings.ValidId(chosen.Id) ? chosen.Id : "";
        EditPlayoutLink(null, new PlayoutLinkEdit(PlayoutRules.SuggestName(chosen.Name, kind, _links.Select(l => l.Name)), true, kind, chosen.Address,
            chosen.WirePort, null, nodeId, PlayoutRules.Auto, true, false));

        PlayoutLinkSettings? Linked(PlayoutPeer m) => PlayoutRules.Reaching(_links, m.Id, m.Address, m.WirePort);

        string Who(PlayoutPeer m) => (ShowBusKinds.Label(m.Kind) ?? m.Kind) + (m.Name.Length > 0 ? " " + m.Name : "");

        string Heard(PlayoutPeer m)
            => $"{Who(m)} at {m.Address}:{m.WirePort.ToString(CultureInfo.InvariantCulture)}" + (m.Id.Length > 0 ? $" (node {m.Id})" : "")
               + (Linked(m) is { } linked ? $" — link {linked.Name} already" : "");
    }

    /// <summary>
    /// Trust the certificate the selected row showed waiting for a person: the same door as its Show network row and the status line —
    /// one press bound to the whole fingerprint of a certificate pinned first, whose short form the row showed, or at least 8 hex typed
    /// into an empty box after CERTIFICATE CHANGED. A link whose waiting certificate is no longer the one its row showed is drawn again
    /// and nothing is trusted: the person compares the one it shows now.
    /// </summary>
    private void OnTrustPlayoutLink(object sender, RoutedEventArgs e)
    {
        if (PlayoutLinkList.SelectedItem is not PlayoutRow { Waiting: { } drawn } row || Playout is not { } hooks) return;
        if (hooks.Waiting(row.Name) is not { } live || live.Wait != drawn.Wait)
        {
            SayPlayout($"Nothing trusted: the certificate {drawn.Peer} has waiting is no longer the one its row showed — compare the one it shows now.");
            RefreshPlayoutLinks();
            return;
        }
        if (Help.TrustPrompt.Run(this, drawn, hooks.Trust) is { } said) SayPlayout(said.Words);
        RefreshPlayoutLinks();
    }

    // ------------------------------------------------------------------ triggers' secrets

    /// <summary>
    /// A trigger's secret: its kind, and a box for the secret that shows only whether one is kept. Checked as typed
    /// (<see cref="TriggerAuthRules.SecretProblem"/>) before it is protected; where it may go is Save's to check.
    /// </summary>
    private void OnTriggerSecret(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TriggerRule rule }) return;
        // A row still being typed in is committed first: the grid is drawn again once the secret is set, which no open edit allows.
        if (!TriggerGrid.CommitEdit(DataGridEditingUnit.Row, true))
        {
            MessageBox.Show(this, "Finish the trigger's row first: one of its cells can't be kept as typed.", "Trigger secret", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        const string Kept = "••••••••";
        var panel = new StackPanel { Margin = new Thickness(20), Width = 480 };
        panel.Children.Add(new TextBlock { Text = TriggerAuthRules.Named(rule), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(new TextBlock
        {
            Text = "Sent only to this PC, or over https:// to a certificate you trusted (the first time, the target's certificate is pinned and nothing is sent: "
                   + "compare it on the target's own screen, then trust it from the status line's right-click). With a secret, the target's address is fixed: placeholders only in its path or query.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 0, 0, 8),
        });
        var kind = new ComboBox();
        foreach (var k in TriggerAuthRules.Kinds) kind.Items.Add(new ComboBoxItem { Content = TriggerAuthRules.KindWords(k), Tag = k });
        kind.SelectedItem = kind.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == TriggerAuthRules.KindOf(rule)) ?? kind.Items[0];
        panel.Children.Add(kind);
        panel.Children.Add(new TextBlock { Text = "Secret", Opacity = 0.75, Margin = new Thickness(0, 8, 0, 2) });
        var secret = new PasswordBox { ToolTip = "Kept encrypted for this Windows user; in no status, row, log line, frame or context." };
        if (rule.Auth is { SecretProtected.Length: > 0 }) secret.Password = Kept;
        panel.Children.Add(secret);
        var why = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        panel.Children.Add(why);
        while (AskDialog(panel, "Trigger secret", "Keep"))
        {
            var k = (kind.SelectedItem as ComboBoxItem)?.Tag as string ?? TriggerAuthRules.None;
            string? protectedSecret = null;
            if (k != TriggerAuthRules.None && secret.Password != Kept)
            {
                if (TriggerAuthRules.SecretProblem(secret.Password) is { } bad)
                {
                    why.Text = bad + ".";
                    why.Visibility = Visibility.Visible;
                    continue;
                }
                protectedSecret = ProtectedKey.Protect(secret.Password);
            }
            if (TriggerAuthRules.SetProfile(rule, k, protectedSecret) is { } refused)
            {
                why.Text = refused + ".";
                why.Visibility = Visibility.Visible;
                continue;
            }
            TriggerGrid.Items.Refresh();
            return;
        }
    }

    /// <summary>
    /// The save-time warnings of rules that carry a secret in their address, each said; for a Stagewatch PIN, the move into the rule's
    /// secret is offered (<see cref="TriggerAuthRules.MovePin"/>) and made only where the rule would then be sent — else why not is said,
    /// and the rule fires as before.
    /// </summary>
    private List<string> WarnTriggers(IEnumerable<TriggerRule> rules)
    {
        var warned = new List<string>();
        foreach (var t in rules)
        {
            if (TriggerAuthRules.Warning(t) is not { } why) continue;
            var named = TriggerAuthRules.Named(t);
            if (TriggerAuthRules.QueryPin(t) is not null
                && MessageBox.Show(this, $"{named}: {why}.\n\nMove its PIN out of the address into its secret now?", "Triggers",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                if (TriggerAuthRules.MovePin(t, ProtectedKey.Protect) is { } notMoved) warned.Add($"{named}: its PIN is not moved — {notMoved}");
                continue;
            }
            warned.Add($"{named}: {why}");
        }
        TriggerGrid.Items.Refresh();
        return warned;
    }
}
