# The cross-app plan of record: Show Bus 1.3 and PeerLink across Stagewatch, Patterns, PlayMap and AGMessage

This folder is the plan four repositories are built against, kept here so the work can resume from GitHub alone: the plan, the
owner's answers, the designs behind each unit, the structured data they were drawn from, and the templates every unit was
run with. Stagewatch holds it because Stagewatch holds the contract (`docs/SHOWBUS.md`, Show Bus 1.3 final); the other trees
carry byte-identical copies of the contract, not of this folder.

| File | What it is |
|---|---|
| `master-plan.md` | The plan of record: nine trains of units with their dependencies, rules R1–R7 near the top, each unit's `### <id>` section, and at the end the renumbering note (Patterns' rounds are the plan's P-number + 1 from Train 3 on) |
| `owner-answers.md` | Every owner answer, including each leave extension for PlayMap's fix batches and the commit identity rule |
| `owner-decisions.md`, `owner-questions.txt` | The questions as asked and the decisions as recorded |
| `<area>.md` | The source designs units cite (`spec-1-3`, `peerlink` (the §13 profile), `peerlink-*`, `eye`, `ecosystem-lane`, `cross-app-proposals`, `playout-links`, `windows-for-peers`, `peers-not-screens`, `patterns-playmap-side`, `agmessage`); `plan.md` is an older draft kept for its longer text; `plan-critique.md` the critique the plan answered |
| `agmessage-handoff.md` | What was handed to AGMessage's own session (its AG-1) |
| `data/` | `gaps.json` (G01–G49), `maps-lite.json`, `master-plan.json` (the units as data), `units/*.json` (each design's units) |
| `workflows/` | The scripts each train ran (implement → adversarial review → fold-in and one commit per unit). They name this folder by its path in a cloud checkout (`/home/user/Stagewatch/docs/ecosystem`); edit `S` if the trees live elsewhere |

## Where the work stands (2026-10-28)

Every repository's work is on the session's designated working branch (the same name in all five repositories), committed and
pushed; no working tree holds anything uncommitted. The owner has **paused all three active trees**; nothing resumes until the
owner says so, tree by tree.

### Stagewatch — paused after SW21.2 ("until I say continue")

- Landed: 2.17 (controlled windows), 2.18 (Train 1's harms), 2.19 (Show Bus 1.3 final, the PeerLink kit, hello 1.3, the
  interop lane `tools/test.sh interop` with CI's interop job), the brief's four lines, the temp-folder fence, 2.20 SW20.1–SW20.12
  (playout links on the PeerLink core), 2.21 SW21.1 (the verb table) and SW21.2 (the desk's rules), the last at `5490c89`.
- Next, in order: SW21.3 (DeskService, LineRisk/CommandRisk in Core), SW21.4 (release A grammar: `peer <app> <line>`,
  `desk approve|reject <id>` with receipts), SW21.5 (readers), SW21.7 (one report), SW21.8 (one preflight, "ready for doors"),
  SW21.6 (the desk, WPF — compiled here, walked on the PC), SW21.9 (interop desk rows), then SW21.10 (2.21 papers).
- Waiting on Patterns Round 94: SW20.13 (the VT interop rows S4.1–S4.4) and SW20.14 (2.20 papers). With the owner's S-releases
  answer, 2.20 and 2.21 may ship as one release with combined papers.
- Template: `workflows/train4-stagewatch.js` (drop its `.slice(…)` limit, which stopped it after SW21.2, and the units already
  committed — `git log` names each as `2.21 SW21.k: …`).
- Stagewatch's CI runs on main, tags and pull requests only, so nothing runs on the working branch.

### Patterns — paused before Round 94 ("wait for me to confirm resume")

- Landed and pushed: Rounds 89–93. Round 92 was a repair round (CI's red test job, L64; the twin key, L65; the 1.3 copy; the
  temp-folder fence); Round 93 is the plan's P92 (the PeerLink core, the Stagewatch link on it, its record in every reader,
  STAGEWATCH verbs, papers PLAN §111), closed at `b57410d`. The tag table's next row is round 93's: `b57410d`.
- Next: Round 94 = the plan's P93 (94.0 opening; 94.1 one speaker message — needs Stagewatch's SW20.10, landed; 94.2 playlist hold;
  94.3 `VIDEO … ON <key>`; 94.4 papers), then Round 95 = the plan's P94 (the Eye's NETWORK band), and so on, each one higher.
