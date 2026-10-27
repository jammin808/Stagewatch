## Stagewatch

Controls the **Stagewatch** stage timer and shows its live state on your buttons: the countdown, warning colours, the running order, the schedule, messages and blackout.

### Setup

1. In Stagewatch, open **Settings → Companion** and make sure the server is **Enabled** (port 9923).
2. If Companion runs on a **different computer**, tick **Bind all interfaces** in Stagewatch and allow it through the Windows firewall.
3. Add this connection and enter the Stagewatch PC's address (`127.0.0.1` on the same PC) and port.

The connection status turns green as soon as the first live frame arrives. Companion control is a Stagewatch **Pro** feature (included in the 14-day trial). After the trial, the status shows _Insufficient permissions_ along with the reason.

### Getting started fast

Open the **Presets** tab. Everything is ready to drag onto a page:

- **Showcaller / Producer page.** 32 keys in reading order (8 across, 4 down) — drag them onto a page left to right: the **timer ring** (a progress ring that drains, the time and the cue label), title, schedule, curfew, forecast, assistant suggestions, transport, catch up, nudges, delays, speaker cues with acknowledgement, pocket buzz, call next speaker, cues waiting, preview, slide pace, VT countdown, Q&A, rooms, report and blackout.
- **Cues & departments.** Speaker cues that the speaker confirms (amber = waiting, green ✓ = confirmed, red = nobody answered), call to stage, a podium **Got it** key, and a Standby and GO key for **every department set up in Stagewatch** (built automatically).
- **Schedule recovery & assistant.** Curfew status, likely end, catch up, delays, and the assistant's three suggestions — press one to apply it.
- **Speaker cues & presentation.** Briefing preview, calm / standard speaker clock, one key per cue ladder, slide n/N with rehearsal pace, VT countdown, speech tracking.
- **Q&A, report & rooms.**

