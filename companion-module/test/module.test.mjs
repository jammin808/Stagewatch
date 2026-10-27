import { test } from 'node:test'
import assert from 'node:assert/strict'
import net from 'node:net'
import { StagewatchConnection, quote } from '../dist/connection.js'
import {
	configVariables,
	contrastText,
	deriveVariables,
	diffValues,
	feedbacksAffected,
	FEEDBACK_IDS,
	formatShort,
	hexToNumber,
} from '../dist/state.js'

const snap = (over = {}) => ({
	type: 'state', mode: 'countdown', state: 'running', seconds: 299, text: '04:59', total: 600, phase: 'normal',
	color: '#FFFFFF', title: 'Keynote', next: 'Panel', item: 1, items: 4, preset: 0, endTime: 'ends 14:05:00',
	timeOfDay: '14:00:01', offset: 95, schedule: '+01:35 behind · show ends 16:00', message: '', urgency: 'info',
	flash: 'off', flashTimer: false, blackout: false, configVersion: 1, ...over,
})

// ---------------------------------------------------------------- state

test('variables from a frame', () => {
	const v = deriveVariables(snap(), true)
	assert.equal(v.time, '04:59')
	assert.equal(v.time_hms, '00:04:59')
	assert.equal(v.minutes, '04')
	assert.equal(v.seconds_part, '59')
	assert.equal(v.progress, 50)
	assert.equal(v.elapsed, '05:01')
	assert.equal(v.item, 2)
	assert.equal(v.offset, '+01:35')
	assert.equal(v.end_time, '14:05:00')
	assert.equal(v.running, true)
})

test('overtime variables', () => {
	const v = deriveVariables(snap({ seconds: -75, text: '-01:15', state: 'expired', phase: 'overtime' }), true)
	assert.equal(v.sign, '-')
	assert.equal(v.time_short, '-01:15')
	assert.equal(v.overtime, true)
	assert.equal(v.running, true) // still counting
	assert.equal(v.progress, 0)
	const held = deriveVariables(snap({ seconds: 0, text: '00:00', state: 'expired', phase: 'overtime' }), true)
	assert.equal(held.running, false) // held at zero
})

test('offline blanks the timer instead of freezing it', () => {
	const v = deriveVariables(null, false)
	assert.equal(v.time, 'OFFLINE')
	assert.equal(v.connected, false)
	const changed = diffValues(deriveVariables(snap(), true), v)
	assert.equal(changed.title, '') // stale title cleared
	assert.equal(changed.time, 'OFFLINE')
})

test('diff sends only what changed', () => {
	const a = deriveVariables(snap(), true)
	const b = deriveVariables(snap({ seconds: 298, text: '04:58' }), true)
	const d = diffValues(a, b)
	assert.deepEqual(Object.keys(d).sort(), ['elapsed', 'seconds', 'seconds_part', 'speaker_time', 'time', 'time_hms', 'time_short'].sort())
	assert.deepEqual(diffValues(a, a), {})
})

test('feedback rechecks are targeted', () => {
	assert.deepEqual(feedbacksAffected(snap(), snap(), false), [])
	assert.deepEqual(new Set(feedbacksAffected(snap(), snap({ blackout: true }), false)), new Set(['blackout']))
	const tick = new Set(feedbacksAffected(snap(), snap({ seconds: 298 }), false))
	assert.ok(tick.has('time_below') && !tick.has('blackout') && !tick.has('current_item'))
	const phase = new Set(feedbacksAffected(snap(), snap({ phase: 'warning', color: '#F59E0B' }), false))
	assert.ok(phase.has('phase') && phase.has('phase_color'))
	assert.equal(feedbacksAffected(null, snap(), false).length, FEEDBACK_IDS.length)
})

test('config variables', () => {
	const v = configVariables({
		presets: [{ slot: 1, label: '1 min', seconds: 60 }, { slot: 2, label: 'Q&A', seconds: -1 }],
		messages: [],
		rundown: [{ title: 'Welcome', speaker: 'Ana', seconds: 300 }],
	})
	assert.equal(v.preset_1_duration, '01:00')
	assert.equal(v.preset_2_duration, 'count up')
	assert.equal(v.item_1_title, 'Welcome')
	assert.equal(v.item_1_speaker, 'Ana')
})

test('formatting and colours', () => {
	assert.equal(formatShort(3725), '1:02:05')
	assert.equal(formatShort(-5), '-00:05')
	assert.equal(hexToNumber('#F97316'), 0xf97316)
	assert.equal(hexToNumber('nonsense'), 0xffffff)
	assert.equal(contrastText(0xffffff), 0x000000)
	assert.equal(contrastText(0xdc2626), 0xffffff)
	assert.equal(quote('Say "hi"\nnow \\ ok'), '"Say \\"hi\\"\\nnow \\\\ ok"')
})

// ---------------------------------------------------------------- connection (scripted fake server)

const FAST = { pingEveryMs: 100, deadAfterMs: 400, connectTimeoutMs: 500, deniedRetryMs: 300, minBackoffMs: 50, maxBackoffMs: 200 }

function fakeServer(onClient) {
	return new Promise((resolve) => {
		const sockets = new Set()
		const server = net.createServer((s) => {
			sockets.add(s)
			s.on('close', () => sockets.delete(s))
			s.on('error', () => {})
			onClient(s)
		})
		server.listen(0, '127.0.0.1', () =>
			resolve({
				port: server.address().port,
				sockets,
				close: () => new Promise((r) => { for (const s of sockets) s.destroy(); server.close(() => r()) }),
			}),
		)
	})
}

const waitFor = (emitter, event, pred = () => true, ms = 3000) =>
	new Promise((resolve, reject) => {
		const t = setTimeout(() => reject(new Error(`timeout waiting for ${event}`)), ms)
		const h = (v) => { if (pred(v)) { clearTimeout(t); emitter.off(event, h); resolve(v) } }
		emitter.on(event, h)
	})

/** A well-behaved Stagewatch: v2 frames after SUBSCRIBE JSON, PONG to PING, records commands. */
function stagewatch(received, { split = false, stuck = null } = {}) {
	return (s) => {
		s.setEncoding('utf8')
		s.write('READY stagewatch/2\nSTATE mode=Countdown state=Idle preset=-\n')
		let buf = ''
		s.on('data', (d) => {
			buf += d
			let i
			while ((i = buf.indexOf('\n')) >= 0) {
				const line = buf.slice(0, i)
				buf = buf.slice(i + 1)
				if (line === 'SUBSCRIBE JSON') {
					const out = `CONFIG ${JSON.stringify({ presets: [{ slot: 1, label: 'One', seconds: 60 }], messages: [], rundown: [] })}\nSNAPSHOT ${JSON.stringify(snap())}\n`
					if (split) {
						// Deliver in awkward fragments to prove line reassembly.
						for (let k = 0; k < out.length; k += 7) s.write(out.slice(k, k + 7))
					} else s.write(out)
				} else if (line.startsWith('PING')) s.write(line.replace('PING', 'PONG') + (stuck?.on ? ' STUCK' : '') + '\n')
				else received.push(line)
			}
		})
	}
}

