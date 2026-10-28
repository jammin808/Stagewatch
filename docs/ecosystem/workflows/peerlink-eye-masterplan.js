export const meta = {
  name: 'peerlink-eye-masterplan',
  description: 'Design the PeerLink profile (judge panel) and the Eye as the network view, then integrate every design into one ordered plan per repo',
  phases: [
    { title: 'PeerLink drafts', detail: 'three independent drafts from different angles' },
    { title: 'Judge', detail: 'two judges score the drafts' },
    { title: 'PeerLink synthesis', detail: 'one profile, the best of the drafts' },
    { title: 'Eye', detail: 'the Eye as the view of every app, node, device and window, with a critic' },
    { title: 'Master plan', detail: 'one ordered plan per repo, owner questions, a completeness critic, the final plan' },
  ],
}

const S = '<scratch>'
const CONTEXT = `Four repos sit side by side under /home/user: Stagewatch (C#/.NET 10 WPF stage timer; Show Bus master spec Stagewatch/docs/SHOWBUS.md v1.2; brief Stagewatch/CLAUDE.md), Patterns (C#/.NET 10 Avalonia show-content app with the Eye — the God's Eye view of the room; strict round process in Patterns/CLAUDE.md), Mapper (PlayMap, C#/.NET 10 render node; round-3 ownership rules in Mapper/CLAUDE.md), AGMessage (Node.js; its 0.5.0 is being built on another branch, AGMessage's 0.5.0 branch — read it with git show, e.g. git -C /home/user/AGMessage show AGMessage's 0.5.0 branch:app/lib/peerlinks.js; it has a PeerLink class to Patterns and PlayMap and a StagewatchLink).
The user asked Stagewatch to interact with AGMessage, PlayMap and Patterns using the Show Bus, then: "Remember that Patterns has the Eye View that knows about all connected apps, nodes, and devices. Update Patterns and the ShowBus/PeerLink with the best standards we are developing. Are ShowBus and PeerLink essentially the same thing? Are they both needed? Or could they be used for different things on separate communication layers?" The answer given: Show Bus is the shared contract (discovery hello, receipts, frames, context, keys, TLS, approvals); a PeerLink is one authenticated link between two apps carrying it. Layers: (1) discovery (hello, untrusted), (2) link = PeerLink (pairing by proof, TLS with pins, liveness beats incl. 'show stopped', clock offset, reconnect with jitter, bounds), (3) Show Bus messages over the link (commands+receipts, frames, context, approvals), (4) views (the Eye). Today there are eight link implementations across four apps, each doing layer 2 differently (Stagewatch PatternsLink, Patterns StagewatchLinkService, PlayMap StagewatchLink and PatternsLink, AGMessage StagewatchLink and PeerLink, plus the twin/node links inside Patterns).
Inputs: verified gaps ${S}/gaps.json; each side's map ${S}/maps-lite.json; eight revised area designs as markdown in ${S}/design/*.md (peers-not-screens, playout-links, cross-app-proposals, windows-for-peers, spec-1-3, agmessage, patterns-playmap-side, ecosystem-lane) and as JSON in ${S}/design/json/*.json (each with revised design units and the critic's review). The user chose: change Patterns and PlayMap too, each in its own repo's process. Stagewatch's brief: speed, user experience, security, stability from gaming and medical practice; attempts are not facts; fail closed on the wire; every chain followed.
This is DESIGN work: read code, docs and the inputs; do not edit any repo; do not build. You may write markdown to ${S}/design/<name>.md. Cite file:line for claims about existing behaviour.`

