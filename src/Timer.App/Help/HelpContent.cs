namespace Stagewatch.App.Help;

// The in-app help: topics for the Help window, and short tips for right-click menus.
// Text supports **bold**, *setting names* (italic) and `code`. Keep topics short: one idea per section, steps before detail.

public abstract record HelpBlock;
/// <summary>A sub-heading inside a topic.</summary>
public sealed record H(string Text) : HelpBlock;
/// <summary>A paragraph.</summary>
public sealed record P(string Text) : HelpBlock;
/// <summary>Bulleted list.</summary>
public sealed record Bullets(params string[] Items) : HelpBlock;
/// <summary>Numbered steps.</summary>
public sealed record Steps(params string[] Items) : HelpBlock;
/// <summary>Two-column table: term → meaning.</summary>
public sealed record Table(params (string Term, string Meaning)[] Rows) : HelpBlock;
/// <summary>A highlighted tip or caution.</summary>
public sealed record Note(string Text) : HelpBlock;

/// <param name="Tier">Licence the feature needs (null = every tier), shown as a badge.</param>
public sealed record HelpTopic(string Id, string Group, string Title, string Summary, HelpBlock[] Blocks, string? Tier = null)
{
    /// <summary>All the topic's words, for search.</summary>
    public string SearchText { get; } = string.Join(" ", new[] { Title, Summary, Group }.Concat(Blocks.SelectMany(Words))).ToLowerInvariant();

    private static IEnumerable<string> Words(HelpBlock b) => b switch
    {
        H h => new[] { h.Text },
        P p => new[] { p.Text },
        Bullets l => l.Items,
        Steps s => s.Items,
        Table t => t.Rows.SelectMany(r => new[] { r.Term, r.Meaning }),
        Note n => new[] { n.Text },
        _ => Array.Empty<string>()
    };
}

/// <summary>Concise help for one control or piece of on-screen data (right-click).</summary>
public sealed record HelpTip(string Title, string Text, string Topic);

public static class HelpContent
{
    public const string Pro = "Pro";
    public const string Production = "Production";

