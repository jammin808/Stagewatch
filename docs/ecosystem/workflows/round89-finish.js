export const meta = {
  name: 'round89-finish',
  description: 'Finish Patterns Round 89: 89.6 twin secrets home, 89.8 OSC carry-over ends (ADR), 89.9 papers and tag row 88',
  phases: [{ title: 'Patterns', detail: '89.6, 89.8, 89.9 in /home/user/Patterns' }],
}
const S = '/home/user/Stagewatch/docs/ecosystem'
const COMMON = `You are implementing one unit of a cross-repo plan (Show Bus / PeerLink interop). Authoritative plan: ${S}/master-plan.md (find ### <id>); longer text in its source designs ${S}/<area>.md, ${S}/peerlink.md. The owner's answers are in ${S}/owner-answers.md (P-twin-secrets YES, P-OSC12h YES, and the commit identity rule at its end). Repo /home/user/Patterns, branch the working branch; Round 89.0-89.5 and 89.7 are committed and pushed (git log). Keep Patterns/CLAUDE.md in full: its round process, the doctrine, the analyzers as fences, one dotnet build at a time in this tree, never skip/disable/quarantine a test, no model or tool identifiers, comments state the invariant. Never push. Commit with --author="Patterns <b.rohleder@gmail.com>" and the repo's configured committer (never -c user.name/user.email); subject "Round 89.<k>: <what landed>" with the reasoning in the body; no trailer lines.`
const IMPL = { type: 'object', properties: { unit: { type: 'string' }, done: { type: 'boolean' }, summary: { type: 'string' }, validation: { type: 'string' }, deviations: { type: 'string' }, blocked: { type: 'string' } }, required: ['unit', 'done', 'summary', 'validation', 'deviations', 'blocked'] }
const REVIEW = { type: 'object', properties: { verdict: { type: 'string' }, issues: { type: 'array', items: { type: 'object', properties: { severity: { type: 'string' }, issue: { type: 'string' }, fix: { type: 'string' } }, required: ['severity', 'issue', 'fix'] } } }, required: ['verdict', 'issues'] }
const COMMIT = { type: 'object', properties: { unit: { type: 'string' }, committed: { type: 'boolean' }, commit: { type: 'string' }, rejected: { type: 'array', items: { type: 'string' } }, validation: { type: 'string' }, notes: { type: 'string' } }, required: ['unit', 'committed', 'commit', 'rejected', 'validation', 'notes'] }
const UNITS = [
  { id: 'P89.6', extra: 'The twin: SendSecrets off by default, refused with words on a plain twin link to another machine; the twin listens on the desk\'s bind address; a standby without passwords or kept passes says so on both desks; a QUALIFICATION §22 walk row is named for the papers.' },
  { id: 'P89.8', extra: 'An ADR in docs/ADR.md: a proof grants nothing to an address; on a paired desk OSC comes only from OSC ALLOW and loopback; the Super Check words change; OscAppTests.' },
  { id: 'P89.9', extra: 'The round\'s papers exactly as Patterns/CLAUDE.md "How a round closes" asks: PLAN §107 completed (design, proof, what the round did not do), REVIEW round 89 (found, fixed, the items answered, "Walk: unwalked" with the walk rows the platform paths need: WebView2 resolution, the twin\'s bind address and takeover), CHANGELOG "## Round 89 — <date> — <words>" naming the evidence class (fenced by ChangelogTests), README where the product changed, REMOTE and COMPANION for the wire and module changes, QUALIFICATION rows, OPEN rows closed and added, and the tag table row for round 88 in .github/scripts/tag-rounds.sh (its last commit bcdbf2829cea302078acee7887841389e00d5178). Run ALL seven suites and the module tests before committing.' },
]
const out = []
for (const u of UNITS) {
  const impl = await agent(`${COMMON}\n\nImplement unit ${u.id}. ${u.extra}\nStart from a clean tree. Make every change the unit names, run its validate commands until green, leave the changes UNCOMMITTED. If blocked, stop and say why.`, { label: `impl:${u.id}`, phase: 'Patterns', schema: IMPL })
  if (!impl || !impl.done) { out.push({ id: u.id, impl }); log(`${u.id} not done`); break }
  const review = await agent(`${COMMON}\n\nReview the UNCOMMITTED changes for ${u.id} (git diff, git status) adversarially against its plan section, its design, Patterns/CLAUDE.md (doctrine: one air, every reader, fail closed on the wire, measured then governed; the papers' rules for 89.9). Run the validate commands. Do not edit.`, { label: `review:${u.id}`, phase: 'Patterns', schema: REVIEW })
  const commit = await agent(`${COMMON}\n\nFinish ${u.id}: fold in every valid review issue (reject wrong ones with a reason), validate until green, commit once. Review:\n${JSON.stringify(review)}\nImplementer:\n${JSON.stringify(impl)}`, { label: `commit:${u.id}`, phase: 'Patterns', schema: COMMIT })
  out.push({ id: u.id, impl, review, commit })
  if (!commit || !commit.committed) { log(`${u.id} not committed`); break }
  log(`${u.id} committed ${commit.commit}`)
}
return out