- Template: `workflows/train3-patterns.js` (its units carry both plan and round numbers; set the filter to the round to run).

### PlayMap (repo Mapper) — paused ("until I say continue")

- Landed and pushed, each a round-2 fix batch with its entry in `docs/round2/CARRYOVER.md`: PM-1 fix-showbus-identify, PM-2
  fix-showbus-keys, PM-3 fix-media-page-import, PM-5 fix-showbus-node-link (and its status-light arm), PM-4 fix-showbus-1.3,
  fix-proposal-sink-deadline, PM-6 fix-peerlink-core, PM-7 fix-stagewatch-timer-air, PM-8 fix-stagewatch-context-size,
  fix-desk-unconfirmed. Every leave extension is in `owner-answers.md` and in a "Papers: the owner extends …" commit.
- First on resume: read CI for the pushes after run 468 (PM-6 onward); a red job is ours to root-cause.
- Next units (none started): PM-11 (the context names its peers and timer; deps met); PM-9a–c and PM-10 (wp4-peerlink — the owner's
  answer is "first after round 3's integration pass": check `docs/round3/PLAN.md` first); PM-12…PM-20 per `master-plan.md`.
- Open items each batch left are in its CARRYOVER entry's Open list.
- Template: `workflows/train3-playmap.js` (several batches) or `workflows/playmap-desk-unconfirmed.js` (one batch).

### AGMessage

Untouched on the working branch. Its 0.5.0 lives on its own session's branch; AG-1 was handed over (`agmessage-handoff.md`);
Train 9 (AG-2…) waits for that session's work.

## Resuming in a fresh container

1. Clone the five repositories to `/home/user/<Repo>` and check out the working branch in each; `git log -1` should match the
   remote. Set each repository's committer to the session's identity (`git config user.name/user.email`), as
   `owner-answers.md`'s identity rule asks; authors stay per repository (Stagewatch `jammin808`, Patterns `Patterns`, Mapper the
   default).
2. Bench: `tools/setup-dev.sh` in Stagewatch (the .NET 10 SDK, Node, Chromium's headless shell) and in Mapper (Xvfb and Mesa,
   FFmpeg 9.0 in `/opt/ffmpeg9`, DXC in `/opt/dxc`); Patterns builds with the .NET 10 SDK alone.
3. Read each tree's `CLAUDE.md`, then this folder's `master-plan.md` for the next unit's section and `owner-answers.md`.
4. Run the tree's template (or one unit by hand) the way every unit so far ran: implement and validate with the tree's own lanes,
   an adversarial review that does not edit, then fold-in and **one** commit per unit; push after each landed unit; a file
   outside a PlayMap batch's leave is asked of the owner before the push.
5. Tests make temporary paths only through each tree's fence (Patterns `tests/Shared/TempRun.cs`, Stagewatch
   `tests/Stagewatch.Tests/Scratch.cs`); give a run its own TMPDIR under the tree's ignored output folder.

## The maintainer's PC (not doable from the cloud)

`release.ps1` for Stagewatch 2.18 and 2.19 (then 2.20 + 2.21 together); `smoke.ps1` walks of the WPF desk work (compiled only
here); LB-1's two Windows runs of the carrier probe; `SIBLINGS_READ_TOKEN` and the runner's userns sysctl for CI's interop
job; Patterns' unwalked QUALIFICATION rows.
