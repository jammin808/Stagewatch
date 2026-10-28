# For the AGMessage 0.5.0 session (branch AGMessage's 0.5.0 branch)

A cross-app review of the Show Bus found one security issue on this branch that should be fixed before 0.5.0 is tagged
(the other session's plan calls it "AG-1", meant to land as "AGMessage 0.5.0, unit 21").

**The issue.** `app/lib/peerlinks.js` sends Patterns' or PlayMap's pairing token (their full-control operator token) in
the `X-Patterns-Token` / `X-PlayMap-Token` header over plain HTTP to any node that is neither pinned nor announcing a
certificate (peerlinks.js around :259-267 and :292 at commit 6146ee9), and `send()` (around :346-356, called from
server.js around :1260) drives Patterns and PlayMap with that token. Anyone on the venue network who can see the traffic,
or who answers as that node, gets the operator token.

**The fix the review recommends.**
- The token goes only to a loopback address, or over TLS to a pinned or person-confirmed certificate (at least 8 hex
  characters typed to confirm). Otherwise the link shows "refused" with words, and nothing secret is sent.
- `send()` is off while the only credential is the operator token: a line becomes a Show Bus §11 proposal on
  Stagewatch's desk (a person approves; the node collects it with its own key), or is refused with words. The direct
  path comes back later with scoped keys issued to AGMessage (Patterns' `ptk_` / PlayMap's `pmk_` keys).
- A test that the token never leaves on a plain non-loopback connection, and that `send()` refuses with words.

**Also seen on this branch, smaller (fine for 0.5.1):**
- `readProposal` (nodes.js around :171-180) reads `ok` only from a nested `result` object; Stagewatch sends `ok` and
  `words` at the top level of the proposal, so an approved proposal's outcome reads as unknown.
- A result report that fails once is never retried, so an approved notice can end "undelivered".
- The Stagewatch link dials `/ws?cid=agmessage`: two AGMessages on one Stagewatch share one identity. It should dial
  with its own Show Bus id.
- It shows "connected" and confirms the TLS pin on the WebSocket upgrade, before Stagewatch has accepted its key.

Stagewatch 2.18 (being built) will tell a node link whether its key was accepted, in a first message
`{"type":"auth","ok":…,"node":…,"words":…}` on `/ws?node=<id>&t=messenger&cid=<id>`.
