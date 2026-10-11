using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Stagewatch.App.Help;
using Stagewatch.Core;

namespace Stagewatch.App;

/// <summary>
/// The main window's side panels, arranged by the operator: drag a tab along the strip to reorder it, drag it out of
/// the window to give it a window of its own (beside the timer, on another monitor), drag that window back onto the
/// side panel to dock it, and drag the splitter to widen the column. The rules — every panel always somewhere, each
/// once — are <see cref="PanelLayout"/>'s (Timer.Core, tested); this class only moves controls to match and saves the
/// result. With every panel torn out, the side column folds away and the timer takes the whole window.
/// </summary>
public sealed class PanelDock
{
    private readonly MainWindow _owner;
    private readonly TabControl _tabs;
    private readonly ColumnDefinition _sideCol, _splitCol;
    private readonly FrameworkElement[] _sideParts;
    private readonly Border _sidePanel, _hint;
    private readonly SettingsStore _store;
    private readonly Dictionary<string, TabItem> _items = new();
    private readonly Dictionary<string, PanelWindow> _windows = new();
    private readonly Brush _edge;

    private TabItem? _pressed;
    private Point _pressAt;
    private bool _dragging, _reordered;

    /// <summary>True while the app closes: windows close without docking, so their places are remembered.</summary>
    public bool ShuttingDown { get; private set; }

