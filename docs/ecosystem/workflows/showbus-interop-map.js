export const meta = {
  name: 'showbus-interop-map',
  description: 'Map Show Bus interop between Stagewatch, Patterns, PlayMap and AGMessage; verify every gap',
  phases: [
    { title: 'Map', detail: 'one reader per repo side and one for the spec copies' },
    { title: 'Matrix', detail: 'cross-repo interop matrix and gap list' },
    { title: 'Verify', detail: 'two skeptics per gap, reading both ends' },
  ],
}

const CONTEXT = `Four repos are checked out side by side under /home/user: Stagewatch (C#/.NET 10 WPF stage timer; the Show Bus master spec is Stagewatch/docs/SHOWBUS.md, v1.2), Patterns (C#/.NET 10 Avalonia test-pattern/show-content app; huge, rounds of work documented in docs/PLAN.md and docs/REVIEW.md), Mapper (PlayMap: C#/.NET 10 projection-mapping media player / render node; docs/SHOWBUS-NODE.md, docs/WIRE.md), AGMessage (Node.js messaging app; app/server.js, app/lib/*.js, docs/SHOWBUS.md, docs/NODES.md).
The user wants Stagewatch to interact with AGMessage, PlayMap and Patterns, using the Show Bus. Stagewatch has just gained "windows": browser windows it controls at addresses of their own (GET /w/<token>, a 'window' command verb: new/show/cue/hold/release/identify/reload/loss/fade/rename/revoke/delete; GET /api/windows; window items in the Show Bus context as 'window:<name>' with kind 'screen'). See Stagewatch/docs/REMOTE.md (end) and Stagewatch/src/Timer.Core/ScreenWindows.cs.
This is a READ-ONLY research task: do not edit files, do not run dotnet build or tests, do not commit. Use grep/read. Cite evidence as repo-relative path:line (prefix with the repo name, e.g. Mapper/src/PlayMap.Net/Links/StagewatchLink.cs:42). Never guess: if you could not find something, say absent and where you looked.`

const MAP_SCHEMA = {
  type: 'object',
  properties: {
    side: { type: 'string' },
    showbusVersion: { type: 'string', description: 'Show Bus version this side implements, with evidence' },
    summary: { type: 'string' },
    capabilities: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          area: { type: 'string', description: 'hello/discovery, commands+receipts, state frames, clock offset, context serve, context consume, keys/pairing, TLS/pins, approvals, video/playout, display of web pages, messages, other' },
          peer: { type: 'string', description: 'stagewatch | patterns | playmap | agmessage | any' },
          direction: { type: 'string', description: 'serves | consumes | both' },
          what: { type: 'string' },
          status: { type: 'string', description: 'implemented | partial | stub | absent' },
          evidence: { type: 'string' },
          notes: { type: 'string' },
        },
        required: ['area', 'peer', 'direction', 'what', 'status', 'evidence'],
      },
    },
    expectsFromOthers: {
      type: 'array',
      description: 'every endpoint, line, message or field this side calls or reads on another app, exactly as it calls it',
      items: {
        type: 'object',
        properties: { peer: { type: 'string' }, call: { type: 'string' }, auth: { type: 'string' }, purpose: { type: 'string' }, evidence: { type: 'string' } },
        required: ['peer', 'call', 'purpose', 'evidence'],
      },
    },
    exposesToOthers: {
      type: 'array',
      description: 'every endpoint, verb or message this side serves for other apps, with its auth',
      items: {
        type: 'object',
        properties: { call: { type: 'string' }, auth: { type: 'string' }, purpose: { type: 'string' }, evidence: { type: 'string' } },
        required: ['call', 'purpose', 'evidence'],
      },
    },
    canShowWebPages: { type: 'string', description: 'can this app put a web page / browser source on an output (so a Stagewatch window could be shown there)? how, with evidence; or absent' },
    tests: { type: 'array', items: { type: 'string' }, description: 'interop tests that exist, file and what they prove' },
    openIssues: { type: 'array', items: { type: 'string' }, description: 'known open items about interop in this repo (OPEN.md, CARRYOVER.md, WHATS_NEXT.md, TODOs), with evidence' },
  },
  required: ['side', 'showbusVersion', 'summary', 'capabilities', 'expectsFromOthers', 'exposesToOthers', 'canShowWebPages', 'tests', 'openIssues'],
}