test('connects, subscribes, reassembles fragmented frames, sends commands', async () => {
	const received = []
	const srv = await fakeServer(stagewatch(received, { split: true }))
	const c = new StagewatchConnection('127.0.0.1', srv.port, FAST)
	const cfg = waitFor(c, 'config')
	const first = waitFor(c, 'snapshot')
	const ok = waitFor(c, 'status', (s) => s.kind === 'connected')
	c.start()
	assert.equal((await cfg).presets[0].label, 'One')
	assert.equal((await first).text, '04:59')
	await ok
	assert.equal(c.send('NEXT'), true)
	assert.equal(c.send('MESSAGE "a\nb"'), true) // embedded newline can't inject a second command
	await new Promise((r) => setTimeout(r, 100))
	assert.deepEqual(received, ['NEXT', 'MESSAGE "a b"'])
	c.destroy()
	await srv.close()
})

test('reconnects after the server drops, and does not queue presses while offline', async () => {
	const received = []
	const srv = await fakeServer(stagewatch(received))
	const c = new StagewatchConnection('127.0.0.1', srv.port, FAST)
	c.start()
	await waitFor(c, 'status', (s) => s.kind === 'connected')
	for (const s of srv.sockets) s.destroy()
	await waitFor(c, 'status', (s) => s.kind === 'disconnected')
	assert.equal(c.send('START'), false) // dropped, not queued
	await waitFor(c, 'status', (s) => s.kind === 'connected')
	await new Promise((r) => setTimeout(r, 100))
	assert.ok(!received.includes('START'), 'stale press must not be replayed after reconnect')
	c.destroy()
	await srv.close()
})

test('detects a silent dead link via heartbeat', async () => {
	let silent = false
	const received = []
	const behave = stagewatch(received)
	const srv = await fakeServer((s) => {
		if (silent) return // accept but never speak: a black-holed connection
		behave(s)
		s.on('data', () => {})
	})
	const c = new StagewatchConnection('127.0.0.1', srv.port, FAST)
	c.start()
	await waitFor(c, 'status', (s) => s.kind === 'connected')
	// Freeze the existing socket: stop answering PINGs.
	for (const s of srv.sockets) { s.removeAllListeners('data'); s.pause() }
	silent = true
	const t0 = Date.now()
	await waitFor(c, 'status', (s) => s.kind === 'disconnected', 2000)
	assert.ok(Date.now() - t0 < 1500, 'dead link detected promptly')
	c.destroy()
	await srv.close()
})

test('licence denial is reported, not spammed', async () => {
	let attempts = 0
	const srv = await fakeServer((s) => {
		attempts++
		s.end('READY stagewatch/2\nDENIED license "Companion control needs a Stagewatch Pro license — the trial has ended."\n')
	})
	const c = new StagewatchConnection('127.0.0.1', srv.port, FAST)
	const statuses = []
	c.on('status', (s) => statuses.push(s.kind))
	const denied = waitFor(c, 'status', (s) => s.kind === 'denied')
	c.start()
	const d = await denied
	assert.match(d.reason, /trial has ended/)
	await new Promise((r) => setTimeout(r, 200))
	assert.ok(!statuses.includes('disconnected'), 'denied stays denied, not "connection lost"')
	assert.ok(attempts <= 2, `retries slowly (${attempts})`)
	c.destroy()
	await srv.close()
})

test('unreachable host reports failure and keeps retrying', async () => {
	const srv = await fakeServer(() => {})
	const port = srv.port
	await srv.close()
	const c = new StagewatchConnection('127.0.0.1', port, FAST)
	let fails = 0
	c.on('status', (s) => { if (s.kind === 'disconnected') fails++ })
	c.start()
	await new Promise((r) => setTimeout(r, 700))
	assert.ok(fails >= 2, `retried (${fails})`)
	c.destroy()
})

test('malformed JSON is reported and survived', async () => {
	const srv = await fakeServer((s) => {
		s.write('SNAPSHOT {not json}\n')
		s.write(`SNAPSHOT ${JSON.stringify(snap({ text: '01:00' }))}\n`)
	})
	const c = new StagewatchConnection('127.0.0.1', srv.port, FAST)
	const err = waitFor(c, 'protocolError')
	const good = waitFor(c, 'snapshot')
	c.start()
	await err
	assert.equal((await good).text, '01:00')
	c.destroy()
	await srv.close()
})

// ---------------------------------------------------------------- Stagewatch 2.0

import { adviceCommand, deptState, latestOpenFor, signed, slug, standingBy } from '../dist/state.js'

const v2 = (over = {}) => snap({
	speakerText: '5 MIN', cue: '5 MIN', finishBy: 'FINISH BY 14:05', preview: false, stretching: true, room: 'Hall A',
	messageId: 3, messageAck: 'sent', messageAckBy: '',
	signals: [
		{ id: 3, kind: 'message', target: 'speaker', text: 'Wrap up', state: 'sent', escalated: false, age: 5, by: '' },
		{ id: 4, kind: 'standby', target: 'Lighting', text: 'LX 12', state: 'seen', escalated: false, age: 0, by: 'LX' },
		{ id: 5, kind: 'go', target: 'Sound', text: '', state: 'sent', escalated: true, age: 25, by: '' },
	],
	slides: { slide: 14, count: 30, next: 'Results', notes: '', ghost: 130, projected: 1900, pace: 'behind', rehearsing: false },
	speech: null,
	playout: { title: 'Sizzle', remaining: 44, duration: 90, running: true, source: 'vMix' },
	qaLive: '', qaPending: 2, qaQueued: 1, qaFit: 3,
	hardOut: 'Curfew 17:30: 04:00 OVER', hardOutMargin: -240, forecast: 'Likely end 17:36', delay: 120, clockOffsetMs: -200,
	timings: [
		{ i: 0, title: 'Opening', speaker: 'Ana', kind: 'talk', seconds: 600, adjust: 0, planned: '09:00', expected: '09:00', expectedEnd: '09:10', actual: '09:00:05', actualEnd: '09:11:00', status: 'done', delay: 0, conflict: false, fixed: false, hardOut: '' },
		{ i: 1, title: 'Keynote', speaker: 'Bob', kind: 'talk', seconds: 1800, adjust: 0, planned: '09:10', expected: '09:11', expectedEnd: '09:41', actual: '09:11:00', actualEnd: '', status: 'live', delay: 60, conflict: false, fixed: false, hardOut: '' },
		{ i: 2, title: 'Panel', speaker: '', kind: 'panel', seconds: 1800, adjust: -300, planned: '09:40', expected: '09:45', expectedEnd: '10:10', actual: '', actualEnd: '', status: 'upcoming', delay: 300, conflict: false, fixed: false, hardOut: '' },
	],
	advice: [
		{ key: 'hardout-3', level: 'urgent', text: 'Curfew at risk', command: null, label: null },
		{ key: 'trim-2', level: 'suggest', text: 'Cut panel', command: 'trim 3 -240', label: 'Cut 04:00' },
	],
	rooms: [{ room: 'Hall B', host: '10.0.0.6', webPort: 9924, title: 'Workshop', text: '12:00', state: 'running', phase: 'normal', offset: 400, hardOut: '', hardOutMargin: null, openSignals: 0, stale: false, self: false }],
	...over,
})

