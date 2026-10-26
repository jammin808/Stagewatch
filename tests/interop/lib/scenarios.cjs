// What the interop rows assert, shared by the topologies (lan.interop.cjs, laptop.interop.cjs). Every check reads an app's
// own published state, never the driver's belief: Patterns' STATE and EYE over its wire, paired by proof; PlayMap's STATE and
// NODES over its wire, paired by proof; AGMessage's context, health and admin settings as its admin reads them from its own
// 127.0.0.1; the rig's own lines and frames. Each wait polls until its fact holds or its deadline passes (the contract's number × 1.5 + 2 s, as
// each row says), and nothing is retried: a fact that does not come in time fails the row with the last thing read.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { until } = require('./inbox.cjs');
const { request, get } = require('./net.cjs');
const { sweep } = require('./sweep.cjs');

/** Whether text names addr as a whole address (10.99.0.2 is not named by 10.99.0.25, and is by a sentence that ends on it). */
function namesAddress(text, addr) {
  return new RegExp(`(^|[^0-9.])${addr.replace(/\./g, '\\.')}($|[^0-9.]|\\.(?![0-9]))`).test(String(text || ''));
}

/** A peer's addr as the list of addresses its hellos may come from (one on a LAN; a laptop's own two on one host). */
const addrsOf = (p) => [].concat(p.addr);
const said = (addrs) => addrs.join(' or ');

/** Patterns hears each of peers ({ kind, id, addr }, addr one address or a list of them): its STATE's bus.apps lists the id
 *  as live, and its EYE's card for the id names an address the hellos came from. Resolves with what it read. */
async function patternsHears(pat, peers, ms) {
  return until(async () => {
    const st = await pat.state();
    const apps = (st.bus && st.bus.apps) || [];
    const eye = await pat.eyeJson();
    const read = {};
    for (const p of peers) {
      const app = apps.find((a) => a.kind === p.kind && a.id === p.id);
      if (!app) throw new Error(`Patterns' bus.apps has no ${p.kind} ${p.id}: ${JSON.stringify(apps.map((a) => `${a.kind}:${a.id}`))}`);
      if (app.light === 'red' || app.light === 'amber') throw new Error(`Patterns hears ${p.kind} ${p.id} but not live: ${app.light} "${app.headline}"`);
      const card = (eye.nodes || []).find((n) => n.id === `app:${p.id}`);
      if (!card) throw new Error(`Patterns' EYE has no card app:${p.id}`);
      const words = [card.sub, ...(card.words || [])].join(' · ');
      if (!addrsOf(p).some((a) => namesAddress(words, a))) throw new Error(`Patterns' EYE card for ${p.kind} ${p.id} names no ${said(addrsOf(p))}: "${card.sub}"`);
      read[p.kind] = { id: p.id, light: app.light, headline: app.headline, eye: card.sub };
    }
    return read;
  }, ms, 'Patterns hearing ' + peers.map((p) => p.kind).join(' and '));
}

/**
 * PlayMap hears each of peers ({ t, id, addr, followed }, addr one address or a list of them): its NODES lists the id, of
 * that kind, live, and says where it heard it ("heard at <addr>": NODES has no address field of its own), an address the
 * hellos came from. The one exception is the Patterns it follows (`followed`): once PlayMap holds that Patterns' context,
 * NODES shows that context's own headline, which names no address, and the read says so (address null). Resolves with what
 * it read.
 */
async function playmapHears(pm, peers, ms) {
  return until(async () => {
    const nodes = await pm.nodes();
    const read = {};
    for (const p of peers) {
      const n = nodes.find((x) => x.t === p.t && x.id === p.id);
      if (!n) throw new Error(`PlayMap's NODES has no ${p.t} ${p.id}: ${JSON.stringify(nodes.map((x) => `${x.t}:${x.id}`))}`);
      if (n.live !== true) throw new Error(`PlayMap hears ${p.t} ${p.id} but not live`);
      const heard = /^heard at (\S+)$/.exec(n.headline || '');
      if (heard && !addrsOf(p).includes(heard[1])) throw new Error(`PlayMap heard ${p.t} ${p.id} at ${heard[1]}, not ${said(addrsOf(p))}`);
      if (!heard && !p.followed) throw new Error(`PlayMap's NODES entry for ${p.t} ${p.id} says "${n.headline}", not where it heard it`);
      read[p.t] = { id: n.id, light: n.light, headline: n.headline, address: heard ? heard[1] : null };
    }
    return read;
  }, ms, 'PlayMap hearing ' + peers.map((p) => p.t).join(' and '));
}

