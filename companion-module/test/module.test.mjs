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

import { adviceCommand, deptState, latestOpenFor, signed, slug } from '../dist/state.js'

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
	assert.ok(sig.includes('dept_state') && sig.includes('cue_waiting'))
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
