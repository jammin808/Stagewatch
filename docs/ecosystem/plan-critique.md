# Critique of the integrated master plan (134 units, 9 trains)

Design work only. These findings were checked against the trees on 2026-10-25. Each claim about existing behaviour cites file:line.

## Verdict

The plan's direction is right: Show Bus is the contract, PeerLink is layer 2, and the Eye reads link records. Every gap from G01 to G49 is in `gapMap`. The plan is not schedulable as written, for these reasons:

- Round 89 has a dependency cycle.
- Three safety fixes sit behind feature trains:
  - AGMessage's token sent in clear, which is AG-1 in Train 9;
  - twin secrets, which are P92.4 in Train 3;
  - Patterns' loopback-trusted web remote, which is P97.2 in Train 6 and covers 9697 only.
- The rule for slipped Mapper units does not cover the Stagewatch units and releases that depend on them.
- Several validate commands would fail, or pass without testing anything.

## High

1. **Cycle in Round 89.** P89.2 and P89.4 depend on P89.5. P89.5 depends on P89.3 (which depends on P89.2) and on P89.4. Separately, `owner:P-numbers` blocks only the papers units. Commit subjects carry `Round NN.k`, and pushed history is never rewritten. So the numbering answer has to come before the first commit of each round, not before its papers.
2. **AG-1 is a safety fix, and it is last.** AGMessage's branch has moved since the plan's baseline:
   - it is at `6146ee9`, 8 commits past the plan's `9d7f5c3`;
   - its newest commit is unit 20, "the papers for the last review round", pushed at 07:09;
   - `app/server.js` changed by +236/−60 lines, so `server.js:2012-2024` is now `:2130-2143`.

   Meanwhile `peerlinks.js:259-261,292` still sends the full-control token in plain text. AG-1 must leave Train 9, and the question about holding the 0.5.0 tag has to go now to the session that owns that branch.
3. **Twin secrets after Round 89.**
   - P89.3 makes a window's pass a kept secret that "crosses the twin only under SendSecrets".
   - SendSecrets defaults to on (`ShowState.cs:2080`), and the twin listens on `IPAddress.Any` (`TwinService.cs:488,512`).
   - The fix, P92.4, comes three rounds later.
4. **The release validate gets the release date wrong.**
   - Each release unit hand-sets "Version 2.1x.0" and then runs `release.ps1 -NoBump`.
   - `-NoBump` keeps the old `AppInfo.ReleaseDate` (`tools/release.ps1:83-88`), and that date gates Production licences.
   - The module and installer versions stay wherever the hand edit left them.
   - Use `release.ps1 -Version 2.1x.0 -ModuleVersion <m>` instead.
5. **The slip rule covers Mapper units only.**
   - These Stagewatch units depend on Mapper units that wait for an owner's answer: SW19.13 (on PM-5), SW20.13's and SW21.9's PlayMap rows, and SW-INT-PM.
   - Stagewatch's releases depend on those units: SW20.14 on SW20.13, and SW21.10 on SW21.9.
   - So a Mapper owner's "not yet" blocks Stagewatch 2.19 to 2.21.
6. **The Patterns module validate is incomplete.**
   - It runs `npm test` with no `npm ci`, and `integrations/companion-module-patterns` has no `node_modules`.
   - It skips two checks the CI job runs: "Lines are current" and `npm run check` (`build.yml:81-88`).

## Medium

7. **Filters that test nothing.** VSTest (xunit 2.9.3) exits 0 when a filter matches no test.
   - In Patterns, `ControlProtocol` on `Patterns.Core.Tests` matches nothing: the class is in `tests/Patterns.Rendering.Tests/RemotePresenterWarpTests.cs:9`. This affects P92.3, P97.2 and P99.1.
   - In Mapper, `NetworkHost`, `NodeCredential` and Render's `Timer` match no existing test class.
   - Units that append STATE fields run only name-filtered tests, although STATE is matched as raw substrings across the App suite.
8. **`tools/showbus-copies.sh` cannot pass for seven trains.**
   - SW19.12 runs it before any copy exists.
   - P91.5 calls Stagewatch's copy of the script by absolute path.
   - AGMessage's copy lands only in AG-4, in Train 9.
9. **SW19.1 writes words that owner answers decide, and it is too big.**
   - It writes the D0 wording and the §8 exemptions without depending on `S-D0` or `S-exemptions`.
   - It is also one unit for nine designs' words plus §12, §13, §14 and the Eye's §6. Split it.
