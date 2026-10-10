# PowerPoint add-in

> **Presenting from another computer (2.8):** run **Stagewatch Slides** on it (`Stagewatch.exe --slides`, or the
> installer's *This is a presenter PC* option). It reads that PC's slideshow and sends it to the Stagewatch PC(s);
> see Help → Presenter PCs and [NODES.md](NODES.md).
>
> **You don't need this add-in.** Stagewatch reads the running PowerPoint slideshow on the same PC directly
> (Settings → Integrations → *Read the running PowerPoint slideshow*): slide number, rehearsal timings, speaker
> notes for speech pace. The add-in below is an optional extra (a timer inside presenter view) and is not built
> or installed. Without PowerPoint at all, everything else in Stagewatch works.

> **Status (1.1):** the add-in source is complete but has not been built — VSTO needs Visual Studio with the
> "Office/SharePoint development" workload. The 1.1 installer does not include it. Until it ships, the
> **Floating mini** output (always on top) is the way to put the timer on the presenter's screen.

The Stagewatch add-in puts a live timer + message overlay inside PowerPoint's presenter view. It is the killer feature that IrisDown can't match.

## What the speaker sees

- Lower-left of the presenter screen, just under the current-slide thumbnail.
- Big white digits, color-coded message line under it.
- Always-on-top, frameless, semi-transparent dark background — never blocks slide content.
- Drag to reposition; mouse-wheel to resize; right-click for menu (Hide / Recenter).

## What the operator sees

A "Stagewatch" tab appears in the PowerPoint ribbon with two buttons:

- **Show timer** — toggle the overlay on/off
- **Recenter** — move the overlay back to the default lower-left position

## Install

The Stagewatch installer registers the add-in for the current user. If installing manually:

```
1. Build src/Timer.PowerPoint in Visual Studio with the
   Office Developer Tools workload installed.
2. Right-click the project → Publish, sign the manifest.
3. The signed .vsto file goes to:
       %LOCALAPPDATA%\Stagewatch\PowerPointAddIn\
4. Add a registry value (current user) so PowerPoint loads it:
       HKCU\Software\Microsoft\Office\PowerPoint\Addins\Stagewatch.PowerPoint
         (Default) = ""
         Description = "Stagewatch timer overlay"
         FriendlyName = "Stagewatch"
         LoadBehavior = dword:00000003
         Manifest = "file:///%LOCALAPPDATA%/Stagewatch/PowerPointAddIn/Stagewatch.PowerPoint.vsto|vstolocal"
```

The installer (`installer/Stagewatch.iss`) automates all of this. You only need the manual path during development.

## How the add-in talks to the timer app

It opens a TCP connection to `127.0.0.1:9923` — the same Companion protocol port the timer app already uses for BitFocus. On connect:

```
> SUBSCRIBE
< READY stagewatch/1
< STATE mode=Countdown state=Running preset=3
< TICK ms=120000 formatted=02:00 state=Running
< TICK ms=119950 formatted=02:00 state=Running
…
```

If the timer app isn't running, the add-in retries every 1.5 s. PowerPoint never crashes if the timer app dies.

## Permissions

The add-in needs:
- Read access to PowerPoint's slideshow events (to attach/detach the overlay)
- TCP connect to 127.0.0.1:9923

It does **not** read slide content, presenter notes, or external network.

## Tier gating

The PowerPoint add-in is a **Pro** feature. Standard tier installers should not register the add-in. The trial tier includes it for the 14-day window.
