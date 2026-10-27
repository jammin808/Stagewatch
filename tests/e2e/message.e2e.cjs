// The speaker's message and its acknowledgement, against the rig with one node key: the contract a show app (Patterns) that
// sends its desk's message through Stagewatch reads. A message that asks for its acknowledgement makes a speaker signal:
// the receipt names it (refs.messageId), frames carry it (messageId, messageAck "sent", messageAckBy) and list it in
// signals, and an ACK the app forwards with its desk's name turns it "done" by that name and the device's address (a
// message signal completes on its first answer, as the app's board completes it, so messageAck is never "seen": read seen
// or done as the answer). The name is cut to fit 40 characters, never the address, so a long or made-up name cannot hide
// where the answer came from. An ACK that would change nothing is refused: a 409 receipt while the signal is listed but
// answered, a 403 at the door once it has left the frame's signals (cancelled, or 30 s after its answer); a newer message
// cancels the old signal; clearmessage clears it; a slow show's receipt says only accepted, with no refs, and the frame
// names the signal once the show runs. The speaker's page answers with its own button and the producer's page says who
// answered. The journal and the context say each step as the app's do. These are the bytes Patterns' fake Stagewatch is to
// copy, with three rules no case here can show: a receipt's effect is free words (the rig's own; the app's is the status
// line it made, often empty for a remote message), which a peer neither shows nor asserts; an /ack message answered applied
// without refs.messageId (the app, under a licence with no crew cues) has no acknowledgement to follow; and the three frame
// fields go back to 0, "", "" 30 s after the answer or the cancel, the message maybe still on stage, so a peer keeps done
// for the messageId it sent. With crew links required (not the rig's), a show app's forwarded ACK is refused at the door:
// 403 "This screen can't confirm cues for speaker. Open its crew link (Showcaller page → Crew links)."
'use strict';
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const { Rig, until } = require('./rig.cjs');

const GONE = 'That cue has already been answered or cleared.';
const RECEIPT_WAIT = 3000;   // WebServer.ReceiptWait

let rig, key, link, browser;
before(async () => {
  rig = await Rig.create({ nodeKeys: 1, journal: '-' });
  [key] = rig.keys;
  // The show app follows the rig over /ws with its node key, as Patterns does: frames are read where it reads them.
  link = await rig.link('node=pat00001&t=patterns', key.secret);
  await link.next((j) => j.type === 'auth' && j.ok === true, 5000, 'the verdict');
  await link.next((j) => j.type === 'state', 5000, 'a first frame');
});
after(async () => {
  // The rig goes whatever a close above threw: a rig left running would outlive its folder's removal at this file's end.
  try {
    if (link) await link.close();
    if (browser) await browser.close();
  } finally {
    if (rig) await rig.dispose();
  }
});

/** A POST as a show app sends it: its node key in the Authorization header and a request id. Status, raw text and JSON. */
async function post(route, requestId, credential = key.secret) {
  const headers = { Authorization: 'Bearer ' + credential };
  if (requestId) headers['X-Request-Id'] = requestId;
  const res = await fetch(rig.base + route, { method: 'POST', headers });
  const text = await res.text();
  return { status: res.status, text, json: JSON.parse(text) };
}

/** The message route as Patterns' speaker lane forms it: one percent-encoded segment, then urgency, flash and ack. */
const messageRoute = (text, flash = 'slow', ack = true) => `/api/message/${encodeURIComponent(text)}/info/${flash}${ack ? '/ack' : ''}`;

/** The first frame on the show app's link from index `from` on that passes pred. */
const frame = (pred, what, from, ms = 5000) => link.next((j) => j.type === 'state' && pred(j), ms, what, from).then((m) => m.json);

/** The speaker signals a frame lists. */
const messageSignals = (f) => f.signals.filter((g) => g.kind === 'message' && g.target === 'speaker');

/** The rig's context once its message item passes pred (the context is built once a second). */
const contextWith = (pred, what) =>
  until(async () => {
    const c = await rig.context();
    const item = (c.items || []).find((i) => i.id === 'message');
    return item && pred(item, c) ? { item, context: c } : null;
  }, 5000, what);