test('2.0 variables', () => {
	const cfg = { presets: [], messages: [], rundown: [], departments: ['Lighting', 'Sound', 'Video / LED'], ladders: [{ id: 'keynote', name: 'Keynote' }], ladder: 'keynote' }
	const v = deriveVariables(v2(), true, cfg)
	assert.equal(v.cue, '5 MIN')
	assert.equal(v.finish_by, '14:05')
	assert.equal(v.speaker_time, '5 MIN')
	assert.equal(v.room, 'Hall A')
	assert.equal(v.ladder, 'Keynote')
	assert.equal(v.message_ack, 'sent')
	assert.equal(v.cues_open, 3)
	assert.equal(v.cues_waiting, 2)
	assert.equal(v.cues_unanswered, 1)
	assert.equal(v.slide, 14)
	assert.equal(v.ghost, '+02:10')
	assert.equal(v.vt_remaining, '00:44')
	assert.equal(v.hard_out_margin, '-04:00')
	assert.equal(v.delay, '02:00')
	assert.equal(v.next_expected, '09:45')
	assert.equal(v.qa_new, 2)
	assert.equal(v.advice_1, 'Curfew at risk')
	assert.equal(v.advice_2_action, 'Cut 04:00')
	assert.equal(v.item_3_delay, '+05:00')
	assert.equal(v.item_1_expected, '') // done items have no expectation
	assert.equal(v.item_2_status, 'live')
	assert.equal(v.dept_lighting, 'standby_ack')
	assert.equal(v.dept_sound, 'go')
	assert.equal(v.dept_video_led, 'clear')
	assert.equal(v.rooms_late, 1)
})

test('2.0 helpers', () => {
	assert.equal(slug('Video / LED'), 'video_led')
	assert.equal(signed(-75), '-01:15')
	assert.equal(signed(0), '±00:00')
	assert.equal(deptState(v2(), 'lighting'), 'standby_ack')
	assert.ok(standingBy(v2()).some((g) => g.target.toLowerCase() === 'lighting'))
	assert.equal(deriveVariables(v2(), true).standing_by, standingBy(v2()).length)
	assert.equal(adviceCommand(v2(), 2), 'trim 3 -240')
	assert.equal(adviceCommand(v2(), 1), null)
	assert.equal(adviceCommand(v2(), 9), null)
	assert.equal(latestOpenFor(v2(), 'speaker')?.id, 3)
	assert.equal(latestOpenFor(v2(), 'moderator'), undefined)
	// Older Stagewatch (no 2.0 fields) still works.
	const old = deriveVariables(snap(), true)
	assert.equal(old.cue, '')
	assert.equal(old.cues_open, 0)
})

test('2.0 feedback rechecks are targeted', () => {
	const a = v2()
	const ack = feedbacksAffected(a, v2({ messageAck: 'done' }), false)
	assert.ok(ack.includes('message_ack') && !ack.includes('blackout'))
	const sig = feedbacksAffected(a, v2({ signals: [] }), false)
	assert.ok(sig.includes('dept_state') && sig.includes('cue_waiting') && sig.includes('standing_by'))
	const tick = feedbacksAffected(a, v2({ seconds: 298 }), false)
	assert.ok(tick.includes('remaining') && tick.includes('progress_value') && !tick.includes('dept_state'))
	for (const id of ['message_ack', 'dept_state', 'hard_out_risk', 'remaining', 'progress_value', 'advice_waiting']) assert.ok(FEEDBACK_IDS.includes(id))
})

test('RECORD lines become record events', async () => {
	const srv = await fakeServer((s) => {
		s.write(`READY stagewatch/2\nSNAPSHOT ${JSON.stringify(snap())}\nRECORD nudge 60\nRECORD message "Wrap up" warn slow\n`)
	})
	const c = new StagewatchConnection('127.0.0.1', srv.port, FAST)
	const lines = []
	c.on('record', (l) => lines.push(l))
	c.start()
	await waitFor(c, 'status', (s) => s.kind === 'connected')
	await new Promise((r) => setTimeout(r, 100))
	assert.deepEqual(lines, ['nudge 60', 'message "Wrap up" warn slow'])
	c.destroy()
	await srv.close()
})

// ---------------------------------------------------------------- 2.2: outputs, licence, trims, cues, Q&A