    public static readonly IReadOnlyList<HelpTopic> Topics = new HelpTopic[]
    {
        // ------------------------------------------------------------------ Getting started
        new("start", "Getting started", "Quick start",
            "Run your first countdown in under a minute.",
            new HelpBlock[]
            {
                new Steps(
                    "Type a **Duration** (e.g. `20:00`) and press **Enter**, or click a **preset**.",
                    "Choose **Output → Full screen** to put the timer on the speaker's screen (Settings → Output picks the monitor).",
                    "Press **START** (or **Space**). Nudge time live with the **NUDGE** buttons or the mouse wheel over the timer.",
                    "Type a **Message** for the speaker and press **Send**. **Clear** removes it."),
                new P("The colour changes at the **warning** and **wrap-up** thresholds and turns red at zero; it keeps counting into overtime unless you turn that off (Settings → Timer)."),
                new Note("Right-click almost anything — a button, a chip under the timer, a preset or a running-order item — for a short explanation and quick actions. Press **F1** for this help."),
                new H("Next"),
                new P("The **Workflows** section walks through real setups step by step: a whole conference day, speaker screens, holding images, PowerPoint, speech pace, the Showcaller page, crew and audience screens, several rooms, a backup PC, presenter PCs, planning with your team in a shared sheet, and following a video's timecode."),
            }),

        new("layout", "Getting started", "The main window",
            "What each area of the window does.",
            new HelpBlock[]
            {
                new Table(
                    ("Timer", "The big clock, coloured as the speaker sees it. Above: the clock mode and the current title. Below: progress, end time, what's next and how far ahead or behind the show is."),
                    ("Status chips", "Small labels under the timer that appear only when they have something to say: cue label, finish-by, slides, pace, VT, curfew, forecast, delay, clock drift. Right-click one to learn what it means."),
                    ("Inputs", "Duration, End at and Pace — load a time, or finish at a time of day."),
                    ("Nudge", "Add or remove time while the timer runs."),
                    ("Right panel", "**Presets**, **Running order**, **Cues & Q&A**, **Producer**, **Holding** and **Timers**. Drag a tab to reorder; drag it out of the window for a window of its own (another monitor, beside the timer); drag that window back onto the panel, or press Dock, to put it back. Drag the gap between the timer and the panel to widen it. Right-click a tab for Pop out, Move and Reset panel layout."),
                    ("Transport", "START, STOP, RESET, NEXT, BLACKOUT; the output mode, key colour and NDI."),
                    ("Message", "Text for the speaker, its urgency and flash, quick messages and acknowledgements."),
                    ("Status line", "What just happened, the remote address, the time of day and the timer state.")),
            }),

        new("shows", "Getting started", "Show files and safety",
            "Save each event, and what happens if the PC crashes.",
            new HelpBlock[]
            {
                new Bullets(
                    "**Save show…** (Ctrl+S) keeps the presets, running order and quick messages for one event in a `.stagewatch` file. **Ctrl+Shift+S** saves a copy under a new name.",
                    "**Open show…** (Ctrl+O) or double-click a `.stagewatch` file in Explorer. Opening during a live show asks first; the timer and outputs keep going.",
                    "Settings save themselves as you change them.",
                    "After a crash or power cut, Stagewatch offers to restore the show exactly where it was.",
                    "Closing the window while a show is running asks for confirmation, so a stray click can't end the show."),
            }),

        // ------------------------------------------------------------------ Workflows
        new("wf-day", "Workflows", "Example: a conference day",
            "From an empty laptop to a finished show report.",
            new HelpBlock[]
            {
                new H("The day before"),
                new Steps(
                    "**Running order → Edit…**: paste the agenda from the spreadsheet (title, speaker, duration). Set kinds (Talk, Panel, Break…), any **fixed starts** after lunch, and a **curfew** on the last item.",
                    "Set **Starts** on the Running order tab (e.g. `09:00`).",
                    "**Save show…** as `Client – Day 1.stagewatch`. Keep a copy on a USB stick for the backup PC."),
                new H("Load-in and tech check"),
                new Steps(
                    "Plug in the speaker monitor and choose **Output → Full screen** (Settings → Output → Target monitor if it opens on the wrong screen).",
                    "Turn on **Settings → Remote** and open the pages crew need: `/producer` for the showcaller, `/dept?name=Lighting` for each department, `/backstage` for the green room, `/viewer` for any extra confidence screens. See **Set up the Showcaller page** and **Set up crew screens**.",
                    "Set **Settings → Timer → Play alerts on** to the laptop speaker or headphones, never the PA.",
                    "Add the walk-in and break slides on the **Holding** tab, and put the walk-in slide on air until the show starts.",
                    "With each presenter: Producer tab → **Briefing preview** plays the cue ladder at 10× so they know what 5 MIN, WRAP UP and TIME look like.",
                    "If talks use PowerPoint on this PC: **Record rehearsal** during the run-through (see **Set up PowerPoint**)."),
                new H("Show time"),
                new Steps(
                    "Double-click item 1 (or press **Space** after loading). Press **N** for each next item — with ↪ auto-advance items roll on by themselves.",
                    "Running late? Watch the **curfew** and **forecast** chips. Use **Delay +1m / +5m**, **Catch up**, or right-click an item → **Trim 1 minute**. With Production the assistant proposes the cuts — click its button to apply.",
                    "Send messages with **Ask to acknowledge** for anything the speaker must see; amber means not yet read."),
                new H("Afterwards"),
                new P("Producer tab → **Show report** → Print → Save as PDF for the client (or **Export CSV**). Then **Save show** to keep the timings."),
            }),

        new("wf-screens", "Workflows", "Set up the speaker screens",
            "Confidence monitors, stage displays, vision mixers and streams.",
            new HelpBlock[]
            {
                new Table(
                    ("A monitor plugged into this PC", "**Settings → Output → Target monitor**, then **Output → Full screen** (or press **O**). Choose what shows under *Elements shown on output*."),
                    ("A TV or tablet anywhere on the network", "Turn on **Settings → Remote** with *Allow other devices*. On the device, open the address shown there plus `/viewer` — e.g. `http://192.168.1.20:9924/viewer`. Full-screen the browser."),
                    ("Over a vision mixer (keyed)", "**Output → Keyed full screen** on a monitor output that feeds the mixer; set **Key** to match its chroma key (blue or green)."),
                    ("Into vMix, OBS or a stream", "Press **NDI** — Stagewatch appears as an NDI source (install the free NDI Runtime first). Name it in Settings → Output."),
                    ("On your own laptop screen", "**Output → Floating mini**: a small always-on-top timer."),
                    ("Many TVs and tablets", "**Screens** (top right) lists them all: name each, **Identify** to find it, **Send** it to another page, and save setups to open at `/p/<name>`. See **Screens**."),
                    ("A 3D look (Pro)", "Press **3D** for the hologram, or right-click it → *3D egg timer*. The output and `/viewer` screens follow; NDI and keyed outputs keep the standard look. See **3D displays**.")),
                new Note("Test from the speaker's position: is the colour change visible, and does the **cue label** (\"5 MIN\") read from the stage? Settings → Output → *Cue label* and Settings → Cues → *Colour-blind-safe colours* help in bright rooms."),
            }),

        new("wf-powerpoint", "Workflows", "Set up PowerPoint",
            "Slide numbers, the next slide, and pace against the rehearsal.",
            new HelpBlock[]
            {
                new Steps(
                    "Run the presentation **on this PC** — Stagewatch reads the running PowerPoint here; no add-in is needed. On **another computer**? See **Presenter PCs (Stagewatch Slides)**.",
                    "**Settings → Integrations → Presentation**: tick *Read the running PowerPoint slideshow*. Save.",
                    "**Settings → Output**: tick *Slide n / N and next slide title* to show them on the speaker screen too.",
                    "Start the slideshow (F5). The **Slides** chip under the timer shows e.g. `Slide 4 / 22 · Next: Results`.",
                    "In the run-through, press **Record rehearsal** (Producer tab) at the first slide and **Stop + save rehearsal** at the end. Timings are saved per presentation file.",
                    "Live, the **Pace** chip compares the talk with the rehearsal: \"1:20 behind your rehearsal, projected 21:40\"."),
                new P("Already used PowerPoint's own **Rehearse Timings**? Stagewatch uses those when there's no Stagewatch rehearsal."),
                new H("If nothing appears"),
                new Bullets(
                    "Check Settings → Integrations → Status: *PowerPoint* should be green once a slideshow is running.",
                    "The slideshow must be running (not just the editor), and PowerPoint must be the desktop version on this PC.",
                    "Presenting from another computer? Run **Stagewatch Slides** there — see **Presenter PCs (Stagewatch Slides)**."),
            }, Pro),

        new("wf-sheet", "Workflows", "Plan with your team (Google Sheets, groups, notes)",
            "One shared plan, every department's notes, and screens that show it.",
            new HelpBlock[]
            {
                new Steps(
                    "Plan in a Google Sheet with columns **Title, Duration, Speaker, Kind, Fixed start, Group**, and one column per department (*Lighting*, *Sound*, *Video*) for their notes. Share it as *Anyone with the link can view*.",
                    "Running order → **Edit… → From Google Sheet…**, paste the link, **Replace** the first time. Set **Group targets…** for sessions that must fit a slot.",
                    "Hand each department its crew link (Showcaller page → Crew links). On its page they see their notes for now and next, and **Edit our notes** to add cues as they rehearse.",
                    "When the sheet changes, import again with **Update**: the new plan comes in, and live trims and the departments' notes on the same items stay.",
                    "Put up the screens: `/countdown?to=next` in the green room, `/timeline` for the producer, `/clock` in the gallery — and send them there from **Screens**."),
                new H("On the day"),
                new Bullets(
                    "Day 2 in the same show: **⋯ → New running order** (or Duplicate), plan it while Day 1 runs, switch at the end of the day.",
                    "Unattended breaks or a walk-in loop: press **Roll**; it follows the planned times until you take over."),
            }, Pro),

        new("wf-presenter", "Workflows", "Presenter PCs (Stagewatch Slides)",
            "Slides on another computer — the lectern laptop, a playback PC — still drive Stagewatch.",
            new HelpBlock[]
            {
                new P("**Stagewatch Slides** is a small app for the computer that runs PowerPoint. It only reads the slideshow and sends it to Stagewatch: slide number, next slide, rehearsal timings and the speaker notes (for speech pace). Nothing else to set up, and nothing is installed inside PowerPoint."),
                new Steps(
                    "On the presenter PC, run the Stagewatch installer and tick **This is a presenter PC** (or start **Stagewatch Slides** from the Start menu).",
                    "On the Stagewatch PC: **Settings → Remote** on, with *Allow other devices*. Note the **PIN**.",
                    "In Stagewatch Slides, the Stagewatch PC appears under **Stagewatch on the network** — press **Add**, type the PIN. Add the **backup** Stagewatch too: it sends to both.",
                    "Start the slideshow. Stagewatch Slides shows *sending slide 1/22*; Stagewatch shows the Slides chip as usual.",
                    "Leave it running. *Start with Windows* keeps it there after a restart."),
                new H("Which slideshow does Stagewatch follow?"),
                new Table(
                    ("Automatic (default)", "A presenter PC that's presenting; if none is, this PC's PowerPoint. Several presenting? The one that started most recently."),
                    ("This PC's PowerPoint only", "Ignore presenter PCs."),
                    ("A named presenter PC", "Always follow that one (e.g. the lectern), even if another starts a slideshow.")),
                new P("Choose in **Settings → Integrations → Presentation → Follow slides from**. Settings → Integrations → Status lists every presenter PC and its slide."),
                new H("If it doesn't connect"),
                new Bullets(
                    "*wrong or missing PIN*: type the Stagewatch PIN in Stagewatch Slides.",
                    "*can't reach it* / *no answer*: same network? Stagewatch running with Remote on? Try typing the Stagewatch PC's address (shown in Settings → Remote) under *Or type an address*.",
                    "Nothing under *Stagewatch on the network*: discovery uses UDP 9928 — allow Stagewatch through the Windows firewall on private networks, or type the address.",
                    "*needs Stagewatch Pro*: presenter PCs are a Pro feature on the Stagewatch PC (Stagewatch Slides itself needs no licence)."),
                new Note("Stagewatch Slides sends slide data only to the Stagewatch PCs you add, protected by their PIN. It announces its name on the network so Stagewatch and other presenter PCs can see it; nothing else."),
            }, Pro),

        new("wf-speech", "Workflows", "Set up speech pace",
            "Follow the speaker through their script, offline.",
            new HelpBlock[]
            {
                new Steps(
                    "Install a Windows speech language: **Windows Settings → Time & language → Speech** (and Language → add the language with *Speech recognition*).",
                    "Set the **microphone**: Stagewatch listens on the Windows **default recording device**. Use a feed of the speaker's mic — e.g. a USB audio interface from the sound desk aux — not the laptop's built-in mic.",
                    "**Settings → Integrations**: choose the **Speech language**, and either pick a **Script (.txt)** or leave it blank to use the PowerPoint speaker notes.",
                    "When the speaker starts, press **Listen to script** (Producer tab).",
                    "The **Pace** chip shows how much of the script is delivered, words per minute and a projected finish. Press **Stop listening** at the end."),
                new Note("Audio never leaves the PC — Windows recognises speech offline. The script only needs to be roughly what is said; ad-libs are fine."),
                new H("If it doesn't follow"),
                new Bullets(
                    "Status says *no speech recogniser installed*: add the speech language in Windows (step 1).",
                    "Check the input level in Windows Sound settings → Recording; a clean, close mic works best.",
                    "Choose the language the speaker is actually using."),
            }, Production),

        new("wf-showcaller", "Workflows", "Set up the Showcaller page",
            "The whole show on a second laptop or tablet.",
            new HelpBlock[]
            {
                new Steps(
                    "**Settings → Remote**: *Enabled*, and tick *Allow other devices on the network*. Note the **PIN**.",
                    "Allow Stagewatch through the Windows firewall on **private** networks when asked (installed for all users, this is done for you).",
                    "Under *Open on another device*, **Copy** the address and add `/producer?pin=<PIN>` — e.g. `http://192.168.1.20:9924/producer?pin=4821`.",
                    "Open it on the showcaller's laptop or tablet and bookmark it (on a tablet, *Add to home screen*)."),
                new P("The page shows the timer and cue, schedule offset, curfew and forecast, the assistant's suggestions, transport, speaker cues with acknowledgement, departments, green room, Q&A, and the running order with planned / expected / actual and Load / ±1m per item."),
                new Note("Use a show network you control. Anyone with the PIN can run the show; viewing pages never need it. A Stream Deck page with the same controls is in Companion's presets (**Showcaller / Producer page**)."),
            }, Pro),

        new("wf-crew", "Workflows", "Set up crew screens",
            "Departments, green room, moderator, interpreters and the speaker's pocket.",
            new HelpBlock[]
            {
                new P("Turn on **Settings → Remote** as for the Showcaller page. Open each crew page from its **crew link** — on the Showcaller page under **Crew links** (Open, Copy or QR), or in Settings → Remote. The link carries a key: only that page can confirm the cues sent to it, so a phone on the venue Wi-Fi can't fake a \"Got it\". No PIN is needed."),
                new Table(
                    ("Departments", "`/dept?name=Lighting` (one per department — set the list in **Settings → Cues**). A huge STANDBY / GO; the operator taps to confirm."),
                    ("Green room", "`/backstage` — \"Ana to stage\" calls; the wrangler taps **Found**, then **In position**."),
                    ("Moderator", "`/moderator` — the clock, the Q&A queue and how many more questions fit."),
                    ("Interpreters", "`/interpreter` — the clock and what's next, for the booth."),
                    ("Speaker's phone or watch", "`/haptic?for=speaker` — silent buzz cues. Add it to the home screen; Android vibrates, iPhone flashes.")),
                new Steps(
                    "Open each page from its crew link (scan the QR from the Showcaller page) and keep the screen awake.",
                    "Test: Cues & Q&A tab → **Standby** then **GO** for each department, and watch it turn green when they confirm.",
                    "No confirmation within the escalation time (Settings → Cues, default 20 s)? The cue turns red — radio them."),
            }, Production),

        new("wf-audience", "Workflows", "Set up audience screens and Q&A",
            "Agenda screens and questions from phones.",
            new HelpBlock[]
            {
                new Steps(
                    "Turn on **Settings → Remote** with *Allow other devices*.",
                    "On a lobby or room screen open `/audience`: the agenda with live times, a break countdown, and a QR code.",
                    "Phones scan the QR code and ask on `/ask` — no app or PIN; each phone is rate-limited.",
                    "Moderate in **Cues & Q&A**: ✓ approve, ★ star to ask first, **Next question** to take one live. The moderator sees them on `/moderator`."),
            }, Pro),

        new("wf-rooms", "Workflows", "Run several rooms",
            "One Stagewatch per room, every room on one screen.",
            new HelpBlock[]
            {
                new Steps(
                    "Install Stagewatch on each room's PC, on the same network.",
                    "On each: **Settings → Integrations** → give a **Room name** (e.g. *Hall A*) and tick *Share this room*.",
                    "On any device open `/rooms` from one of them: every room's time, title, lateness and curfew risk, with a link to each room's Showcaller page."),
                new P("Each room stays independent — if one PC or the network goes down, the other rooms carry on."),
            }, Production),

        new("wf-backup", "Workflows", "A main and backup PC",
            "Keep the show running if the main laptop fails.",
            new HelpBlock[]
            {
                new Steps(
                    "A Production licence covers two machines: install the **main** licence on one PC and the **backup** licence on the other.",
                    "Open the same show file on both. On the backup, keep outputs ready but switched to **Closed**, or feed a different input on the switcher.",
                    "If the main fails: on the backup, load the current item, set the time with **End at** (Real time) or nudge to match, and switch the screens."),
                new Note("After a crash or power cut, Stagewatch itself offers to restore the show exactly where it was."),
            }, Production),

        // ------------------------------------------------------------------ Timing
        new("wf-timecode", "Workflows", "Follow a video's timecode (LTC)",
            "The timer runs, pauses and counts down with the playback — it can't drift.",
            new HelpBlock[]
            {
                new Steps(
                    "Feed the playback machine's (or lighting/sound desk's) **LTC audio** into a **line input** on this PC — a USB audio interface is ideal. Avoid microphone inputs with Windows sound enhancements.",
                    "**Settings → Integrations → Time**: tick *LTC timecode in from* and choose that input. Save.",
                    "Roll the video: an amber **LTC** chip under the timer shows the timecode and *rolling*. Settings → Integrations → Status should be green.",
                    "Tick **Timer follows it** to make the timer start when the code rolls and pause when it stops (or the cable is pulled).",
                    "For an exact countdown to the end of the video, type its **last-frame timecode** (e.g. `01:04:32:00`) as the end. The timer then always shows end − timecode, even after a pause or a jump."),
                new H("Cues on timecode"),
                new P("**Settings → Triggers**: choose *Timecode* and put the timecode in **Filter** (e.g. `01:02:00:00`). It fires once as the code passes it — send a message, an OSC or HTTP cue, or press a Companion button. Rewinding or locating past it doesn't fire it."),
                new H("Live"),
                new Bullets(
                    "Right-click the LTC chip to stop following or clear the end timecode; from Companion or a remote: `ltc chase off`, `ltc end clear`.",
                    "You can still pause, nudge or start the timer by hand — Stagewatch only acts when the timecode starts or stops.",
                    "24, 25 and 30 fps are detected by themselves."),
                new Note("Needs **Production**. To *send* timecode instead, use *LTC timecode out* on the line above."),
            }, Production),

        new("timer", "Timing", "The timer and clock modes",
            "Countdown, count up and time of day; colours and overtime.",
            new HelpBlock[]
            {
                new Table(
                    ("Countdown", "Counts down from the loaded duration, then into overtime (shown as +MM:SS)."),
                    ("Count up", "Counts up from zero — for open-ended sessions."),
                    ("Time of day", "Shows the clock instead of a timer.")),
                new H("Colours"),
                new P("Normal → **warning** → **wrap-up** → **overtime**. Set the colours and thresholds in **Settings → Timer**, plus optional flashing and sound alerts. Colour-blind-safe colours are in Settings → Cues."),
                new H("Transport"),
                new Table(
                    ("START / STOP", "Start or pause (**Space** toggles). Pausing also freezes overtime."),
                    ("RESET", "Back to the loaded duration (**R**)."),
                    ("NEXT", "Load the next running-order item (**N**); it starts if the show is live."),
                    ("BLACKOUT", "Hide everything on every output (**B**). Press again to bring it back.")),
            }),

        new("durations", "Timing", "Durations, end-at and nudging",
            "Load a time, finish at a clock time, and adjust live.",
            new HelpBlock[]
            {
                new H("Duration"),
                new P("Type `MM:SS` or `HH:MM:SS` and press **Enter** (or **Load**). It loads without starting."),
                new H("End at"),
                new P("Type a time of day (`14:30` or `2:30 pm`) and press **Set**: the countdown finishes exactly then. **Clear** goes back to normal time."),
                new Table(
                    ("Elastic (Real time off)", "Stretches or squeezes the loaded duration so it lands on the end time — the seconds tick slightly faster or slower. The amber line under the timer shows the stretch."),
                    ("Real time (on)", "Counts the actual time left at normal speed.")),
                new Note("With **Honest clock** (Settings → Cues) the speaker always sees the real time left, even while you stretch."),
                new H("Nudge"),
                new Bullets(
                    "The **NUDGE** buttons add or remove time live (±1 s up to ±1 h).",
                    "Mouse wheel over the timer: **10 s** per notch, **Shift** = 1 min, **Ctrl** = 5 min."),
            }, "Standard"),

        new("aux", "Timing", "Extra timers (VT, changeover...)",
            "Up to three named timers beside the show. They never change the show's timing.",
            new HelpBlock[]
            {
                new P("Open the **Timers** tab. Each timer has a name, a time, and its own controls."),
                new Table(
                    ("Name", "Click the name and type, e.g. *VT*, *Changeover*, *Ana's clock*."),
                    ("Start / Pause, Reset", "Run it whenever you like; it doesn't touch the main timer or the running order."),
                    ("Down / Up", "Count down from the loaded time (it goes red past zero), or count up from zero."),
                    ("Load", "Type a duration (5:00, 1:30:00 or seconds) and press Load."),
                    ("Show on stage", "Adds it as a green line under the clock on the output and speaker screens, e.g. *VT  02:13*. One at a time.")),
                new P("Companion, HTTP, OSC and triggers: `aux 1 toggle`, `aux 2 set 3:00`, `aux 2 start`, `aux 1 stage`, `aux 1 nudge 30`, `aux 3 name Changeover`. Companion has a variable for each (`aux_1`, `aux_1_name`)."),
            }),

        new("presets", "Timing", "Presets",
            "Twelve one-click timers for the times you use most.",
            new HelpBlock[]
            {
                new Table(
                    ("Click", "Load the preset."),
                    ("Double-click", "Load and start."),
                    ("Right-click", "Load, start, edit or edit all."),
                    ("Keys 1–9, 0, −, =", "Load presets 1–12 (operator shortcuts on).")),
                new P("**Edit all…** sets names and durations for every slot. Presets are saved in the show file. The free mode keeps 2 presets; every licence has 12."),
            }),

        new("rundown", "Timing", "Running order",
            "Plan the whole show and step through it.",
            new HelpBlock[]
            {
                new Steps(
                    "Click **Edit…** on the Running order tab. Add items, paste rows from a spreadsheet, or import a CSV.",
                    "Set **Starts** (planned start of item 1, e.g. `09:00`) to track ahead/behind against the clock.",
                    "Click an item to load it, double-click to load and start. **Next ▶ / ◀ Prev** (or **N / P**) step through; **↺** returns to the top."),
                new H("In the editor"),
                new Table(
                    ("Kind", "Talk, Panel, Q&A, Break, Award, Video, Buffer — guides trims and the forecast. **Buffer** items shrink to absorb lateness."),
                    ("Auto-advance (↪)", "The item rolls into the next one on its own at zero."),
                    ("Fixed start (📌)", "Hold the item to a published time; early waits, late is flagged."),
                    ("Min", "How short the item may be cut."),
                    ("Curfew (⏹)", "A hard-out at the end of this item."),
                    ("Cue ladder", "This item's own speaker cues."),
                    ("Notes", "Operator-only notes."),
                    ("Group", "A session or block (e.g. *Morning*, *Panels*). Consecutive items with the same group show under one heading with their total; **Group targets…** sets a target length, and a group over target turns red here, on the Showcaller page and on `/timeline`."),
                    ("Dept notes", "Each department's note on the item (*Lighting: blue wash, follow spot*). Departments can also edit their own from their crew page — see **Crew cues**.")),
                new P("Right-click an item to load, start, trim ±1 min or edit the running order."),
                new H("Importing"),
                new Table(
                    ("Paste / CSV", "Columns Title, Duration, Speaker, Message, Kind, Fixed start, Min, Curfew, Group — plus one column per department (*Lighting* or *Lighting notes*) for their notes."),
                    ("From Google Sheet…", "Paste the sheet's link (share it as *Anyone with the link can view*, or publish it to the web). **Update** takes the sheet's plan but keeps live trims, notes and department notes on items with the same title; **Replace** takes the sheet as it is. The rows appear in the editor for you to check before OK."),
                    ("✨ From any agenda text…", "The copilot drafts rows from an email or PDF agenda (Production).")),
                new H("Several running orders"),
                new P("A show can hold several running orders — Day 1 and Day 2, the rehearsal version, a backup plan. Pick the live one at the top of the Running order tab; **⋯** adds, duplicates, renames, edits or deletes them. You can edit one while another is live. Switching ends the current item cleanly and starts the new running order from the top. All of them are saved in the show file. Command: `rundown Day 2`."),
                new H("Roll: follow the clock"),
                new P("Press **Roll** and the running order runs itself by its planned times — ideal for walk-in loops, breaks or a room with nobody at the desk. Each item starts at its planned start with the time left to its planned end; between items the timer counts down to the next start (*Next: Keynote*). Anything you do — pause, load, start — takes over, and Roll switches off. Needs a planned start (**Starts**). Command: `roll on|off`."),
            }, Pro),

        new("recovery", "Timing", "Delays, catch up and curfews",
            "Keep the show on time when it runs late.",
            new HelpBlock[]
            {
                new Table(
                    ("Delay +1m / +5m", "Announces a delay before the next item. It ripples on until a gap, buffer or fixed start absorbs it."),
                    ("Catch up", "Re-times the live item (elastic) so it lands on its planned end."),
                    ("Trim", "Right-click a running-order item → **−1 min / +1 min**. Trims are kept apart from the plan, so the report shows plan, adjusted and actual."),
                    ("Curfew chip", "Shows the hard-out and the margin; turns red when at risk.")),
                new P("With **Production**, the assistant proposes concrete cuts when a curfew is at risk — Q&A first, then panels, never below each item's minimum — and **Apply** does it."),
            }, Pro),

        // ------------------------------------------------------------------ Speaker
        new("messages", "Speaker", "Messages",
            "Text on the speaker's screen, with urgency and flash.",
            new HelpBlock[]
            {
                new Steps(
                    "Type in **Message**, choose **Info / Warning / Critical** and a flash, then **Send**.",
                    "**Clear** removes it.",
                    "**Quick messages** send a saved message in one click. Right-click the message box → **Save as quick message**; right-click a quick message to edit or remove it. They're saved with the show."),
                new H("Live placeholders"),
                new P("`{FINISH_BY}` `{NEXT}` `{OVER_UNDER}` `{REMAINING}` `{TITLE}` `{SPEAKER}` `{TIME}` stay up to date while the message is on screen."),
                new H("Ask to acknowledge"),
                new P("Tick **Ask to acknowledge**: the speaker screen shows **Got it** and you see waiting → ✓ acknowledged. No answer within 20 s (Settings → Cues) escalates. Needs **Production**; otherwise the message simply goes out."),
            }),

        new("cues", "Speaker", "Speaker cues and cue ladders",
            "Graduated signals the speaker agrees to before going on.",
            new HelpBlock[]
            {
                new Table(
                    ("Cue ladder", "Producer tab. Standard (your thresholds), Keynote 10/5/2/0, Panel, Toastmasters green/yellow/red, Awards, Spoken (in-ear). Items can have their own."),
                    ("Speaker clock", "Standard MM:SS, **Calm** (\"12 MIN\" until the last minute) or progress bar and cue only."),
                    ("Cue label", "\"5 MIN\", \"WRAP UP\" on the output — a cue that doesn't rely on colour (Settings → Output)."),
                    ("Briefing preview", "Plays the whole ladder at 10× on every output during the tech check."),
                    ("Cue sounds", "Send each cue to its own audio device, plus spoken cues (Settings → Cues)."),
                    ("Chimes", "Add your own WAV chimes (Settings → Timer → Chime library), then give any item its own sound at start, warning, wrap-up or zero (Running order → Edit → Chimes; **Test** plays it). \"Chime when an item starts\" adds a short rising chime to every item."),
                    ("Chimes on screens", "Open any screen with **?sound=1** (a lectern tablet, an in-ear feed) and it plays the same chime the operator hears. Tap once to allow sound."),
                    ("Hide at zero", "Speaker screens show nothing at zero instead of overtime — the operator still sees it (Settings → Timer).")),
            }, "Standard"),

        new("outputs", "Speaker", "Outputs, keying and NDI",
            "Getting the timer onto screens, vision mixers and streams.",
            new HelpBlock[]
            {
                new Table(
                    ("Full screen", "Borderless on the monitor chosen in Settings → Output (**O** opens it)."),
                    ("Keyed full screen", "Full screen on a solid **Key** colour, for a vision mixer's chroma key."),
                    ("Floating mini", "A small always-on-top window for your own screen."),
                    ("Closed", "No output window."),
                    ("NDI", "Sends the output as an NDI source on the network (needs the free NDI Runtime).")),
                new P("**Settings → Output** chooses what appears: timer, message, end time, clock, title, progress, next item, ahead/behind, cue label, finish-by, slides and VT countdown."),
                new Note("Free mode (after the trial, without a licence) shows a watermark on every output."),
            }),

        new("holding", "Speaker", "Holding images",
            "A still image on every screen — walk-in slides, sponsor logos, \"we'll be back\" cards.",
            new HelpBlock[]
            {
                new Steps(
                    "Open the **Holding** tab and click **Add images…** (or drop PNG, JPG, GIF, BMP, TIFF or WebP files on it, up to 25 MB each). Stagewatch keeps its own copy, so the originals can move.",
                    "Click a thumbnail: it goes **on air** full screen on every screen, covering the timer. A violet **HOLDING** chip under the timer reminds you.",
                    "Click it again, press **Off** or **H** to take it down — the timer comes straight back. **H** brings the last image back."),
                new H("Where it shows"),
                new Table(
                    ("Output window", "Full screen, keyed and floating outputs — and **NDI**, since NDI sends the output."),
                    ("Browser screens", "`/viewer` and `/audience` show it over their own content. Add `?holding=0` to a screen's address to keep it off that screen."),
                    ("A dedicated screen", "`/holding` on any TV or tablet shows only holding images, black otherwise — ideal for the lobby or overflow room."),
                    ("Not shown", "Crew pages (Showcaller, departments, green room, moderator) keep working — they show which image is on air instead.")),
                new H("Options"),
                new Bullets(
                    "**Fill the screen (crop)**: off fits the whole image with black bars; on fills the screen and crops the edges.",
                    "**Blackout** hides holding images too.",
                    "Right-click a thumbnail to **rename** or **remove** it. Images are kept with Stagewatch, not in show files.",
                    "Animated GIFs show their first frame."),
                new H("From other devices"),
                new P("The Showcaller page (`/producer`) lists the images — tap one to put it on air — and has **Upload…** to add new ones from a phone or laptop. Companion, HTTP, OSC and triggers use the command `holding <number|name|off|toggle>`, e.g. `holding 2` or `holding Sponsors`."),
            }, "Standard"),

        new("holographic", "Speaker", "3D displays: hologram and egg timer",
            "The time as a hologram above a projector, or beside a 3D egg timer — on the output and browser screens.",
            new HelpBlock[]
            {
                new Steps(
                    "Press **3D** next to NDI. Right-click it to choose **Hologram** or **3D egg timer**, and to switch animations, particles and the projector base.",
                    "The output window switches within a second or two; browser screens on `/viewer` follow automatically.",
                    "Press **3D** again for the standard display."),
                new H("Colours follow the show"),
                new P("The timer and cue use your warning-stage colours (Settings → Timer). Messages take their own colour: info pale, **warning amber**, **critical red** — and the beam, glass glow and particles lean to it while it's up."),
                new H("Animated transitions (optional)"),
                new Table(
                    ("Messages", "Build in from the top like a hologram forming; dissolve when cleared."),
                    ("Stage changes", "A shockwave ring and a flash as the colour sweeps over."),
                    ("Warnings", "Everything breathes gently; particles quicken."),
                    ("Critical and overtime", "Faster pulse, glitch bursts, a slight shake, alarm rings and a particle surge."),
                    ("Start / pause", "The beam powers up and down.")),
                new H("The egg timer"),
                new P("Lit glass, a wood-and-brass frame and a soft shadow. The sand is measured like a real one — the top holds exactly the share of time left, the bottom the time gone — the stream runs only while the timer runs, and it turns over when a new time is loaded. The sand takes the stage colour; the glass glows with warnings and it rocks and shakes when something is critical."),
                new H("Built to be safe on stage"),
                new Bullets(
                    "It runs on the graphics card (three.js / WebGL) and lowers its own detail if a computer struggles, so the timer stays smooth.",
                    "The standard display keeps running underneath. If the 3D view can't start, or stops responding for 3 seconds, the output switches back on its own and the status line says why — it returns when the 3D view recovers.",
                    "NDI keeps sending the standard look."),
                new H("Browser screens"),
                new Table(
                    ("/holo", "A dedicated holographic screen for any TV or tablet."),
                    ("/viewer?style=holo", "Force the 3D view on one screen; `?style=flat` forces the standard view."),
                    ("?floor=0", "Without the projector base and reflection — the text hovers on its own."),
                    ("?scene=egg / ?scene=holo", "Choose the scene for one screen."),
                    ("?anim=0, ?particles=0", "No animations or no particles on one screen (e.g. a slow tablet).")),
                new P("**Projector base** (Settings → Output) turns the base, beam and reflection on or off everywhere."),
            }, Pro),

        // ------------------------------------------------------------------ Crew
        new("crew", "Crew & audience", "Crew cues and acknowledgements",
            "Standby / GO for departments, the green room and the speaker's pocket.",
            new HelpBlock[]
            {
                new Table(
                    ("Standby / GO", "Cues & Q&A tab, per department. Each department opens its own page: `/dept?name=Lighting`."),
                    ("Call next speaker", "Sends \"<speaker> to stage\" to the `/backstage` page; the wrangler taps Found, then In position."),
                    ("Buzz", "A discreet cue to the speaker's phone or watch (`/haptic?for=speaker`). Android vibrates; iPhone flashes."),
                    ("Cues sent", "Every cue and its state: amber = waiting, green = acknowledged, red = nobody answered — radio them."),
                    ("Department notes", "Each department page shows its notes for the item now and next, and **Edit our notes** lets the department keep its own notes on every item. Opened from its crew link, a department can only read and edit its own.")),
                new P("Right-click a sent cue to resend or clear it. Departments and the escalation time are in **Settings → Cues**."),
            }, Production),

        new("qa", "Crew & audience", "Audience Q&A",
            "Questions from phones, moderated in one place.",
            new HelpBlock[]
            {
                new Steps(
                    "Put `http://<this-pc>:9924/audience` on a screen: agenda, break countdown and a QR code.",
                    "Phones scan it and ask on `/ask` — no app, no PIN, rate-limited.",
                    "Moderate in the Cues & Q&A tab: ✓ approve, ★ star (asked first), ✕ dismiss, **Next question** takes the next one live."),
                new P("The moderator's page (`/moderator`) shows the queue and how many more questions fit the time left."),
            }, Pro),

        // ------------------------------------------------------------------ Producer
        new("producer", "Producer", "Assistant, forecast and report",
            "Know when the show will really end, and prove it afterwards.",
            new HelpBlock[]
            {
                new Table(
                    ("Assistant", "Suggestions on the Producer tab — trims, cues, fixes. Nothing changes until you click its button. Rule-based and offline; it can also ask Claude (Settings → Integrations, your API key)."),
                    ("Forecast", "\"Likely end 17:24 · 80% by 17:38\" — learned from how your shows actually run."),
                    ("Show report", "Planned vs actual for every item, cues and acknowledgements. Print → Save as PDF for the client (Production)."),
                    ("Export CSV", "The same data for a spreadsheet (Pro). Reports are saved in Documents\\Stagewatch Shows\\Reports.")),
            }, Production),

        new("pace", "Producer", "PowerPoint, rehearsal and speech pace",
            "A timer that knows the slides.",
            new HelpBlock[]
            {
                new Steps(
                    "Turn on **PowerPoint** in Settings → Integrations (no add-in needed). Slide n / N and the next title appear.",
                    "During the run-through press **Record rehearsal**; press again to save it.",
                    "Live, the pace chip says e.g. \"2:10 behind your rehearsal, projected 23:40\"."),
                new P("Step by step: **Set up PowerPoint** in Workflows."),
                new H("Listen to script (Production)"),
                new P("Windows' offline speech recognition follows the speaker through the speaker notes or a `.txt` script: percent delivered, words per minute and a projected finish. Audio never leaves the PC."),
            }, Pro),

        new("copilot", "Producer", "Copilot: AI with you in charge",
            "Plain English in, a plan you approve out. Nothing runs on its own.",
            new HelpBlock[]
            {
                new P("Press **✨ Copilot** (top right) or **Ctrl+K** and type what you need, e.g. *We're 6 minutes late — get us back on time without touching the keynote*, *Tell Ana kindly she has 2 minutes*, *Put the lobby screens on the agenda and start a 15-minute changeover timer*, or a question: *Will we make the curfew?*"),
                new P("The copilot answers, or proposes a **plan**: Stagewatch's own commands, each with a reason. Tick what should run and press **Run ticked steps**, or **Discard**."),
                new Table(
                    ("SAFE (green)", "Changes nothing anyone sees: notes, naming screens, saving screen setups. Ticked for you."),
                    ("REVIEW (amber)", "Changes timing or sends words to someone: trims, delays, messages, cues, extra timers that aren't on stage. Ticked for you — read it first."),
                    ("CHANGES THE ROOM (red)", "Anything that changes or covers what a screen shows, even briefly: start, pause, reset, next item, blackout, outputs, 3D style, holding images, preview, putting an extra timer on stage, identifying or moving screens, Roll. **Never ticked for you.**")),
                new P("Every command is checked against Stagewatch's grammar before you see it; anything invalid is listed as *not proposed*. The copilot can't invent items or screens — it only sees this show."),
                new H("More AI, same rule"),
                new Table(
                    ("From any agenda text", "Running order → Edit… → **✨ From any agenda text…**: paste an email or PDF agenda; the copilot drafts the rows, you check them in the grid before OK."),
                    ("Show summary", "Producer tab → **✨ Draft the show summary…**: the report's narrative and lessons, drafted from the journal; you edit before it's kept."),
                    ("Assistant", "Proactive suggestions on the Producer tab while the show runs; press a suggestion to apply it."),
                    ("AI agents (MCP)", "Tools like Claude connected to `/mcp` propose commands into Copilot with a reason; you approve them. Settings → Integrations can let them act directly instead."),
                    ("Review the plan", "Producer tab or Copilot → **✨ Review the plan**: findings for the whole running order the day before, using how much each kind of item and each speaker has overrun in *your* past shows, with fixes to approve one by one."),
                    ("Explain a problem", "**✨ Explain a problem**: the copilot reads every link's health, refused commands and the show, then says what's wrong and exactly which setting to change."),
                    ("Hold to talk", "Hold **F9** (or the 🎙 button), say *\"we're five minutes late, protect the keynote\"*, let go. What it heard appears in Copilot and becomes a plan you approve. Windows speech — offline."),
                    ("Message ideas", "**✨** beside the message box: three short, kind messages for this moment with the real minutes left (or your draft, tightened). Pick one; nothing is sent until you press Send."),
                    ("Screen designs", "Screens → **Design screens…** → *Describe it*: the copilot drafts a screen layout; you see it on the preview and keep or discard it.")),
                new P("The journal records every request, approval and discard, so the show report shows what the AI suggested and who decided."),
                new Note("Needs **Production** and Settings → Integrations → *Also ask Claude* with an API key. Copilot requests send the show's running order, timings, messages, questions, screen names, link status lines and your overrun history to Anthropic. Voice is recognised on this PC; only the words are sent."),
            }, Production),

        // ------------------------------------------------------------------ Connect
        new("remote", "Connect", "Browser pages and remote control",
            "Any phone, tablet or laptop on the network — no app.",
            new HelpBlock[]
            {
                new P("Turn it on in **Settings → Remote**, tick **Allow other devices on the network**, and allow Stagewatch through the Windows firewall on private networks. Pages are at `http://<this-pc>:9924/…`."),
                new Table(
                    ("/viewer", "Confidence monitor (read-only)."),
                    ("/control", "Remote control (PIN)."),
                    ("/producer", "The whole show for the showcaller (PIN)."),
                    ("/moderator, /backstage, /dept, /interpreter, /haptic", "Role pages for the crew."),
                    ("/audience, /ask", "Agenda screen and audience questions."),
                    ("/rooms", "Every Stagewatch on the network (Production)."),
                    ("/holding", "A screen that shows only holding images (black otherwise)."),
                    ("/holo", "The holographic 3D display (Pro)."),
                    ("/clock", "A studio clock: time of day with a ring of seconds, plus the show timer (`?h=12`, `?timer=0`)."),
                    ("/countdown", "Time until the next item (`?to=next`), item n (`?to=3`), a group (`?to=group:Morning`) or the end of the show (`?to=end`); `&label=Doors open` for your own words."),
                    ("/timeline", "The running order on a time axis with groups and a live now-line (`?span=3h` for a window around now)."),
                    ("/report", "The show report.")),
                new P("**Custom look:** Settings → Remote → *Custom CSS* styles every browser screen; target one page with `.sw-page-viewer`, `.sw-page-clock`, `.sw-page-timeline` …"),
                new P("The **PIN** is in Settings → Remote; a link with `?pin=1234` logs in directly. Five wrong PINs lock that device out for a minute. Five wrong viewing codes, window addresses or crew links lock it out of those for a minute, counted apart: the PIN still works on it, and nothing but the minute ends the wait."),
                new Table(
                    ("Anyone on the network", "Speaker, agenda, rooms and holding screens — the display only. Never speaker notes, assistant suggestions or unmoderated questions."),
                    ("Crew links", "Crew pages confirm only their own cues (Settings → Remote → *Crew pages confirm cues only…*). **New crew links** revokes every old one."),
                    ("PIN", "Control, the Showcaller page, the show report and CSV, the full operator view, MCP."),
                    ("API keys", "For scripts, dashboards and AI agents instead of the PIN (Settings → Remote → API keys). **Read** sees the operator view, reports and screens; **control** also runs `/api` commands; **MCP** also reaches `/mcp`. Send `Authorization: Bearer swk_…`. Each key: 600 requests a minute (`X-RateLimit-Remaining` in every reply, `Retry-After` when over). Shown once; revoking works at once. A **node** key is another show app's and speaks for one app (its node id): the first that uses it, or the one you choose with **Let it speak for…** (button or right-click). Its row says which (\"speaks for patterns 9f8e7d6c\", \"no node yet\") and when it was refused for another; every change is saved and journalled.")),
                new P("Every screen checks it is still hearing from Stagewatch: after about 6 seconds of silence it dims and shows **NOT CONNECTED TO STAGEWATCH** instead of a time that may be stale, and reconnects by itself."),
                new H("For other gear"),
                new P("HTTP API (`/api/…`), WebSocket (`/ws`) and OSC (UDP 9925, off by default). One command grammar for all; see `/api/help`."),
            }, Pro),

        new("screens", "Connect", "Screens: name, find and move browser screens",
            "Every TV, tablet and laptop showing Stagewatch, managed from this PC.",
            new HelpBlock[]
            {
                new P("Click **Screens** (top right). Every browser showing a Stagewatch page is listed with the page it shows, the device and its address. A screen keeps its identity in that browser, so a name you give it sticks through reloads and restarts. Show apps that follow this Stagewatch (Patterns, PlayMap, AGMessage) are not screens: they are listed under **Producer → Show network**, the line under the title counts them, and screen commands never reach them. Something that only claims to be a show app, with nothing to prove it, is listed here marked ○ so you can find it or drop it."),
                new Table(
                    ("Name a screen", "Double-click its name and type, e.g. *Lobby TV* or *Stage left*."),
                    ("Find it", "Select it and press **Identify**: its name fills that screen for 6 seconds. Nothing selected = every screen."),
                    ("Send it somewhere", "Pick a saved setup or type a page (e.g. `/audience`, `/viewer?cue=0`) and press **Send**. The screen switches on its own — no walking to the lobby."),
                    ("Save a setup", "Select a screen that looks right and press **Save as setup…**, or add one below with a name and a page."),
                    ("Open a setup on a new device", "Type `http://<this-pc>:9924/p/<name>` — e.g. `/p/lobby`. **Copy address** puts it on the clipboard for a QR code or a message.")),
                new P("Control pages (🔒) and unproven claims (○) are listed but never moved by *all*; a claim is reached only by its own id. Screens are only ever sent to pages on this Stagewatch, and a PIN is never stored in a setup."),
                new H("Design your own screens"),
                new P("**Design screens…** (in Screens) opens the designer in your browser: drag the timer, text, the message, a progress bar, labels, boxes and holding images onto a live preview of the real show, snap them to centres and edges, and **Save** — every screen showing `/layout?name=<name>` updates within a second. Arrows nudge, Shift+arrows move 5%, Ctrl+D duplicates, Ctrl+Z undoes, Tab cycles."),
                new P("Text uses placeholders: `{title}` `{speaker}` `{next}` `{nextStart}` `{nextIn}` `{item}` `{group}` `{end}` `{showEnd}` `{clock}` `{date}` `{room}` `{message}` `{cue}` `{aux}` `{slide}` `{time}`. A line whose placeholders are all empty hides itself."),
                new P("**Describe it:** type *\"foyer sign, big clock, next session and when it starts, our logo bottom right\"* and press **Draft with AI**. The copilot's draft appears on the preview with its notes; **Keep it** or **Discard** — nothing reaches a screen until you save."),
                new H("From Companion, HTTP, OSC or triggers"),
                new P("`screen identify [screen]`, `screen go <setup|/page> [screen]`, `screen name <id> <name>`, `screen save <setup> <screen>` — a screen is its name or id; leave it out (or say `all`) for every browser screen, never a control page, a claim or a show app. E.g. a trigger on *Item start* → `screen go agenda Lobby TV`."),
            }, Pro),

        new("companion", "Connect", "Bitfocus Companion and Stream Deck",
            "Buttons that show the live timer and drive the show.",
            new HelpBlock[]
            {
                new Steps(
                    "In Companion: **Modules → Import module package**, choose `stagewatch-<version>.tgz` (installed in the Stagewatch folder under **Companion**).",
                    "Add a **Stagewatch** connection: address `127.0.0.1` (same PC) and port **9923**.",
                    "In Stagewatch, **Settings → Companion → Enabled**. For another PC, tick **Bind on all interfaces**.",
                    "Drag keys from Companion's **Presets** tab — e.g. the 32-key **Showcaller / Producer page**."),
                new P("Keys follow Stagewatch live: colours, the time, running-order items and presets update by themselves. Companion's **action recorder** captures what you click in Stagewatch — including right-click actions."),
                new H("Useful preset sections"),
                new Table(
                    ("Showcaller / Producer page", "32 keys in reading order: timer ring, schedule, curfew, assistant, transport, cues, Q&A, blackout."),
                    ("Outputs, NDI & 3D", "Open full screen, keyed or mini, close, NDI, hologram and egg timer, and LTC chase — lit while active."),
                    ("Holding images", "One key per image (violet while on air), plus off and toggle."),
                    ("Schedule recovery", "Cut / extend the live item by a minute, undo trims, delays, catch up, and an *On now* key coloured by kind."),
                    ("Cues & departments", "Wrap-up and finish-by cues with acknowledgement, call to stage, resend an unanswered cue, Standby / GO per department.")),
                new P("Keys for features your licence doesn't include turn **dark grey**, so nobody presses a key that won't work."),
            }, Pro),

        new("integrations", "Connect", "Integrations",
            "Video playout, cue lights, timecode, triggers and more.",
            new HelpBlock[]
            {
                new Table(
                    ("vMix", "VT countdown from the clip playing in Program (host and port of vMix)."),
                    ("Playout links", "Patterns desks and PlayMap nodes, up to 8, in **Settings → Integrations → Playout links**: **Find** listens for them, **Add** / **Edit** set each one's name, peer, address, port, pairing token, node and transport. Each one's clip on air can feed the VT countdown (with vMix too, the first link whose clip runs, else vMix's), and `playout <link> play|pause|stop|restart|end|seek|cue|go …` drives it from Companion, triggers, the wire or the copilot (`patterns …` still means link patterns). The token never crosses the network in clear: Stagewatch answers the peer's NONCE with a proof, and sends the token itself only to a desk or node on this PC (a loopback socket), so a desk on another PC older than Show Bus 1.1 stays unpaired (amber: update Patterns). A PlayMap node runs over TLS where it offers it, pinned and never dialled plain again. **Last 10 s** beside the VT chip jumps the clip to its end; right-click the VT chip for play, pause, stop, restart and seek, each greyed with why when its press would be refused. Over OSC and Companion TCP these lines come only from this PC or **Settings → Remote → May drive playout**."),
                    ("Running-order cues", "A running-order item can cue up to four playout links: a PlayMap playlist's item, or a Patterns cue, that GOes when the item starts (a Patterns cue by CUE GO alone — its own standby decides). Standby is off unless you set it: then the cue stands by when the item loads — a PlayMap standby shows the item's first frame on air (only on a playlist that is idle), a Patterns one moves that desk's own standby. Set them in **Edit running order → Playout**: pick each cue from what its peer lists (or type its ids), with standby and together as options that say what they do. Cues marked together start at one instant, your lead ahead (**Settings → Integrations**, 300 ms unless you change it): a PlayMap node gets AT on its own clock when its clock is fit for it (measured over TLS or on this PC), the others are held here until their moment; `playout <link> go <playlist> <item> together` does the same for one GO. A cue whose link is missing or off, or that names what its peer doesn't list, is skipped — said in amber before its item, assistant on or not, and on the status line as it starts; the show log says when each cue went, or why it didn't, and calls a timed GO scheduled only once its node says it took it."),
                    ("Cue lights", "Art-Net, sACN or Philips Hue: a lectern or stage-edge light that follows the cues — dark, then cue colours, flashing at time."),
                    ("Time server", "Checks this PC's clock; a red chip warns when it's more than a second off."),
                    ("LTC out", "Timecode out on any audio output (24/25/30 fps), time of day or the countdown."),
                    ("LTC in", "Reads timecode from an audio input (24/25/30 fps, detected by itself). **Timer follows it**: starts when the code rolls, pauses when it stops; with an end timecode (e.g. the video's last frame) it counts down exactly to it. Timecode triggers fire as the code passes them."),
                    ("Rooms", "Give this PC a room name and share it on `/rooms`."),
                    ("Assistant (Claude)", "Optional AI suggestions; sends the running order and timings to Anthropic, so it's off until you add a key."),
                    ("MCP", "`/mcp` lets AI tools and Patterns read and drive the show (PIN).")),
                new P("Settings → Integrations shows each one's status at the top. All of these need **Production**, except PowerPoint (Pro)."),
            }, Production),

        new("triggers", "Connect", "Triggers",
            "When something happens in the show, tell other gear.",
            new HelpBlock[]
            {
                new P("**Settings → Triggers**: one row per rule. Click the empty last row to add one; select a row and press Delete to remove it."),
                new Table(
                    ("When", "Item start / end, phase change, overtime, cue acknowledged, message, curfew at risk, blackout, or **Timecode** (incoming LTC passes the filter, e.g. `01:00:30:00`)."),
                    ("Filter", "Narrow it: phase name (warning, wrapup, overtime), item number, or a target (speaker, Lighting)."),
                    ("Do", "OSC, HTTP, a Companion button or a Stagewatch command."),
                    ("Target", "OSC/Companion `host:port`, or the HTTP URL."),
                    ("Payload", "OSC `/address args`, HTTP body (blank = GET), Companion `page/row/column`, or the command.")),
                new P("Placeholders: `{ITEM}` `{TITLE}` `{PHASE}` `{REMAINING}` `{DETAIL}` `{ROOM}`. Each trigger fires at most once a second."),
                new P("**An HTTP trigger with a secret** (a PlayMap or Patterns token, a Stagewatch PIN or key, a bearer token) keeps it encrypted for your Windows user and sends it only to this PC, or over `https://` to a certificate you trusted: the first time, the target's certificate is pinned and nothing is sent — compare its short form with the target's own screen, then right-click the status line and trust it. A changed certificate is refused until you type at least 8 hex of the new one. Its address is fixed (placeholders only in the path or query), it follows no redirect, and its answer — applied, refused, accepted or unconfirmed — is in the status and the show log, never the secret. A secret its target refuses (401) or locks out (429) is held — every trigger with that secret, across a restart — until the hold ends or you set a new one, so the target never learns to lock this PC out. A `?pin=` in an address still works, with a warning; Save offers to move it into the rule's secret where it would then be sent. Set a rule's secret in its **Secret** column."),
            }, Pro),

        // ------------------------------------------------------------------ Reference
        new("shortcuts", "Reference", "Keyboard and mouse",
            "Every shortcut in one place.",
            new HelpBlock[]
            {
                new Table(
                    ("Space", "Start / pause"),
                    ("R", "Reset"),
                    ("N / P", "Next / previous running-order item"),
                    ("B", "Blackout"),
                    ("F", "Flash the timer (again to stop)"),
                    ("O", "Open the output window"),
                    ("H", "Holding image off / back on"),
                    ("1–9, 0, −, =", "Presets 1–12"),
                    ("Ctrl+O / Ctrl+S / Ctrl+Shift+S", "Open / save / save show as"),
                    ("F1", "This help"),
                    ("Mouse wheel on the timer", "±10 s; Shift ±1 min; Ctrl ±5 min"),
                    ("Right-click", "Help and quick actions for what's under the pointer")),
                new Note("Single-key shortcuts work when **Enable operator keyboard shortcuts** is on (Settings → Timer) and you're not typing in a box."),
            }),

        new("licence", "Reference", "Licences and plans",
            "What each licence includes.",
            new HelpBlock[]
            {
                new Table(
                    ("Trial", "14 days, everything."),
                    ("Free", "After the trial: the basic timer with a watermark."),
                    ("Standard", "The core timer: all presets, end-at, alerts, quick messages, NDI, keyed outputs, cue ladders, holding images, extra timers."),
                    ("Pro", "+ running order, browser remotes and Screens, Companion, PowerPoint and presenter PCs, Q&A, triggers, rehearsal, CSV report, 3D hologram and egg timer."),
                    ("Production", "+ crew cues and acknowledgements, forecast and trim suggestions, multi-room, cue lights, LTC in and out, vMix, speech pace, full report, the AI copilot and assistant, MCP. Main + backup PC.")),
                new P("**Help → License** shows prices, your hardware ID (send it with your order) and installs the licence file."),
            }),

        new("guides", "Reference", "Guides: ATEM, OBS, Zoom, Raspberry Pi, TVs, websites",
            "Getting the timer onto the gear you already have.",
            new HelpBlock[]
            {
                new H("ATEM or any vision mixer"),
                new P("Send the **output window** to the mixer over HDMI/SDI: Output → *Keyed full screen* gives a solid key colour behind the timer (Key: Blue, Green, Magenta or Black) — use an upstream or downstream **chroma key** (or a luma key on Black) on that input. NDI-capable mixers can take the **NDI** source instead of a cable; it is the same picture, so key it the same way."),
                new H("OBS Studio and vMix"),
                new Steps(
                    "Add a **Browser source** (OBS) or **Web Browser input** (vMix).",
                    "URL: `http://<this-pc>:9924/viewer?style=flat` for the timer, `/layout?name=<your design>` for a designed screen, `/clock` or `/countdown?to=next` for the others.",
                    "Width 1920, height 1080. In OBS untick *Shutdown source when not visible* so it stays connected.",
                    "Or use NDI: Output → **NDI** and add the Stagewatch NDI source (with Output → *Keyed full screen*, chroma-key it)."),
                new H("Zoom, Teams and other video calls"),
                new P("Remote speakers see the timer best as a camera: add the OBS browser source above, start OBS **Virtual Camera**, and pick *OBS Virtual Camera* as the camera in Zoom/Teams for a host or a dedicated *Timer* participant. Or send them the `/viewer` link to open in their own browser — no install, it works over the internet if you forward the port or use a tunnel."),
                new H("Raspberry Pi as a screen"),
                new Steps(
                    "Raspberry Pi OS with desktop, on the same network as this PC.",
                    "Save the screen as a setup here (Screens → Save as setup…, e.g. *stage*).",
                    "On the Pi, autostart: `chromium-browser --kiosk --noerrdialogs --disable-infobars --check-for-update-interval=31536000 http://<this-pc>:9924/p/stage`",
                    "Name it in **Screens**; from then on you can move it, reload it or identify it from here."),
                new H("Smart TVs, Fire TV, tablets"),
                new P("Open the TV's browser (or *Silk* on Fire TV) at `http://<this-pc>:9924/p/<setup>`. Screens stay awake on their own where the browser allows; disable the TV's eco/standby timer. For a tablet, add the page to the home screen to get it full screen."),
                new H("Embed in a website or intranet"),
                new P("`<iframe src=\"http://<this-pc>:9924/countdown?to=next\" width=\"800\" height=\"450\" style=\"border:0\"></iframe>` — any page can be embedded. On a shared network set a **viewing code** (Settings → Remote) and add `&view=<code>` to the address. For a public website, put Stagewatch behind a tunnel or reverse proxy you control; never expose the PIN."),
                new H("Scripts and dashboards"),
                new P("Make an **API key** (Settings → Remote) instead of sharing the PIN: read keys for dashboards (`/api/state?detail=1`), control keys for scripts (`/api/...`), MCP keys for AI agents."),
            }),

        new("trouble", "Reference", "Troubleshooting",
            "Quick fixes for the usual problems.",
            new HelpBlock[]
            {
                new Table(
                    ("A screen says NOT CONNECTED", "It hasn't heard from Stagewatch for 6 seconds (Wi-Fi, sleep, the PC). It reconnects by itself; meanwhile trust the operator's screen."),
                    ("\"Can't confirm cues\" on a crew page", "Open that page from its crew link (Showcaller page → Crew links). Links stop working after **New crew links**."),
                    ("\"Too many wrong viewing codes, window addresses or crew links\"", "That device gave five wrong ones within a minute (an old link or a mistyped code). It waits out the minute; the right code doesn't end the wait, and the PIN still works on it. A window page tries again by itself."),
                    ("Phones can't connect", "Same network? **Allow other devices** ticked (Settings → Remote)? Firewall allowed on **private** networks? Try the address shown in Settings → Remote."),
                    ("Companion shows disconnected", "Settings → Companion → Enabled; port 9923; for another PC, Bind on all interfaces and the firewall."),
                    ("Output on the wrong screen", "Settings → Output → Target monitor, then choose the output mode again."),
                    ("No NDI source", "Install the free NDI Runtime (NDI Tools) and press NDI again."),
                    ("Alerts play in the room", "Settings → Timer → Play alerts on: choose the laptop speaker or headphones."),
                    ("Slides not detected", "Settings → Integrations → PowerPoint on, and the slideshow running. No add-in is needed. On another computer: run Stagewatch Slides there (Help → Presenter PCs)."),
                    ("A TV shows the wrong page", "**Screens** (top right) → select it → choose a page or setup → **Send**. **Identify** shows which TV is which."),
                    ("The copilot says it's unavailable", "It needs Production, and Settings → Integrations → *Also ask Claude* with an API key. Offline? Everything else keeps working."),
                    ("LTC in says no signal", "Check the input in Settings → Integrations and its level in Windows Sound settings (not muted, not too quiet). Use a line input; a microphone input with enhancements can mangle timecode."),
                    ("A feature says it needs a licence", "Help → License shows your plan and what each one includes.")),
                new P("Settings → About has diagnostics you can copy into a support email."),
            }),
    };

