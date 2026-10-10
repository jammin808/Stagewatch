using System;
using System.Net.Sockets;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Office.Tools;

namespace Stagewatch.PowerPoint;

/// <summary>
/// Stagewatch VSTO add-in entry point.
///
/// Lifecycle: PowerPoint loads ThisAddIn at startup. Startup creates a
/// long-lived TCP client to the Stagewatch app on 127.0.0.1:9923, subscribes
/// to TICK events, and renders the timer + message into <see cref="PresenterOverlay"/>
/// pinned to the lower-left of the slide-show or presenter window.
/// </summary>
public partial class ThisAddIn
{
    public static ThisAddIn Instance { get; private set; } = null!;

    private CancellationTokenSource? _cts;
    private Task? _connector;
    private readonly object _lock = new();

    public PresenterOverlay? Overlay { get; private set; }

    public bool Visible { get; private set; } = true;

    private void ThisAddIn_Startup(object sender, EventArgs e)
    {
        Instance = this;

        // Wire to the slide-show start so we only show the overlay during shows
        // (avoids floating it over the editor). Operators can override.
        Application.SlideShowBegin += OnSlideShowBegin;
        Application.SlideShowEnd += OnSlideShowEnd;
        Application.SlideShowNextSlide += OnSlideChange;

        StartConnector();
    }

    private void ThisAddIn_Shutdown(object sender, EventArgs e)
    {
        try { _cts?.Cancel(); } catch { }
        try { Overlay?.Close(); } catch { }
    }

    private void OnSlideShowBegin(Microsoft.Office.Interop.PowerPoint.SlideShowWindow Wn)
    {
        if (!Visible) return;
        EnsureOverlay();
        Overlay?.AttachToShow(Wn);
    }

    private void OnSlideShowEnd(Microsoft.Office.Interop.PowerPoint.Presentation Pres)
    {
        Overlay?.Detach();
    }

    private void OnSlideChange(Microsoft.Office.Interop.PowerPoint.SlideShowWindow Wn)
    {
        Overlay?.RepositionUnderSlide(Wn);
    }

    private void EnsureOverlay()
    {
        if (Overlay is not null) return;
        Overlay = new PresenterOverlay();
        Overlay.Show();
    }

    public void ToggleVisible()
    {
        Visible = !Visible;
        if (Visible)
        {
            EnsureOverlay();
            Overlay?.Show();
        }
        else
        {
            Overlay?.Hide();
        }
    }

    public void RecenterOverlay()
    {
        Overlay?.MoveToDefaultPosition();
    }

    private void StartConnector()
    {
        _cts = new CancellationTokenSource();
        _connector = Task.Run(() => RunConnectorAsync(_cts.Token));
    }

    private async Task RunConnectorAsync(CancellationToken ct)
    {
        // Reconnect loop. Don't crash PowerPoint if the timer app isn't running.
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync("127.0.0.1", 9923);
                using var stream = tcp.GetStream();
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

                await writer.WriteLineAsync("SUBSCRIBE");

                string? line;
                while ((line = await reader.ReadLineAsync()) is not null && !ct.IsCancellationRequested)
                {
                    HandleEvent(line);
                }
            }
            catch (Exception)
            {
                // Backoff and retry.
                await Task.Delay(1500, ct);
            }
        }
    }

    private void HandleEvent(string line)
    {
        // Parse "TICK ms=12345 formatted=04:12 state=Running"
        // and "MESSAGE \"text\" urgency=Info flash=Off"
        try
        {
            var first = line.IndexOf(' ');
            var verb = first < 0 ? line : line.Substring(0, first);
            var rest = first < 0 ? "" : line.Substring(first + 1);

            switch (verb)
            {
                case "TICK":
                    var formatted = ParseToken(rest, "formatted=");
                    var state = ParseToken(rest, "state=");
                    Overlay?.SetTimer(formatted, state);
                    break;
                case "MESSAGE":
                    var urgency = ParseToken(rest, "urgency=");
                    var flash = ParseToken(rest, "flash=");
                    var text = ParseQuoted(rest);
                    Overlay?.SetMessage(text, urgency, flash);
                    break;
                case "STATE":
                    Overlay?.SetState(ParseToken(rest, "state="));
                    break;
            }
        }
        catch { /* tolerate junk */ }
    }

    private static string ParseToken(string s, string prefix)
    {
        var i = s.IndexOf(prefix, StringComparison.Ordinal);
        if (i < 0) return "";
        var start = i + prefix.Length;
        var end = s.IndexOf(' ', start);
        return end < 0 ? s.Substring(start) : s.Substring(start, end - start);
    }

    private static string ParseQuoted(string s)
    {
        var i = s.IndexOf('"');
        if (i < 0) return "";
        var sb = new System.Text.StringBuilder();
        bool esc = false;
        for (int p = i + 1; p < s.Length; p++)
        {
            var c = s[p];
            if (esc) { sb.Append(c == 'n' ? '\n' : c); esc = false; continue; }
            if (c == '\\') { esc = true; continue; }
            if (c == '"') break;
            sb.Append(c);
        }
        return sb.ToString();
    }

    #region VSTO generated code
    private void InternalStartup()
    {
        Startup += ThisAddIn_Startup;
        Shutdown += ThisAddIn_Shutdown;
    }
    #endregion
}