const READERS = [
  { key: 'stagewatch', prompt: `Map STAGEWATCH's side of the Show Bus and of every link to Patterns, PlayMap and AGMessage. Read Stagewatch/docs/SHOWBUS.md, ECOSYSTEM.md, NODES.md, REMOTE.md (2.15, 2.16, 2.17 sections), and the code: src/Timer.Integrations (PatternsLink, rooms/hello/discovery, RemoteSlides, anything with Patterns/peer/hello/beacon/9928), src/Timer.Core (ShowBus.cs, Approvals.cs, ApiKeys.cs, PeerContexts, ClockOffsetEstimator, CommandReceipt, TlsPins), src/Timer.Show/ShowContext.cs, src/Timer.Remote/WebServer.cs (proposals, peers/context, context, keys), src/Timer.Companion (receipts, PING), src/Timer.App/ViewModels/MainViewModel.ShowBus.cs, .Desk.cs, .Patterns.cs. For each peer (patterns, playmap, agmessage) list exactly what Stagewatch sends it and reads from it. Note whether Stagewatch knows PlayMap exists at all (hello kinds it recognises, any 'playmap'/'mapper' code).` },
  { key: 'patterns', prompt: `Map PATTERNS' side toward Stagewatch (and toward PlayMap and AGMessage if any). Read Patterns/docs/SHOWBUS.md, docs/REMOTE.md (Stagewatch sections), and the code: src/Patterns.App/Services/StagewatchLinkService.cs, src/Patterns.Core/Services/StagewatchFollow.cs, ShowBus.cs, ShowBusNetwork.cs, ShowBusNodes.cs, ShowContext.cs, ControlProtocol.cs (the wire verbs Stagewatch uses: NONCE/AUTH-PROOF pairing, CONTEXT, video transport verbs that Stagewatch's 'patterns end/restart/play/pause/stop/seek' map to), Eye.cs, BeaconService.cs, NodesService.cs, CommandRouter.cs. What does Patterns send Stagewatch, what does it read from Stagewatch (frames? /api/context? hello?), how it pairs, which Show Bus version, and whether Patterns can show a web page or browser source on an output (docs/WEB-VIDEO.md, any WebView2/web source) so a Stagewatch window address could be shown there. Check docs/OPEN.md for open interop rows.` },
  { key: 'playmap', prompt: `Map PLAYMAP's (repo Mapper) side toward Stagewatch, Patterns and AGMessage. Read Mapper/docs/SHOWBUS-NODE.md, docs/WIRE.md (verbs relevant to peers), docs/round2/CARRYOVER.md (Stagewatch entries), and the code: src/PlayMap.Core/Bus/ShowBus.cs, Pairing.cs, NodeSettings.Control.cs, src/PlayMap.Net/Links/StagewatchLink.cs, PatternsLink.cs, WireClient.cs, src/PlayMap.Net/Bus/HelloService.cs, PeerTable.cs, src/PlayMap.Engine/Assistant/StagewatchProposalSink.cs, ContextBuilder.cs, Host/NetworkHost.cs, src/PlayMap.Core/Wire/VerbCatalog.cs (which verbs a peer may send PlayMap). List exactly which Stagewatch endpoints PlayMap calls (paths, headers, auth, payload fields) and what it does with the answers; what hello it sends (kind string, caps, ports); and whether PlayMap can render a web page / browser source as content on an output (so a Stagewatch window could be shown on a projector). Note what playback verbs a show controller like Stagewatch could send PlayMap (play/pause/cue/seek of media, sync to show time).` },
  { key: 'agmessage', prompt: `Map AGMESSAGE's side toward Stagewatch, Patterns and PlayMap. Read AGMessage/docs/SHOWBUS.md, docs/NODES.md, README.md, CHANGELOG.md, app/server.js and app/lib/nodes.js, context.js, ai.js, tls.js, guard.js. List exactly which Stagewatch endpoints it calls (paths, headers, auth/node key, payloads), what it reads, what hello it sends (kind string, caps, ports), how it uses the approvals queue (/api/proposals collect/result), what it exposes for others, and how it handles Patterns and PlayMap (if at all). Check app/test for interop tests.` },
  { key: 'spec', prompt: `Compare the Show Bus SPEC COPIES: Stagewatch/docs/SHOWBUS.md (the master, 1.2), Patterns/docs/SHOWBUS.md, AGMessage/docs/SHOWBUS.md, and Mapper/docs/SHOWBUS-NODE.md (PlayMap's own node document). Run diff between the three SHOWBUS.md copies and report every substantive difference (not whitespace), which copy is newer on each point, and whether PlayMap is named anywhere in the master (hello kinds, §8 what each node publishes and consumes). Then list, per section of the master (§1-§11), every normative rule, and for each note any app whose own docs contradict it. Also read Stagewatch/docs/ECOSYSTEM.md for the roadmap of interop not yet built.` },
]