test('2.2 outputs, licence and live-item helpers', async () => {
	const st = await import('../dist/state.js')
	const timings = [
		{ i: 0, title: 'Welcome', speaker: '', kind: 'talk', seconds: 600, adjust: 0, planned: '09:00', expected: '09:00', expectedEnd: '', actual: '', actualEnd: '', status: 'done', delay: 0, conflict: false, fixed: false, hardOut: '' },
		{ i: 1, title: 'Panel', speaker: '', kind: 'panel', seconds: 1800, adjust: -120, planned: '09:10', expected: '09:11', expectedEnd: '', actual: '', actualEnd: '', status: 'live', delay: 60, conflict: false, fixed: false, hardOut: '' },
	]
	const s = snap({ output: 'keyed', ndi: true, timings, signals: [
		{ id: 3, kind: 'go', target: 'Lighting', text: '', state: 'done', escalated: false, age: 1, by: '' },
		{ id: 7, kind: 'message', target: 'speaker', text: 'Wrap', state: 'sent', escalated: true, age: 30, by: '' },
	], questions: [{ id: 9, text: 'Why?', name: '', state: 'live' }] })
	const cfg = { presets: [], messages: [], rundown: [], tier: 'Pro', features: ['runningorder', 'remote'] }
	const v = st.deriveVariables(s, true, cfg)
	assert.equal(v.output, 'keyed')
	assert.equal(v.ndi, true)
	assert.equal(v.licence, 'Pro')
	assert.equal(v.live_kind, 'panel')
	assert.equal(v.live_trim, '-02:00')
	assert.equal(v.qa_live_id, 9)
	assert.equal(st.resolveItem(s, 0), 2)
	assert.equal(st.resolveItem(s, 1), 1)
	assert.equal(st.itemAdjust(s, 2), -120)
	assert.equal(st.newestUnanswered(s)?.id, 7)
	assert.equal(st.newestUnanswered(s, 'lighting'), undefined)
	assert.equal(st.liveQuestion(s)?.id, 9)
	assert.equal(st.hasFeature(cfg, 'crewcues'), false)
	assert.equal(st.hasFeature(cfg, 'runningorder'), true)
	assert.equal(st.hasFeature({ presets: [], messages: [], rundown: [] }, 'crewcues'), true, 'older Stagewatch: assume included')
	const out = st.feedbacksAffected(snap({ output: 'closed' }), snap({ output: 'full' }), false)
	assert.ok(out.includes('output') && !out.includes('ndi'))
	const kind = st.feedbacksAffected(snap({ item: 0, timings }), snap({ item: 1, timings }), false)
	assert.ok(kind.includes('item_kind') && kind.includes('item_trimmed'))
	for (const id of ['output', 'ndi', 'feature_locked', 'item_kind', 'item_trimmed']) assert.ok(FEEDBACK_IDS.includes(id))
})

test('2.3 holding image variables and rechecks', async () => {
	const st = await import('../dist/state.js')
	const cfg = { presets: [], messages: [], rundown: [], holding: [{ id: 'a1b2c3d4e5f6', name: 'Walk-in' }, { id: '0123456789ab', name: 'Sponsors' }] }
	const on = snap({ holding: 'a1b2c3d4e5f6', holdingName: 'Walk-in' })
	const v = st.deriveVariables(on, true, cfg)
	assert.equal(v.holding, 'Walk-in')
	assert.equal(v.holding_on, true)
	assert.equal(st.deriveVariables(snap(), true, cfg).holding_on, false)
	const c = st.configVariables(cfg)
	assert.equal(c.holding_1_name, 'Walk-in')
	assert.equal(c.holding_2_name, 'Sponsors')
	const changed = st.feedbacksAffected(snap(), on, false)
	assert.ok(changed.includes('holding') && !changed.includes('ndi'))
	assert.ok(FEEDBACK_IDS.includes('holding'))
})

test('2.7 LTC in variables and rechecks', async () => {
	const st = await import('../dist/state.js')
	const cfg = { presets: [], messages: [], rundown: [] }
	const on = snap({ ltc: '01:00:10:05', ltcRunning: true, ltcChase: true })
	const v = st.deriveVariables(on, true, cfg)
	assert.equal(v.ltc, '01:00:10:05')
	assert.equal(v.ltc_rolling, true)
	assert.equal(v.ltc_chase, true)
	assert.equal(st.deriveVariables(snap(), true, cfg).ltc, '')
	const changed = st.feedbacksAffected(snap(), on, false)
	assert.ok(changed.includes('ltc_running') && changed.includes('ltc_chase') && !changed.includes('holding'))
	assert.ok(FEEDBACK_IDS.includes('ltc_running'))
})

test('a connected but stuck Stagewatch is reported, and recovery too', async () => {
	const stuck = { on: false }
	const srv = await fakeServer(stagewatch([], { stuck }))
	const c = new StagewatchConnection('127.0.0.1', srv.port, FAST)
	c.start()
	await waitFor(c, 'status', (s) => s.kind === 'connected')
	stuck.on = true
	await waitFor(c, 'status', (s) => s.kind === 'stuck', 1500)
	stuck.on = false
	await waitFor(c, 'status', (s) => s.kind === 'connected', 1500)
	c.destroy()
	await srv.close()
})

test('2.8 extra timer variables and rechecks', async () => {
	const st = await import('../dist/state.js')
	const cfg = { presets: [], messages: [], rundown: [] }
	const on = snap({ aux: [{ n: 1, name: 'VT', text: '02:13', seconds: 133, duration: 180, running: true, up: false, stage: true }], auxLine: 'VT  02:13' })
	const v = st.deriveVariables(on, true, cfg)
	assert.equal(v.aux_1, '02:13')
	assert.equal(v.aux_1_name, 'VT')
	assert.equal(v.aux_1_running, true)
	assert.equal(v.aux_2, '')
	assert.equal(v.aux_on_stage, 'VT  02:13')
	assert.ok(st.feedbacksAffected(snap(), on, false).includes('aux_running'))
	assert.ok(FEEDBACK_IDS.includes('aux_running'))
})

test('2.9 roll and running order variables and rechecks', async () => {
	const st = await import('../dist/state.js')
	const cfg = { presets: [], messages: [], rundown: [] }
	const on = snap({ roll: true, rundown: 'Day 2', rundowns: ['Day 2', 'Day 1'] })
	const v = st.deriveVariables(on, true, cfg)
	assert.equal(v.roll, true)
	assert.equal(v.rundown, 'Day 2')
	assert.equal(v.rundowns, 'Day 2, Day 1')
	const changed = st.feedbacksAffected(snap(), on, false)
	assert.ok(changed.includes('roll') && changed.includes('rundown_live'))
})

test('2.17 window names: one word, empty is every window', async () => {
	const { windowName } = await import('../dist/actions.js')
	assert.equal(windowName(''), 'all')
	assert.equal(windowName('  lobby '), 'lobby')
	assert.equal(windowName('stage-left_2'), 'stage-left_2')
	assert.equal(windowName('Lobby TV'), null)
	assert.equal(windowName('x'.repeat(41)), null)
})

// ---------------------------------------------------------------- 2.20: playout links, receipts, vt_link

import { readFileSync } from 'node:fs'
import { answerShown, parseReceipt, receiptWords, FINAL_WAIT_MS, MAX_PENDING } from '../dist/connection.js'

const LINKS = [
	{ name: 'stage-left', kind: 'playmap', label: 'PlayMap Stage left' },
	{ name: 'patterns', kind: 'patterns', label: 'Patterns' },
]
const LICENSED = { presets: [], messages: [], rundown: [], tier: 'Production', features: ['vmix', 'runningorder'], playoutLinks: LINKS }

