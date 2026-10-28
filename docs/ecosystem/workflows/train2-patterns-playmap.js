export const meta = {
  name: 'train2-patterns-playmap',
  description: 'Train 2 in Patterns (Round 90 bench host, Round 91 Patterns side of 1.3) and PlayMap (PM-5 node link, PM-4 if 1.3 is final): implement, review, fix and commit each unit',
  phases: [
    { title: 'Patterns', detail: 'P90.0-P90.2, P91.0-P91.6 in /home/user/Patterns' },
    { title: 'PlayMap', detail: 'PM-5, then PM-4 if Show Bus 1.3 is final, in /home/user/Mapper' },
  ],
}
const S = '/home/user/Stagewatch/docs/ecosystem'
const PLAN = `Authoritative plan: ${S}/master-plan.md (find ### <id>; Train 2's overview sits above SW19.1a; rules R1-R3 near the top: R3 says a unit waiting on an answer or another repo's unlanded unit moves to the next round/train and the papers say which moved and why). Longer text: its source designs ${S}/<area>.md (ecosystem-lane for the bench host P-B1/P-B2; peers-not-screens PT-1/PT-2/PM-2; spec-1-3; cross-app-proposals; patterns-playmap-side; windows-for-peers), the PeerLink profile ${S}/peerlink.md, the Eye ${S}/eye.md. ${S}/plan.md is an older draft (longer text only). Owner answers: ${S}/owner-answers.md (all recommended: P-legacy = labelled amber fallbacks for OTHER apps' older versions only, each an OPEN row, removed at 1.4's default flip; P-ADR016 = scope A; M-PMX yes with CARRYOVER entries and spine lines through round 3 §5.7; the commit identity rule at its end). Stagewatch at /home/user/Stagewatch is READ-ONLY for you (another chain is its only writer: never build, test, edit or git-write there; read its code and docs/SHOWBUS.md freely, and git -C /home/user/Stagewatch log to see what has landed).`
const PCOMMON = `You are implementing one unit of a cross-repo plan (Show Bus 1.3 and the PeerLink profile between Stagewatch, Patterns, PlayMap and AGMessage). ${PLAN}
Repo /home/user/Patterns, branch the working branch; Round 89 is committed and pushed (its last commit c6236d6). Keep Patterns/CLAUDE.md in full: how a round opens and closes, the doctrine (attempts are not facts, one air, every reader, fail closed on the wire, measured then governed), the analyzers as fences, one dotnet build at a time in this tree (this chain is its only writer), never skip/disable/quarantine a test, no model or tool identifiers, comments state the invariant, STATE's new fields at the end of their row, loopback tests set ControlService.TrustLoopback = false and restore it in finally. Today is 2026-10-26. Never push. Commit with --author="Patterns <b.rohleder@gmail.com>" and the repo's configured committer (never -c user.name/user.email); subject "Round <NN>.<k>: <what landed>" keeping the plan's unit number (P90.1 -> Round 90.1), the reasoning in the body; no trailer lines.`
const MCOMMON = `You are implementing one unit of a cross-repo plan (Show Bus 1.3 and the PeerLink profile between Stagewatch, Patterns, PlayMap and AGMessage). ${PLAN}
Repo /home/user/Mapper (PlayMap), branch the working branch; PM-1..PM-3 are committed and pushed. Read Mapper/CLAUDE.md and keep every rule (round 3 ownership: only the files the batch names plus spine lines through the §5.7 amendment lane; the GL line; threads and deadlines; the desk's lights; analyzers as fences; one dotnet build at a time in this tree, this chain is its only writer; never skip/disable/quarantine a test; comments state the invariant; no model or tool identifiers). Each unit is a named round-2 fix batch with a docs/round2/CARRYOVER.md entry in the existing "Integrator notes from the round-2 fix batch (<name>)" shape. Never push. Commit with the repo's default identity and subject style ("Area: what landed"), the body naming the batch, the gaps closed and the tests; no trailer lines.`
const GATE = `This unit needs Stagewatch's SW19.12 (Show Bus 1.3 final). FIRST run: git -C /home/user/Stagewatch log --oneline | grep -F 'SW19.12'. If there is no such commit, change nothing and return done=false with blocked="moved per R3: SW19.12 has not landed". Only if it has landed, implement the unit.`
const IMPL = { type: 'object', properties: { unit: { type: 'string' }, done: { type: 'boolean' }, summary: { type: 'string' }, validation: { type: 'string' }, deviations: { type: 'string' }, blocked: { type: 'string' } }, required: ['unit', 'done', 'summary', 'validation', 'deviations', 'blocked'] }
const REVIEW = { type: 'object', properties: { verdict: { type: 'string' }, issues: { type: 'array', items: { type: 'object', properties: { severity: { type: 'string' }, issue: { type: 'string' }, fix: { type: 'string' } }, required: ['severity', 'issue', 'fix'] } } }, required: ['verdict', 'issues'] }
const COMMIT = { type: 'object', properties: { unit: { type: 'string' }, committed: { type: 'boolean' }, commit: { type: 'string' }, rejected: { type: 'array', items: { type: 'string' } }, validation: { type: 'string' }, notes: { type: 'string' } }, required: ['unit', 'committed', 'commit', 'rejected', 'validation', 'notes'] }