const PROFILE_SCHEMA = {
  type: 'object',
  properties: {
    angle: { type: 'string' },
    summary: { type: 'string' },
    layers: { type: 'string', description: 'what belongs to discovery, PeerLink, Show Bus messages and views, precisely' },
    profile: { type: 'string', description: 'the PeerLink profile as normative spec text (MUST/SHOULD), ready to be §13 of Show Bus 1.3: transports allowed per app, identity, pairing by proof (no secret on the wire), keys and binding, TLS and pinning, versions and negotiation, liveness (beats, live:false, stale and dead thresholds), clock offset, receipts and idempotent request ids, ordering and latest-wins, backpressure and bounds, reconnect with jitter, degraded and refused states, what each side shows when, logging rules for secrets' },
    perApp: { type: 'array', items: { type: 'object', properties: { app: { type: 'string' }, link: { type: 'string' }, today: { type: 'string' }, changes: { type: 'string' } }, required: ['app', 'link', 'today', 'changes'] } },
    conformance: { type: 'string', description: 'a shared conformance kit: test vectors (format, where they live, how each repo C# and JS consumes them), a scripted peer, what each link must pass' },
    migration: { type: 'string', description: 'how older peers keep working; the order apps adopt it in; what is refused when' },
    risks: { type: 'array', items: { type: 'string' } },
  },
  required: ['angle', 'summary', 'layers', 'profile', 'perApp', 'conformance', 'migration', 'risks'],
}

const SCORE_SCHEMA = {
  type: 'object',
  properties: {
    scores: { type: 'array', items: { type: 'object', properties: { angle: { type: 'string' }, security: { type: 'number' }, reliability: { type: 'number' }, adoptability: { type: 'number' }, clarity: { type: 'number' }, notes: { type: 'string' } }, required: ['angle', 'security', 'reliability', 'adoptability', 'clarity', 'notes'] } },
    best: { type: 'string' },
    graft: { type: 'array', items: { type: 'string' }, description: 'ideas from the others the synthesis must keep' },
    reject: { type: 'array', items: { type: 'string' }, description: 'ideas that must not survive, with why' },
  },
  required: ['scores', 'best', 'graft', 'reject'],
}

const EYE_SCHEMA = {
  type: 'object',
  properties: {
    summary: { type: 'string' },
    today: { type: 'string', description: 'what the Eye reads and draws today, with evidence' },
    model: { type: 'string', description: 'the Eye model: apps, nodes, devices, windows, displays, outputs, links between them (who shows whose window on which screen), lights, how it is fed through PeerLinks and the hello, bounds' },
    changes: { type: 'array', items: { type: 'object', properties: { repo: { type: 'string' }, file: { type: 'string' }, change: { type: 'string' } }, required: ['repo', 'file', 'change'] } },
    tests: { type: 'array', items: { type: 'string' } },
    units: { type: 'array', items: { type: 'object', properties: { repo: { type: 'string' }, title: { type: 'string' }, dependsOn: { type: 'array', items: { type: 'string' } } }, required: ['repo', 'title'] } },
    risks: { type: 'array', items: { type: 'string' } },
  },
  required: ['summary', 'today', 'model', 'changes', 'tests', 'units', 'risks'],
}

const PLAN_SCHEMA = {
  type: 'object',
  properties: {
    overview: { type: 'string' },
    releases: { type: 'array', description: 'ordered trains of work; each lands and is validated before the next', items: { type: 'object', properties: { name: { type: 'string' }, goal: { type: 'string' }, units: { type: 'array', items: { type: 'object', properties: { id: { type: 'string' }, repo: { type: 'string' }, title: { type: 'string' }, detail: { type: 'string', description: 'what to change, files, tests, papers; enough for an implementer with the design docs' }, sources: { type: 'array', items: { type: 'string' }, description: 'design units it comes from' }, gaps: { type: 'array', items: { type: 'string' } }, dependsOn: { type: 'array', items: { type: 'string' } }, validate: { type: 'string', description: 'the exact commands that prove it' } }, required: ['id', 'repo', 'title', 'detail', 'sources', 'gaps', 'dependsOn', 'validate'] } } }, required: ['name', 'goal', 'units'] } },
    numbering: { type: 'string', description: 'Patterns round numbers and unit numbers, PlayMap batch names, AGMessage versions, Stagewatch versions' },
    ownerQuestions: { type: 'array', items: { type: 'object', properties: { repo: { type: 'string' }, question: { type: 'string' }, why: { type: 'string' }, recommended: { type: 'string' }, blocks: { type: 'array', items: { type: 'string' } } }, required: ['repo', 'question', 'why', 'recommended', 'blocks'] } },
    gapMap: { type: 'array', items: { type: 'object', properties: { gap: { type: 'string' }, units: { type: 'array', items: { type: 'string' } }, leftWhy: { type: 'string' } }, required: ['gap', 'units'] } },
    deferred: { type: 'array', items: { type: 'string' } },
  },
  required: ['overview', 'releases', 'numbering', 'ownerQuestions', 'gapMap', 'deferred'],
}

