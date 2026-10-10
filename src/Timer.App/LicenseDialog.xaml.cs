using System.IO;
using System.Windows;
using Microsoft.Win32;
using Stagewatch.Core;
using Stagewatch.Licensing;

namespace Stagewatch.App;

public partial class LicenseDialog : Window
{
    private readonly LicenseManager _mgr;

    public LicenseDialog(LicenseManager mgr)
    {
        InitializeComponent();
        Help.ContextHelp.SetTopic(this, "licence");
        Help.ContextHelp.Attach(this);
        _mgr = mgr;
        StatusText.Text = mgr.CurrentStatus.DisplayName;
        if (mgr.CurrentStatus.Notice is { } notice) { NoticeText.Text = notice; NoticeText.Visibility = Visibility.Visible; }
        PlansText.Text = PlansSummary();
        HwidBox.Text = HardwareId.Get();
    }

    /// <summary>The price list, from <see cref="Pricing"/> so the app and the docs can't drift apart.</summary>
    public static string PlansSummary() => $"""
        Standard — £{Pricing.Standard} one-off
           The core timer: all presets, end-at timing, alerts, quick messages, holding images, NDI, keyed outputs.

        Pro — £{Pricing.Pro} one-off
           One operator, one room: running order, browser remotes and roles, Companion, PowerPoint,
           Q&A, triggers, rehearsal ghost, CSV report, holographic 3D display.

        Production — £{Pricing.Production} one-off (launch price £{Pricing.ProductionLaunch})
           The crew loop and multi-room kit: multi-room dashboard, DMX / sACN / Hue cue lights,
           acknowledged cues, department STANDBY/GO, green-room calls, pocket buzz, end-of-show
           forecast and curfew trim suggestions, LTC and time-server drift check, vMix VT countdowns,
           full printable report, speech pace, producer assistant and MCP.
           Covers {Pricing.ProductionMachines} machines (main + backup), activated offline.
           Early adopters get lifetime updates. Later licences include a year of updates, then
           £{Pricing.UpdateRenewalPerYear}/year to keep updating; the last version you're entitled to keeps working for ever.

        Production event pass — £{Pricing.EventPass} for {Pricing.EventPassDays} days
           Everything in Production for one event; the price counts towards a full Production licence.
           It warns {Pricing.EventPassWarnDays} days before it ends and keeps working {Pricing.EventPassGraceDays} days after, so an event is never cut short.

        Charities, churches, schools and universities — {Pricing.NonprofitDiscountPercent}% off everything:
           Standard £{Pricing.Nonprofit(Pricing.Standard)} · Pro £{Pricing.Nonprofit(Pricing.Pro)} · Production £{Pricing.Nonprofit(Pricing.Production)} · event pass £{Pricing.Nonprofit(Pricing.EventPass)}.
           Mention your organisation when you send your hardware ID.

        Rental houses: 5-packs {Pricing.RentalPackDiscountPercent}% off.
        """;

    private void OnCopyHwid(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(HwidBox.Text); } catch (Exception) { }
    }

    private void OnActivateSerial(object sender, RoutedEventArgs e)
    {
        var st = _mgr.TryActivateMasterSerial(SerialBox.Text.Trim());
        StatusText.Text = st.LastError ?? st.DisplayName;
        if (st.LastError is null)
            MessageBox.Show("Activated. Restart Stagewatch to apply.", "Stagewatch", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnInstallFile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "License files (*.dat)|*.dat|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        var st = _mgr.TryInstallLicenseFile(File.ReadAllBytes(dlg.FileName));
        StatusText.Text = st.LastError ?? st.DisplayName;
        if (st.LastError is null)
            MessageBox.Show("Installed. Restart Stagewatch to apply.", "Stagewatch", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnSignOut(object sender, RoutedEventArgs e)
    {
        _mgr.SignOut();
        StatusText.Text = _mgr.CurrentStatus.DisplayName;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
