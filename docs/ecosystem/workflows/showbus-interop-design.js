export const meta = {
  name: 'showbus-interop-design',
  description: 'Design how Stagewatch interacts with Patterns, PlayMap and AGMessage over the Show Bus, area by area, with a critic and a revision each',
  phases: [
    { title: 'Design', detail: 'one designer per area' },
    { title: 'Critique', detail: 'an adversarial critic per design' },
    { title: 'Revise', detail: 'the designer folds in the critique' },
  ],
}

const S = '<scratch>'
const CONTEXT = `Four repos sit side by side under /home/user: Stagewatch (C#/.NET 10 WPF stage timer, the Show Bus master spec Stagewatch/docs/SHOWBUS.md v1.2, agent brief Stagewatch/CLAUDE.md), Patterns (C#/.NET 10 Avalonia show-content app; strict round process in Patterns/CLAUDE.md), Mapper (PlayMap, C#/.NET 10 media player / projection-mapping render node; round-3 ownership rules in Mapper/CLAUDE.md), AGMessage (Node.js messaging app, no agent brief; app/server.js, app/lib/*.js, app/test).
The user asked: "StageWatch also needs to be able to interact with AGMessage, PlayMap, and Patterns. Consider use of the ShowBus." Stagewatch has just gained "windows": browser windows it controls at their own addresses (/w/<token>, the 'window' verb, GET /api/windows, window items in its Show Bus context) — see Stagewatch/docs/REMOTE.md (2.17), Stagewatch/src/Timer.Core/ScreenWindows.cs, Stagewatch/src/Timer.Remote/WindowDirector.cs, WebServer.cs.
A verified gap analysis is in ${S}/gaps.json (49 confirmed gaps; use each gap's 'statement', the corrected version). Each side's map (what it serves and calls, with evidence) is in ${S}/maps-lite.json. Read both files first.
Stagewatch's standing brief: speed, user experience, security, stability, from gaming and medical practice (one writer, immutable frames, latest-wins, fail-safe states, observed state over sent state, bounded everything, deterministic tests); attempts are not facts (receipts, refusals with reasons); fail closed on the wire (unknown words refused, secrets in no log/frame/journal); every chain followed (core, desk + right-click menus, wire grammar on HTTP/WS/OSC/Companion/MCP, pages, Companion module, context, copilot risk, papers).
Test environment: Stagewatch builds and runs its suite on Linux (tools/test.sh lanes: build, unit, module, holo, e2e; tools/Stagewatch.Rig is the real servers around a simulated show; tests/e2e drives it with Playwright). Mapper's PlayMap.Node runs the engine headless (tools/PlayMap.Node). AGMessage runs on node 22.
This is a DESIGN task: read code and docs, do not edit any repo, do not build. You may write your design as markdown to ${S}/design/<area>.md. Cite file:line evidence for every claim about existing behaviour.`

const DESIGN_SCHEMA = {
  type: 'object',
  properties: {
    area: { type: 'string' },
    summary: { type: 'string', description: 'what the operator and each app gain, in plain words' },
    decisions: { type: 'array', items: { type: 'object', properties: { decision: { type: 'string' }, why: { type: 'string' }, alternatives: { type: 'string' } }, required: ['decision', 'why'] } },
    wire: { type: 'string', description: 'every new or changed endpoint, verb, message, field, hello cap, context item, with exact shapes and auth' },
    changes: { type: 'array', items: { type: 'object', properties: { repo: { type: 'string' }, file: { type: 'string' }, change: { type: 'string' } }, required: ['repo', 'file', 'change'] } },
    tests: { type: 'array', items: { type: 'object', properties: { repo: { type: 'string' }, where: { type: 'string' }, proves: { type: 'string' } }, required: ['repo', 'where', 'proves'] } },
    units: { type: 'array', description: 'commit-sized units in order, each buildable and testable alone', items: { type: 'object', properties: { repo: { type: 'string' }, title: { type: 'string' }, gaps: { type: 'array', items: { type: 'string' } }, dependsOn: { type: 'array', items: { type: 'string' } } }, required: ['repo', 'title', 'gaps'] } },
    gapsClosed: { type: 'array', items: { type: 'string' } },
    gapsLeft: { type: 'array', items: { type: 'object', properties: { id: { type: 'string' }, why: { type: 'string' } }, required: ['id', 'why'] } },
    risks: { type: 'array', items: { type: 'string' } },
    processNotes: { type: 'string', description: 'what each touched repo’s own rules (CLAUDE.md: rounds, papers, ownership) require for these changes' },
  },
  required: ['area', 'summary', 'decisions', 'wire', 'changes', 'tests', 'units', 'gapsClosed', 'gapsLeft', 'risks', 'processNotes'],
}

