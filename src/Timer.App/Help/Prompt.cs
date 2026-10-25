using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Stagewatch.Core;

namespace Stagewatch.App.Help;

/// <summary>A one-line text prompt in the app's dark style (WPF has no input box).</summary>
public static class Prompt
{
    public static string? Ask(Window owner, string title, string label, string initial)
    {
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 6, 0, 14), MinWidth = 320 };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = label, Opacity = 0.7 });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        var win = new Window
        {
            Title = title, Owner = owner, Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = (System.Windows.Media.Brush)owner.FindResource("BgDeepBrush")
        };
        ok.Click += (_, _) => win.DialogResult = true;
        win.Loaded += (_, _) => { box.Focus(); box.SelectAll(); Keyboard.Focus(box); };
        return win.ShowDialog() == true && box.Text.Trim().Length > 0 ? box.Text.Trim() : null;
    }
}

/// <summary>Choose one of a few labelled items, in the app's dark style: the first is selected; null when cancelled.</summary>
public static class Pick
{
    public static T? One<T>(Window owner, string title, string label, IReadOnlyList<T> items, Func<T, string> text, string okLabel) where T : class
    {
        var list = new ListBox { MaxHeight = 260, Width = 520, Margin = new Thickness(0, 6, 0, 14) };
        foreach (var item in items) list.Items.Add(new ListBoxItem { Content = text(item), Tag = item });
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        var ok = new Button { Content = okLabel, IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = label, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
        panel.Children.Add(list);
        panel.Children.Add(buttons);
        var win = new Window
        {
            Title = title, Owner = owner, Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Background = (System.Windows.Media.Brush)owner.FindResource("BgDeepBrush")
        };
        ok.Click += (_, _) => win.DialogResult = true;
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is not null) win.DialogResult = true; };
        win.Loaded += (_, _) => { list.Focus(); Keyboard.Focus(list); };
        return win.ShowDialog() == true && list.SelectedItem is ListBoxItem { Tag: T chosen } ? chosen : null;
    }
}

/// <summary>A larger text box to paste into or review AI drafts before they're kept (OK = keep, Cancel = discard).</summary>
public static class TextReview
{
    public static string? Show(Window owner, string title, string label, string initial)
    {
        var box = new TextBox
        {
            Text = initial, Margin = new Thickness(0, 6, 0, 14), Width = 640, Height = 360, AcceptsReturn = true, AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13
        };
        var ok = new Button { Content = "OK", IsDefault = false, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = label, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, MaxWidth = 640 });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        var win = new Window
        {
            Title = title, Owner = owner, Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Background = (System.Windows.Media.Brush)owner.FindResource("BgDeepBrush")
        };
        ok.Click += (_, _) => win.DialogResult = true;
        win.Loaded += (_, _) => { box.Focus(); Keyboard.Focus(box); };
        return win.ShowDialog() == true && box.Text.Trim().Length > 0 ? box.Text.Trim() : null;
    }
}

/// <summary>Quick filters for files dropped on the Holding tab (the library checks the real contents).</summary>
public static class HoldingFiles
{
    public static bool LooksLikeImage(string path)
        => HoldingLibrary.Extensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());
}