/** A stand-in for the module instance: what the definitions read, and what a press sends or logs. */
function fakeInstance(showConfig, configHeard = true) {
	const self = {
		label: 'stagewatch',
		showConfig,
		configHeard,
		snapshot: null,
		sent: [],
		commands: [],
		logs: [],
		actions: {},
		feedbacks: {},
		presets: null,
		structure: null,
		sendPlayout: (line) => self.sent.push(line),
		sendCommand: (line) => self.commands.push(line),
		log: (level, text) => self.logs.push([level, text]),
		setActionDefinitions: (d) => (self.actions = d),
		setFeedbackDefinitions: (d) => (self.feedbacks = d),
		setPresetDefinitions: (structure, presets) => ((self.structure = structure), (self.presets = presets)),
	}
	return self
}

/** test/playout-lines.tsv, the file Stagewatch's suite reads too: name, press and refuse rows. */
function playoutRows() {
	const text = readFileSync(new URL('./playout-lines.tsv', import.meta.url), 'utf8')
	// A checkout may end its lines with CRLF (.gitattributes keeps this file LF; a copy made elsewhere may not): a row never ends in \r.
	return text.split(/\r?\n/).filter((l) => l.length > 0 && !l.startsWith('#')).map((l) => l.split('\t'))
}

test('2.20 playout lines: every press sends exactly its line with a request id, every refusal sends nothing and says why', async () => {
	const { UpdateActions, playoutLinkName } = await import('../dist/actions.js')
	const rows = playoutRows()
	assert.ok(rows.filter((r) => r[0] === 'press').length >= 20 && rows.filter((r) => r[0] === 'refuse').length >= 10)
	for (const r of rows) {
		if (r[0] === 'name') {
			assert.equal(playoutLinkName(r[1]) !== null, r[2] === 'ok', `name ${r[1]} → ${r[2]}`)
			continue
		}
		const self = fakeInstance(LICENSED)
		UpdateActions(self)
		const action = self.actions[r[1]]
		assert.ok(action, `action ${r[1]} is defined`)
		action.callback({ options: JSON.parse(r[2]) })
		assert.deepEqual(self.commands, [], `${r[1]} never sends a playout line without its request id`)
		if (r[0] === 'press') {
			assert.deepEqual(self.sent, [r[3]], `${r[1]} ${r[2]}`)
			assert.equal(r.length, 7, `press row ${r[3]} has its kind, arguments and number`)
		} else {
			assert.equal(r[0], 'refuse')
			assert.deepEqual(self.sent, [], `${r[1]} ${r[2]} sends nothing`)
			assert.ok(self.logs.some(([level, text]) => level === 'warn' && text.startsWith(r[3])), `${r[1]} ${r[2]} logs "${r[3]}…" (${JSON.stringify(self.logs)})`)
		}
	}
	// Every name Stagewatch reads as an action, trust, or another integration's has a row (Stagewatch's suite holds its own lists to them).
	const { PLAYOUT_ACTION_WORDS, PLAYOUT_RESERVED } = await import('../dist/actions.js')
	const no = new Set(rows.filter((r) => r[0] === 'name' && r[2] === 'no').map((r) => r[1]))
	for (const w of [...PLAYOUT_ACTION_WORDS, 'trust', ...PLAYOUT_RESERVED]) assert.ok(no.has(w), `a name row refuses ${w}`)
})

test('2.20 the playout actions and keys exist only where Stagewatch runs playout links under its licence', async () => {
	const { UpdateActions } = await import('../dist/actions.js')
	const { UpdatePresets } = await import('../dist/presets.js')
	const st = await import('../dist/state.js')
	const ids = ['playout_transport', 'playout_go', 'playout_cue']
	const offered = (cfg, heard) => {
		const self = fakeInstance(cfg, heard)
		UpdateActions(self)
		UpdatePresets(self)
		const has = ids.map((id) => id in self.actions)
		assert.ok(has.every((h) => h === has[0]), 'all three or none')
		const section = self.structure.find((s) => s.id === 'playout')
		assert.equal(!!section, has[0], 'the keys come and go with the actions')
		for (const id of section?.definitions ?? [])
			for (const step of self.presets[id].steps ?? []) for (const a of step.down) assert.ok(a.actionId in self.actions, `${id} presses a defined action`)
		return { has: has[0], self, section }
	}
	assert.equal(offered(LICENSED, true).has, true)
	assert.equal(offered({ ...LICENSED, features: ['runningorder'] }, true).has, false, 'a licence without playout: none')
	assert.equal(offered({ ...LICENSED, playoutLinks: undefined }, true).has, false, 'a Stagewatch older than 2.20 (no playoutLinks): none')
	assert.equal(offered(st.EMPTY_CONFIG, false).has, true, 'before any CONFIG: kept, so buttons keep their actions while Stagewatch is away')
	assert.equal(offered({ ...LICENSED, features: undefined }, true).has, true, 'a CONFIG that names no features allows them all')
	// Every link switched off (an empty list): kept, so a press is refused in Stagewatch's words, not dropped by Companion unsaid.
	const none = offered({ ...LICENSED, playoutLinks: [] }, true)
	assert.equal(none.has, true, 'an empty list keeps the actions')
	assert.deepEqual(none.self.actions.playout_transport.options.find((o) => o.id === 'link').choices.map((c) => c.id), [''])
	assert.ok(none.section.definitions.includes('playout_vt_pause') && !none.section.definitions.some((d) => d.startsWith('playout_link_')))
	// The link dropdown: none named first, then each link by its peer's name; a custom name is typed.
	const { self, section } = offered(LICENSED, true)
	const link = self.actions.playout_go.options.find((o) => o.id === 'link')
	assert.deepEqual(link.choices.map((c) => c.id), ['', 'stage-left', 'patterns'])
	assert.equal(link.choices[1].label, 'stage-left — PlayMap Stage left')
	assert.equal(link.allowCustom, true)
	// One set of transport keys with no link named, one per link, and the answer key; each lit by what its press reaches feeding the VT.
	assert.ok(section.definitions.includes('playout_vt_pause') && section.definitions.includes('playout_link_stage-left_last10') && section.definitions.includes('playout_answer'))
	const key = self.presets['playout_link_stage-left_pause']
	assert.deepEqual(key.steps[0].down[0], { actionId: 'playout_transport', options: { link: 'stage-left', what: 'pause', seconds: 10, at: '0:00' } })
	assert.ok(key.feedbacks.some((f) => f.feedbackId === 'vt_link' && f.options.link === 'stage-left' && f.options.last === 0))
	assert.ok(key.feedbacks.some((f) => f.feedbackId === 'vt_link' && f.options.link === 'stage-left' && f.options.running === true && f.options.last === 10))
	assert.ok(key.feedbacks.some((f) => f.feedbackId === 'feature_locked' && f.options.feature === 'vmix'), 'dimmed on a licence without playout')
	// With no link named, a press reaches the only link, else the one feeding the VT: lit by any link feeding it, never by vMix's clip,
	// which no playout press reaches.
	const unnamed = self.presets.playout_vt_pause
	assert.equal(unnamed.steps[0].down[0].options.link, '')
	assert.ok(!unnamed.feedbacks.some((f) => f.feedbackId === 'vt_running'), 'never lit by a clip a press does not reach')
	assert.deepEqual(
		unnamed.feedbacks.filter((f) => f.feedbackId === 'vt_link').map((f) => [f.options.link, f.options.running, f.options.last]),
		[['', false, 0], ['', true, 10]],
	)
	assert.match(unnamed.name, /no link named/)
	const st2 = await import('../dist/state.js')
	const vmixRunning = snap({ playout: { title: 'Sizzle', remaining: 5, duration: 30, running: true, source: 'vMix', link: '', node: '' } })
	for (const f of unnamed.feedbacks.filter((f) => f.feedbackId === 'vt_link'))
		assert.equal(st2.vtFrom(vmixRunning, f.options.link, f.options.running, f.options.last), false, "vMix's clip lights no playout key")
})