const CRITIQUE_SCHEMA = {
  type: 'object',
  properties: {
    verdict: { type: 'string', description: 'sound | needs-changes | wrong' },
    issues: { type: 'array', items: { type: 'object', properties: { severity: { type: 'string' }, issue: { type: 'string' }, evidence: { type: 'string' }, fix: { type: 'string' } }, required: ['severity', 'issue', 'fix'] } },
    missing: { type: 'array', items: { type: 'string' }, description: 'gaps or chains the design should cover but does not' },
  },
  required: ['verdict', 'issues', 'missing'],
}

const AREAS = [
  { key: 'peers-not-screens', gaps: ['G06', 'G15', 'G32', 'G43'], brief: 'Peer apps that open Stagewatch’s /ws (Patterns, PlayMap, AGMessage) must not be treated as browser screens (screen identify/reload/disconnect/go must never reach them), each peer must have its own identity (no shared fixed cids), a node key must not post a context or bind under another node’s id, and a peer must learn whether its key was accepted on /ws. Design node identity on Stagewatch’s WebSocket and HTTP, and how the desk lists peers apart from screens.' },
  { key: 'playout-links', gaps: ['G01', 'G07', 'G08', 'G26', 'G48', 'G28', 'G29', 'G30'], brief: 'Stagewatch drives playout on Patterns today through one PatternsLink (VIDEO verbs, VT countdown, clip-end trigger). Design first-class PlayMap support (finder, link, VT follow, transport, cueing PlayMap playlist items from the running order) alongside Patterns, possibly both at once; receipts that report the peer’s real outcome (journal and the sender’s receipt); never sending the pairing token in clear to a remote desk (and TLS where the peer offers it); line caps sized for PlayMap STATE; refusals Patterns gives for playlist items or stingers surfaced with reasons; timed cues using the measured clock offset; HTTP triggers that can authenticate and read receipts.' },
  { key: 'cross-app-proposals', gaps: ['G14', 'G20', 'G02', 'G03', 'G05', 'G13', 'G33'], brief: 'Stagewatch hosts the shared approval queue (Show Bus §11). Design how Stagewatch’s own copilot, its MCP tools and its operator can propose a line FOR another app (Patterns, PlayMap, AGMessage notices) that the target collects; how risk is read from a target’s context correctly (PlayMap’s verbs: more than 64 commands, [..] and A|B placeholders); what Patterns and AGMessage must change so their proposals are valid and rated right; and how PlayMap’s assistant could use the desk.' },
  { key: 'windows-for-peers', gaps: ['G10', 'G11', 'G24', 'G46', 'G09', 'G47', 'G12', 'G22', 'G23'], brief: 'Stagewatch windows (/w/<token>) are browser pages Stagewatch controls. Design how a Show Bus peer can be given a window’s address without a person copying it (a window lease or grant to a node key, never readable by other nodes, never in STATE or the journal), how Patterns would show one on an output (its WEB verbs) without leaking the token (G09), whether and how PlayMap could (it has no web renderer today; it does have a timer source fed from Stagewatch’s /ws — G12), how the context and the Eye show "Patterns screen 2 shows Stagewatch window lobby", and the window items and links fix in Stagewatch’s context (G23).' },
  { key: 'spec-1-3', gaps: ['G22', 'G27', 'G38', 'G39', 'G40', 'G41', 'G45', 'G42'], brief: 'Write the changes for Show Bus 1.3 (the master at Stagewatch/docs/SHOWBUS.md): PlayMap as a node kind with its caps and §8 row, windows and window leases, explicit limits (sizes, counts, rates), the proto/version field meaning, loopback hello and loopback-only nodes, certificate lifetime, the read-path exemption for STATE and CONTEXT on unpaired links (or the rule that closes it), and what changes in Stagewatch’s hello sender (G27) and Stagewatch/docs/NODES.md (G41). Say how the copies in Patterns and AGMessage and PlayMap’s SHOWBUS-NODE.md are kept in step (and whether a fence or test could hold them).' },
  { key: 'agmessage', gaps: ['G05', 'G16', 'G17', 'G18', 'G19', 'G42', 'G43', 'G32', 'G37'], brief: 'AGMessage is a small Node.js app with tests under AGMessage/app/test. Design its fixes: a valid Stagewatch line for its stage-message proposal, retrying a failed result report until delivered or expired, reading Stagewatch’s result shape, hearing PlayMap’s hello, reading Stagewatch’s items and commands (windows included) for its brief and admin page, showing connected and the pin only on an authenticated frame, a per-install cid, and whether it should read Patterns’ context.' },
  { key: 'patterns-playmap-side', gaps: ['G04', 'G09', 'G21', 'G33', 'G34', 'G35', 'G36', 'G03', 'G12', 'G13', 'G25', 'G31', 'G47', 'G48'], brief: 'Design the Patterns and PlayMap side of the interop as work packages each repo’s process can take: for Patterns a round in its own shape (Patterns/CLAUDE.md: PLAN §NN, REVIEW, CHANGELOG, OPEN rows, Round NN.k commits), for PlayMap within round 3’s ownership rules (Mapper/CLAUDE.md; docs/round3/PLAN.md §4 and the spine amendment lane §5.7). Rank by show risk: which are safety-relevant now (a stuck Stagewatch not seen; a window token published in STATE) and which are features. For each, the exact files and changes and tests in that repo.' },
  { key: 'ecosystem-lane', gaps: ['G44', 'G49'], brief: 'No test runs two real apps against each other. Design an ecosystem interop lane that runs on Linux (the cloud bench and a CI job): the Stagewatch rig (or the real Stagewatch servers), AGMessage (node), PlayMap.Node (headless engine) and, if possible, Patterns headless (check Patterns/CLAUDE.md and its CI lifecycle lane, and whether Patterns can run without a display or under Xvfb). Scenarios: hello discovery both ways, context exchange, a proposal from each app through Stagewatch’s desk collected and reported, the VT link to Patterns and PlayMap, a Stagewatch window shown by Patterns, link loss and recovery. Where it lives (which repo or a new top-level folder), how each app is built and started, ports, time budget, and what it can and cannot prove. Also say which pieces of G49 (manifest, merged journal, one preflight) are worth building now.' },
]

