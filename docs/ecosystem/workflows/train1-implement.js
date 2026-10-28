export const meta = {
  name: 'train1-implement',
  description: 'Implement Train 1 (Stagewatch 2.18 SW18.1-18.7; Patterns Round 89.0-89.5, 89.7): implement, adversarial review, fix and commit, unit by unit',
  phases: [
    { title: 'Stagewatch', detail: 'SW18.1-SW18.7, one unit at a time in /home/user/Stagewatch' },
    { title: 'Patterns', detail: 'Round 89.0-89.5 and 89.7, one unit at a time in /home/user/Patterns' },
  ],
}

const S = '/home/user/Stagewatch/docs/ecosystem'
const COMMON = `You are implementing one unit of a cross-repo plan that makes Stagewatch interact with Patterns, PlayMap and AGMessage over the Show Bus. The authoritative plan is ${S}/master-plan.md: find your unit's section (### <id>) and follow it. Longer text for each unit is in the source design it names: ${S}/<area>.md (peers-not-screens, playout-links, cross-app-proposals, windows-for-peers, spec-1-3, agmessage, patterns-playmap-side, ecosystem-lane), the PeerLink profile ${S}/peerlink.md and the Eye ${S}/eye.md. ${S}/plan.md is an earlier draft whose Patterns numbering differs: use it only for longer text. Owner answers not yet given: take the plan's recommended answer, and say so in your result.
Rules: work only in the repo named; never push; never touch another repo; never rewrite history; one dotnet build at a time in a tree (this chain is the only writer in it); never skip, disable or quarantine a test; no model or tool identifiers in anything you write; comments state the invariant; culture-invariant parsing and formatting; the repo's BannedSymbols are build failures. Read the repo's agent brief first (CLAUDE.md at the repo root). WPF/Avalonia UI that cannot run here is compiled, never claimed as seen.`

const REPOS = {
  Stagewatch: {
    path: '/home/user/Stagewatch',
    rules: 'Stagewatch: brief Stagewatch/CLAUDE.md. Validate with tools/test.sh lanes (build, unit, module, e2e as the unit names). Commit author: -c user.name=jammin808 -c user.email=b.rohleder@gmail.com. Subject: "2.18 <unit id>: <what landed>", body with the reasoning, the gaps closed, the tests and their counts. Papers that belong to SW18.8 are not written in these units.',
    units: ['SW18.1', 'SW18.2', 'SW18.3', 'SW18.4', 'SW18.5', 'SW18.6', 'SW18.7'],
    phase: 'Stagewatch',
  },
  Patterns: {
    path: '/home/user/Patterns',
    rules: 'Patterns: brief Patterns/CLAUDE.md (its round process: how a round opens, one commit per unit, the doctrine, the analyzers as fences). Validate with the unit\'s validate commands (dotnet build Patterns.sln then the named suites with --no-build; the companion module\'s npm test and lines check where named). Commit author: -c user.name=Patterns -c user.email=b.rohleder@gmail.com. Subject: "Round 89.<k>: <what landed>", body with the reasoning. The round\'s papers commit is P89.9, not these units, but P89.0 writes PLAN §107\'s opening lines as Patterns/CLAUDE.md "How a round opens" asks.',
    units: ['P89.0', 'P89.1', 'P89.2', 'P89.3', 'P89.4', 'P89.5', 'P89.7'],
    phase: 'Patterns',
  },
}

const IMPL_SCHEMA = {
  type: 'object',
  properties: {
    unit: { type: 'string' }, done: { type: 'boolean' }, summary: { type: 'string' },
    files: { type: 'array', items: { type: 'string' } },
    validation: { type: 'string', description: 'the commands run and their outcome, with test counts' },
    deviations: { type: 'string', description: 'where and why the implementation departs from the plan, owner answers assumed' },
    blocked: { type: 'string' },
  },
  required: ['unit', 'done', 'summary', 'files', 'validation', 'deviations', 'blocked'],
}
const REVIEW_SCHEMA = {
  type: 'object',
  properties: {
    verdict: { type: 'string', description: 'ship | fix' },
    issues: { type: 'array', items: { type: 'object', properties: { severity: { type: 'string' }, file: { type: 'string' }, issue: { type: 'string' }, fix: { type: 'string' } }, required: ['severity', 'issue', 'fix'] } },
  },
  required: ['verdict', 'issues'],
}
const COMMIT_SCHEMA = {
  type: 'object',
  properties: {
    unit: { type: 'string' }, committed: { type: 'boolean' }, commit: { type: 'string' },
    fixed: { type: 'array', items: { type: 'string' } }, rejected: { type: 'array', items: { type: 'string' } },
    validation: { type: 'string' }, notes: { type: 'string' },
  },
  required: ['unit', 'committed', 'commit', 'fixed', 'rejected', 'validation', 'notes'],
}

async function runTrack(name) {
  const r = REPOS[name]
  const out = []
  for (const id of r.units) {
    const impl = await agent(`${COMMON}\n${r.rules}\n\nImplement unit ${id} in ${r.path}. Start from a clean tree (git status); the previous units of this train are already committed. Make every change the unit names (code, tests, the readers it lists), run its validate commands until green, and leave the changes UNCOMMITTED for review. If something truly blocks the unit (an owner decision with no recommended answer, a dependency missing), stop and say what.`,
      { label: `impl:${id}`, phase: r.phase, schema: IMPL_SCHEMA })
    if (!impl || !impl.done) { out.push({ id, impl, stopped: true }); log(`${id} not done: ${impl ? impl.blocked : 'agent failed'}`); break }
    const review = await agent(`${COMMON}\n${r.rules}\n\nReview the UNCOMMITTED changes in ${r.path} for unit ${id} (git diff, git status for new files) adversarially against the unit's section in master-plan.md, its source design, the repo's brief and doctrine. Look for: behaviour the unit promises that is missing; regressions in existing behaviour or tests; security holes (a secret in a log, frame, journal or STATE; a peer acting as another; fail-open paths); thread-safety and one-writer violations; unbounded growth; tests that would pass without the fix; readers left out. Run the validate commands yourself. Do not edit files.`,
      { label: `review:${id}`, phase: r.phase, schema: REVIEW_SCHEMA })
    const commit = await agent(`${COMMON}\n${r.rules}\n\nFinish unit ${id} in ${r.path}: fold in every valid review issue (check each against the code; reject wrong ones with a reason), run the validate commands until green, then commit the unit as ONE commit with the repo's conventions (author and subject above). Never push. Review:\n${JSON.stringify(review)}\nImplementer's notes:\n${JSON.stringify(impl)}`,
      { label: `commit:${id}`, phase: r.phase, schema: COMMIT_SCHEMA })
    out.push({ id, impl, review, commit })
    if (!commit || !commit.committed) { log(`${id} not committed`); break }
    log(`${id} committed ${commit.commit}`)
  }
  return out
}

const [sw, pt] = await parallel([() => runTrack('Stagewatch'), () => runTrack('Patterns')])
return { stagewatch: sw, patterns: pt }