test('2.20 a link named vt: its keys and the unnamed set never share an id', async () => {
	const { UpdatePresets } = await import('../dist/presets.js')
	const { playoutLinkName } = await import('../dist/actions.js')
	assert.equal(playoutLinkName('vt'), 'vt', 'vt names a link')
	const self = fakeInstance({ ...LICENSED, playoutLinks: [{ name: 'vt', kind: 'playmap', label: 'PlayMap VT' }, ...LINKS] })
	UpdatePresets(self)
	const defs = self.structure.find((s) => s.id === 'playout').definitions
	assert.equal(new Set(defs).size, defs.length, 'each key listed once')
	assert.equal(defs.length, 4 * 4 + 1, 'four keys with no link named, four for each of three links, and the answer key')
	for (const what of ['last10', 'pause', 'play', 'restart']) {
		assert.equal(self.presets[`playout_vt_${what}`].steps[0].down[0].options.link, '', `playout_vt_${what} names no link`)
		assert.equal(self.presets[`playout_link_vt_${what}`].steps[0].down[0].options.link, 'vt', `playout_link_vt_${what} names vt`)
	}
})

test('2.20 vt_link: the variable, the feedback and its recheck follow the frame\'s playout row', async () => {
	const st = await import('../dist/state.js')
	const { UpdateFeedbacks } = await import('../dist/feedbacks.js')
	const linked = snap({ playout: { title: 'Intro', remaining: 42, duration: 90, running: true, source: 'PlayMap', link: 'stage-left', node: 'a1b2c3d4' } })
	const vmix = snap({ playout: { title: 'Sizzle', remaining: 12, duration: 30, running: false, source: 'vMix', link: '', node: '' } })
	const v = st.deriveVariables(linked, true)
	assert.equal(v.vt_link, 'stage-left')
	assert.equal(v.vt_source, 'PlayMap')
	assert.equal(v.vt_title, 'Intro')
	assert.equal(st.deriveVariables(vmix, true).vt_link, '')
	assert.equal(st.deriveVariables(snap({ playout: null }), true).vt_source, '')
	assert.equal(st.vtFrom(linked, 'stage-left'), true)
	assert.equal(st.vtFrom(linked, 'patterns'), false)
	assert.equal(st.vtFrom(linked, ''), true, 'any link')
	assert.equal(st.vtFrom(linked, 'vmix'), false)
	assert.equal(st.vtFrom(vmix, 'vmix'), true)
	assert.equal(st.vtFrom(vmix, 'vmix', true), false, 'not running')
	assert.equal(st.vtFrom(vmix, ''), false, "vMix's clip is no link's")
	assert.equal(st.vtFrom(snap({ playout: { title: 'Old', remaining: 5, duration: 9, running: true, source: 'Patterns' } }), ''), true, 'a frame before 2.20 names no link: a Patterns clip is still a link\'s')
	assert.equal(st.vtFrom(snap({ playout: null }), ''), false)
	// Its last N seconds: 42 left is not in the last 10, 5 left is; 0 is always.
	assert.equal(st.vtFrom(linked, 'stage-left', true, 10), false)
	assert.equal(st.vtFrom(linked, 'stage-left', true, 60), true)
	assert.equal(st.vtFrom(snap({ playout: { ...linked.playout, remaining: 5 } }), '', true, 10), true)
	assert.equal(st.vtFrom(snap({ playout: { ...linked.playout, remaining: 5, running: false } }), '', true, 10), false, 'held: not running')
	assert.equal(st.vtFrom(linked, 'stage-left', false, 0), true)
	assert.ok(st.FEEDBACK_IDS.includes('vt_link'))
	const moved = st.feedbacksAffected(linked, snap({ playout: { ...linked.playout, link: 'patterns', source: 'Patterns' } }), false)
	assert.ok(moved.includes('vt_link') && moved.includes('vt_running') && !moved.includes('blackout'))
	const self = fakeInstance(LICENSED)
	self.snapshot = linked
	UpdateFeedbacks(self)
	const fb = self.feedbacks.vt_link
	assert.deepEqual(fb.options[0].choices.map((c) => c.id), ['', 'vmix', 'stage-left', 'patterns'])
	assert.equal(fb.callback({ options: { link: 'Stage-Left', running: true } }), true, 'a button saved without the last option')
	assert.equal(fb.callback({ options: { link: 'vmix', running: false } }), false)
	assert.equal(fb.callback({ options: { link: 'stage-left', running: true, last: 10 } }), false, '42 s left')
	assert.equal(fb.callback({ options: { link: 'stage-left', running: true, last: 60 } }), true)
	assert.ok(fb.options.some((o) => o.id === 'last' && o.default === 0))
})

test('2.20 the config\'s playout links are read as Stagewatch names them, and the licence rule', async () => {
	const st = await import('../dist/state.js')
	assert.equal(st.readPlayoutLinks(undefined), undefined)
	assert.deepEqual(st.readPlayoutLinks([{ name: 'stage-left', kind: 'playmap', label: 'PlayMap Stage left' }, { name: 'Bad Name' }, null, { name: 'foh' }]), [
		{ name: 'stage-left', kind: 'playmap', label: 'PlayMap Stage left' },
		{ name: 'foh', kind: '', label: 'foh' },
	])
	assert.equal(st.playoutOffered(LICENSED, true), true)
	assert.equal(st.playoutOffered({ ...LICENSED, features: ['ndi'] }, true), false)
	assert.equal(st.playoutOffered({ ...LICENSED, playoutLinks: undefined }, true), false)
	assert.equal(st.playoutOffered(st.EMPTY_CONFIG, false), true)
	assert.ok(st.FEATURE_CHOICES.some((f) => f.id === st.PLAYOUT_FEATURE && /playout/.test(f.label)))
})

