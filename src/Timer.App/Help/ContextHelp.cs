using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Stagewatch.App.Help;

/// <summary>Builds a window's right-click menus: actions for the element, then what it is (short help).</summary>
public interface IContextActions
{
    /// <summary>Quick actions for the element tagged <paramref name="tip"/> (its DataContext is the row, preset, cue…).</summary>
    IEnumerable<Control> ActionsFor(string tip, FrameworkElement element);
}

/// <summary>
/// Right-click help for every window. Tag an element with <c>help:ContextHelp.Tip="key"</c> (see
/// <see cref="HelpContent.Tips"/>), or let it fall back to the control's own tooltip; <c>ContextHelp.Topic</c>
/// (inherited) says which help topic a whole tab or dialog belongs to. One handler per window builds the menu
/// on demand — nothing is created until someone right-clicks.
/// </summary>
public static class ContextHelp
{
    public static readonly DependencyProperty TipProperty = DependencyProperty.RegisterAttached(
        "Tip", typeof(string), typeof(ContextHelp), new PropertyMetadata(null));
    public static string? GetTip(DependencyObject d) => (string?)d.GetValue(TipProperty);
    public static void SetTip(DependencyObject d, string? value) => d.SetValue(TipProperty, value);

    public static readonly DependencyProperty TopicProperty = DependencyProperty.RegisterAttached(
        "Topic", typeof(string), typeof(ContextHelp), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    public static string? GetTopic(DependencyObject d) => (string?)d.GetValue(TopicProperty);
    public static void SetTopic(DependencyObject d, string? value) => d.SetValue(TopicProperty, value);

    /// <summary>Turn on right-click help (and F1) for a window.</summary>
    public static void Attach(Window window)
    {
        window.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && Open(window, source)) e.Handled = true;
        };
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F1)
            {
                var topic = Keyboard.FocusedElement is DependencyObject d ? TopicFor(d) : null;
                HelpWindow.ShowTopic(window, topic ?? GetTopic(window) ?? "start");
                e.Handled = true;
            }
            // The context-menu key and Shift+F10 open the same menu for the focused control.
            else if ((e.Key == Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift))
                     && Keyboard.FocusedElement is DependencyObject focused && Open(window, focused, atMouse: false))
                e.Handled = true;
        };
    }

    private static string? TopicFor(DependencyObject d)
    {
        for (var x = d; x is not null; x = Parent(x))
            if (GetTip(x) is { } key && HelpContent.Tip(key) is { } tip) return tip.Topic;
        return GetTopic(d);
    }

    /// <summary>Build and show the menu for the element under the pointer. False = nothing to say (default behaviour).</summary>
    public static bool Open(Window window, DependencyObject source, bool atMouse = true)
    {
        // Already inside a menu: let it be.
        if (FindAncestor<ContextMenu>(source) is not null || FindAncestor<MenuItem>(source) is not null) return false;

        // The nearest tagged element wins; otherwise the nearest control that explains itself.
        FrameworkElement? target = null;
        string? key = null;
        for (var x = source; x is not null; x = Parent(x))
        {
            if (GetTip(x) is { } k && x is FrameworkElement fe) { target = fe; key = k; break; }
        }
        if (target is null)
        {
            for (var x = source; x is not null; x = Parent(x))
            {
                if (x is FrameworkElement fe && (fe.ToolTip is string || fe is System.Windows.Controls.Primitives.ButtonBase || fe is ComboBox || fe is TextBox || fe is System.Windows.Controls.DataGrid))
                {
                    target = fe;
                    break;
                }
            }
        }
        if (target is null) return false;

        var tip = HelpContent.Tip(key);
        var title = tip?.Title ?? LabelOf(target);
        var text = tip?.Text ?? (target.ToolTip as string);
        var topicId = tip?.Topic ?? GetTopic(target);
        var actions = key is not null && window is IContextActions provider ? provider.ActionsFor(key, target).ToList() : new List<Control>();
        var textBox = target as TextBox ?? FindAncestor<TextBox>(source);
        if (textBox is not null && !textBox.IsEnabled) textBox = null;

        if (title is null && text is null && actions.Count == 0 && textBox is null && topicId is null) return false;

        // What it is, then what you can do with it, then where to read more.
        var menu = new ContextMenu { PlacementTarget = target, Placement = atMouse ? PlacementMode.MousePoint : PlacementMode.Bottom };
        void Group(IEnumerable<Control> items)
        {
            var list = items.ToList();
            if (list.Count == 0) return;
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            foreach (var i in list) menu.Items.Add(i);
        }
        if (title is not null || text is not null) Group(new[] { Explanation(title, text) });
        Group(actions);
        if (textBox is not null)
            Group(new Control[]
            {
                Item("Cut", () => textBox.Cut(), textBox.SelectionLength > 0 && !textBox.IsReadOnly, "Ctrl+X"),
                Item("Copy", () => textBox.Copy(), textBox.SelectionLength > 0, "Ctrl+C"),
                Item("Paste", () => textBox.Paste(), !textBox.IsReadOnly && Clipboard.ContainsText(), "Ctrl+V"),
                Item("Select all", () => { textBox.Focus(); textBox.SelectAll(); }, textBox.Text.Length > 0, "Ctrl+A"),
            });
        if (HelpContent.Topic(topicId) is { } topic)
            Group(new[] { Item($"Help: {topic.Title}…", () => HelpWindow.ShowTopic(window, topic.Id), true, "F1") });

        menu.IsOpen = true;
        return true;
    }

    /// <summary>A clickable menu item.</summary>
    public static MenuItem Item(string header, Action run, bool enabled = true, string? gesture = null)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled, InputGestureText = gesture ?? "" };
        item.Click += (_, _) => run();
        return item;
    }

    /// <summary>
    /// A menu item that is live when <paramref name="why"/> is null, else greyed with why — the words its press would be refused in —
    /// shown when the pointer rests on it, greyed as it is.
    /// </summary>
    public static MenuItem Offer(string header, Action run, string? why, string? gesture = null)
    {
        var item = Item(header, run, why is null, gesture);
        if (why is not null)
        {
            item.ToolTip = why;
            ToolTipService.SetShowOnDisabled(item, true);
        }
        return item;
    }

    /// <summary>The help text block at the foot of a menu: title in bold, then one or two lines.</summary>
    private static MenuItem Explanation(string? title, string? text)
    {
        var panel = new StackPanel { MaxWidth = 340, Margin = new Thickness(0, 2, 0, 2) };
        if (title is not null)
            panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        if (text is not null)
            panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });
        return new MenuItem { Header = panel, StaysOpenOnClick = true, Focusable = false, Tag = "explanation" };
    }

    private static string? LabelOf(FrameworkElement e) => e switch
    {
        ContentControl { Content: string s } => s.Trim(),
        _ => null
    };

    private static DependencyObject? Parent(DependencyObject d)
        => d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d)
                                                                : LogicalTreeHelper.GetParent(d);

    private static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        for (var x = d; x is not null; x = Parent(x)) if (x is T t) return t;
        return null;
    }
}