/** AGMessage hears each of peers ({ t, id, addr }, addr one address or a list of them): its context's peers list the id, of
 *  that kind, live (green: amber is a peer it has stopped hearing), and its admin's settings list it live from an address its
 *  hellos came from (the context carries no address). Resolves with what it read, and every peer its context lists. */
async function agmessageHears(agm, peers, ms) {
  return until(async () => {
    const ctx = await agm.context();
    const listed = ctx.peers || [];
    const heard = (await agm.settings()).peers || [];
    const read = {};
    for (const p of peers) {
      const n = listed.find((x) => x.t === p.t && x.id === p.id);
      if (!n) throw new Error(`AGMessage's context peers have no ${p.t} ${p.id}: ${JSON.stringify(listed.map((x) => `${x.t}:${x.id}`))}`);
      if (n.light === 'amber') throw new Error(`AGMessage hears ${p.t} ${p.id} but not live: "${n.headline}"`);
      const h = heard.find((x) => x.t === p.t && x.id === p.id);
      if (!h) throw new Error(`AGMessage's admin settings list no peer ${p.t} ${p.id}`);
      if (h.status !== 'live') throw new Error(`AGMessage's admin settings say ${p.t} ${p.id} is ${h.status}`);
      if (!addrsOf(p).includes(h.address)) throw new Error(`AGMessage heard ${p.t} ${p.id} from ${h.address}, not ${said(addrsOf(p))}`);
      read[p.t] = { id: n.id, light: n.light, headline: n.headline, address: h.address, status: h.status };
    }
    return { read, listed: listed.map((x) => `${x.t}:${x.id}`) };
  }, ms, 'AGMessage hearing ' + peers.map((p) => p.t).join(' and '));
}

/**
 * Every app's view of its Stagewatch link, as each publishes it: PlayMap's STATE link (its state, the address it dials and
 * its timer), AGMessage's health (`stage`), Patterns' STATE bus.stagewatch (its state, the address it follows, its time).
 */
async function stagewatchLinks(apps) {
  const pm = (await apps.playmap.state()).links.stagewatch;
  const agm = (await apps.agmessage.hostHealth()).stage;
  const pat = (await apps.patterns.state()).bus.stagewatch;
  return {
    playmap: { state: pm.state, host: pm.host, text: pm.timer && pm.timer.text, trusted: pm.timer && pm.timer.trusted },
    agmessage: { state: agm },
    patterns: { state: pat.state, name: pat.name, text: pat.text, stuck: pat.stuck },
  };
}

/** Every link to the rig live: PlayMap's with a time it trusts, AGMessage's connected, Patterns' live. */
const allLive = (l) => l.playmap.state === 'live' && l.playmap.trusted === true && l.agmessage.state === 'connected' && l.patterns.state === 'live';

/** The links that still hold on to the rig (live, connected or stale), as [app, state] pairs; none once every link is gone. */
const stillHeld = (l) => [['PlayMap', l.playmap.state], ['AGMessage', l.agmessage.state], ['Patterns', l.patterns.state]]
  .filter(([, s]) => s === 'live' || s === 'connected' || s === 'stale');

/**
 * The rig's two lockout tables now, for the addresses in never: address → { pins, viewPasses }, and the words for each count
 * that is not 0.
 */
async function readLockout(sw, never) {
  const t = await sw.auth();
  const read = {};
  const counted = [];
  for (const a of never) {
    read[a] = { pins: t.pins[a] || 0, viewPasses: t.viewPasses[a] || 0 };
    if (read[a].pins) counted.push(`${a} (${read[a].pins} wrong PINs)`);
    if (read[a].viewPasses) counted.push(`${a} (${read[a].viewPasses} wrong view passes)`);
  }
  return { read, counted };
}

/** After a row: no address in never is counted in either of the rig's lockout tables; what was read goes in the row. */
async function lockoutAfter(sw, r, never) {
  const { read, counted } = await readLockout(sw, never);
  Object.assign(r.auth, read);
  if (counted.length) throw new Error(`the rig counted failures against ${counted.join(', ')}`);
}

/** The time since `from` (unix ms), for a row's measured facts. */
const since = (from) => Date.now() - from;