test('2.20 receipt lines: stage, words, and final only after an accepted', () => {
	assert.deepEqual(parseReceipt('OK @c1 accepted PlayMap Stage left: starting Intro', false), { id: 'c1', ok: true, stage: 'accepted', final: false, words: 'PlayMap Stage left: starting Intro' })
	assert.deepEqual(parseReceipt('OK @c1 final applied PlayMap Stage left took PLAYLIST GO pl1 it1 (playing Intro)', true), {
		id: 'c1', ok: true, stage: 'applied', final: true, words: 'PlayMap Stage left took PLAYLIST GO pl1 it1 (playing Intro)',
	})
	assert.deepEqual(parseReceipt('ERROR @c2 final PlayMap Stage left refused PLAYLIST GO pl1 it9: no item', true), { id: 'c2', ok: false, stage: '', final: true, words: 'PlayMap Stage left refused PLAYLIST GO pl1 it9: no item' })
	assert.equal(parseReceipt('ERROR @c3 final answer withheld', false).final, false, 'the word final is a follow-up only after an accepted')
	assert.equal(parseReceipt('ERROR @c3 final answer withheld', false).words, 'final answer withheld')
	assert.deepEqual(parseReceipt('OK @c4', false), { id: 'c4', ok: true, stage: '', final: false, words: '' })
	assert.equal(parseReceipt('OK PONG 1 2 3', false), null)
	assert.equal(parseReceipt('ERROR bad duration', false), null)
	const r = (o) => ({ id: 'c1', line: 'PLAYOUT stage-left go pl1 it1', kind: 'answered', ok: true, stage: 'applied', final: false, words: '', wasAccepted: false, ...o })
	assert.equal(receiptWords(r({ words: 'PlayMap Stage left took PLAYLIST GO pl1 it1' })), 'applied — PlayMap Stage left took PLAYLIST GO pl1 it1')
	assert.equal(receiptWords(r({ final: true, stage: 'accepted', words: 'unconfirmed — no answer' })), 'final accepted — unconfirmed — no answer')
	assert.equal(receiptWords(r({ ok: false, stage: '', words: 'playout from another machine over Companion TCP needs its address' })), 'refused — playout from another machine over Companion TCP needs its address')
	assert.equal(receiptWords(r({ stage: '' })), 'ok')
	assert.match(receiptWords(r({ kind: 'lost', ok: false, stage: '' })), /^no answer — the link to Stagewatch dropped first, so it may or may not have run/)
	const afterAccepted = receiptWords(r({ kind: 'lost', ok: false, stage: '', wasAccepted: true }))
	assert.match(afterAccepted, /^accepted; the link to Stagewatch dropped before any final answer came/)
	assert.doesNotMatch(afterAccepted, /lost|owed/, 'never says a final answer was owed: Stagewatch sends none to an accepted it ran itself')
	// playout_answer: the latest press's answer as its words; an earlier press's names its line first.
	const late = r({ id: 'c1', words: 'PlayMap Stage left took PLAYLIST GO pl1 it1', final: true })
	assert.equal(answerShown(late, 'c1'), 'final applied — PlayMap Stage left took PLAYLIST GO pl1 it1')
	assert.equal(answerShown(late, 'c2'), 'PLAYOUT stage-left go pl1 it1: final applied — PlayMap Stage left took PLAYLIST GO pl1 it1')
	assert.equal(answerShown(late, null), 'PLAYOUT stage-left go pl1 it1: final applied — PlayMap Stage left took PLAYLIST GO pl1 it1', 'the latest press was not sent')
})

/** A Stagewatch that answers each `@id PLAYOUT …` as `answer(line)` says (lines to write back, or nothing), and records what came. */
function answering(received, answer) {
	return (s) => {
		s.setEncoding('utf8')
		s.write(`READY stagewatch/2\nSNAPSHOT ${JSON.stringify(snap())}\n`)
		let buf = ''
		s.on('data', (d) => {
			buf += d
			let i
			while ((i = buf.indexOf('\n')) >= 0) {
				const line = buf.slice(0, i)
				buf = buf.slice(i + 1)
				if (line.startsWith('PING')) s.write(line.replace('PING', 'PONG') + '\n')
				else if (line !== 'SUBSCRIBE JSON') {
					received.push(line)
					for (const out of answer(line, s)) s.write(out + '\n')
				}
			}
		})
	}
}

