using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Stagewatch.App.Help;

/// <summary>The in-app help: one window, topics on the left, the chosen topic on the right.</summary>
public partial class HelpWindow : Window
{
    private static HelpWindow? _instance;
    private readonly ICollectionView _view;
    private HelpTopic? _current;

    private HelpWindow()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(HelpContent.Topics);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(HelpTopic.Group)));
        TopicList.ItemsSource = _view;
        Closed += (_, _) => _instance = null;
        PreviewKeyDown += OnKey;
    }

    /// <summary>Open the help (or bring it forward) at a topic.</summary>
    public static void ShowTopic(Window? owner, string topicId)
    {
        // A help window opened before a modal dialog is disabled by that dialog: reopen it from the dialog.
        if (_instance is { IsEnabled: false }) _instance.Close();
        if (_instance is null)
        {
            _instance = new HelpWindow();
            if (owner is { IsVisible: true }) _instance.Owner = owner;
            else _instance.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _instance.Show();
        }
        if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
        _instance.Activate();
        _instance.Select(HelpContent.Topic(topicId) ?? HelpContent.Topics[0]);
    }

    private void Select(HelpTopic topic)
    {
        if (!string.IsNullOrEmpty(SearchBox.Text) && !Matches(topic, SearchBox.Text)) SearchBox.Text = "";
        TopicList.SelectedItem = topic;
        TopicList.ScrollIntoView(topic);
    }

    private void OnTopicSelected(object sender, SelectionChangedEventArgs e)
    {
        if (TopicList.SelectedItem is HelpTopic t) Render(t);
    }

    private void OnSearch(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.Trim();
        SearchHint.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _view.Filter = q.Length == 0 ? null : o => o is HelpTopic t && Matches(t, q);
        var any = !_view.IsEmpty;
        NoResults.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        if (any && (TopicList.SelectedItem is not HelpTopic sel || !Matches(sel, q)))
        {
            _view.MoveCurrentToFirst();
            TopicList.SelectedItem = _view.CurrentItem;
        }
    }

    private static bool Matches(HelpTopic t, string query)
        => query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => t.SearchText.Contains(w.ToLowerInvariant()));

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
    }

    private void OnPrev(object sender, RoutedEventArgs e) => Step(-1);
    private void OnNext(object sender, RoutedEventArgs e) => Step(+1);

    private void Step(int delta)
    {
        if (_current is null) return;
        var i = IndexOf(_current) + delta;
        if (i >= 0 && i < HelpContent.Topics.Count) Select(HelpContent.Topics[i]);
    }

    private static int IndexOf(HelpTopic t)
    {
        for (int i = 0; i < HelpContent.Topics.Count; i++) if (HelpContent.Topics[i] == t) return i;
        return -1;
    }

    // ------------------------------------------------------------------ rendering

    private void Render(HelpTopic t)
    {
        _current = t;
        Page.Children.Clear();

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock { Text = t.Title, FontSize = 26, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        if (t.Tier is { } tier) head.Children.Add(Badge(tier));
        Page.Children.Add(head);
        Page.Children.Add(new TextBlock { Text = t.Summary, FontSize = 15, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 14) });

        foreach (var b in t.Blocks) Page.Children.Add(Block(b));

        var i = IndexOf(t);
        PrevButton.Visibility = i > 0 ? Visibility.Visible : Visibility.Hidden;
        NextButton.Visibility = i < HelpContent.Topics.Count - 1 ? Visibility.Visible : Visibility.Hidden;
        if (i > 0) PrevButton.Content = "◀  " + HelpContent.Topics[i - 1].Title;
        if (i < HelpContent.Topics.Count - 1) NextButton.Content = HelpContent.Topics[i + 1].Title + "  ▶";
        Scroller.ScrollToTop();
    }

    private Border Badge(string tier) => new()
    {
        Margin = new Thickness(12, 4, 0, 0),
        Padding = new Thickness(8, 1, 8, 2),
        CornerRadius = new CornerRadius(9),
        VerticalAlignment = VerticalAlignment.Center,
        BorderThickness = new Thickness(1),
        BorderBrush = (Brush)FindResource(tier == HelpContent.Production ? "AccentAmberBrush" : "AccentBlueBrush"),
        ToolTip = $"Needs Stagewatch {tier} (or the trial)",
        Child = new TextBlock { Text = tier, FontSize = 12, FontWeight = FontWeights.SemiBold,
                                Foreground = (Brush)FindResource(tier == HelpContent.Production ? "AccentAmberBrush" : "AccentBlueBrush") }
    };

    private UIElement Block(HelpBlock b) => b switch
    {
        H h => Text(h.Text, size: 17, weight: FontWeights.SemiBold, margin: new Thickness(0, 14, 0, 6)),
        P p => Text(p.Text, margin: new Thickness(0, 0, 0, 10)),
        Note n => new Border
        {
            Background = (Brush)FindResource("BgRaisedBrush"),
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 4, 0, 12),
            Child = Text(n.Text)
        },
        Bullets l => List(l.Items, _ => "•"),
        Steps s => List(s.Items, i => $"{i + 1}."),
        Table t => TableOf(t),
        _ => new TextBlock()
    };

    private StackPanel List(string[] items, Func<int, string> marker)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        for (int i = 0; i < items.Length; i++)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = marker(i), Opacity = 0.6, FontWeight = FontWeights.SemiBold });
            var text = Text(items[i]);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            panel.Children.Add(row);
        }
        return panel;
    }

    private Border TableOf(Table t)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto), MaxWidth = 230 });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < t.Rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            var shade = new Border { Background = i % 2 == 0 ? (Brush)FindResource("BgRaisedBrush") : Brushes.Transparent };
            Grid.SetRow(shade, i);
            Grid.SetColumnSpan(shade, 2);
            grid.Children.Add(shade);

            var term = Text(t.Rows[i].Term, weight: FontWeights.SemiBold, margin: new Thickness(10, 6, 16, 6));
            Grid.SetRow(term, i);
            grid.Children.Add(term);
            var meaning = Text(t.Rows[i].Meaning, margin: new Thickness(0, 6, 10, 6));
            Grid.SetRow(meaning, i);
            Grid.SetColumn(meaning, 1);
            grid.Children.Add(meaning);
        }
        return new Border
        {
            Child = grid,
            BorderBrush = (Brush)FindResource("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 12),
            ClipToBounds = true
        };
    }

    private static readonly Regex Markup = new(@"(\*\*[^*]+\*\*|`[^`]+`|\*[^*\s][^*]*\*)", RegexOptions.Compiled);

    /// <summary>Text with **bold**, *a setting's name* and `code`.</summary>
    private TextBlock Text(string text, double size = 14, FontWeight? weight = null, Thickness? margin = null)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            Margin = margin ?? new Thickness(0),
            LineHeight = size * 1.45
        };
        foreach (var part in Markup.Split(text))
        {
            if (part.Length == 0) continue;
            if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                tb.Inlines.Add(new Run(part[2..^2]) { FontWeight = FontWeights.SemiBold });
            else if (part.StartsWith('`') && part.EndsWith('`') && part.Length > 2)
                tb.Inlines.Add(new Run(part[1..^1])
                {
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = size - 1,
                    Background = (Brush)FindResource("BgRaisedHoverBrush"),
                    Foreground = (Brush)FindResource("AccentBrush")
                });
            else if (part.StartsWith('*') && part.EndsWith('*') && part.Length > 2)
                tb.Inlines.Add(new Run(part[1..^1]) { FontStyle = FontStyles.Italic, Foreground = (Brush)FindResource("TextMutedBrush") });
            else tb.Inlines.Add(new Run(part));
        }
        return tb;
    }
}