const MATRIX_SCHEMA = {
  type: 'object',
  properties: {
    matrix: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          from: { type: 'string' }, to: { type: 'string' }, capability: { type: 'string' },
          status: { type: 'string', description: 'works | partial | broken | missing' },
          evidence: { type: 'string' }, notes: { type: 'string' },
        },
        required: ['from', 'to', 'capability', 'status', 'evidence'],
      },
    },
    gaps: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          id: { type: 'string' }, title: { type: 'string' },
          pair: { type: 'string', description: 'e.g. stagewatch<->playmap' },
          kind: { type: 'string', description: 'missing | mismatch | broken | spec-drift | untested | opportunity' },
          detail: { type: 'string' }, evidence: { type: 'string' },
          fix: { type: 'string', description: 'the smallest change that closes it, and which repo(s)' },
          value: { type: 'string', description: 'high | medium | low, for a live show' },
        },
        required: ['id', 'title', 'pair', 'kind', 'detail', 'evidence', 'fix', 'value'],
      },
    },
  },
  required: ['matrix', 'gaps'],
}

const VERDICT_SCHEMA = {
  type: 'object',
  properties: {
    refuted: { type: 'boolean' },
    reason: { type: 'string' },
    corrected: { type: 'string', description: 'if the gap is real but mis-stated, the accurate statement' },
    evidence: { type: 'string' },
  },
  required: ['refuted', 'reason', 'evidence'],
}

phase('Map')
const maps = await parallel(READERS.map(r => () =>
  agent(`${CONTEXT}\n\n${r.prompt}`, { label: `map:${r.key}`, phase: 'Map', schema: MAP_SCHEMA })))
const got = maps.filter(Boolean)
log(`${got.length}/${READERS.length} maps returned`)

phase('Matrix')
const matrix = await agent(`${CONTEXT}

Five readers mapped each side of the Show Bus. Their maps (JSON):
${JSON.stringify(got)}

Build the interop matrix: for every ordered pair among stagewatch, patterns, playmap, agmessage and every capability (discovery/hello, pairing/keys, commands+receipts, state frames, clock offset, context serve/consume, approvals, TLS/pins, video/playout control, messages, showing another app's web page/window), say works / partial / broken / missing, with evidence from both ends (open the files to confirm where the maps disagree or are vague). Then list every gap: a call one side makes that the other doesn't serve or serves differently (path, field, auth, version), spec drift between copies, a capability the user would expect for Stagewatch to "interact with" each app that is missing, untested interop, and opportunities that come from Stagewatch's new windows (e.g. Patterns or PlayMap showing a Stagewatch window on an output; peers proposing window commands through the approval queue; window items in the context the Eye reads). Be exhaustive and concrete; each gap must cite evidence.`,
  { label: 'matrix', phase: 'Matrix', schema: MATRIX_SCHEMA })

phase('Verify')
const gaps = (matrix && matrix.gaps) || []
log(`${gaps.length} gaps to verify`)
const verified = await pipeline(gaps,
  g => parallel([0, 1].map(i => () => agent(`${CONTEXT}

A reviewer claims this Show Bus interop gap (verify by reading BOTH ends of the interaction yourself — the evidence cited may be wrong or incomplete):
${JSON.stringify(g)}

Your job is to try to REFUTE it: find code or docs showing it already works, is not needed, or is mis-stated. ${i === 0 ? 'Focus on the sending/calling side.' : 'Focus on the receiving/serving side and the spec.'} If it is real but mis-stated, set refuted=false and give the corrected statement. Default to refuted=true only when you found evidence it is wrong; if real, refuted=false.`,
    { label: `verify:${g.id}:${i}`, phase: 'Verify', schema: VERDICT_SCHEMA }))),
  (votes, g) => {
    const v = votes.filter(Boolean)
    const refutes = v.filter(x => x.refuted).length
    return { ...g, votes: v, survives: refutes < 2 }
  })

return {
  maps: got,
  matrix: matrix ? matrix.matrix : [],
  confirmed: verified.filter(Boolean).filter(g => g.survives),
  refuted: verified.filter(Boolean).filter(g => !g.survives).map(g => ({ id: g.id, title: g.title, votes: g.votes })),
}