- **Show control.** The _Timer_ key starts and pauses the timer, shows the live time and turns the same colour as Stagewatch. It also has Start, Pause, Reset, Next / Prev (the Next key shows what's coming up), Blackout, nudges and clock mode.
- **Live displays.** Title, next item, ahead/behind schedule, end time, percent left, time of day and link status.
- **Running order.** One key per item, built from Stagewatch automatically. _Tap_ to load an item, _hold for 1 second_ to load and start it. The current item is green and finished items are grey.
- **Presets.** One key per Stagewatch preset, with the same tap / hold behaviour. Labels follow Stagewatch live.
- **Messages.** Your Stagewatch quick messages, plus Clear.

When you change the running order or presets in Stagewatch, the keys, dropdowns and variables update by themselves.

### Actions

| Action | Notes |
|---|---|
| Start / Pause (toggle), Start, Pause, Reset | Pause also freezes overtime |
| Add / remove time | ±seconds, works live |
| Load duration | `5:00`, `1:30:00` or seconds; optional start. **Learn** copies the loaded duration |
| Finish at a time of day | `HH:MM`, empty to clear. The timer stretches to land on zero then |
| Clock mode | Countdown, count up, time of day |
| Load preset | Optional start |
| Next / Previous / Go to item | Running order. **Learn** on _Go to item_ picks the current item |
| Show message / quick message / clear / flash | Text accepts variables. **Learn** copies the message on screen |
| Flash the timer | On / off |
| Blackout | Toggle / on / off |
| Send raw command | Any protocol command, e.g. `NUDGE -30` or `GOTO 4 start` |
| **Cues: Send a cue** | Message / standby / GO / call to stage / pocket buzz to speaker, moderator, backstage, interpreter or a department — with acknowledgement |
| **Cues: Department standby / GO** | Department list comes from Stagewatch |
| **Cues: Call the next speaker to stage** | "<speaker> to stage" on the backstage page |
| **Cues: Acknowledge (Got it)** | For a speaker's or green room's own Stream Deck |
| **Cues: Clear cues** | All, or one target |
| **Schedule: Cut / extend an item** | Trims are kept apart from the plan for the report |
| **Schedule: Announce a delay** | Add / set / clear; it ripples until a gap absorbs it |
| **Schedule: Catch up** | Lands the live item on its planned end (elastic) |
| **Assistant: Apply suggestion 1–3** | Runs the assistant's proposed command |
| **Display: Briefing preview**, **Cue ladder**, **Speaker clock format** | |
| **Q&A: Next question**, **Record rehearsal**, **Speech tracking**, **Save report**, **Log a note** | |
| **Value: Seconds remaining / Expected start of an item / Curfew margin** | Actions that return a value, for Companion logic and expressions |

### Feedbacks

- **State is…**: running / paused / stopped / reached zero.
- **Clock is running**: includes overtime.
- **Over time**: at or after zero.
- **Warning stage is…**: normal / warning / wrap-up / overtime.
- **Mirror Stagewatch timer colour**: the button uses exactly the colours set in Stagewatch, as text, as background, or as background only when not normal.
- **Remaining at or below…**: your own early warnings.
- **Clock mode is…**, **Blackout is on**, **A message is showing** (filter by urgency).
- **Item is current**, **Item is finished**, **Preset is loaded**.
- **Behind schedule by at least…** and **Connected to Stagewatch** (invert it to light a key when the link is down).
- **2.0:** **Speaker message is…** (waiting / acknowledged), **A cue is waiting** (any or one target; only unanswered), **Department is…** (standby / standing by / GO / done / clear), **Curfew margin below…**, **Item expected late by…**, **Assistant has a suggestion**, **Elastic is stretching**, **PC clock is off**, **Cue label is…**, **Pace against rehearsal**, **Video is running** (vMix's or a playout link's clip; optionally its last N seconds), **Briefing preview**, **Questions waiting**, **A room is late**.
- **2.2:** **Output window is…** (open / full / keyed / mini / closed), **NDI is sending**, **Live item is a…** (talk, panel, Q&A, break…), **Item has been trimmed** (cut / extended; item 0 = live), and **Licence: feature not included** — dims a key when your Stagewatch licence doesn't include that feature (the built-in presets already use it, so Production-only keys show grey on a Pro licence).
- **Value feedbacks:** *Seconds remaining* and *Percent left* — for expressions and layered graphics (the timer ring uses them).

### Variables

`$(stagewatch:time)` gives the timer as shown. Also available:

- **Time:** `time_short`, `time_hms`, `hours`, `minutes`, `seconds_part`, `sign`, `seconds`, `total`, `elapsed`, `progress`
- **State:** `state`, `mode`, `phase`, `running`, `overtime`, `color`
- **Running order and schedule:** `title`, `next`, `item`, `items`, `preset`, `end_time`, `time_of_day`, `offset`, `offset_seconds`, `schedule`
- **Message and display:** `message`, `message_urgency`, `blackout`, `connected`
- **Per preset:** `preset_N_label`, `preset_N_duration`
- **Per running-order item:** `item_N_title`, `item_N_speaker`, `item_N_duration`

- **2.0:** `speaker_time`, `cue`, `finish_by`, `preview`, `stretching`, `room`, `ladder`, `message_ack`, `message_ack_by`, `cues_open`, `cues_waiting`, `cues_unanswered`, `last_cue`, `slide`, `slides`, `slide_next`, `pace`, `ghost`, `script_percent`, `wpm`, `vt_title`, `vt_remaining`, `vt_seconds`, `hard_out`, `hard_out_margin`, `forecast`, `delay`, `next_expected`, `clock_offset_ms`, `qa_live`, `qa_new`, `qa_queued`, `qa_fit`, `advice_count`, `advice_1…3`, `advice_1…3_action`, `rooms`, `rooms_late`
- **2.2:** `output`, `ndi`, `licence`, `live_kind`, `live_trim`, `qa_live_id`
- **Per item:** `item_N_planned`, `item_N_expected`, `item_N_actual`, `item_N_status`, `item_N_delay`
- **Per department:** `dept_name_N`, `dept_<name>` (clear / standby / standby_ack / go / go_ack)

(Replace `stagewatch` with your connection's label.)

### New in 2.20: playout links (Patterns desks and PlayMap nodes)

Stagewatch drives the clip on air, a PlayMap node's playlist items and a Patterns desk's cues over its playout links (set up in Stagewatch's Settings → Integrations). These actions and keys appear when Stagewatch is 2.20 or later and its licence carries playout (Production). With no link switched on they stay, and Stagewatch refuses a press in its own words (on the log and the answer key).

- **Playout: the clip on air** — play, pause, stop, restart, jump to its last seconds, or move to a point. Pick a link, or none for the only link (with several, the one feeding the VT); a link's name can also be typed.
- **Playout: GO** — on a PlayMap node a playlist and its item (the playlist's next item when the item is empty); on a Patterns desk a cue, in the item field. **Together** starts every link's GO asked together at one instant, where each one's clock allows. With two links, name one.
- **Playout: cue** — a PlayMap playlist's item (only while that playlist is idle: a cued item shows its first frame on air), or a Patterns cue to that desk's own standby.
- **Every answer is shown.** Each press goes with a request id, and what Stagewatch answers — a refusal at its door (for example from a PC not listed in Settings → Remote → May drive playout), the peer's own words, the final answer of a peer that runs it later, or "no answer" when the link dropped first — is a line in this connection's log and in `$(stagewatch:playout_answer)` (an earlier press's late answer names its line there). A press is never reported as done unless Stagewatch said so. The *Patterns* actions answer the same way.
- **Feedback:** *Playout: The VT comes from…* (a link, any link, or vMix; optionally only while it runs, or only in its last N seconds). **Variables:** `vt_link` (the link feeding the VT), `vt_source` (vMix, Patterns or PlayMap), `playout_answer`.
- **Presets:** the *Playout links* section — last 10 s, pause, play and restart with no link named (the only link switched on, else the one feeding the VT) and for each link: blue while what the key reaches feeds the VT, red in that clip's last 10 s, grey on a licence without playout. A vMix clip lights none of them, since no playout press reaches vMix. And a key showing the last answer.

### New in 2.6: the 3D egg timer

- *Display: Holographic 3D style* gains **3D egg timer**; the **3D EGG** key switches to it. The feedback *3D display is on* can check for either scene, and `$(stagewatch:style)` reads `standard`, `holo` or `egg`.

### New in 2.4: holographic 3D display

- **Action:** *Display: Holographic 3D style* (toggle / holographic / standard). **Feedback:** *Holographic 3D style is on*. **Variable:** `style`.
- A **3D HOLO** key in the *Outputs, NDI & 3D* preset section (violet while on; grey without a Pro licence).

### New in 2.3: holding images

- **Holding images** preset section: one key per image in Stagewatch's holding library — press to put it full screen on every output, NDI and browser screen (violet while on air) — plus **Off** and **Toggle**.
- **Action:** *Display: Holding image* (an image, off, or toggle). **Feedback:** *Holding image is on air* (any, or one image). **Variables:** `holding` (name on air), `holding_on`, `holding_N_name`.
- Add images on the Holding tab in Stagewatch, or upload them from the Showcaller page.

### New in 2.2

- **Outputs & NDI** preset section: full screen (toggle), keyed, floating mini, close and NDI — each lit while active.
- **Actions:** *Display: Output window*, *Display: NDI output*, *Schedule: Cut / extend the live item* (one key works all show), *Schedule: Undo trims on an item*, *Cues: Send an unanswered cue again*, *Q&A: Finish the question on air*.
- **Recovery presets:** cut / extend the live item by a minute (showing `$(stagewatch:live_trim)`), undo trims, and an *On now* key coloured by the kind of item.
- **Licence-aware keys:** keys for features your licence doesn't include turn dark grey, and `$(stagewatch:licence)` shows the plan.

### Program a button by doing it

Press **record** on a Companion button, then operate Stagewatch as normal — start, nudge, next, a message, a
department GO… Each action arrives in Companion and is added to the button, in order. Stop recording and the
button replays it.

### Pages from Companion

Companion serves this connection at `http://<companion>:8000/instance/<label>/…`: `/producer` (the Showcaller
page), `/speaker`, `/backstage`, `/moderator`, `/rooms`, `/audience` jump to Stagewatch's own pages (set the
Stagewatch web port in the connection settings), and `/state` returns the live frame as JSON.

### Reliability

- **Efficient updates.** Stagewatch pushes a complete frame only when something visible changes, about once a second while running. The module sends Companion only the values and feedbacks that actually changed.
- **Dead-link detection.** A heartbeat catches a dead link within 12 seconds, and the module reconnects automatically (1 s, backing off to 10 s). While offline, the timer keys show `OFFLINE` rather than a frozen time.
- **No stale presses.** A key pressed while disconnected is **not** queued and replayed later, because a stale _Start_ or _Next_ arriving after a reconnect could disrupt a live show. It is logged instead.
