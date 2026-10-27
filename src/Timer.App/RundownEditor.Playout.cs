using System.Windows;
using System.Windows.Controls;
using Stagewatch.Core;
using Stagewatch.Integrations;

namespace Stagewatch.App;

/// <summary>
/// The running-order editor's playout cues: up to four per item, each a PlayMap playlist's item or a Patterns cue picked from what the
/// peer lists (its INDEX or CUE LIST, as the desk's links read them), or typed by id while the peer lists nothing. Standby is off unless
/// set, with the words of what it does on that kind of peer; together is said as what it is. A cue is made only by the rules a running
/// order keeps (<see cref="PlayoutCueChoices.Make"/>), so a save never loses one.
/// </summary>
public partial class RundownEditor
{
    /// <summary>The desk's playout links as the picker offers them (their kinds and lists); none while the editor runs alone.</summary>
    public Func<IReadOnlyList<PlayoutCueSource>>? PlayoutSources { get; init; }

    private void OnPlayoutCues(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Row row }) return;
        Grid.CommitEdit();
        var sources = PlayoutSources?.Invoke() ?? [];
        var cues = row.Playout.Select(c => c.Clone()).ToList();
        var panel = new StackPanel { Margin = new Thickness(20), Width = 560 };
        panel.Children.Add(new TextBlock { Text = $"{row.Number}. {row.Title}", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(new TextBlock
        {
            Text = $"Up to {PlayoutRules.MaxCuesPerItem} cues. Each GOes when the item starts (a Patterns cue by CUE GO alone: its own standby decides). "
                   + "Right-click the item in the running order to cue or GO them by hand.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 0, 0, 8),
        });
        var list = new ListBox { Height = 120, Margin = new Thickness(0, 0, 0, 6) };
        void Fill()
        {
            var at = list.SelectedIndex;
            list.Items.Clear();
            foreach (var c in cues) list.Items.Add(PlayoutCueChoices.Describe(c));
            list.SelectedIndex = Math.Min(at, list.Items.Count - 1);
        }
        Fill();
        panel.Children.Add(list);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var add = new Button { Content = "Add…", Margin = new Thickness(0, 0, 6, 0) };
        var edit = new Button { Content = "Edit…", Margin = new Thickness(0, 0, 6, 0) };
        var remove = new Button { Content = "Remove" };
        buttons.Children.Add(add);
        buttons.Children.Add(edit);
        buttons.Children.Add(remove);
        panel.Children.Add(buttons);
        if (sources.Count == 0)
            panel.Children.Add(new TextBlock
            {
                Text = "No playout links are set up on this PC (Settings → Integrations): a cue names a link by its name, and is skipped with advice until there is one.",
                TextWrapping = TextWrapping.Wrap, Opacity = 0.6, FontSize = 12, Margin = new Thickness(0, 8, 0, 0),
            });
        void Sync()
        {
            add.IsEnabled = cues.Count < PlayoutRules.MaxCuesPerItem;
            edit.IsEnabled = remove.IsEnabled = list.SelectedIndex >= 0;
        }
        list.SelectionChanged += (_, _) => Sync();
        Sync();
        add.Click += (_, _) =>
        {
            if (EditCue(sources, null) is { } made) { cues.Add(made); Fill(); list.SelectedIndex = cues.Count - 1; Sync(); }
        };
        edit.Click += (_, _) =>
        {
            var i = list.SelectedIndex;
            if (i >= 0 && EditCue(sources, cues[i]) is { } made) { cues[i] = made; Fill(); Sync(); }
        };
        list.MouseDoubleClick += (_, _) =>
        {
            var i = list.SelectedIndex;
            if (i >= 0 && EditCue(sources, cues[i]) is { } made) { cues[i] = made; Fill(); Sync(); }
        };
        remove.Click += (_, _) =>
        {
            var i = list.SelectedIndex;
            if (i < 0) return;
            cues.RemoveAt(i);
            Fill();
            Sync();
        };
        if (!ShowDialogWith(panel, "Playout cues")) return;
        row.SetPlayout(cues);
    }

    /// <summary>
    /// One cue: its link, what it cues — picked from the peer's own list, or typed by id — its label, and the standby and together
    /// options with the words of what each does. Null when cancelled; a refusal keeps the dialog open with why.
    /// </summary>
    private PlayoutCue? EditCue(IReadOnlyList<PlayoutCueSource> sources, PlayoutCue? start)
    {
        var panel = new StackPanel { Margin = new Thickness(20), Width = 520 };
        TextBlock Label(string text) => new() { Text = text, Opacity = 0.75, Margin = new Thickness(0, 6, 0, 2), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(Label("Link"));
        var link = new ComboBox { IsEditable = true, ToolTip = "A playout link of this PC (Settings → Integrations), or a link's name typed" };
        foreach (var src in sources) link.Items.Add(src.Name);
        link.Text = start?.Link ?? sources.FirstOrDefault()?.Name ?? "";
        panel.Children.Add(link);
        var status = new TextBlock { Opacity = 0.6, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        panel.Children.Add(status);
        panel.Children.Add(Label("What it cues"));
        var pick = new ComboBox();
        panel.Children.Add(pick);
        var left = new TextBlock { Opacity = 0.6, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(left);
        panel.Children.Add(Label("…or by id: a PlayMap playlist and its item (a Patterns cue: its id alone, playlist left empty)"));
        var ids = new StackPanel { Orientation = Orientation.Horizontal };
        var list = new TextBox { Width = 200, Text = start?.List ?? "", ToolTip = "PlayMap playlist id (empty for a Patterns cue)", Margin = new Thickness(0, 0, 8, 0) };
        var item = new TextBox { Width = 200, Text = start?.Item ?? "", ToolTip = "PlayMap item id, or the Patterns cue's id" };
        ids.Children.Add(list);
        ids.Children.Add(item);
        panel.Children.Add(ids);
        panel.Children.Add(Label($"Label (how people read it, at most {PlayoutRules.MaxLabelChars})"));
        var label = new TextBox { Text = start?.Label ?? "", MaxLength = PlayoutRules.MaxLabelChars };
        panel.Children.Add(label);
        var standby = new CheckBox { IsChecked = start?.Standby == true, Margin = new Thickness(0, 10, 0, 2) };
        var standbyWords = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 480 };
        standby.Content = standbyWords;
        var together = new CheckBox { Content = new TextBlock { Text = PlayoutCueChoices.TogetherWords, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 }, IsChecked = start?.Together == true, Margin = new Thickness(0, 4, 0, 2) };
        panel.Children.Add(standby);
        panel.Children.Add(together);
        var why = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        panel.Children.Add(why);

        var filling = false;
        void ForLink()
        {
            var name = (link.Text ?? "").Trim().ToLowerInvariant();
            var src = sources.FirstOrDefault(x => x.Name == name);
            status.Text = src is null
                ? (name.Length == 0 ? "" : $"No playout link called {name} on this PC: the cue is kept, and skipped with advice until there is one.")
                : $"{src.Peer}: {src.Status}";
            standbyWords.Text = PlayoutCueChoices.StandbyWords(src?.Kind);
            var (choices, unkept) = PlayoutCueChoices.Of(src?.Index);
            filling = true;
            pick.Items.Clear();
            foreach (var c in choices) pick.Items.Add(c);
            pick.SelectedItem = choices.FirstOrDefault(c => c.List == list.Text.Trim() && c.Item == item.Text.Trim());
            filling = false;
            pick.IsEnabled = choices.Count > 0;
            left.Text = src is { Index: null } ? $"{src.Peer} lists nothing yet (not paired, or another kind or node answers) — type the ids, or pick them once it is paired."
                : PlayoutCueChoices.LeftWords(unkept);
        }
        link.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(new Action(ForLink));
        link.LostFocus += (_, _) => ForLink();
        pick.SelectionChanged += (_, _) =>
        {
            if (filling || pick.SelectedItem is not PlayoutCueChoice c) return;
            list.Text = c.List;
            item.Text = c.Item;
            label.Text = c.Label;
        };
        ForLink();

        while (ShowDialogWith(panel, start is null ? "Add a playout cue" : "Playout cue"))
        {
            var (cue, refused) = PlayoutCueChoices.Make(link.Text ?? "", list.Text, item.Text, label.Text, standby.IsChecked == true, together.IsChecked == true);
            if (cue is not null) return cue;
            why.Text = refused + ".";
            why.Visibility = Visibility.Visible;
            // The panel leaves the closed dialog's window so it can be shown again with what was typed.
            if (panel.Parent is System.Windows.Controls.Panel holder) holder.Children.Remove(panel);
        }
        return null;
    }
}