test('2.20 a press with a request id: the peer\'s answer, its final follow-up, a refusal at the door and a lost answer each reach one receipt', async () => {
	const received = []
	const srv = await fakeServer(
		answering(received, (line, s) => {
			const id = line.slice(1, line.indexOf(' '))
			if (line.endsWith('go pl1 it1')) return [`OK @${id} accepted PlayMap Stage left: starting Intro`, 'OK @zz9 applied not ours', `OK @${id} final applied PlayMap Stage left took PLAYLIST GO pl1 it1 (playing Intro)`]
			if (line.endsWith('cue pl1 it2'))
				return [`OK @${id} accepted sent to PlayMap Stage left — waiting for its answer`, `OK @${id} final accepted unconfirmed — PlayMap Stage left took it and gave no final answer in 30 s — it may have run; look at its state`]
			if (line.endsWith('PATTERNS PAUSE')) return [`ERROR @${id} patterns from another machine over Companion TCP needs its address in Settings → Remote → May drive playout (Companion TCP carries no PIN)`]
			if (line.endsWith('restart')) {
				setTimeout(() => s.destroy(), 30) // the link drops before any answer
				return []
			}
			return [`OK @${id} applied done`]
		}),
	)
	const c = new StagewatchConnection('127.0.0.1', srv.port, FAST)
	const receipts = []
	const errors = []
	c.on('receipt', (r) => receipts.push(r))
	c.on('protocolError', (m) => errors.push(m))
	try {
		assert.equal(c.request('PLAYOUT pause'), null, 'not connected: nothing sent, nothing queued')
		c.start()
		await waitFor(c, 'status', (s) => s.kind === 'connected')
		const go = c.request('PLAYOUT stage-left go pl1 it1')
		await waitFor(c, 'receipt', (r) => r.id === go && r.final)
		assert.deepEqual(
			receipts.map((r) => [r.id, r.kind, r.ok, r.stage, r.final, r.words, r.line]),
			[
				[go, 'answered', true, 'accepted', false, 'PlayMap Stage left: starting Intro', 'PLAYOUT stage-left go pl1 it1'],
				[go, 'answered', true, 'applied', true, 'PlayMap Stage left took PLAYLIST GO pl1 it1 (playing Intro)', 'PLAYOUT stage-left go pl1 it1'],
			],
		)
		assert.deepEqual(errors, [], "an answer to an id this connection never sent is no one's receipt and no error")
		const pause = c.request('PATTERNS PAUSE')
		const refused = await waitFor(c, 'receipt', (r) => r.id === pause)
		assert.equal(refused.ok, false)
		assert.match(refused.words, /^patterns from another machine over Companion TCP needs its address/)
		assert.deepEqual(errors, [], 'a refusal of a line sent with an id is its receipt, not a protocol error')
		const cue = c.request('PLAYOUT stage-left cue pl1 it2')
		const unconfirmed = await waitFor(c, 'receipt', (r) => r.id === cue && r.final)
		assert.deepEqual([unconfirmed.ok, unconfirmed.stage], [true, 'accepted'], "Stagewatch's own final word: still only accepted, never applied")
		assert.match(receiptWords(unconfirmed), /^final accepted — unconfirmed — PlayMap Stage left took it/)
		const restart = c.request('PLAYOUT stage-left restart')
		const lost = await waitFor(c, 'receipt', (r) => r.id === restart)
		assert.deepEqual([lost.kind, lost.ok, lost.line], ['lost', false, 'PLAYOUT stage-left restart'])
		assert.deepEqual(receipts.filter((r) => r.kind === 'lost').map((r) => r.id), [restart], 'a line answered for good is never lost after')
		assert.deepEqual(received, [`@${go} PLAYOUT stage-left go pl1 it1`, `@${pause} PATTERNS PAUSE`, `@${cue} PLAYOUT stage-left cue pl1 it2`, `@${restart} PLAYOUT stage-left restart`])
		assert.match(go, /^c\d+$/)
		assert.notEqual(go, pause)
	} finally {
		c.destroy()
		await srv.close()
	}
})

test('2.20 lines waiting for an answer are bounded: the oldest is forgotten, never resent', async () => {
	const received = []
	const srv = await fakeServer(answering(received, () => []))
	const c = new StagewatchConnection('127.0.0.1', srv.port, FAST)
	try {
		c.start()
		await waitFor(c, 'status', (s) => s.kind === 'connected')
		const ids = []
		for (let i = 0; i <= MAX_PENDING; i++) ids.push(c.request(`PLAYOUT seek ${i}`))
		await new Promise((r) => setTimeout(r, 100))
		assert.equal(received.length, MAX_PENDING + 1, 'each sent once')
		const lost = []
		c.on('receipt', (r) => lost.push(r.id))
		for (const s of srv.sockets) s.destroy()
		await waitFor(c, 'status', (s) => s.kind === 'disconnected')
		assert.equal(lost.length, MAX_PENDING, 'one lost receipt for each line still waiting')
		assert.ok(!lost.includes(ids[0]), 'the oldest was forgotten when the 65th went')
	} finally {
		c.destroy()
		await srv.close()
	}
})

test('2.20 an accepted with no follow-up: settled by unconfirmed, forgotten after FINAL_WAIT_MS, and a drop never says a final was owed', async () => {
	const received = []
	const srv = await fakeServer(
		answering(received, (line) => {
			const id = line.slice(1, line.indexOf(' '))
			// Stagewatch's show thread missed its 3 s deadline: a bare accepted, and no follow-up ever.
			if (line.endsWith('PATTERNS PAUSE')) return [`OK @${id} accepted`]
			// A peer's first answer was unconfirmed: Stagewatch already knows no final is coming, and sends none.
			if (line.endsWith('cue pl1 it2')) return [`OK @${id} accepted unconfirmed — no answer from PlayMap Stage left in 30 s — it may have run · cued`]
			// A peer took it and will answer later: accepted, its final still to come.
			if (line.endsWith('go pl1 it1')) return [`OK @${id} accepted PlayMap Stage left: starting Intro`]
			return [`OK @${id} applied done`]
		}),
	)
	let clock = 1_000_000
	const c = new StagewatchConnection('127.0.0.1', srv.port, FAST, () => clock)
	const receipts = []
	c.on('receipt', (r) => receipts.push(r))
	try {
		c.start()
		await waitFor(c, 'status', (s) => s.kind === 'connected')
		// An accepted with no follow-up coming (the show thread's deadline), older than FINAL_WAIT_MS when the link drops, and a newer
		// press answered applied: the drop reports neither.
		const stale = c.request('PATTERNS PAUSE')
		await waitFor(c, 'receipt', (r) => r.id === stale)
		clock += FINAL_WAIT_MS - 10000
		const newer = c.request('PLAYOUT stage-left play')
		await waitFor(c, 'receipt', (r) => r.id === newer)
		const unconfirmed = c.request('PLAYOUT stage-left cue pl1 it2')
		await waitFor(c, 'receipt', (r) => r.id === unconfirmed)
		// A fresh accepted whose final may still come: the drop reports it, in words that claim no final was owed.
		const fresh = c.request('PLAYOUT stage-left go pl1 it1')
		await waitFor(c, 'receipt', (r) => r.id === fresh)
		clock += 10001 // the stale line is now past FINAL_WAIT_MS; the fresh one is 10 s old
		const before = receipts.length
		for (const s of srv.sockets) s.destroy()
		await waitFor(c, 'status', (s) => s.kind === 'disconnected')
		const lost = receipts.slice(before)
		assert.deepEqual(lost.map((r) => [r.id, r.kind, r.wasAccepted]), [[fresh, 'lost', true]], 'only the fresh accepted is reported; the stale and the settled ones are not')
		assert.equal(receiptWords(lost[0]), 'accepted; the link to Stagewatch dropped before any final answer came (look at Stagewatch)')
		assert.equal(answerShown(lost[0], newer).startsWith('PLAYOUT stage-left go pl1 it1: '), true, "an earlier press's answer never reads as the newer press's")
		assert.deepEqual(
			receipts.filter((r) => r.id === unconfirmed).map((r) => [r.stage, r.final, r.words.startsWith('unconfirmed')]),
			[['accepted', false, true]],
			'the unconfirmed first answer is its one receipt',
		)
	} finally {
		c.destroy()
		await srv.close()
	}
})
