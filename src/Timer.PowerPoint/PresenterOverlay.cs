using System;
using System.Drawing;
using System.Windows.Forms;
using PPT = Microsoft.Office.Interop.PowerPoint;

namespace Stagewatch.PowerPoint;

/// <summary>
/// Always-on-top, frameless WinForms window that pins to the lower-left of the
/// presenter view. Drag to reposition; mouse-wheel resizes; right-click for menu.
/// </summary>
public sealed class PresenterOverlay : Form
{
    private readonly Label _timer = new();
    private readonly Label _message = new();
    private PPT.SlideShowWindow? _attached;
    private bool _dragging;
    private Point _dragOffset;

    public PresenterOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Opacity = 0.85;
        Width = 360;
        Height = 120;

        _timer.AutoSize = false;
        _timer.Dock = DockStyle.Top;
        _timer.Height = 80;
        _timer.Font = new Font("Segoe UI", 44, FontStyle.Bold);
        _timer.ForeColor = Color.White;
        _timer.TextAlign = ContentAlignment.MiddleCenter;
        _timer.Text = "00:00";

        _message.AutoSize = false;
        _message.Dock = DockStyle.Bottom;
        _message.Height = 40;
        _message.Font = new Font("Segoe UI", 14);
        _message.ForeColor = Color.LightGray;
        _message.TextAlign = ContentAlignment.MiddleCenter;
        _message.Text = "";

        Controls.Add(_message);
        Controls.Add(_timer);

        // Drag-to-position
        foreach (Control c in new Control[] { this, _timer, _message })
        {
            c.MouseDown += (s, e) => { _dragging = true; _dragOffset = e.Location; };
            c.MouseUp += (s, e) => _dragging = false;
            c.MouseMove += (s, e) =>
            {
                if (_dragging) Location = new Point(
                    Cursor.Position.X - _dragOffset.X,
                    Cursor.Position.Y - _dragOffset.Y);
            };
            c.MouseWheel += (s, e) =>
            {
                var step = e.Delta > 0 ? 1.1 : 0.9;
                Width = Math.Max(160, (int)(Width * step));
                Height = Math.Max(60, (int)(Height * step));
                _timer.Font = new Font("Segoe UI", Math.Max(20, Width / 8.0f), FontStyle.Bold);
                _message.Font = new Font("Segoe UI", Math.Max(10, Width / 24.0f));
            };
        }

        ContextMenuStrip = BuildMenu();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var hide = new ToolStripMenuItem("Hide", null, (s, e) => ThisAddIn.Instance.ToggleVisible());
        var recenter = new ToolStripMenuItem("Recenter", null, (s, e) => MoveToDefaultPosition());
        menu.Items.AddRange(new ToolStripItem[] { hide, recenter });
        return menu;
    }

    public void AttachToShow(PPT.SlideShowWindow wn)
    {
        _attached = wn;
        RepositionUnderSlide(wn);
    }

    public void Detach() { _attached = null; Hide(); }

    public void RepositionUnderSlide(PPT.SlideShowWindow wn)
    {
        // Position lower-left of the show window, just inside the bottom margin.
        // Presenter view is the *separate* window; we sit under the current-slide thumbnail.
        try
        {
            var screen = Screen.FromPoint(new Point(wn.Left + wn.Width / 2, wn.Top + wn.Height / 2));
            var area = screen.WorkingArea;
            const int margin = 24;
            Left = area.Left + margin;
            Top = area.Bottom - Height - margin;
        }
        catch { MoveToDefaultPosition(); }
    }

    public void MoveToDefaultPosition()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Left = area.Left + 24;
        Top = area.Bottom - Height - 24;
    }

    public void SetTimer(string formatted, string state)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => SetTimer(formatted, state))); return; }
        _timer.Text = formatted;
        _timer.ForeColor = state == "Expired" ? Color.FromArgb(220, 38, 38) : Color.White;
    }

    public void SetMessage(string text, string urgency, string flash)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => SetMessage(text, urgency, flash))); return; }
        _message.Text = text ?? "";
        _message.ForeColor = urgency switch
        {
            "Critical" => Color.FromArgb(220, 38, 38),
            "Warning"  => Color.FromArgb(245, 158, 11),
            _          => Color.LightGray
        };
    }

    public void SetState(string state)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => SetState(state))); return; }
        // Could change border or icon based on state; minimal for now.
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW — no taskbar, no Alt+Tab
            cp.ExStyle |= 0x00080000; // WS_EX_LAYERED — needed for Opacity to work cleanly
            return cp;
        }
    }
}