const CRITIC_SCHEMA = {
  type: 'object',
  properties: {
    verdict: { type: 'string' },
    issues: { type: 'array', items: { type: 'object', properties: { severity: { type: 'string' }, issue: { type: 'string' }, fix: { type: 'string' } }, required: ['severity', 'issue', 'fix'] } },
    unmappedGaps: { type: 'array', items: { type: 'string' } },
  },
  required: ['verdict', 'issues', 'unmappedGaps'],
}

const ANGLES = [
  { key: 'security', text: 'SECURITY FIRST: zero trust on the LAN, proof of possession and never a secret on the wire, keys scoped and bound to one node, TLS everywhere it can be with first-use pinning and no downgrade, replay protection, what an attacker on the venue Wi-Fi can and cannot do, secrets out of every log, frame, journal and STATE.' },
  { key: 'reliability', text: 'RELIABILITY FIRST, from gaming netcode and medical alarm practice: heartbeats that carry liveness of the SHOW not just the socket (live:false), stale and dead thresholds with fixed numbers, sequence numbers and latest-wins state, idempotent request ids and receipts that report the observed outcome, bounded queues and backpressure, reconnect with jitter so a room does not stampede, clock offset with quality, degraded modes that fail safe and say so, deterministic tests on injected time.' },
  { key: 'adoption', text: 'ADOPTION FIRST: the four apps already have different transports (Stagewatch HTTP/WS on 9924 with TLS on the same port; Patterns a TCP line wire on 9697 with NONCE/AUTH-PROOF; PlayMap HTTP+WS and a TCP wire on 9941; AGMessage HTTP/WS and its new PeerLink). Define the profile so each existing link converges with the smallest change, keeps older peers working with a clear version negotiation, and can be held by one shared conformance kit (JSON test vectors and a scripted peer used by C# and JS tests alike).' },
]

phase('PeerLink drafts')
const drafts = (await parallel(ANGLES.map(a => () => agent(`${CONTEXT}\n\nDraft the PeerLink profile — layer 2 of the Show Bus, one standard every link in the four apps follows — from this angle: ${a.text}\nRead the existing link code in all four repos first (Stagewatch/src/Timer.Integrations/PatternsLink.cs, Stagewatch/src/Timer.Remote/WebServer.cs and Timer.Core ShowBus/Approvals/ApiKeys/TlsPins, Patterns/src/Patterns.App/Services/StagewatchLinkService.cs and Patterns/src/Patterns.Core/Services/ControlProtocol.cs, ShowBus*.cs, Mapper/src/PlayMap.Net/Links/*.cs and Mapper/src/PlayMap.Core/Bus/*.cs, AGMessage lib/nodes.js and the 0.5.0 branch's lib/peerlinks.js) and the eight designs (they already decide many link details: node identity on /ws, pairing fail-closed, verdicts, caps). Keep what the designs decided unless you show it is wrong.`,
  { label: `draft:${a.key}`, phase: 'PeerLink drafts', schema: PROFILE_SCHEMA })))).filter(Boolean)

phase('Judge')
const judged = (await parallel([0, 1].map(i => () => agent(`${CONTEXT}\n\nJudge these ${drafts.length} PeerLink profile drafts (score 1-10 on security, reliability, adoptability, clarity). ${i === 0 ? 'Weigh what a live show needs: nothing a link does may freeze or mislead a screen.' : 'Weigh what four small teams can actually build and keep in step.'} Name the best, the ideas from the others to graft, and ideas to reject.\n${JSON.stringify(drafts)}`,
  { label: `judge:${i}`, phase: 'Judge', schema: SCORE_SCHEMA })))).filter(Boolean)

phase('PeerLink synthesis')
const profile = await agent(`${CONTEXT}\n\nSynthesize ONE PeerLink profile from these drafts and the judges' scores: start from the best, graft what they say to keep, drop what they reject (check each against the code). The result is §13 of Show Bus 1.3 plus per-app changes, a conformance kit and a migration order. Write it in full to ${S}/design/peerlink.md too.\nDrafts:\n${JSON.stringify(drafts)}\nJudges:\n${JSON.stringify(judged)}`,
  { label: 'peerlink', phase: 'PeerLink synthesis', schema: PROFILE_SCHEMA })