10. **The critical path runs through the interop lane.** SW19.12 depends on SW19.11, which depends on SW19.9 (the namespaced LAN). Every unit of Train 3 therefore waits for the heaviest infrastructure unit.
11. **Mapper ownership.**
    - M-PMX's file list omits files the batches touch:
      - `Engine/Devices/NodeCredentialStore.cs` (PM-2);
      - `Core/Time/ClockOffsetEstimator.cs` (PM-6);
      - `Net/Bus/HelloService.cs` (PM-4);
      - the timer's renderer (PM-7).
    - `VerbCatalog.Redacted` (`VerbCatalog.cs:617-641`) is outside every block. That makes PM-3's edit a spine edit (`round3/PLAN.md:307-311`), not the "block-local" edit the plan says.
    - `docs/WIRE.md`, `README.md` and `docs/ROADMAP.md` are the integrator's, and packages only write papers notes (`round3/PLAN.md:2198`). PM-4 and PM-20 edit them directly.
12. **PM-13 can miss its window.** It has to land inside the integration pass (§5.6 item 7, `round3/PLAN.md:2135`), but it sits in Train 8 behind Train 7.
13. **Units that need work scheduled later.**
    - PM-11 publishes drawn windows, but PlayMap draws windows only from PM-15.
    - AG-8 and AG-10 reach PlayMap, but they need PM-9, and no unit makes PlayMap accept CONTEXT POST.
    - P89.1 replays bytes that SW18.6 writes, but does not depend on it.
    - P95.2 receives grants, but does not depend on P95.1, unless the D0 answer accepts key-verified node links.
14. **Patterns' web remote 9696 trusts loopback as paired.**
    - Loopback counts as paired (`ControlService.cs:977-978`; `TrustLoopback` is true at `:548`).
    - Non-cue verbs need no client header (`:1355`).
    - So a simple cross-origin POST from any page in the show laptop's browser can run BLACKOUT.
    - The Host/Origin rule arrives only in P97.2, and only for 9697.
15. **Process problems in Stagewatch's validates.**
    - SW19.10's validate runs `git push` and then `gh run watch` on the latest run, which can be the previous one.
    - SW19.11 edits Stagewatch's `CLAUDE.md`, although the plan itself says that file's lines are the owner's to write.
16. **SW19.7's validate fails.** `dotnet run --no-build` of `tools/BusCarrierProbe` fails, because `tools/test.sh build` builds only `Timer.slnf`, PageShot and the Rig.
17. **Mapper's fixture validates cannot fail.** `PLAYMAP_WRITE_FIXTURES=1` inside the validate rewrites the fixtures it is meant to check.
18. **AGMessage's process.**
    - AG-1's validate runs `git switch -c`, which is not idempotent and creates a branch nobody asked for.
    - AGMessage's brief requires the browser smoke and an `AGMessage X.Y.Z, unit N:` subject on every commit. Most AG validates omit the smoke.
19. **Patterns' process.**
    - Rounds 90–99 have no step that opens the round from `docs/field` and OPEN.
    - Round 90 gives the maintainer's list as its evidence class.
    - The plan keeps level-0 legacy paths, although the standing brief says "No legacy or previous version is considered".

## Units too big for one agent

SW19.1, SW19.2, SW19.9, SW20.4, SW22.1, P89.3, P92.2, P97.3, P98.1, P98.3, PM-9, PM-15, AG-5.

## Owner questions the plan does not ask

- **Stagewatch 2.17.** It is unreleased: 5 commits are not merged, and the csproj still says 2.16.0 (`Timer.App.csproj:18`).
- **Seven releases.** Seven releases means the maintainer's PC is needed seven times.
- **The interop CI job.**
  - It needs a `SIBLINGS_READ_TOKEN` to read the sibling repos.
  - It needs sudo for the namespaces.
- **Which branch each tree uses.**
- **Who owns the AGMessage branch.**
- **Shared node keys.** SW18.2 binds a key on first use, so a key shared by two apps today breaks on upgrade.
- **Stagewatch's CLAUDE.md line.**
- **Legacy paths in Patterns.** Should Patterns keep level-0 paths, against its own brief?
- **Fixes declined without asking.** G46, the 'Link all' fix in G49, and G19's picker are all declined.
- **The timing of P-numbers.** It must be answered before each round's first commit.
