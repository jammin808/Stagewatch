using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Stagewatch.App.ViewModels;
using Stagewatch.Show;

namespace Stagewatch.App;

/// <summary>
/// The desk's door to another show app (Show Bus §11, a person's line): one line typed in that app's own words, with what the desk
/// service says of it as it stands — the form that takes it, that form's risk, whether the app is asking this desk for its lines, and
/// why it would be refused — read again at every keystroke and once a second, since an app stops collecting with time alone. The
/// window holds no rule: Send is live exactly while the service would take the line, the press hands it the string as typed, and the
/// service's answer to that send is what stands, then what the app did with the line. It never goes through the command box's path,
/// so no deck's recorder takes it.
/// </summary>
public partial class SendLineWindow : Window
{
    private static readonly Brush Low = Tint("#334ADE80"), Review = Tint("#33F59E0B"), High = Tint("#55DC2626"), None = Tint("#33808080");

    private readonly MainViewModel _vm;
    private readonly string _appId;
    private readonly string _name;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private IReadOnlyList<DeskVerb> _forms = [];
    /// <summary>The last line this window sent, by its proposal's id: only its outcome is shown, never an earlier line's arriving late.</summary>
    private string _sent = "";
    private bool _closed;

    public SendLineWindow(MainViewModel vm, string appId, string name, string start, bool verbs)
    {
        InitializeComponent();
        Help.ContextHelp.SetTopic(this, "integrations");
        Help.ContextHelp.Attach(this);
        _vm = vm;
        _appId = appId;
        _name = name;
        Title = verbs ? $"{name}'s verbs" : $"Send a line to {name}";
        Heading.Text = verbs ? $"{name}'s verbs" : $"A line for {name}";
        LineBox.Text = start;
        LineBox.TextChanged += (_, _) => Refresh();
        _tick.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            Refresh();
            _tick.Start();
            LineBox.Focus();
            LineBox.CaretIndex = LineBox.Text.Length;
        };
        Closed += (_, _) => { _closed = true; _tick.Stop(); };
    }

    /// <summary>What the service last said of the line as it stands: what the window draws.</summary>
    public LinePreview? Shown { get; private set; }

    /// <summary>Start the line again with a form's opening words (a quick verb, a form picked from the list), the caret after them.</summary>
    public void Start(string lead)
    {
        LineBox.Text = lead;
        LineBox.Focus();
        LineBox.CaretIndex = LineBox.Text.Length;
    }

    /// <summary>Everything drawn follows the service's answer for the line as it stands now; nothing is decided here.</summary>
    private void Refresh()
    {
        var typed = LineBox.Text.Trim().Length > 0;
        var p = _vm.PreviewPeerLine(_appId, LineBox.Text);
        Shown = p;

        CollectText.Text = (p.Collects ? "✓ " : "△ ") + p.Collecting;
        CollectText.Foreground = (Brush)FindResource(p.Collects ? "TextMutedBrush" : "AccentAmberBrush");

        // The chip is a word first: the form's risk as its app lists it, or NOT LISTED only when the send refuses the line because
        // no form takes it. A line refused for anything else (verbs not verified, a filled secret, the fence) has no chip: the words
        // beside Send say why, and no chip reads softer than the refusal.
        var risk = p.Risk.Length > 0 ? p.Risk.ToUpperInvariant() : typed && p.Unlisted ? "NOT LISTED" : "";
        RiskText.Text = risk;
        RiskChip.Background = p.Risk switch { "low" => Low, "review" => Review, "high" => High, _ => None };
        RiskChip.Visibility = risk.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        FormText.Text = p.Form ?? "";
        FormWords.Text = p.Words;

        if (!_forms.SequenceEqual(p.Forms))
        {
            _forms = p.Forms;
            FormsList.ItemsSource = _forms;
        }
        FormsEmpty.Text = typed ? $"None of {_name}'s verbs opens with that." : $"{_name} lists no verbs this desk can read.";
        FormsEmpty.Visibility = _forms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Send is live exactly while the service would take the line; greyed, it says why in the send's own words.
        SendButton.IsEnabled = p.Refused is null;
        SendButton.Content = p.Refused is null && p.Risk == "high" ? "Send (high)" : "Send";
        var why = p.Refused is null ? "" : typed ? p.Refused : "Type a line, or pick one of its verbs.";
        WhyText.Text = why;
        SendButton.ToolTip = why.Length > 0 ? why : null;
    }

    private void OnPickForm(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DeskVerb { Lead.Length: > 0 } verb) Start(verb.Lead + " ");
    }

    /// <summary>
    /// The press: the line goes to the service as typed. Its answer is shown as it is — a refusal in its words, the line left to
    /// mend — and a line that went is then waited for: what its app did with it, in the app's words, replaces "sent" when it comes.
    /// </summary>
    private async void OnSend(object sender, RoutedEventArgs e)
    {
        var outcome = _vm.SendPeerLine(_appId, LineBox.Text);
        if (!outcome.Ok || outcome.Proposal is not { } sent)
        {
            Say("✕ ", outcome.Words, "AccentRedBrush");
            Refresh();
            return;
        }
        _sent = sent.Id;
        Say("→ ", outcome.Words, "TextMutedBrush");
        // Cleared, so a second press sends nothing: the next line is typed anew.
        LineBox.Clear();
        var result = await _vm.PeerLineSettled(sent.Id);
        if (_closed || _sent != sent.Id) return;
        Say(result.Ok == true ? "✓ " : "✕ ", result.Words, result.Ok == true ? "NeonGreenBrush" : "AccentRedBrush");
    }

    /// <summary>What the last press came to: a shape and the words, the colour only following them.</summary>
    private void Say(string mark, string words, string brush)
    {
        ResultText.Text = mark + words;
        ResultText.Foreground = (Brush)FindResource(brush);
        ResultText.Visibility = Visibility.Visible;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private static Brush Tint(string hex)
    {
        var brush = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