/** The ms left of a deadline counted from `from` (unix ms, the moment the contract's clock starts), at least 1. */
const left = (from, deadlineMs) => Math.max(1, from + deadlineMs - Date.now());

/**
 * PlayMap follows Patterns, paired by proof (row S4.5, and S1c.2 on one laptop): its link to Patterns at `patternsHost` is
 * live and paired; a clip on Patterns' air reaches PlayMap's link by its tag; Patterns' blackout reaches PlayMap's air
 * (air.blackout and air.patternsBlackout) and its end leaves it; each within ms of the step that caused it.
 */
async function playmapFollowsPatterns(pm, pat, patternsHost, ms) {
  const facts = {};
  let t = Date.now();
  const linked = await until(async () => {
    const l = (await pm.state()).links.patterns;
    if (l.state !== 'live' || l.paired !== true) throw new Error(`PlayMap's link to Patterns is ${l.state}, paired ${l.paired}: ${l.detail}`);
    if (l.host !== patternsHost) throw new Error(`PlayMap's link to Patterns is to ${l.host}, not ${patternsHost}`);
    return l;
  }, ms, "PlayMap's link to Patterns live and paired");
  facts.linkedMs = since(t);
  facts.link = { state: linked.state, host: linked.host, paired: linked.paired };

  // A clip on Patterns' air: Patterns' own STATE says it plays, and PlayMap's link names it by Patterns' tag with the time
  // it has left, within the clip's length.
  const clip = await pat.clip('vt.mp4', 120);
  t = Date.now();
  const named = await until(async () => {
    const v = (await pat.state()).video;
    if (!v || v.file !== 'vt.mp4' || v.playing !== true) throw new Error(`Patterns' STATE video is ${JSON.stringify(v && { file: v.file, playing: v.playing })}`);
    const label = v.tag || v.file;
    const l = (await pm.state()).links.patterns;
    const m = new RegExp(`^${label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')} (\\d+):(\\d\\d)(?:\\.\\d+)?$`).exec(l.clip || '');
    if (!m) throw new Error(`PlayMap's link says "${l.clip}", not Patterns' ${label} and its time left`);
    const leftS = Number(m[1]) * 60 + Number(m[2]);
    if (leftS > 120) throw new Error(`PlayMap's link says ${leftS} s left of a 120 s clip`);
    return { clip: l.clip, tag: v.tag, file: v.file };
  }, ms, "PlayMap's link naming Patterns' clip");
  facts.clipMs = since(t);
  facts.clip = { ...named, lengthMs: clip.lengthMs };

  // Patterns' blackout, on and off, reaches PlayMap's air; PlayMap is not blacked out before.
  const before = (await pm.state()).air;
  if (before.blackout || before.patternsBlackout || !before.followPatternsBlackout) {
    throw new Error(`PlayMap's air before Patterns' blackout: ${JSON.stringify({ blackout: before.blackout, patternsBlackout: before.patternsBlackout, follow: before.followPatternsBlackout })}`);
  }
  const on = await pat.say('BLACKOUT ON');
  if (on !== 'OK') throw new Error(`Patterns answered BLACKOUT ON with ${JSON.stringify(on)}`);
  t = Date.now();
  await until(async () => {
    const a = (await pm.state()).air;
    return a.blackout === true && a.patternsBlackout === true;
  }, ms, "PlayMap's air blacked out by Patterns");
  facts.blackoutOnMs = since(t);
  const off = await pat.say('BLACKOUT OFF');
  if (off !== 'OK') throw new Error(`Patterns answered BLACKOUT OFF with ${JSON.stringify(off)}`);
  t = Date.now();
  await until(async () => {
    const a = (await pm.state()).air;
    return a.blackout === false && a.patternsBlackout === false;
  }, ms, "PlayMap's air back from Patterns' blackout");
  facts.blackoutOffMs = since(t);
  return facts;
}

/**
 * Unknown words are refused, never defaulted (row S7.3's first three): Patterns' wire and PlayMap's answer ERR naming the
 * word; the rig's command route answers 400 with ok:false and words naming it, to the operator's own PIN (so it is the
 * word that is refused, not the request).
 */
