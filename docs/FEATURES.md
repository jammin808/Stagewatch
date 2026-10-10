# Stagewatch 2.0 — what's new and how to use it

Stagewatch 2.0 closes the loop around the clock: cues that are confirmed, a timer that knows the slides,
schedule recovery that proposes the fix, and every room on one screen. Everything is local — no cloud — and
runs on the same single show loop as 1.x.

**Which licence?** Cue ladders, the calm clock and the other speaker-screen options come with **Standard**.
The running order, remotes, Companion, PowerPoint, Q&A, triggers, rehearsal ghost and the CSV report are **Pro**.
The crew loop and show kit are **Production**: acknowledged cues, departments, green-room calls and pocket buzz,
the end-of-show forecast and curfew trim suggestions (producer assistant), the multi-room dashboard, cue lights,
LTC and the time-server drift check, vMix VT countdowns, speech pace, the full printable report and MCP.
The 14-day trial has everything. See [LICENSING.md](LICENSING.md).

## 2.11: Patterns video and AGMessage

- **Patterns video**: the clip on air in Patterns drives the VT countdown (like vMix), and **Last 10 s** (or
  `patterns end [seconds]`, `patterns restart`) jumps it to its end over Patterns' wire. Settings → Integrations →
  Patterns (with **Find** for the Patterns beacon). With Patterns Round 86: `patterns play | pause | stop | seek <m:ss>`,
  a millisecond-accurate countdown that follows pause and playback speed, and discovery on the show network's hello. Only `VIDEO END` / `VIDEO RESTART` are ever sent — never a bare
  `STOP`, which in Patterns turns every output off.
- **AGMessage** (the board ↔ research messenger) links to Stagewatch with a control API key: everyone in the meeting
  sees the live stage timer, and the research team can put an answer on the confidence monitor with an
  acknowledgement. See NODES.md.
- Fixes from review:
  - Undo keeps a history per running order and checks each item's identity, so it never changes the wrong running
    order or an item that moved.
  - Recovery never cuts below an item's minimum.
  - A one-item Roll loop restarts on every pass.
  - Nudging or trimming the live item hands over from Roll.
  - Switching running order stops the old timer and clears its title.
  - `/api/config` needs the viewing code too.
  - Department notes merge three ways on save, so a sheet update can't undo a revised note and crew edits made
    meanwhile are kept.

## 2.10: faster live work, designed screens, keys and a copilot that sees more

- **Undo live edits** (Ctrl+Z / Ctrl+Y, Producer tab): trims, delays, nudges (merged while you wheel), department
  notes, a recovery or a whole approved copilot plan undo as one step each. Commands `undo`, `redo`.
- **Recover time…**: win back (or give out) N minutes from the rest of the show, up to the next curfew, or this
  group — proportionally, evenly, or buffers first, never below an item's minimum or half its length, in 15 s steps.
  A live preview shows every change, the new end and the curfew margin before you apply; one Undo reverts it.
  Copilot plans show the same simulated consequence ("Ends 17:52 (was 17:58) · curfew +8 min") before you approve.
- **Back-timing**: give the running order an end time (*Ends* on the Running order tab, command `endofshow 17:30`)
  and, with no start time, the plan counts back from it; either way it's the last curfew.
- **Loops**: run the running order again when it ends — n extra passes or for ever (walk-ins, exhibitions); Roll
  follows the loop. Command `loop off|n|forever`.
- **Labels**: tag items (VT, GFX, CAM 2) with colours that show on the list, the Showcaller page and `/timeline`;
  triggers can follow a label (`label:VT`), a group or a kind. CSV carries them.
- **Chimes**: a WAV chime library (Settings → Timer) and per-item chimes at start, warning, wrap-up and zero
  (Running order → Edit → Chimes, with Test); "chime when an item starts"; screens opened with `?sound=1` play the
  same chime; **Hide at zero** blanks speaker screens instead of overtime.
