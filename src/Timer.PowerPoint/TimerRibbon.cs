using System;
using Microsoft.Office.Tools.Ribbon;

namespace Stagewatch.PowerPoint;

/// <summary>
/// The Stagewatch ribbon tab in PowerPoint. Toggles overlay visibility and
/// provides "Recenter" + a help blurb. Keep it minimal — operators don't need
/// 30 buttons; the timer app itself drives the actual countdown.
/// </summary>
public partial class TimerRibbon : RibbonBase
{
    private RibbonTab _tab = null!;
    private RibbonGroup _group = null!;
    private RibbonToggleButton _toggleBtn = null!;
    private RibbonButton _recenterBtn = null!;

    public TimerRibbon() : base(Globals.Factory.GetRibbonFactory()) { InitializeComponent(); }

    private void InitializeComponent()
    {
        _tab = Factory.CreateRibbonTab();
        _group = Factory.CreateRibbonGroup();
        _toggleBtn = Factory.CreateRibbonToggleButton();
        _recenterBtn = Factory.CreateRibbonButton();

        _tab.Label = "Stagewatch";

        _toggleBtn.Label = "Show timer";
        _toggleBtn.ShowImage = false;
        _toggleBtn.Checked = true;
        _toggleBtn.Click += (s, e) => ThisAddIn.Instance.ToggleVisible();

        _recenterBtn.Label = "Recenter";
        _recenterBtn.ShowImage = false;
        _recenterBtn.Click += (s, e) => ThisAddIn.Instance.RecenterOverlay();

        _group.Label = "Timer overlay";
        _group.Items.Add(_toggleBtn);
        _group.Items.Add(_recenterBtn);

        _tab.Groups.Add(_group);
        Tabs.Add(_tab);
    }
}