const results = await pipeline(AREAS,
  a => agent(`${CONTEXT}\n\nYour area: ${a.key}. Gaps: ${a.gaps.join(', ')}.\n${a.brief}\nProduce a concrete design: decisions with reasons, exact wire shapes, file-by-file changes in each repo, tests (Stagewatch suite / rig e2e / the other repo's tests), commit-sized units in order, gaps it closes and leaves (with why), risks, and what each touched repo's own process requires.`,
    { label: `design:${a.key}`, phase: 'Design', schema: DESIGN_SCHEMA }),
  (design, a) => agent(`${CONTEXT}\n\nCritique this design for area ${a.key} adversarially. Read the code it touches yourself. Look for: things that would break existing behaviour or tests, security holes (token leaks, privilege escalation, a peer acting as another), wrong claims about existing code, violations of the Show Bus spec or of a repo's own rules (Patterns/CLAUDE.md rounds; Mapper/CLAUDE.md ownership; Stagewatch/CLAUDE.md doctrine), missing chains (desk, right-click menus, Companion module, MCP, copilot risk, context, papers), untestable claims, over-building. Design:\n${JSON.stringify(design)}`,
    { label: `critique:${a.key}`, phase: 'Critique', schema: CRITIQUE_SCHEMA }).then(c => ({ design, critique: c })),
  (dc, a) => agent(`${CONTEXT}\n\nYou designed area ${a.key}. A critic reviewed it. Fold in every valid issue (check each against the code; reject with a reason any that are wrong) and return the revised design in full. Original design:\n${JSON.stringify(dc.design)}\n\nCritique:\n${JSON.stringify(dc.critique)}`,
    { label: `revise:${a.key}`, phase: 'Revise', schema: DESIGN_SCHEMA }).then(r => ({ area: a.key, revised: r, critique: dc.critique })),
)
return results.filter(Boolean)