    public static HelpTopic? Topic(string? id) => id is null ? null : Topics.FirstOrDefault(t => t.Id == id);

    /// <summary>Right-click help, keyed by <see cref="ContextHelp.TipProperty"/>.</summary>
    public static readonly IReadOnlyDictionary<string, HelpTip> Tips = new Dictionary<string, HelpTip>
    {
        // Top bar
        ["license"] = new("Licence", "Your plan. Help → License shows the plans, your hardware ID and installs a licence.", "licence"),
        ["open-show"] = new("Open show", "Load a saved event: presets, running order and quick messages (Ctrl+O).", "shows"),
        ["save-show"] = new("Save show", "Save this event's presets, running order and quick messages (Ctrl+S; Ctrl+Shift+S = save as).", "shows"),
        ["rundown-pick"] = new("Running orders", "A show can have several running orders (Day 1, Day 2, the rehearsal version). Choose one to make it live; ⋯ adds, duplicates, renames, edits (even while another is live) or deletes them.", "rundown"),
        ["recover"] = new("Recover time", "Win back (or give out) time across upcoming items — proportionally, evenly or buffers first, from the rest of the show, up to the next curfew or this group. Shows every change, the new end and the curfew margin before you apply; one Undo puts it back.", "recovery"),
        ["review-plan"] = new("Review the plan", "The copilot reads the running order like an experienced show caller the day before — using how much each kind of item and each speaker has overrun in your own past shows — and lists what will go wrong, with fixes you approve one by one.", "copilot"),
        ["explain"] = new("Explain a problem", "The copilot checks every link's health (PowerPoint, presenter PCs, vMix, LTC, the time server, cue lights, triggers…), refused commands and the show, then says what's wrong and exactly where to fix it.", "copilot"),
        ["suggest-message"] = new("Suggest messages", "Three short, kind messages that fit this moment (with the real minutes left), or a tightened version of what you've typed. Pick one to put it in the box — nothing is sent until you press Send.", "copilot"),
        ["undo"] = new("Undo / redo", "Reverses the last live edit — trims, delays, nudges, department notes, a recovery or an approved copilot plan (as one step). Ctrl+Z / Ctrl+Y.", "recovery"),
        ["rundown-end"] = new("Show ends", "When the show must end. With no start time the plan is back-timed from it (every planned time counts back from the end); either way it's the last item's curfew, so over/under, the forecast and Recover all aim at it.", "rundown"),
        ["rundown-roll"] = new("Roll", "The running order runs itself by its planned times: each item starts at its planned start; between items the timer counts down to the next. Pause, load or start anything yourself and Roll switches off.", "rundown"),
        ["rundown-group"] = new("Group", "A session or block of items: its planned length, and its target if one is set (red = over). Set groups in Running order → Edit (Group column, Group targets…).", "rundown"),
        ["aux-timer"] = new("Extra timer", "A named timer beside the show (VT, changeover...). Start, reset, count up or down, and show it on stage as a second line.", "aux"),
        ["copilot"] = new("Copilot", "Ask in plain English (Ctrl+K). The copilot answers or proposes Stagewatch commands; you tick what runs. AI agents on MCP propose here too. The badge counts proposals waiting for you.", "copilot"),
        ["report-summary"] = new("Draft the show summary", "The copilot writes the report's opening paragraph and lessons from the journal. You edit it; only what you keep goes in the report.", "copilot"),
        ["screens"] = new("Screens", "Every browser showing Stagewatch: name each one, make it flash its name (Identify), send it to another page or a saved setup, and save setups to open at /p/name.", "screens"),
        ["settings"] = new("Settings", "Outputs, timer colours and alerts, cues, remote access, Companion, integrations and triggers.", "layout"),
        ["help"] = new("Help", "How to use Stagewatch (F1), keyboard shortcuts and your licence.", "start"),

        // Timer
        ["mode"] = new("Clock mode", "Countdown from a duration, count up from zero, or show the time of day.", "timer"),
        ["title"] = new("Current title", "The loaded preset or running-order item.", "timer"),
        ["timer"] = new("Timer", "What the speaker sees, in the same colour. Mouse wheel: ±10 s (Shift ±1 min, Ctrl ±1 h).", "timer"),
        ["progress"] = new("Progress", "How much of the loaded time is left.", "timer"),
        ["end-time"] = new("End time", "When the timer reaches zero at the current pace.", "durations"),
        ["stretch"] = new("Elastic stretch", "End-at is stretching or squeezing time so the countdown lands on the end time.", "durations"),
        ["next-title"] = new("Next", "The next item in the running order.", "rundown"),
        ["schedule"] = new("Ahead / behind", "How far the show is from the running order's plan.", "rundown"),

        // Chips
        ["chip-cue"] = new("Cue label", "The speaker's current cue (\"5 MIN\", \"WRAP UP\") from the cue ladder.", "cues"),
        ["chip-finish"] = new("Finish by", "The clock time the speaker should finish — it moves when you nudge.", "cues"),
        ["chip-slides"] = new("Slides", "Slide n / N from PowerPoint and the next slide's title.", "wf-powerpoint"),
        ["chip-pace"] = new("Pace", "Ahead or behind the rehearsal (or the script), and the projected finish.", "pace"),
        ["chip-vt"] = new("VT countdown", "Time left on the clip feeding the VT: vMix's, or the clip on air on a playout link (a Patterns desk or a PlayMap node). Right-click: Play, Pause, Stop (back to the top), Restart, Last 10 s and Seek to… on that link — the lines a deck sends (playout <link> pause …). An entry is greyed with why when its press would be refused: Patterns' own words for a playlist item or a stinger's clip (its clock keeps running: end, restart or seek move it), a peer not connected or its kind not heard, no licence, or a PlayMap node armed that lets this PC only read (its desk can SHOW SET REMOTE-VIEW-ONLY OFF). Nothing for vMix: Stagewatch moves no vMix clip.", "integrations"),
        ["patterns-last10"] = new("Last 10 s", "Jumps the clip feeding the VT to its last 10 seconds, over its own playout link — its end, its out and whatever follows still play. Only shown when the link is connected and the clip counts down. Right-click it, or the VT chip, for the clip's other entries.", "integrations"),
        ["chip-curfew"] = new("Curfew", "The next hard-out and the margin. Red = at risk: see the assistant on the Producer tab.", "recovery"),
        ["chip-forecast"] = new("Forecast", "When the show is likely to end, learned from how your shows really run.", "producer"),
        ["chip-delay"] = new("Delay", "An announced delay before the next item. It ripples on until a gap absorbs it.", "recovery"),
        ["chip-ltc"] = new("LTC in", "Incoming timecode, and whether it's rolling. Right-click: make the timer follow it, or clear the end timecode.", "integrations"),
        ["chip-clock"] = new("Clock check", "This PC's clock is off compared with the time server. Fix Windows time before the show.", "integrations"),

        // Inputs
        ["duration"] = new("Duration", "Type MM:SS or HH:MM:SS and press Enter to load it (it doesn't start).", "durations"),
        ["end-at"] = new("End at", "Finish the countdown at a time of day, e.g. 14:30 or 2:30 pm. Set applies, Clear removes.", "durations"),
        ["pace"] = new("Real time", "On: count the real time left at normal speed. Off (elastic): stretch the loaded duration to land on the end time.", "durations"),
        ["nudge"] = new("Nudge", "Add or remove time live. The speaker's screen follows immediately.", "durations"),

        // Presets & running order
        ["preset"] = new("Preset", "Click = load, double-click = load and start. Keys 1–9, 0, −, = load presets 1–12.", "presets"),
        ["presets-edit"] = new("Edit all presets", "Names and durations for all 12 slots.", "presets"),
        ["rundown-row"] = new("Running-order item", "Click = load, double-click = load and start. 📌 fixed start, ⏹ curfew, ▶ n playout cues, ↪ auto-advance, ± = trimmed. An item with playout cues: right-click Cue playout now (each cue stands by — a PlayMap one shows its first frame on air, only on an idle playlist; a Patterns one moves that desk's own standby) or GO playout now (as when it starts), the running order left where it is.", "rundown"),
        ["rundown-start"] = new("Starts", "Planned start of item 1 (HH:MM). Ahead/behind is measured against it. Blank = the plan starts when an item first runs.", "rundown"),
        ["rundown-edit"] = new("Edit running order", "Add items, paste from a spreadsheet, import CSV; kinds, fixed starts, curfews, cue ladders and playout cues.", "rundown"),
        ["rundown-nav"] = new("Previous / next", "Step through the running order (P / N). ↺ goes back to the top.", "rundown"),

        // Cues & Q&A
        ["department"] = new("Department", "Standby, then GO. The department confirms on its own page (/dept?name=…). The line under its name shows where its cue is: STANDBY (sent), STANDING BY ✓ (seen), IN POSITION, GO.", "wf-crew"),
        ["show-network"] = new("Show network", "Another show app this Stagewatch can hear — Patterns, AGMessage, another Stagewatch — with its light (worst thing it knows about itself) and its one-line headline. Problems it reports are listed under it. All worked out from facts on the local network: no internet or AI needed. Grey = heard but no details yet, or not heard lately. The mark before its name says what proves what its row shows: ✓ its own node key (or one of this Stagewatch's playout links, to the address you set, while no key speaks for that app), △ the operator's PIN or control key, ○ nothing (only claimed, or only heard). A claim's problems reach the advice marked 'unverified:', never as urgent, and a claim never shows under an app something proves. The small line under it says how it reaches Stagewatch: following the timer over /ws, with which key and from which address, over one of this Stagewatch's playout links (named), or only posting its context. Show apps are never screens: screen commands never reach them. A node key speaks for one app: the first that uses it, or the one you choose. When a second app uses it, that app's context and proposals are refused, its row says why, and 'Make a key for…' (here or on right-click) gives it a key of its own to paste in. 'Let a key speak for this node…' moves a key you already have onto it (a reinstalled app has a new id): the node that key spoke for before is freed, and so is any key that spoke for this one; the choice is saved and journalled, and links already open hear it at once. Right-click: Copy node id, Make a key for…, Let a key speak for this node… (as Let it speak for… in Settings → Remote), Revoke its key… (as Revoke there). Amber with 'two machines say they are…': two running apps send one id — a copied install keeps the id it was copied with. Their hello is listed nowhere in this Stagewatch's context while the mark stands; a context that id posts, or a link its key proves, still shows (amber here, and to the AI), and the advice says so. The mark clears a minute after one of them stops, so give one of them a new id. A PlayMap node a playout link reaches over TLS: the link pins its certificate first (kept to compare, not trust — nothing proves it yet), and its row offers 'Trust its certificate …' beside the short form: compare that with the node's own screen, then press it — the press trusts the certificate it showed, or nothing; from then on a changed certificate is refused. Pinned first or by you, the link is never dialled plain: it dials the TLS door the node's hello names, else the one its certificate last answered at. CERTIFICATE CHANGED (red): nothing is sent to the node until you compare the new one on its own screen and type at least 8 hex of it into the box this row's button (or right-click, or the status line's right-click) opens — it is never filled in for you, and no deck, trigger, remote line, proposal or assistant can trust a certificate. Amber with 'answers as node …': the link's address answers as another node than the one it is set to, and nothing is sent to it.", "layout"),
        ["show-hello"] = new("Show network hello", "Whether other show apps can find this Stagewatch: every 2 seconds it says hello on UDP 9928, naming only the doors other machines can open. 'Said on the network' — the broadcast went. 'Said on this PC only' — no network, so only apps on this PC can hear it. 'Not said' with a reason — every send failed; after three tries in a row its context says so amber, so other apps and their AI know why they can't find it. 'Remote is set to this PC' — it says no hello at all, as Show Bus asks of a node no other machine can reach; apps on this PC link to it at 127.0.0.1 and the port. Amber with 'another machine says it is this Stagewatch' — a copy of this install runs elsewhere with the same id: other apps can't tell the two apart, so its context says so too; give one of them a new id. The line under it counts the apps heard, red ones, refused keys and ids two machines say. Right-click: Copy, Remote settings… (Allow other devices).", "layout"),
        ["panel"] = new("Panel", "Drag a tab along the strip to reorder the panels. Drag it out of the window to give it a window of its own — beside the timer or on another monitor — and drag that window back onto the side panel (or press Dock) to put it back. With every panel out, the timer fills the main window. Your layout is remembered.", "layout"),
        ["panel-window"] = new("Panel window", "A panel in its own window. Drag it onto the main window's side panel to dock it, or press Dock (closing it docks it too). Shortcuts work here as in the main window.", "layout"),
        ["panel-splitter"] = new("Panel width", "Drag to widen or narrow the side panels; double-click for the standard width.", "layout"),
        ["all-go"] = new("ALL GO", "Fires GO to every department standing by, at the same moment — for a cue that spans departments (lights, sound and video together). Stand each one by with its cue first, check they're in position, then ALL GO. Each department still gets its own GO to confirm. Shortcut Ctrl+G; Companion, OSC and the producer page send \"signal go all\".", "wf-crew"),
        ["call-stage"] = new("Call to stage", "Tells the green room (/backstage) to bring the next speaker.", "crew"),
        ["buzz"] = new("Pocket buzz", "A silent cue to the speaker's phone or watch (/haptic?for=speaker).", "crew"),
        ["signal"] = new("Sent cue", "Amber = waiting, green = acknowledged, red = nobody answered — radio them.", "crew"),
        ["question"] = new("Audience question", "✓ approve, ★ ask first, ✕ dismiss. Next question takes the next one live.", "qa"),
        ["qa-next"] = new("Next question", "Marks the current question answered and puts the next approved one live.", "qa"),

        // Producer
        ["advice"] = new("Assistant suggestion", "A proposal only — nothing changes until you click its button.", "producer"),
        ["catch-up"] = new("Catch up", "Re-time the live item (elastic) so it lands on its planned end.", "recovery"),
        ["delay"] = new("Delay", "Announce a delay before the next item; it ripples on until a gap absorbs it.", "recovery"),
        ["ladder"] = new("Cue ladder", "The graduated signals the speaker sees. Items in the running order can choose their own.", "cues"),
        ["speaker-clock"] = new("Speaker clock", "Standard MM:SS, Calm (\"12 MIN\"), or progress bar and cue only.", "cues"),
        ["preview"] = new("Briefing preview", "Plays the cue ladder at 10× on every output, so the speaker knows each signal.", "cues"),
        ["rehearse"] = new("Record rehearsal", "Times every slide of a run-through; the live talk is compared against it.", "wf-powerpoint"),
        ["listen"] = new("Listen to script", "Follows the speaker through their notes or script with offline speech recognition.", "wf-speech"),
        ["report"] = new("Show report", "Planned vs actual, cues and acknowledgements — print to PDF for the client.", "producer"),
        ["report-csv"] = new("Export CSV", "The report as a spreadsheet.", "producer"),
        ["rooms"] = new("Rooms", "Other Stagewatch PCs on the network. Open /rooms for all of them on one screen.", "integrations"),

        // Transport & output
        ["start"] = new("Start", "Start or resume the timer (Space toggles).", "timer"),
        ["stop"] = new("Stop", "Pause the timer. Overtime pauses too.", "timer"),
        ["reset"] = new("Reset", "Back to the loaded duration (R).", "timer"),
        ["next"] = new("Next", "Load the next running-order item (N); it starts if the show is live.", "rundown"),
        ["blackout"] = new("Blackout", "Hide everything on every output (B). Press again to restore.", "timer"),
        ["output-mode"] = new("Output", "Full screen, keyed full screen (for chroma key), floating mini, or closed.", "wf-screens"),
        ["key-colour"] = new("Key colour", "Background colour for keyed output, to match the vision mixer's chroma key.", "outputs"),
        ["ndi"] = new("NDI", "Send the output as an NDI source (needs the free NDI Runtime).", "outputs"),

        // Message
        ["message"] = new("Message", "Text for the speaker. Placeholders like {FINISH_BY} and {REMAINING} stay up to date.", "messages"),
        ["urgency"] = new("Urgency", "Info, Warning or Critical — sets the message colour.", "messages"),
        ["flash"] = new("Flash", "Make the message flash slowly or fast to catch the speaker's eye.", "messages"),
        ["send"] = new("Send", "Put the message on every output.", "messages"),
        ["clear-message"] = new("Clear", "Remove the message.", "messages"),
        ["ack"] = new("Ask to acknowledge", "The speaker taps Got it; you see when they've read it (Production).", "messages"),
        ["ack-state"] = new("Acknowledgement", "Whether the speaker has seen or acknowledged the message.", "messages"),
        ["quick"] = new("Quick message", "Send this saved message in one click.", "messages"),

        // Holographic display
        ["holo"] = new("Holographic 3D", "Switch the output and browser screens to the floating 3D look (three.js). Falls back to the standard display by itself if the graphics can't keep up.", "holographic"),
        ["holo-setting"] = new("Holographic 3D", "The time floats above a projector with glow and scanlines. Needs a graphics card with WebGL; NDI keeps the standard look.", "holographic"),

        // Holding images
        ["holding-item"] = new("Holding image", "Click to show it full screen on every output and browser screen; click again to take it down.", "holding"),
        ["holding-add"] = new("Add images", "PNG, JPG, GIF, BMP, TIFF or WebP, up to 25 MB. You can also drop files on the Holding tab.", "holding"),
        ["holding-fill"] = new("Fill the screen", "Off: fit the whole image (black bars). On: fill the screen, cropping the edges.", "holding"),
        ["holding-clear"] = new("Holding off", "Take the holding image down; the timer comes back. H toggles the last one.", "holding"),
        ["chip-holding"] = new("Holding image on air", "Every screen is showing this image instead of the timer.", "holding"),

        // Settings
        ["trigger-grid"] = new("Triggers", "One rule per row: When (event) + Filter → Do (OSC, HTTP, Companion, command) to Target with Payload, and an HTTP rule's Secret. Click the last row to add; Delete removes the selected row.", "triggers"),
        ["trigger-secret"] = new("Trigger secret", "An HTTP trigger's secret: a PlayMap token, a Patterns token, a Stagewatch PIN or key, or a bearer token — kept encrypted for this Windows user, sent in its header only to this PC or over https:// to a certificate you trusted (the first time nothing is sent: trust it from the status line's right-click after comparing it on the target's own screen). With a secret, the target's address is fixed: placeholders only in its path or query. Save refuses a rule that would send it elsewhere, and offers to move a ?pin= out of an address.", "triggers"),
        ["playout-links"] = new("Playout links", "Patterns desks and PlayMap nodes this Stagewatch drives, up to 8: each by a name (playout stage-left go …), its clip on air feeding the VT countdown when set, and cued by running-order items. Find listens for them; Add or Edit sets a link's peer, address and port, pairing token (kept encrypted; proved, never sent in clear off this PC), node id, transport (auto: TLS where a PlayMap node offers it, never plain once its certificate is pinned), and whether its clip feeds the VT or moves the running order at its end. Renaming a link moves its running-order cues with it. Trust its certificate… is the same press as the node's Show network row. Right-click: Find, Add, Edit, Remove, Trust its certificate….", "integrations"),
        ["may-drive-playout"] = new("May drive playout", "The machines whose playout and patterns lines this Stagewatch takes over OSC and Companion TCP (which carry no PIN), and over HTTP, a WebSocket or MCP while no PIN is set. One address a line, written out; nothing here is widened to what you did not write, and Save refuses an entry it can't read. OSC's sender can be forged: list only machines on a show network you trust.", "remote"),

        // Status line
        ["status"] = new("Status", "What just happened, or what needs your attention. Right-click: Copy, and every playout link's certificate that waits for you — 'Trust …'s certificate … (compared on its screen)' after you compared its short form with the node's own screen, or 'Trust …'s new certificate … (type it)…' after CERTIFICATE CHANGED, which opens an empty box for at least 8 hex of it — the same entries as the node's Show network row, for a link whose node has no row.", "layout"),
        ["remote-line"] = new("Remote", "Where phones and tablets can reach this Stagewatch.", "wf-showcaller"),
        ["clock"] = new("Time of day", "This PC's clock.", "timer"),
        ["timer-state"] = new("Timer state", "Running, paused, expired or overtime.", "timer"),
    };

    public static HelpTip? Tip(string? key) => key is not null && Tips.TryGetValue(key, out var t) ? t : null;
}