test('a message that asks for its acknowledgement: the receipt names its signal, frames carry it sent, a forwarded ACK turns it done by name, and a second ACK is refused', async () => {
  let from = link.messages.length;
  const sent = await post(messageRoute('Wrap up'), 'pt1');
  assert.equal(sent.status, 200, sent.text);
  const id = sent.json.refs.messageId;
  assert.ok(Number.isInteger(id) && id >= 1, 'refs.messageId names the signal: ' + sent.text);
  // The bytes, as Patterns' fake replays them: applied, the rig's effect, no error, the signal in refs.
  assert.ok(sent.text.startsWith('{"type":"receipt","id":"pt1","ok":true,"stage":"applied","effect":"'), sent.text);
  assert.ok(sent.text.endsWith(`"error":null,"refs":{"messageId":${id}}}`), sent.text);

  const onStage = await frame((j) => j.messageId === id, 'the frame naming the signal', from);
  assert.equal(onStage.message, 'Wrap up');
  assert.equal(onStage.urgency, 'info');
  assert.equal(onStage.flash, 'slow');
  assert.equal(onStage.messageAck, 'sent');
  assert.equal(onStage.messageAckBy, '');
  assert.deepEqual(messageSignals(onStage).map(({ id: i, text, state, by }) => ({ id: i, text, state, by })),
    [{ id, text: 'Wrap up', state: 'sent', by: '' }], 'the signal is listed, so a page without the PIN may answer it');
  const waiting = await contextWith((i) => i.light === 'amber', 'the context waiting for the speaker');
  assert.equal(waiting.item.state, 'waiting for the speaker to see it');

  // The show app forwards its page's ACK with its desk's name: a public verb, so its key speaks for no operator, and the
  // device's address is added to the name.
  from = link.messages.length;
  const ack = await post(`/api/ack/${id}/seen/${encodeURIComponent('Patterns FOH')}`, 'pt2');
  assert.equal(ack.status, 200, ack.text);
  assert.equal(ack.json.ok, true);
  assert.equal(ack.json.stage, 'applied');
  // The app's status line for an answer, which the rig says too; free words, never a peer's to show or assert.
  assert.equal(ack.json.effect, '✓ speaker confirmed "Wrap up" (Patterns FOH (127.0.0.1))');
  assert.deepEqual(ack.json.refs, {});
  const answered = await frame((j) => j.messageId === id && j.messageAck !== 'sent', 'the frame after the ACK', from);
  assert.equal(answered.messageAck, 'done', 'a message signal completes on its first answer');
  assert.ok(answered.messageAckBy.startsWith('Patterns FOH'), answered.messageAckBy);
  assert.equal(answered.messageAckBy, 'Patterns FOH (127.0.0.1)');
  assert.equal(messageSignals(answered).find((g) => g.id === id).state, 'done');
  const seen = await contextWith((i) => i.light === 'green', 'the context acknowledged');
  assert.equal(seen.item.state, 'acknowledged');
  const recent = seen.context.recent.map((r) => r.text).join('\n');
  assert.match(recent, / Signal: Message speaker: Wrap up/, 'the journal keeps the signal, not a plain message row');
  assert.match(recent, / Ack: Message speaker: Wrap up \(Done by Patterns FOH \(127\.0\.0\.1\)\)/, 'the journal keeps the answer');
  assert.doesNotMatch(recent, / Message: Wrap up/, 'one row for the message that asked for its acknowledgement');

  // The same ACK again would change nothing: the signal is still listed, so the door lets it in, and the show refuses it.
  from = link.messages.length;
  const again = await post(`/api/ack/${id}/seen/${encodeURIComponent('Patterns FOH')}`, 'pt3');
  assert.equal(again.status, 409, again.text);
  assert.equal(again.text, `{"type":"receipt","id":"pt3","ok":false,"stage":"applied","effect":"","error":"${GONE}","refs":{}}`);
  await new Promise((r) => setTimeout(r, 600));
  const after = link.messages.slice(from).filter((m) => m.json && m.json.type === 'state' && m.json.messageId === id);
  assert.ok(after.every((m) => m.json.messageAck === 'done' && m.json.messageAckBy === 'Patterns FOH (127.0.0.1)'), 'no frame since says otherwise');
  // And the show as it stands, read after the refusal (a frame need not have come in the wait): still done, by the same name.
  const now = await rig.request('GET', '/api/state', key.secret);
  assert.equal(now.status, 200, now.text);
  assert.deepEqual([now.json.messageId, now.json.messageAck, now.json.messageAckBy], [id, 'done', 'Patterns FOH (127.0.0.1)'], 'the refused ACK moved nothing');
});