async function unknownWordsRefused(pat, pm, sw) {
  const facts = {};
  const pa = await pat.say('FROBNICATE');
  if (!/^ERR /.test(pa) || !/frobnicate/i.test(pa)) throw new Error(`Patterns answered FROBNICATE with ${JSON.stringify(pa)}`);
  facts.patterns = pa;
  const pma = await pm.say('FROBNICATE');
  if (!/^ERR /.test(pma) || !/frobnicate/i.test(pma)) throw new Error(`PlayMap answered FROBNICATE with ${JSON.stringify(pma)}`);
  facts.playmap = pma;
  const url = `http://${sw.address}/api/frobnicate`;
  const res = await request('POST', url, { headers: { 'X-Stagewatch-Pin': sw.pin, 'X-Request-Id': 'interop-s7-3' } });
  if (res.status !== 400 || !res.json || res.json.ok !== false || !/frobnicate/i.test(res.json.error || '')) {
    throw new Error(`the rig answered POST /api/frobnicate with ${res.status} ${res.text.slice(0, 160)}`);
  }
  facts.stagewatch = `${res.status} ${res.json.error}`;
  return facts;
}

/**
 * No secret anywhere (row S7.1). Before the apps stop, the driver reads each one's published state once more and keeps what
 * it read: the rig's context, operator frame, report and its CSV (with the PIN, as the operator's own pages read them),
 * Patterns' and PlayMap's CONTEXT over their wires (kept with every line those wires heard), and AGMessage's context,
 * health and admin settings (its admin helper's output, the settings' context token said by its place). Then the apps stop (in the reverse of their start, each as it is asked to), the files
 * each keeps a secret in by design are removed (named in the matrix), and every file the run kept is swept for every
 * secret in the register, with the matrix as written so far and builds.json. A control keeps the sweep honest: swept, the
 * register's own folder shows every secret held there. Resolves with what was swept; a finding fails the row, naming the
 * file and the secret's name.
 */
async function noSecretAnywhere({ run, matrix, apps }) {
  const sw = apps.stagewatch;
  const pin = { 'X-Stagewatch-Pin': sw.pin };
  const captured = [];
  for (const [name, route] of [['stagewatch.context.json', '/api/context'], ['stagewatch.state.json', '/api/state'],
    ['stagewatch.report.html', '/report'], ['stagewatch.report.csv', '/api/report.csv']]) {
    const res = await get(`http://${sw.address}${route}`, 5000, pin);
    if (res.status !== 200) throw new Error(`the rig answered GET ${route} with ${res.status}`);
    captured.push(run.rel(run.capture(name, res.text)));
  }
  for (const [name, app] of [['patterns', apps.patterns], ['playmap', apps.playmap]]) {
    const said = await app.say('CONTEXT');
    if (!/^OK [{]/.test(said)) throw new Error(`${name} answered CONTEXT with ${JSON.stringify(said.slice(0, 120))}`);
  }
  await apps.agmessage.context();
  await apps.agmessage.hostHealth();
  await apps.agmessage.settings();

  const removed = [];
  const exits = {};
  for (const [name, app] of Object.entries(apps).reverse()) {
    exits[name] = await app.stop();
    removed.push(...run.removeSecretFiles(app.secretFiles()));
  }
  const secretsDir = run.secrets.dir;
  const control = sweep(run.secrets, { roots: [secretsDir] });
  const held = fs.readdirSync(secretsDir).filter((f) => run.secrets.byName.has(f));
  const seen = new Set(control.found.flatMap((f) => f.names));
  const unseen = held.filter((n) => !seen.has(n));
  if (held.length === 0 || unseen.length) throw new Error(`the sweep's control: the register's own folder did not show ${unseen.join(', ') || 'any secret'}`);

  const result = sweep(run.secrets, {
    roots: [run.dir, path.join(run.out, '..', 'builds.json')],
    skip: [secretsDir],
    texts: { 'matrix (as written so far)': JSON.stringify(matrix.doc) },
  });
  if (result.found.length) {
    throw new Error(`a secret where none may be: ${result.found.map((f) => `${f.where.startsWith('/') ? run.rel(f.where) : f.where} holds ${f.names.join(', ')}`).join('; ')}`);
  }
  return { swept: { files: result.files, bytes: result.bytes }, secrets: run.secrets.names(), captured, removed, exits };
}

module.exports = {
  patternsHears, playmapHears, agmessageHears, playmapFollowsPatterns, unknownWordsRefused, noSecretAnywhere,
  stagewatchLinks, allLive, stillHeld, readLockout, lockoutAfter, namesAddress, since, left,
};
