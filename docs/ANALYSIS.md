# IrisDown CountdownTimer — analysis & how Stagewatch beats it

> Stagewatch is the working name for this app. Rename in `AppInfo.cs` if you prefer.

IrisDown's CountdownTimer is a long-standing standard for corporate event countdowns: reliable, paid after a 30-day trial, with live jog, flashing messages, a browser view and Companion feedback. Its gaps are in show workflow — running order, crew confirmation, schedule recovery, presentation awareness — which is where Stagewatch should compete.

> Corrected 2026-10-09 after an external review, against the vendor page (irisdown.co.uk/countdowntimer.html). Earlier versions of this analysis wrongly said IrisDown was free and lacked live adjustment, flashing messages and a web view, and credited it with NDI, chroma key and multiple outputs, which its page doesn't mention.

## What IrisDown does well (we must match or beat all of these)

| Strength | Notes |
|---|---|
| **Rock-solid clock** | Drift is imperceptible across multi-hour sessions. |
| **Companion with feedback** | Long-established network control; many show ops have pages ready. |
| **Live jog** | Add or subtract time while running, also over the network. |
| **Flashing messages** | Messages flash briefly on the output to catch the presenter's eye. |
| **Web view** | A simple browser page of the timer (port 8181). |
| **Presets** | Up to 9, plus manual entry. |
| **Paid after a 30-day trial** | A licence key; online activation. |
| **Tiny footprint** | Low CPU, no install bloat. |
| **Hardware receivers** | Broadcasts status to listening devices (e.g. CDEther receiver). |

## Where IrisDown is weak (Stagewatch fixes these)

### 1. UI is a 1990s WinForms grid
- **Theirs**: dense, gray, every option has equal visual weight — operators hunt for the right control under stress.
- **Ours**: dark, high-contrast WPF UI with a clear hierarchy: huge live timer at top, transport controls (start/stop/reset) front-and-center with hardware-style buttons, presets and message/flash on a secondary row, settings and outputs tucked into modal dialogs. Operator-mode keyboard shortcuts (space = start/stop, R = reset, F = flash, 1–9/0/-/= = presets 1–12).

### 2. Presets are limited
- **Theirs**: up to 9 saved presets.
- **Ours**: 12 always-visible preset tiles on the main panel. Each has a label, a duration, and an optional pre-loaded message. Right-click a tile to rename or change it; long-press loads without auto-starting. **Drag to reorder.** Color-tag presets (e.g. green = keynote, amber = Q&A).

### 3. Live adjustment — parity, not an advantage
- **Theirs**: Jog buttons add or subtract time while running (also over the network).
- **Ours**: dedicated **Nudge** panel with ±10m, ±5m, ±1m, ±10s, ±1s buttons, and a global modifier key (Shift = ×5, Ctrl = ×10). Nudge applies live without stopping the clock. Mouse-wheel over the timer also nudges.

### 4. Messaging — they flash; we add urgency, acknowledgement and placeholders
- **Theirs**: messages flash briefly on the output, then stay until cleared.
- **Ours**: message line with three urgency tiers (info / warning / critical), each with its own color and optional flash rate (off / slow / fast). Send button. Pre-canned messages list ("5 min remaining", "Wrap up", "Stop now"). Auto-flash when timer crosses thresholds you configure (e.g. flash amber under 2:00, red under 0:30, hard-flash at 0:00).

### 5. Output window options are awkward
- **Theirs**: keying behavior is OK but resize/element-toggle is clunky.
- **Ours**: the output window has three modes you switch with one click:
  - **Full screen** on a chosen display (with proper handling of monitor changes mid-show).
  - **Keyed full screen** with selectable key color (blue / green / magenta / black) and an alpha-aware NDI alpha output for soft-edged keys.
  - **Floating minimal** — small, resizable, always-on-top, with checkboxes for what to show: timer / message / time-of-day / session-elapsed / preset name. Lets you stick a 200px-tall timer in the corner of a confidence monitor without a full-screen window.

### 6. No PowerPoint integration at all
- **Theirs**: nothing. Speakers see the timer only on a confidence monitor.
- **Ours**: VSTO add-in shows the timer **inside the presenter view**, lower-left under the current-slide thumbnail by default, drag-to-position and pinch-to-resize, with a Ribbon toggle. Speakers see their countdown on the laptop they're already looking at. **This is the killer feature** — IrisDown can't do this.

### 7. Tiering and licensing — none
- **Theirs**: free, no tiers, no licensing.
- **Ours** (from 2.1): 14-day full trial (then a watermarked Free mode) → Standard £9 → Pro £29 → Production £79 (launch £59), all one-off and offline. Production covers main + backup PCs; early adopters get lifetime updates, later £20/year for updates (the last entitled version keeps working); £15 30-day event pass credited towards Production. Standard unlocks NDI, end-at and >2 presets; Pro unlocks the running order, remotes, Companion, PowerPoint; Production unlocks the crew loop, multi-room, cue lights, LTC, forecast, full report and MCP. Master serial built in for owner testing. Honest disclaimer in `LICENSING.md` about the limits of client-side anti-tamper.

### 8. Settings persistence is fragile
- **Theirs**: saves window positions, but sometimes loses preset state across versions.
- **Ours**: settings are JSON in `%APPDATA%\Stagewatch\settings.json`, schema-versioned with forward-compat migration. Auto-saves on every meaningful change (debounced 500ms) so a crash never loses presets, message banks, or output layout.

### 9. Companion compatibility — but better
- **Theirs**: existing Companion module, well-loved, but limited feedback channels.
- **Ours**: text protocol with the same verb shapes show ops already know (START / STOP / RESET / SET / PRESET / MESSAGE / FLASH) plus richer feedback: timer state, current ms remaining, message text, flash state, mode — pushed as line-delimited events so a Companion module can drive button feedback (variables, presets, button text). Spec in `docs/COMPANION.md`.

### 10. Multi-display dependability
- **Theirs**: occasional issues when monitors are disconnected mid-show.
- **Ours**: WPF window listens to `SystemEvents.DisplaySettingsChanged` and re-anchors on a sensible fallback monitor with a status banner so the operator notices instead of guessing.

## Things we deliberately don't try to win on

- **Free.** They're free; we're not. Our trial covers the demo path.
- **Featherweight install.** WPF is heavier than WinForms, but still <60 MB installed. We're not chasing the 5 MB target.
- **Mac/Linux.** Windows-only, intentional, mirrors the theatre/AV reality.

## Summary: positioning sentence

> Stagewatch is what IrisDown CountdownTimer would look like if it were redesigned in 2026 for paid users — same rock-solid clock, same NDI/Companion DNA, but with a presenter-view PowerPoint plug-in, live nudge, structured messaging, modern UI, and proper tiered licensing.