- **Screens**: reload or disconnect any screen remotely; `?mirror=h|v|both` (teleprompters), `?delay=N` (match a
  video delay), `/clock?tz=Europe/Paris`; an optional **viewing code** for public networks.
- **Screen designer** (Screens → *Design screens…*, `/designer`): drag the timer, text with placeholders
  (`{title}`, `{next}`, `{nextIn}`, `{clock}` …), the message, progress, labels, boxes and holding images onto a
  live preview of the real show, with snapping, undo and keyboard nudging; screens at `/layout?name=…` update within
  a second of Save. **Describe it** and the copilot drafts a layout you keep or discard.
- **API keys** (Settings → Remote): named, scoped (read / control / MCP), revocable keys instead of the PIN; only a
  hash is stored; 600 requests a minute each with `X-RateLimit-*` headers and `Retry-After`.
- **Copilot**: **Review the plan** (findings using *your* overrun history, fixes to approve), **Explain a problem**
  (reads every link's health and refused commands), **hold F9 to talk** (offline Windows speech → a plan you
  approve), and **✨ message ideas** beside the message box (three short, kind options; you pick, you send).
- **Event passes** warn before they end and keep working 2 days after, so an event is never cut short; nonprofit
  prices are listed in the licence dialog.
- **Help → Guides**: ATEM and vision mixers, OBS/vMix, Zoom/Teams, Raspberry Pi kiosks, smart TVs, embedding.

## 2.9: planning with the team, more screens, Roll and several running orders

- **Groups**: give items a Group (a session, an act); groups show with their total on the Running order tab, the
  Showcaller page and `/timeline`, and **Group targets…** turns an overfull group red.
- **Department notes**: each department keeps its own notes on every item. They edit them on their crew page (their
  crew link is their permission, and they can only see and edit their own) or in the editor; spreadsheets carry them as
  one column per department.
- **Google Sheets**: Running order → Edit → *From Google Sheet…*. **Update** takes the sheet's plan but keeps live
  trims, notes and department notes; **Replace** takes it as it is. Reviewed in the editor before it's kept.
- **New screens**: `/clock` (studio clock with a ring of seconds), `/countdown?to=next|3|end|group:Name` (time until
  anything) and `/timeline` (the running order on a time axis with a live now-line). Times are sent relative to each
  frame and counted on locally, so a screen's own clock never matters.
- **Custom CSS** for every browser screen (Settings → Remote), with a `sw-page-<name>` class per page.
- **Roll**: the running order runs itself by its planned times — counting down to each start in the gaps — until the
  operator does anything, when it hands over. Command `roll on|off`.
- **Several running orders** in one show (Day 1 / Day 2, a rehearsal version): pick the live one, edit others at any
  time, switch cleanly; all saved in the show file. Command `rundown <name>`.
- Fixes from review: the copilot never pre-ticks preview, putting an extra timer on stage, identifying screens or Roll;
  a restarted Stagewatch gets its presenter scripts again; malformed discovery packets can't stop the listeners.

## 2.8: screens, extra timers, presenter PCs and the AI copilot

- **Screens** (top right): every browser showing Stagewatch — name it, **Identify** it (its name fills that screen),
  **Send** it to another page or a saved setup, and save setups to open at `/p/<name>`. Commands `screen …`.
- **Extra timers** (Timers tab): three named timers beside the show (VT, changeover, a speaker's own clock), up or
  down, with one optionally on stage as a second line. Derived from the clock, so they can't drift. Commands `aux …`;
  Companion keys and variables.
- **Presenter PCs — Stagewatch Slides** (`Stagewatch.exe --slides`): the PowerPoint on another computer drives slide
  awareness, rehearsal pace and speech pace. Finds Stagewatch by itself (UDP 9928), sends to main and backup, PIN
  protected. Stagewatch follows automatically whichever presenter PC is presenting, or one you choose.
- **AI copilot** (Ctrl+K, Production): plain English → an answer, or a plan in Stagewatch's own commands with a
  reason per step. Every step is validated against the command grammar and risk-rated by Stagewatch (not the model);
  anything that changes the room is never pre-ticked. Nothing runs until the operator approves. Also: draft a
  running order from any agenda text (reviewed in the editor), draft the show report's narrative (edited before it's
  kept), and AI agents on MCP propose into the same queue. The report lists every AI suggestion and decision.