const PUNITS = [
  { id: 'P90.0', extra: 'Open Round 90 exactly as Patterns/CLAUDE.md "How a round opens" asks: PLAN §108 first lines (what the files said: the newest docs/field/round-NN/; OPEN rows to annotate; the requester; the evidence class). Small unit.' },
  { id: 'P90.1', extra: 'The bench host (ecosystem-lane P-B1): the real desk headless off Windows so the interop lane can run Patterns beside Stagewatch, PlayMap and AGMessage on one Linux kernel. No libVLC or WebView2 off Windows; the exe\'s start and the lifecycle census unchanged. It prints a first stdout line the lane can read (addresses, ports, its Show Bus id) and never a secret on stdout.' },
  { id: 'P90.2', extra: 'Round 90\'s papers as Patterns/CLAUDE.md "How a round closes" asks (PLAN §108 completed, REVIEW round 90 "Walk: none asked", CHANGELOG "## Round 90 — 2026-10-26 — <words>" naming the evidence class, README names the bench, OPEN annotations and the synthetic-clips row, the tag table row for round 89 = c6236d6eb in .github/scripts/tag-rounds.sh (use the full hash from git rev-parse c6236d6)). Closing: no verb an output can show. Run ALL seven suites and the module tests before committing.' },
  { id: 'P91.0', extra: 'Open Round 91 (PLAN §109 first lines) as "How a round opens" asks. Small unit.' },
  { id: 'P91.1', extra: 'Read Stagewatch\'s /ws node-link verdict (its SW18.3: src/Timer.Remote/WebServer.cs, docs/NODES.md, docs/REMOTE.md) to match the bytes exactly. Every reader the plan lists (STATE bus.stagewatch key after stuck, the Eye, the Nodes card, the context, the assistant\'s facts, the module\'s stagewatch_key with lines.txt). The legacy HTTP-evidence branch only per P-legacy, labelled amber and an OPEN row.' },
  { id: 'P91.2', extra: 'ADR-016 (scope A) in docs/ADR.md in this commit, before the code. The Companion module and every test helper pair first; the refused unpaired read has its log line and a Super Check Remote row (that is the round\'s closing row).' },
  { id: 'P91.3', extra: 'The context lists what peers may propose; APPLY <id> at review runs only this desk\'s own proposal; the reader keeps 256 commands (verbs and words up to 512 characters); the brief lists 32. Match Stagewatch\'s proposal and context shape (its ShowContext, docs/SHOWBUS.md §6/§11).' },
  { id: 'P91.4', extra: 'Certificates 825 days in all with one renewal of an over-long one; the hello 1.3 (proto 3, inst, every address per id, sticky dial, conflicts amber) matching Stagewatch\'s SW19.3 if it has landed, else docs/SHOWBUS.md and the spec-1-3 design; the hello table capped at 128 with the oldest unneeded app evicted and the drops counted; a forged-hello flood test.' },
  { id: 'P91.5', gate: true, extra: 'The Show Bus 1.3 copy (docs/SHOWBUS.md byte-identical with Stagewatch\'s master) and tools/showbus/peerlink-peer.mjs byte-identical; ShowBusSpecTests hold both ```showbus and ```peerlink blocks and both hashes to the numbers pinned in the master\'s §14; no validate calls into another tree.' },
  { id: 'P91.6', extra: 'Round 91\'s papers as "How a round closes" asks (PLAN §109 completed with what the round did not do, REVIEW round 91 "Walk: none asked", CHANGELOG, README, REMOTE, COMPANION with the module\'s minor version for stagewatch_key, OPEN L59 closed and the P-legacy rows, a QUALIFICATION §26 note, the tag table row for round 90 = the last Round 90 commit). Every unit of 91.1-91.5 that is not committed (git log) moved per rule R3: say which and why, and that it takes Round 92\'s number. Closing: the refused unpaired read beside its log line. Run ALL seven suites and the module tests before committing.' },
]
const MUNITS = [
  { id: 'PM-5', extra: 'Batch fix-showbus-node-link. Read Stagewatch\'s /ws node-link verdict (src/Timer.Remote/WebServer.cs, docs/NODES.md) for the exact bytes. The ContextBuilder and Seams lines go through the §5.7 spine lane as PM-3 did with VerbCatalog.' },
  { id: 'PM-4', gate: true, extra: 'Batch fix-showbus-1.3.' },
]