phase('Eye')
const eye = await agent(`${CONTEXT}\n\nDesign the Eye (Patterns' God's Eye view: Patterns/src/Patterns.Core/Services/Eye.cs, ShowBusNetwork.cs, ShowBusNodes.cs, ShowContext.cs, Patterns/src/Patterns.App/Services/EyeService.cs, ViewModels/MainViewModel.Eye.cs, Views/Controls/EyeCanvas.cs, docs/EYE.md) as the one view of every connected app, node and device: Stagewatch (its timer, cues, windows and which app shows each window where), PlayMap (its outputs, projectors, playback, as a node and as devices), AGMessage (its halls, displays, help requests), Patterns' own screens, twins and devices, each fed through PeerLinks and the hello per this profile:\n${JSON.stringify(profile)}\nSay what the Eye reads and draws today (with evidence), the model, the changes per repo (what each app must publish in its context for the Eye to draw it), tests, units in Patterns' round shape, and risks (bounds: the Eye must stay instant with many nodes).`,
  { label: 'eye', phase: 'Eye', schema: EYE_SCHEMA })
const eyeCritique = await agent(`${CONTEXT}\n\nCritique this Eye design adversarially against Patterns' code and rules (Patterns/CLAUDE.md doctrine: one air, every reader, measured then governed) and the PeerLink profile. Read the code it touches.\nEye design:\n${JSON.stringify(eye)}`,
  { label: 'eye-critique', phase: 'Eye', schema: CRITIC_SCHEMA })

phase('Master plan')
const plan = await agent(`${CONTEXT}\n\nIntegrate everything into ONE plan: the eight revised area designs (${S}/design/json/*.json and ${S}/design/*.md), the PeerLink profile and the Eye design with its critique below. De-duplicate units that several designs share (e.g. caps on PeerContexts, the 1.3 spec text, Patterns round numbers which the designs assign inconsistently: 89, 90, 91, 92), order them into release trains where each train lands and is validated before the next (safety first: the stuck Stagewatch unseen in Patterns, a window token in Patterns STATE, peers hit by screen commands, the token sent in clear, a node key speaking for another node), number Patterns rounds consecutively from the next free round (read Patterns/CHANGELOG.md and .github/scripts/tag-rounds.sh), fit PlayMap's work to its round-3 rules (list every owner decision it needs as an ownerQuestion), put AGMessage last on top of its 0.5.0 branch (only what that branch has not already done), and give each unit the exact validate commands (Stagewatch: tools/test.sh lanes; Patterns: the loop in Patterns/CLAUDE.md; Mapper: its loop; AGMessage: npm test in app). Map every gap G01-G49 to units or say why it is left. Keep units implementable by one agent each with the design docs as reference.\nPeerLink profile:\n${JSON.stringify(profile)}\nEye design:\n${JSON.stringify(eye)}\nEye critique:\n${JSON.stringify(eyeCritique)}`,
  { label: 'master-plan', phase: 'Master plan', schema: PLAN_SCHEMA })
const planCritique = await agent(`${CONTEXT}\n\nCritique this master plan: gaps unmapped (check G01-G49 in ${S}/gaps.json), units that depend on something scheduled later, units too big for one agent, process violations in Patterns/Mapper, safety items not first, validate commands that would not work (check the repos' scripts), owner questions missing.\nPlan:\n${JSON.stringify(plan)}`,
  { label: 'plan-critique', phase: 'Master plan', schema: CRITIC_SCHEMA })
const finalPlan = await agent(`${CONTEXT}\n\nRevise the master plan with every valid issue from the critique (check each; reject wrong ones with a reason). Return the full revised plan and write it as readable markdown to ${S}/design/master-plan.md.\nPlan:\n${JSON.stringify(plan)}\nCritique:\n${JSON.stringify(planCritique)}`,
  { label: 'final-plan', phase: 'Master plan', schema: PLAN_SCHEMA })

return { profile, judged, eye, eyeCritique, plan: finalPlan, planCritique }