    public PanelDock(MainWindow owner, TabControl tabs, IReadOnlyDictionary<string, TabItem> items, ColumnDefinition sideCol, ColumnDefinition splitCol,
        GridSplitter splitter, Border sidePlate, Border sidePanel, Border hint, SettingsStore store)
    {
        _owner = owner;
        _tabs = tabs;
        _sideCol = sideCol;
        _splitCol = splitCol;
        _sidePanel = sidePanel;
        _hint = hint;
        _store = store;
        _edge = sidePanel.BorderBrush;
        _sideParts = new FrameworkElement[] { splitter, sidePlate, sidePanel };
        foreach (var (id, item) in items)
        {
            _items[id] = item;
            // The header gets a right-click menu of its own; its colour follows the tab (selected / muted).
            var header = new TextBlock { Text = item.Header as string ?? id };
            header.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Control.Foreground)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(TabItem), 1) });
            ContextHelp.SetTip(header, "panel");
            header.Tag = id;   // which panel this header names (data, as Tag is used throughout)
            item.Header = header;
            item.ToolTip = "Drag to reorder · drag out of the window to give it its own window · right-click for more";
            item.PreviewMouseLeftButtonDown += OnTabDown;
        }
        _tabs.PreviewMouseMove += OnTabMove;
        _tabs.PreviewMouseLeftButtonUp += OnTabUp;
        _tabs.LostMouseCapture += (_, _) => EndTabDrag();
        splitter.DragCompleted += (_, _) => _store.Update(s => s.Panels.SideWidth = Math.Round(_sideCol.ActualWidth));
        splitter.MouseDoubleClick += (_, _) =>
        {
            _store.Update(s => s.Panels.SideWidth = PanelLayout.DefaultSideWidth);
            _sideCol.Width = new GridLength(PanelLayout.DefaultSideWidth);
        };
    }

    private PanelLayoutSettings Layout => _store.Settings.Panels;

    public static string IdOf(FrameworkElement header) => header.Tag as string ?? "";

    public string Title(string id) => _items.TryGetValue(id, out var item) && item.Header is TextBlock tb ? tb.Text : id;

    public bool IsFloating(string id) => _windows.ContainsKey(id);

    /// <summary>Lay the panels out as saved: order, torn-out windows, width. Call once the main window is shown.</summary>
    public void Restore()
    {
        var layout = Layout;
        PanelLayout.Normalize(layout);
        var selected = _tabs.SelectedItem;
        _tabs.Items.Clear();
        foreach (var id in PanelLayout.Docked(layout)) _tabs.Items.Add(_items[id]);
        _tabs.SelectedItem = selected is TabItem t && _tabs.Items.Contains(t) ? t : _tabs.Items.Count > 0 ? _tabs.Items[0] : null;
        foreach (var f in layout.Floating.ToList()) OpenWindow(f.Id, OnScreen(f.Left, f.Top, f.Width, f.Height));
        UpdateSide();
    }

    /// <summary>Show a panel wherever it is: select its tab, or bring its window forward.</summary>
    public void Show(string id)
    {
        if (_windows.TryGetValue(id, out var w))
        {
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
        }
        else if (_items.TryGetValue(id, out var item)) _tabs.SelectedItem = item;
    }

    public string? IdOf(TabItem item) => _items.FirstOrDefault(kv => kv.Value == item).Key;

    // ------------------------------------------------------------------ tear out / dock

    /// <summary>Give a panel its own window. With <paramref name="follow"/>, the window keeps moving with the pointer.</summary>
    public void PopOut(string id, Point? screenDip = null, bool follow = false)
    {
        if (_windows.ContainsKey(id) || !_items.TryGetValue(id, out var item)) return;
        var width = Math.Max(PanelLayout.MinPanelWidth + 60, _tabs.ActualWidth + 40);
        var height = Math.Max(PanelLayout.MinPanelHeight + 200, _tabs.ActualHeight + 70);
        var at = screenDip ?? new Point(_owner.Left + _owner.ActualWidth - width - 40, _owner.Top + 80);
        var bounds = new Rect(follow ? at.X - 80 : at.X, follow ? at.Y - 14 : at.Y, width, height);
        _store.Update(s => PanelLayout.Float(s.Panels, id, bounds.X, bounds.Y, bounds.Width, bounds.Height));
        var window = OpenWindow(id, OnScreen(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        UpdateSide();
        if (follow && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            // Carry on the same drag with the new window under the pointer; releasing over the side panel docks it again.
            try { window.DragMove(); } catch (InvalidOperationException) { }
        }
    }

    /// <summary>Put a torn-out panel back in the side column (at <paramref name="dockedIndex"/>, or where it was).</summary>
    public void Dock(string id, int? dockedIndex = null)
    {
        if (!_windows.Remove(id, out var window)) return;
        var item = _items[id];
        item.Content = window.TakeContent();
        _store.Update(s => PanelLayout.Dock(s.Panels, id, dockedIndex));
        var docked = PanelLayout.Docked(Layout);
        _tabs.Items.Insert(Math.Min(docked.ToList().IndexOf(id), _tabs.Items.Count), item);
        _tabs.SelectedItem = item;
        window.CloseDocked();
        UpdateSide();
        _owner.Activate();
    }

    /// <summary>Everything back where it started: all panels docked, default order and width.</summary>
    public void Reset()
    {
        foreach (var id in _windows.Keys.ToList()) Dock(id);
        _store.Update(s => PanelLayout.Reset(s.Panels));
        Restore();
    }

    /// <summary>Move a docked panel one place left (-1) or right (+1).</summary>
    public void Nudge(string id, int by)
    {
        if (!_items.TryGetValue(id, out var item) || !_tabs.Items.Contains(item)) return;
        var to = Math.Clamp(_tabs.Items.IndexOf(item) + by, 0, _tabs.Items.Count - 1);
        MoveTab(item, to);
        SaveOrder();
    }

    /// <summary>The app is closing: remember where each torn-out window is, then close them as they are.</summary>
    public void Shutdown()
    {
        ShuttingDown = true;
        foreach (var (id, w) in _windows)
        {
            var r = w.WindowState == WindowState.Normal ? new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight) : w.RestoreBounds;
            _store.Update(s => PanelLayout.Moved(s.Panels, id, r.X, r.Y, r.Width, r.Height));
        }
        foreach (var w in _windows.Values.ToList()) w.Close();
    }

    private PanelWindow OpenWindow(string id, Rect bounds)
    {
        var item = _items[id];
        var content = item.Content as UIElement;
        item.Content = null;
        _tabs.Items.Remove(item);
        var window = new PanelWindow(_owner, this, id, Title(id), content)
        {
            Left = bounds.X, Top = bounds.Y, Width = bounds.Width, Height = bounds.Height,
        };
        _windows[id] = window;
        window.Show();
        return window;
    }

    /// <summary>Keep a saved window on a screen that still exists (a projector or second monitor may be gone).</summary>
    private Rect OnScreen(double left, double top, double width, double height)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        width = Math.Min(width, screen.Width);
        height = Math.Min(height, screen.Height);
        var r = new Rect(left, top, width, height);
        var visible = Rect.Intersect(r, screen);
        if (visible.IsEmpty || visible.Width < 120 || visible.Height < 80)
            r = new Rect(_owner.Left + Math.Max(0, _owner.ActualWidth - width) / 2, _owner.Top + 60, width, height);
        return r;
    }

    /// <summary>Fold the side column away when nothing is docked in it; bring it back at its saved width.</summary>
    private void UpdateSide()
    {
        var any = _tabs.Items.Count > 0;
        foreach (var p in _sideParts) p.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _splitCol.Width = new GridLength(any ? 14 : 0);
        _sideCol.MinWidth = any ? PanelLayout.MinSideWidth : 0;
        _sideCol.Width = new GridLength(any ? Layout.SideWidth : 0);
        if (any && _tabs.SelectedItem is null) _tabs.SelectedIndex = 0;
    }

    // ------------------------------------------------------------------ a panel window moving over the main window

    /// <summary>Where a panel window being dragged would dock: an index in the strip, or null when it's not over a dock zone.</summary>
    internal int? DropIndex(Point screenPx)
    {
        if (_tabs.Items.Count > 0)
        {
            if (!Contains(_sidePanel, screenPx)) return null;
            var p = _tabs.PointFromScreen(screenPx);
            for (int i = 0; i < _tabs.Items.Count; i++)
            {
                if (_tabs.Items[i] is not TabItem t || !t.IsVisible) continue;
                var b = t.TransformToAncestor(_tabs).TransformBounds(new Rect(t.RenderSize));
                if (p.Y <= b.Bottom + 6 && p.X < b.Left + b.Width / 2 && p.Y >= b.Top - 6) return i;
            }
            return _tabs.Items.Count;
        }
        // Nothing docked: the strip on the right edge of the main window is the dock zone.
        return Contains(_hint, screenPx, whenCollapsed: true) ? 0 : null;
    }

    internal void Hover(bool over)
    {
        if (_tabs.Items.Count > 0)
        {
            _sidePanel.BorderBrush = over ? (Brush)_owner.FindResource("NeonCyanBrush") : _edge;
            _sidePanel.BorderThickness = new Thickness(over ? 2 : 1);
            _hint.Visibility = Visibility.Collapsed;
        }
        else _hint.Visibility = over ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A panel window was resized: remember its size.</summary>
    internal void Resized(PanelWindow w)
    {
        Hover(false);
        _store.Update(s => PanelLayout.Moved(s.Panels, w.PanelId, w.Left, w.Top, w.ActualWidth, w.ActualHeight));
    }

    /// <summary>A panel window was dragged and let go: dock it if it was released over the side panel.</summary>
    internal void Released(PanelWindow w, Point screenPx)
    {
        var index = DropIndex(screenPx);
        Hover(false);
        _hint.Visibility = Visibility.Collapsed;
        if (index is { } i) Dock(w.PanelId, i);
        else _store.Update(s => PanelLayout.Moved(s.Panels, w.PanelId, w.Left, w.Top, w.ActualWidth, w.ActualHeight));
    }

    private bool Contains(FrameworkElement e, Point screenPx, bool whenCollapsed = false)
    {
        if (PresentationSource.FromVisual(_owner) is null) return false;
        FrameworkElement target = e;
        if (!e.IsVisible)
        {
            if (!whenCollapsed) return false;
            target = (FrameworkElement)e.Parent;   // the hint isn't shown yet: test its place on the right of the middle row
            var pp = target.PointFromScreen(screenPx);
            return pp.Y >= 0 && pp.Y <= target.ActualHeight && pp.X >= target.ActualWidth - e.Width && pp.X <= target.ActualWidth + 40;
        }
        var p = target.PointFromScreen(screenPx);
        return p.X >= 0 && p.Y >= 0 && p.X <= target.ActualWidth && p.Y <= target.ActualHeight;
    }

    // ------------------------------------------------------------------ dragging a tab

    private void OnTabDown(object sender, MouseButtonEventArgs e)
    {
        // The tab's content isn't inside the TabItem (the TabControl shows it), so this is always a press on the header.
        if (sender is not TabItem item) return;
        _pressed = item;
        _pressAt = e.GetPosition(_tabs);
        _reordered = false;
    }

    private void OnTabMove(object sender, MouseEventArgs e)
    {
        if (_pressed is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndTabDrag(); return; }
        var p = e.GetPosition(_tabs);
        if (!_dragging)
        {
            if (Math.Abs(p.X - _pressAt.X) < SystemParameters.MinimumHorizontalDragDistance * 2
                && Math.Abs(p.Y - _pressAt.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;
            _dragging = true;
            _tabs.CaptureMouse();
        }
        // Out of the panel: tear it off into its own window and keep dragging that.
        const double tear = 28;
        if (p.X < -tear || p.Y < -tear || p.X > _tabs.ActualWidth + tear || p.Y > _tabs.ActualHeight + tear)
        {
            var item = _pressed;
            var id = IdOf(item)!;
            var screen = _owner.PointToScreen(e.GetPosition(_owner));
            var dip = PresentationSource.FromVisual(_owner)?.CompositionTarget?.TransformFromDevice.Transform(screen) ?? screen;
            EndTabDrag();
            PopOut(id, dip, follow: true);
            return;
        }
        // Along the strip: move the tab to the header under the pointer.
        for (int i = 0; i < _tabs.Items.Count; i++)
        {
            if (_tabs.Items[i] is not TabItem t || t == _pressed) continue;
            var b = t.TransformToAncestor(_tabs).TransformBounds(new Rect(t.RenderSize));
            if (b.Contains(p))
            {
                MoveTab(_pressed, i);
                _reordered = true;
                break;
            }
        }
    }

    private void OnTabUp(object sender, MouseButtonEventArgs e)
    {
        var moved = _dragging && _reordered;
        EndTabDrag();
        if (moved) SaveOrder();
    }

    private void EndTabDrag()
    {
        var wasDragging = _dragging;
        _pressed = null;
        _dragging = false;
        if (wasDragging && _tabs.IsMouseCaptured) _tabs.ReleaseMouseCapture();
    }

    private void MoveTab(TabItem item, int to)
    {
        var from = _tabs.Items.IndexOf(item);
        if (from < 0 || from == to) return;
        var wasSelected = _tabs.SelectedItem == item;
        _tabs.Items.RemoveAt(from);
        _tabs.Items.Insert(to, item);
        if (wasSelected) _tabs.SelectedItem = item;
    }

    private void SaveOrder()
    {
        var order = _tabs.Items.OfType<TabItem>().Select(IdOf).OfType<string>().ToList();
        _store.Update(s => { for (int i = 0; i < order.Count; i++) PanelLayout.Move(s.Panels, order[i], i); });
    }
}

/// <summary>A side panel in a window of its own. Closing it docks it back (a panel is never lost).</summary>
public sealed class PanelWindow : Window, IContextActions
{
    private const int WmEnterSizeMove = 0x0231, WmExitSizeMove = 0x0232, WmMoving = 0x0216, WmSizing = 0x0214;

    private readonly MainWindow _main;
    private readonly PanelDock _dock;
    private readonly Border _host;
    private bool _docking, _sized, _moving;

    public string PanelId { get; }

    public PanelWindow(MainWindow main, PanelDock dock, string id, string title, UIElement? content)
    {
        _main = main;
        _dock = dock;
        PanelId = id;
        Owner = main;
        Title = title;
        ShowInTaskbar = false;
        ShowActivated = main.IsActive;   // never steal focus from another app
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.CanResize;
        MinWidth = PanelLayout.MinPanelWidth;
        MinHeight = PanelLayout.MinPanelHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = (Brush)main.FindResource("BgDeepBrush");
        Foreground = (Brush)main.FindResource("TextPrimaryBrush");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        DataContext = main.DataContext;

        var bar = new DockPanel { Margin = new Thickness(12, 8, 8, 4), Background = Brushes.Transparent };
        ContextHelp.SetTip(bar, "panel-window");
        var dockButton = new Button
        {
            Content = "⇥ Dock", Padding = new Thickness(10, 3, 10, 3), MinHeight = 0, MinWidth = 0, FontSize = 12,
            ToolTip = "Put this panel back in the main window (or drag this window onto the side panel)",
        };
        dockButton.Click += (_, _) => _dock.Dock(PanelId);
        DockPanel.SetDock(dockButton, Dock.Right);
        bar.Children.Add(dockButton);
        bar.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontWeight = FontWeights.Bold, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
        _host = new Border
        {
            Background = (Brush)main.FindResource("BgPanelBrush"),
            BorderBrush = (Brush)main.FindResource("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(8, 0, 8, 8),
            Padding = new Thickness(4),
            Child = content,
        };
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(_host);
        Content = root;
        ContextHelp.SetTopic(this, "layout");

        ContextHelp.Attach(this);
        // The operator's shortcuts (Space, N, Ctrl+G…) work whichever window has focus.
        PreviewKeyDown += (s, e) => { if (!e.Handled) _main.HandleShortcut(e); };
        PreviewKeyUp += (s, e) => { if (!e.Handled) _main.HandleShortcutUp(e); };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int dark = 1;
            try { DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
        };
        Closing += (_, e) =>
        {
            // The × docks it back; closing the app keeps it torn out for next time.
            if (!_docking && !_dock.ShuttingDown) { e.Cancel = true; Dispatcher.BeginInvoke(() => _dock.Dock(PanelId)); }
        };
    }

    /// <summary>Hand the panel's content back for docking.</summary>
    internal UIElement? TakeContent()
    {
        var c = _host.Child;
        _host.Child = null;
        return c;
    }

    internal void CloseDocked()
    {
        _docking = true;
        Close();
    }

    public IEnumerable<Control> ActionsFor(string tip, FrameworkElement element)
    {
        if (tip == "panel-window")
        {
            yield return ContextHelp.Item("Dock back into the main window", () => _dock.Dock(PanelId));
            yield return ContextHelp.Item("Reset panel layout", () => _dock.Reset());
            yield break;
        }
        foreach (var c in _main.ActionsFor(tip, element)) yield return c;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmEnterSizeMove: _sized = false; _moving = true; break;
            case WmSizing: _sized = true; break;
            case WmMoving:
                if (!_sized && GetCursorPos(out NativePoint p)) _dock.Hover(_dock.DropIndex(new Point(p.X, p.Y)) is not null);
                break;
            case WmExitSizeMove:
                if (_moving && GetCursorPos(out NativePoint at))
                {
                    var sized = _sized;
                    var pt = new Point(at.X, at.Y);
                    // After the move loop has unwound: docking closes this window.
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (_docking) return;
                        if (sized) _dock.Resized(this); else _dock.Released(this, pt);
                    });
                }
                _moving = false;
                break;
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint p);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