- Fixes: drop-frame LTC (29.97) is numbered correctly; a chased timer pauses when its LTC source disappears.

## LTC timecode in (2.7)

- **Settings → Integrations → Time**: *LTC timecode in from* an audio input. 24 / 25 / 30 fps are detected by
  themselves; inverted, quiet or noisy feeds are fine. The main window shows a chip with the incoming code.
- **Timer follows it (chase)**: the timer starts when the code rolls and pauses when it stops or the feed drops.
  Add an **end timecode** (the video's last frame, the end of the lighting cue list) and the countdown is held
  exactly at *end − timecode*, so it can't drift from the playback.
- **Timecode triggers**: Settings → Triggers → *Timecode*, filter `01:00:30:00` — fires once as rolling code
  passes it (not on a locate or rewind). Any trigger action: OSC, HTTP, Companion or a Stagewatch command.
- Commands `ltc chase on|off|toggle`, `ltc end <HH:MM:SS:FF|clear>`; Companion variables `ltc`, `ltc_rolling`,
  `ltc_chase`, feedbacks and a preset key. Production.

## 3D: colours, animated transitions and the egg timer (2.6)

- **Colours follow the show** on every 3D scene: the timer and cue use your warning-stage colours; messages take
  their urgency colour (info pale, warning amber, critical red), and the beam / glass glow / particles lean to it.
- **Animated transitions** (Settings → Output, on by default): messages materialise and dissolve; stage changes send
  a shockwave ring and a flash; warnings breathe; critical messages and overtime pulse faster with glitch bursts, a
  slight shake, alarm rings and a particle surge; start and pause power the beam up and down. Every value runs on
  critically damped springs (no jumps, no overshoot) and the time-like values are integrated, so speed changes are
  seamless. Off = colours change instantly and nothing moves on its own.
- **Particles** can be switched off (slow screens, or taste).
- **3D egg timer** — right-click **3D** → *3D egg timer*: lit glass with reflections, a wood-and-brass frame and a
  soft shadow. The sand drains with the time (clipping planes on fixed geometry — nothing rebuilt per frame), the
  stream falls only while running, it turns over when a new time is loaded, the sand takes the stage colour and the
  glass glows, rocks and shakes with warnings, overtime and critical messages.
- Per screen: `?scene=egg|holo`, `?anim=0`, `?particles=0`, `?floor=0`. Command: `style holo|egg|standard`.

## Holographic 3D display (Pro)

Press **3D** (next to NDI) and the time floats above a projector: glowing, scan-lined text with a chromatic
fringe, a light beam with drifting dust, a ringed base and a floor reflection — coloured by the warning stage. It
runs on the output window and on browser screens (`/viewer` follows the setting; `/holo` is a dedicated screen).

Built like a game renderer: three.js (r186, bundled — no internet needed) draws from the latest frame on a
`requestAnimationFrame` loop; text textures redraw only when the text changes; the beam, dust and scanlines are
animated on the GPU from one time value, with no per-frame allocation; a quality governor lowers resolution,
particles and ghost layers when frames run slow (and raises them again); a lost graphics context is recovered.
In the output window the standard display keeps running underneath, and a one-second heartbeat watchdog hides
the 3D layer within 3 seconds if it ever stops — the speaker never sees a frozen screen. NDI keeps the standard look.

## Holding images

Walk-in slides, sponsor logos, "we'll be back" cards: add images on the **Holding** tab (Add images… or drop
files; PNG, JPG, GIF, BMP, TIFF, WebP up to 25 MB) and click one to put it **full screen on every screen** — the
output window (so keyed outputs and NDI too), `/viewer` and `/audience`, and `/holding`, a page that shows nothing
but holding images. Click again, **Off** or **H** to bring the timer back. Fit or fill, blackout still wins, and a
violet chip shows what's on air. From elsewhere: the Showcaller page lists and uploads images, and Companion has a
key per image (`holding <n|name|off|toggle>` for everything else). Images are decoded off the show thread, so
switching never stalls the timer.

## Speaker cues

| Feature | Where | What it does |
|---|---|---|
| **Cue ladders** | Main window → Producer tab, or per item in the running order | Graduated signals agreed with the speaker: Standard (your thresholds), Keynote 10/5/2/0, Panel, Toastmasters green/yellow/red (no overtime signal), Awards, Spoken (talent in-ear). |
| **Cue label** | Settings → Output → *Cue label* | "5 MIN", "WRAP UP" on the speaker screen — a cue that doesn't rely on colour. A coloured frame around the output marks every cue too. |
| **Colour-blind-safe colours** | Settings → Cues | Blue / yellow / vermillion instead of amber / orange / red. |
| **Finish by** | Settings → Output → *FINISH BY* | "FINISH BY 10:49" — speakers think in clock times. Updates when you nudge. |
| **Calm clock** | Producer tab → *Speaker clock* | "12 MIN" until the last minute, or progress bar and cue only — no ticking seconds. |
| **Honest clock** | Settings → Cues | While elastic end-at stretches time, the speaker sees the real time left; you still see the stretch. |
| **Stable layout** | Settings → Cues | The message area keeps its space, so the timer never jumps when a message appears. |
| **Briefing preview** | Producer tab → *Briefing preview* | Plays the whole cue ladder at 10× on every output during the tech check: "this is what you'll see at 5 minutes". |
| **Message placeholders** | Any message | `{FINISH_BY}`, `{NEXT}`, `{OVER_UNDER}`, `{REMAINING}`, `{TITLE}`, `{SPEAKER}`, `{TIME}` stay up to date. |
| **Cue sounds per device** | Settings → Cues | Send each cue to its own output (e.g. wrap-up to the talent's IEM feed, never the PA), plus spoken cues ("two minutes") with Windows' voices. |

## Acknowledged cues, departments and the green room

- Tick **Ask to acknowledge** (or use *Send + ack* on the Showcaller page): the speaker screen shows **Got it**,
  and you see ⏳ waiting → ✓ acknowledged. Nobody answers in 20 s (Settings → Cues)? It escalates — flashes,
  and the assistant tells you to radio them.
- **Departments** (Settings → Cues; default Lighting, Sound, Video, Stage): Cues & Q&A tab → *Standby* / *GO*.
  Each department opens `http://<pc>:9924/dept?name=Lighting` — a huge STANDBY / GO with a confirm button.
- **Call to stage**: *Call next speaker to stage* sends "Ana to stage" to `/backstage`; the wrangler taps
  **Found**, then **In position**.
- **Pocket buzz**: open `/haptic?for=speaker` (or `for=moderator`) on a phone — add it to the home screen.
  Cues buzz (Android) and flash the screen; tap to acknowledge. iPhones flash only (no vibration API).
- **Podium Stream Deck**: the Companion *Got it* key acknowledges the newest cue.

## Running order: schedule recovery

New columns in **Running order → Edit…**:

| Column | Meaning |
|---|---|
| Kind | Talk, Panel, Q&A, Break, Award, Video, Buffer — drives trim priorities and the forecaster |
| Fixed start | Hold the item to a published time (`14:00`, or `2026-10-24 14:00` for multi-day). Early → it waits (absorbing delay); late → flagged. |
| Min | How short it may be cut (blank = not compressible) |
| Curfew | A hard-out at the end of this item (`17:30`) |
| Cue ladder | This item's own ladder |
| Notes | Operator only |

- **Buffer** items shrink to absorb lateness. **Show time zone** (top of the editor) keeps the plan right when
  the laptop is on another zone.
- **Delay +1m / +5m** announces a delay before the next item; it ripples until a gap absorbs it.
- **Catch up** re-times the live item (elastic) to land on its planned end.
- When a curfew is at risk, the **assistant** proposes concrete cuts — Q&A first, then panels, down to each
  item's minimum — and **Apply** does it. Trims are kept apart from the plan, so the report shows plan,
  adjusted and actual.
- **Forecast**: "Likely end 17:24 · 80% by 17:38 · curfew risk 35%" — Monte Carlo over how your shows actually
  run (it learns from every finished item; panels and Q&A start from realistic overrun priors).

## Presentation awareness

Turn on **PowerPoint** in Settings → Integrations (no add-in needed):

- Slide *n / N* and the next slide's title on the operator, speaker and Companion displays.
- **Rehearsal ghost**: press *Record rehearsal* during the run-through; live, Stagewatch says
  "2:10 behind your rehearsal, projected 23:40". PowerPoint's own *Rehearse Timings* work too.
- **Listen to script**: Windows' offline speech recognition follows the speaker through the speaker notes (or a
  .txt script): percent delivered, words per minute and a projected finish. Audio never leaves the PC.

**vMix**: VT countdowns from the clip in Program (Settings → Integrations).

## Showcaller / Producer page

`http://<pc>:9924/producer` (PIN): the whole show on a laptop or tablet — the timer with its cue, schedule
offset, curfew, forecast, the assistant's suggestions, transport, speaker cues with acknowledgement,
departments, green room, Q&A, the running order with planned / expected / actual and Load / ±1m per item,
delays, and a show-log note box. Report and CSV links at the top.

The Companion module has a matching **Showcaller / Producer page** preset section — 32 keys in reading order.

## Q&A

Put `/audience` on a screen: agenda with live times, break countdown and a QR code. Phones scan it and ask on
`/ask` (no app, no PIN, rate-limited). Moderate in the Cues & Q&A tab, the Showcaller page or Companion:
approve, star, take live, answered. The moderator view (`/moderator`) shows the queue and how many more fit.

## Multi-room

Give each PC a room name (Settings → Integrations). Every Stagewatch on the network appears on `/rooms`:
time, title, lateness and curfew risk, with a link to each room's producer page. Each room stays its own
authority; nothing to set up but the name.

## Cue lights, timecode and time

- **Cue lights**: Art-Net, sACN or Philips Hue — a lectern or stage-edge light that follows the cue ladder
  (dark while normal, then the cue colours, flashing at time).
- **LTC timecode out** on any audio output (24 / 25 / 30 fps), time of day or the countdown.
- **LTC timecode in** from any audio input, with chase and timecode triggers (see above).
- **Clock check** against a time server; a warning if this PC is more than a second off.

## Triggers (Settings → Triggers)

When an item starts or ends, the phase changes, overtime starts, a cue is acknowledged, a curfew goes at risk or
blackout toggles — send OSC, an HTTP request, press a Companion button (`page/row/column`) or run a Stagewatch
command. Placeholders `{ITEM}` `{TITLE}` `{PHASE}` `{REMAINING}` `{DETAIL}` `{ROOM}`.

## Report

Producer tab → **Show report**: planned vs actual for every item, over/under, how long each speaker took to
finish after the wrap-up cue, cues and acknowledgements. Print → Save as PDF for the client, or export CSV.
Saved under `Documents\Stagewatch Shows\Reports`.

## Assistant

Rule-based and offline by default. Optionally (Settings → Integrations) it also asks Claude for suggestions —
this sends the running order and timings to Anthropic, so it's off unless you add your API key (stored
encrypted for your Windows user). Suggestions are only proposals: every command is checked against an
allow-list and nothing happens until you click **Apply**.

## AI tools and Patterns

`POST /mcp` is a Model Context Protocol endpoint (PIN-protected): AI assistants and other Patterns tools can read
the show and run commands through the same single input path as a button.