async function chain(units, common, phase, label) {
  const out = []
  for (const u of units) {
    const gate = u.gate ? `\n${GATE}` : ''
    const impl = await agent(`${common}\n\nImplement unit ${u.id}. ${u.extra}${gate}\nStart from a clean tree (git status). Make every change the unit names (code, tests, fixtures, every reader it lists, the papers it names), run its validate commands from the plan until green, leave the changes UNCOMMITTED. If blocked (an answer or another repo's unit not landed, a seam you may not touch), change nothing you cannot finish, say exactly why, and set done=false.`, { label: `impl:${u.id}`, phase, schema: IMPL })
    if (!impl || !impl.done) { out.push({ id: u.id, impl }); log(`${label} ${u.id} not done: ${impl ? impl.blocked : 'agent failed'}`); continue }
    const review = await agent(`${common}\n\nReview the UNCOMMITTED changes for ${u.id} adversarially (git diff; git status for new files) against its plan section, its design, the PeerLink profile where relevant and the repo's CLAUDE.md. Look for: missing promised behaviour or readers; regressions; security (a key, token, PIN or window address in clear, in a log, STATE, the journal or stdout; facts taken before pairing; one node acting as another; fail-open on an unknown word); thread and lane rules; unbounded growth; wire bytes that disagree with Stagewatch's real code; tests that pass without the fix; files outside the unit's leave. Run the validate commands. Do not edit.`, { label: `review:${u.id}`, phase, schema: REVIEW })
    const commit = await agent(`${common}\n\nFinish ${u.id}: fold in every valid review issue (reject wrong ones with a reason), validate until green, commit once. Never push.\nReview:\n${JSON.stringify(review)}\nImplementer:\n${JSON.stringify(impl)}`, { label: `commit:${u.id}`, phase, schema: COMMIT })
    out.push({ id: u.id, impl, review, commit })
    if (!commit || !commit.committed) { log(`${label} ${u.id} not committed`); break }
    log(`${label} ${u.id} committed ${commit.commit}`)
  }
  return out
}

const [patterns, playmap] = await parallel([
  () => chain(PUNITS, PCOMMON, 'Patterns', 'Patterns'),
  () => chain(MUNITS, MCOMMON, 'PlayMap', 'PlayMap'),
])
return { patterns, playmap }