test('who answered keeps the device\'s address: a 40-character desk name is cut, never the address, and a made-up address stays a name', async () => {
  // Patterns' desk name may be up to 40 characters: the name gives way, the address the web server adds never does.
  const desk = 'Patterns Front of House 2nd Main Hall AB';
  assert.equal(desk.length, 40);
  let from = link.messages.length;
  const sent = await post(messageRoute('Five minutes'), 'pt40');
  const id = sent.json.refs.messageId;
  const ack = await post(`/api/ack/${id}/seen/${encodeURIComponent(desk)}`, 'pt41');
  assert.equal(ack.status, 200, ack.text);
  const cut = await frame((j) => j.messageId === id && j.messageAck === 'done', 'the frame after the long name\'s answer', from);
  assert.equal(cut.messageAckBy, 'Patterns Front of House 2nd (127.0.0.1)', 'the name cut and trimmed to fit 40 characters, the address whole');
  assert.ok(cut.messageAckBy.length <= 40);

  // A name dressed as another app's name and address, padded to 40 characters: the real address still ends who answered.
  from = link.messages.length;
  const again = await post(messageRoute('Four minutes'), 'pt42');
  const id2 = again.json.refs.messageId;
  const forged = await post(`/api/ack/${id2}/seen/${encodeURIComponent('Patterns FOH (10.0.0.5)'.padEnd(40, ' '))}`, 'pt43');
  assert.equal(forged.status, 200, forged.text);
  const named = await frame((j) => j.messageId === id2 && j.messageAck === 'done', 'the frame after the made-up name\'s answer', from);
  assert.equal(named.messageAckBy, 'Patterns FOH (10.0.0.5) (127.0.0.1)');
});

test('a newer message cancels the old signal and its ACK is refused at the door; a message without ack names none; clearmessage clears', async () => {
  let from = link.messages.length;
  const a = await post(messageRoute('Two minutes'), 'pt10');
  const b = await post(messageRoute('One minute', 'fast'), 'pt11');
  assert.equal(a.status, 200, a.text);
  assert.equal(b.status, 200, b.text);
  const [ida, idb] = [a.json.refs.messageId, b.json.refs.messageId];
  assert.equal(idb, ida + 1, 'signal ids count on');
  const second = await frame((j) => j.messageId === idb, 'the second message on stage', from);
  assert.equal(second.message, 'One minute');
  assert.equal(second.messageAck, 'sent');
  assert.ok(!messageSignals(second).some((g) => g.id === ida), 'the first signal is cancelled, gone from signals');

  const late = await post(`/api/ack/${ida}/seen/${encodeURIComponent('Patterns FOH')}`, 'pt12');
  assert.equal(late.status, 403, late.text);
  assert.deepEqual(late.json, { ok: false, error: GONE });
  // The operator's ACK passes the door; the show refuses it with the same words.
  const operator = await rig.run(`ack ${ida} seen Desk`);
  assert.equal(operator.ok, false, JSON.stringify(operator));
  assert.equal(operator.error, GONE);

  // A message without ack carries no signal: no refs, and the frame's three fields say none.
  from = link.messages.length;
  const plain = await post(messageRoute('Thank you', 'off', false), 'pt13');
  assert.equal(plain.status, 200, plain.text);
  assert.deepEqual(plain.json.refs, {});
  const thanks = await frame((j) => j.message === 'Thank you', 'the plain message on stage', from);
  assert.deepEqual([thanks.messageId, thanks.messageAck, thanks.messageAckBy], [0, '', '']);
  assert.ok(!messageSignals(thanks).some((g) => g.id === idb), 'the second signal is cancelled too');

  // clearmessage, with the node key as a show app clears its own: the stage and its signal both go.
  const d = await post(messageRoute('Hold for applause'), 'pt14');
  const idd = d.json.refs.messageId;
  from = link.messages.length;
  const cleared = await post('/api/clearmessage', 'pt15');
  assert.equal(cleared.status, 200, cleared.text);
  assert.equal(cleared.json.stage, 'applied');
  assert.deepEqual(cleared.json.refs, {});
  const empty = await frame((j) => j.message === '', 'the cleared stage', from);
  assert.deepEqual([empty.messageId, empty.messageAck, empty.messageAckBy], [0, '', '']);
  assert.ok(!messageSignals(empty).some((g) => g.id === idd), 'clearmessage cancels the signal');
  const ctx = await until(async () => {
    const c = await rig.context();
    return (c.items || []).some((i) => i.id === 'message') ? null : c;
  }, 5000, 'the context with no message');
  assert.ok(ctx, 'no message item once the stage is clear');
});

