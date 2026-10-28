export const meta = {
  name: 'train1-playmap',
  description: 'Implement Train 1 for PlayMap (PM-1 identify, PM-2 keys never in clear, PM-3 page imports refused) as named round-2 fix batches: implement, review, fix and commit',
  phases: [{ title: 'PlayMap', detail: 'PM-1, PM-2, PM-3 in /home/user/Mapper, one at a time' }],
}

const S = '/home/user/Stagewatch/docs/ecosystem'
const COMMON = `You are implementing one unit of a cross-repo plan that makes Stagewatch interact with Patterns, PlayMap and AGMessage over the Show Bus. The authoritative plan is ${S}/master-plan.md: find your unit's section (### <id>) and follow it. Longer text is in its source design ${S}/<area>.md (peers-not-screens, patterns-playmap-side, windows-for-peers), the PeerLink profile ${S}/peerlink.md. The owner's answers are in ${S}/owner-answers.md: M-PMX is YES (PM-1 to PM-8 land now as named round-2 fix batches, each with a docs/round2/CARRYOVER.md entry in the existing "Integrator notes from the round-2 fix batch (<name>)" shape; spine-file lines only through round 3 §5.7's amendment lane), M-content-before-W3 is YES only if wave 3 has not started (decide from docs/round3/PLAN.md and the git history which waves have merged; if W3 has started, PM-3 is not done now: stop and say so).
Repo: /home/user/Mapper (PlayMap). Read Mapper/CLAUDE.md first and keep every rule (the GL line, threads and deadlines, the desk's lights, native libraries, analyzers as fences, one dotnet build at a time in this tree, never skip/disable/quarantine a test, comments state the invariant, no model or tool identifiers). Work only in this repo; never push; never rewrite history. Commit with the repo's default identity (recent history is authored that way) and its subject style ("Area: what landed", e.g. "Bus: …", "Engine: …"), the body naming the batch (fix-showbus-identify, fix-showbus-keys, fix-media-page-import), the gaps closed and the tests; no Co-Authored-By or session trailer lines.`

const IMPL = { type: 'object', properties: { unit: { type: 'string' }, done: { type: 'boolean' }, summary: { type: 'string' }, files: { type: 'array', items: { type: 'string' } }, validation: { type: 'string' }, deviations: { type: 'string' }, blocked: { type: 'string' } }, required: ['unit', 'done', 'summary', 'files', 'validation', 'deviations', 'blocked'] }
const REVIEW = { type: 'object', properties: { verdict: { type: 'string' }, issues: { type: 'array', items: { type: 'object', properties: { severity: { type: 'string' }, file: { type: 'string' }, issue: { type: 'string' }, fix: { type: 'string' } }, required: ['severity', 'issue', 'fix'] } } }, required: ['verdict', 'issues'] }
const COMMIT = { type: 'object', properties: { unit: { type: 'string' }, committed: { type: 'boolean' }, commit: { type: 'string' }, fixed: { type: 'array', items: { type: 'string' } }, rejected: { type: 'array', items: { type: 'string' } }, validation: { type: 'string' }, notes: { type: 'string' } }, required: ['unit', 'committed', 'commit', 'fixed', 'rejected', 'validation', 'notes'] }

const out = []
for (const id of ['PM-1', 'PM-2', 'PM-3']) {
  const impl = await agent(`${COMMON}\n\nImplement unit ${id}. Start from a clean tree (git status). Make every change the unit names (code, tests, the CARRYOVER entry), run its validate commands until green (GL tests need Xvfb: tools/setup-dev.sh start prints the variables; run only what the unit's validate names plus anything your change touches), and leave the changes UNCOMMITTED for review. If the unit is blocked (W3 started for PM-3, a missing seam that needs the §5.7 lane you cannot use), stop and say exactly why.`,
    { label: `impl:${id}`, phase: 'PlayMap', schema: IMPL })
  if (!impl || !impl.done) { out.push({ id, impl, stopped: true }); log(`${id} not done: ${impl ? impl.blocked : 'agent failed'}`); continue }
  const review = await agent(`${COMMON}\n\nReview the UNCOMMITTED changes for ${id} adversarially (git diff; git status for new files) against its plan section, its design, Mapper/CLAUDE.md and round 3's ownership rules (docs/round3/PLAN.md §4, §5.7). Look for missing promised behaviour, regressions, security holes (a key or token in clear, in a log, STATE or the journal; facts taken before pairing), thread and deadline rule breaks, tests that pass without the fix, files outside the batch's named list. Run the validate commands. Do not edit.`,
    { label: `review:${id}`, phase: 'PlayMap', schema: REVIEW })
  const commit = await agent(`${COMMON}\n\nFinish ${id}: fold in every valid review issue (reject wrong ones with a reason), run the validate commands until green, and commit as ONE commit with the conventions above. Never push.\nReview:\n${JSON.stringify(review)}\nImplementer:\n${JSON.stringify(impl)}`,
    { label: `commit:${id}`, phase: 'PlayMap', schema: COMMIT })
  out.push({ id, impl, review, commit })
  if (commit && commit.committed) log(`${id} committed ${commit.commit}`)
  else { log(`${id} not committed`); break }
}
return out