test('a slow show: the receipt says only accepted, with no refs, and the frame names the signal once the show runs', async () => {
  const stall = await rig.stall(RECEIPT_WAIT + 1500);
  const from = link.messages.length;
  const slow = await post(messageRoute('Lights in five'), 'pt20');
  assert.equal(slow.status, 200, slow.text);
  assert.equal(slow.text, '{"type":"receipt","id":"pt20","ok":true,"stage":"accepted","effect":"","error":null,"refs":{}}');
  await stall.running;
  const bound = await frame((j) => j.message === 'Lights in five' && j.messageId > 0, 'the frame naming the signal once the show runs', from);
  assert.equal(bound.messageAck, 'sent', 'a new messageId with the text it sent: what a show app binds an accepted message by');
  assert.equal(messageSignals(bound).find((g) => g.id === bound.messageId).text, 'Lights in five');
});

test('the pages: the speaker answers with the viewer\'s button, and the producer\'s page says who answered', async () => {
  browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 720 } });
  const errors = [];
  const viewer = await ctx.newPage();
  viewer.on('pageerror', (e) => errors.push('viewer: ' + e.message));
  await viewer.goto(rig.base + '/viewer');
  const producer = await ctx.newPage();
  producer.on('pageerror', (e) => errors.push('producer: ' + e.message));
  await producer.goto(rig.base + '/producer?pin=' + encodeURIComponent(rig.pin));

  const from = link.messages.length;
  const sent = await post(messageRoute('Please wrap up'), 'pt30');
  const id = sent.json.refs.messageId;
  await viewer.locator('#ackbox button').waitFor({ timeout: 8000 });
  assert.equal(await viewer.locator('#ackbox').getAttribute('data-id'), String(id), 'the button answers this message\'s signal');
  await producer.waitForFunction(() => document.getElementById('ack').textContent.startsWith('⏳ Waiting for the speaker'), null, { timeout: 8000 });

  await viewer.locator('#ackbox button').click();
  const answered = await frame((j) => j.messageId === id && j.messageAck === 'done', 'the frame after the speaker\'s answer', from);
  assert.equal(answered.messageAckBy, '127.0.0.1', 'a page without a name is named by its address');
  await viewer.waitForFunction(() => document.querySelector('#ackbox .ok') !== null, null, { timeout: 8000 });
  await producer.waitForFunction(() => document.getElementById('ack').textContent === '✓ Speaker acknowledged “Please wrap up” (127.0.0.1)', null, { timeout: 8000 });
  assert.deepEqual(errors, []);
  await ctx.close();
});
