import type { CompanionActionDefinitions, CompanionOptionValues, DropdownChoice } from '@companion-module/base'
import { quote } from './connection.js'
import { adviceCommand, formatShort, itemAdjust, latestOpenFor, liveQuestion, newestUnanswered, playoutOffered, resolveItem } from './state.js'
import { deskNext, type DeskAccess, type PeerInfo, type PlayoutLinkInfo, type Snapshot } from './state.js'
import type ModuleInstance from './main.js'

const URGENCY_CHOICES: DropdownChoice[] = [
	{ id: 'info', label: 'Info' },
	{ id: 'warn', label: 'Warning' },
	{ id: 'crit', label: 'Critical' },
]
const FLASH_CHOICES: DropdownChoice[] = [
	{ id: 'off', label: 'No flash' },
	{ id: 'slow', label: 'Slow flash' },
	{ id: 'fast', label: 'Fast flash' },
]

const str = (v: unknown, fallback = ''): string => (v === undefined || v === null ? fallback : String(v)).trim()

/** A window name as Stagewatch reads it (one word: letters, digits, - and _), "all" when empty, null when not a name. */
export const windowName = (v: string): string | null => {
	const t = v.trim()
	if (t === '') return 'all'
	return /^[A-Za-z0-9_-]{1,40}$/.test(t) ? t : null
}
const num = (v: unknown, fallback = 0): number => {
	const n = Number(v)
	return Number.isFinite(n) ? n : fallback
}

/** Stagewatch's playout actions: a first word that is one of these is an action, never a link (PlayoutRules.Actions). */
export const PLAYOUT_ACTION_WORDS = ['end', 'restart', 'play', 'pause', 'stop', 'seek', 'cue', 'go']
/** The word after a link that trusts its certificate — a person's choice on the desk only, never a button's. */
const TRUST_WORD = 'trust'
/** Names no playout link takes: the other integrations' context slugs and the assistant's (PlayoutRules.Reserved). */
export const PLAYOUT_RESERVED = ['powerpoint', 'presenter-pcs', 'vmix', 'speech', 'time-server', 'ltc', 'ltc-in', 'cue-lights', 'triggers', 'rooms', 'assistant']

/**
 * A playout link's name as Stagewatch reads it: "" when empty (no link named: Stagewatch takes the only link switched on, else — for the clip on air — the link feeding the VT), the
 * name in lower case, or null when it can't name a link — so a press never sends a word Stagewatch would read as an action
 * (`PLAYOUT go pause` is a GO of a cue called pause, not a pause of a link called go).
 */
export const playoutLinkName = (v: string): string | null => {
	const t = v.trim().toLowerCase()
	if (t === '') return ''
	if (!/^[a-z0-9][a-z0-9-]{0,23}$/.test(t)) return null
	return PLAYOUT_ACTION_WORDS.includes(t) || t === TRUST_WORD || PLAYOUT_RESERVED.includes(t) ? null : t
}

/** A playlist, item or cue for a playout line: 1–64 characters, none a control character; quoted when it holds a space or a quote. */
export const playoutWord = (v: string): string | null => {
	const t = v.trim()
	if (t.length < 1 || t.length > 64 || /[\u0000-\u001f\u007f-\u009f]/.test(t)) return null
	return /[\s"\\]/.test(t) ? quote(t) : t
}

const LINK_WORDS = 'a playout link is named with 1 to 24 of a-z, 0-9 and -, never an action word, trust or another integration\'s name'

/** `PLAYOUT [<link>] end|restart|play|pause|stop|seek …` for the clip on air, or why not (nothing is sent). */
export function playoutTransportLine(link: string, what: string, seconds: number, at: string): { line: string } | { error: string } {
	const name = playoutLinkName(link)
	if (name === null) return { error: `Playout: "${link.trim()}" — ${LINK_WORDS}` }
	const head = name ? `PLAYOUT ${name}` : 'PLAYOUT'
	switch (what) {
		case 'restart':
		case 'play':
		case 'pause':
		case 'stop':
			return { line: `${head} ${what}` }
		case 'end': {
			const n = Math.trunc(seconds)
			return n >= 1 && n <= 3600 ? { line: `${head} end ${n}` } : { error: 'Playout: the clip jumps to its last 1–3600 seconds' }
		}
		case 'seek': {
			const t = at.trim()
			return /^(\d{1,2}:)?\d{1,2}:\d{2}$|^\d{1,5}$/.test(t) ? { line: `${head} seek ${t}` } : { error: `Playout seek: "${t}" is not a time (use m:ss)` }
		}
		default:
			return { error: `Playout: "${what}" is not play, pause, stop, restart, end or seek` }
	}
}

/**
 * `PLAYOUT [<link>] cue|go …`: on a PlayMap node a playlist and its item (GO with no item plays the playlist's next one, as the
 * node's STATE names it); on a Patterns desk one cue, in the item field. `together` (GO only) starts every link's GO at one
 * instant where each one's clock allows. Which the link is, and so which words fit, is Stagewatch's to check: it refuses a line
 * that does not fit its peer, with the words, and the log shows them.
 */
export function playoutStartLine(action: 'cue' | 'go', link: string, playlist: string, item: string, together: boolean): { line: string } | { error: string } {
	const name = playoutLinkName(link)
	if (name === null) return { error: `Playout: "${link.trim()}" — ${LINK_WORDS}` }
	const words: string[] = []
	for (const [v, what] of [
		[playlist, 'playlist'],
		[item, 'item or cue'],
	] as const) {
		if (v.trim() === '') continue
		const w = playoutWord(v)
		if (w === null) return { error: `Playout ${action === 'go' ? 'GO' : 'cue'}: the ${what} is 1–64 characters with no control character` }
		words.push(w)
	}
	if (words.length === 0) return { error: action === 'cue' ? 'Playout cue: name a playlist and its item (PlayMap) or a cue (Patterns)' : 'Playout GO: name a playlist (PlayMap) or a cue (Patterns)' }
	if (action === 'cue' && together) return { error: 'Playout cue: together is for a GO' }
	// The word together, last, asks for a GO together: a playlist or item of that name can't be the last word.
	if (action === 'go' && /^"?together"?$/i.test(words[words.length - 1])) return { error: 'Playout GO: a playlist or item called "together" can\'t be named from a button — the word asks for a GO together' }
	const head = name ? `PLAYOUT ${name}` : 'PLAYOUT'
	return { line: `${head} ${action} ${words.join(' ')}${together ? ' together' : ''}` }
}

/** A Show Bus id as Stagewatch reads one (NodeBindings.ValidId): 1–40 of letters, digits, - and _. */
export const SHOW_BUS_ID = /^[A-Za-z0-9_-]{1,40}$/

/** The longest show app's name a window line takes (ScreenWindows.MaxAppName): a longer one is named by its id. */
export const MAX_APP_NAME = 60

/**
 * A show app as a window line names it: its Show Bus id bare (1–40 of letters, digits, - and _), any other name quoted — the whole of
 * it is the app, as Stagewatch reads the words after the window — or null when it is empty, longer than Stagewatch takes, or holds a
 * control character.
 */
export function showAppWord(v: string): string | null {
	const t = v.trim()
	if (t.length < 1 || t.length > MAX_APP_NAME || /[\u0000-\u001f\u007f-\u009f]/.test(t)) return null
	return SHOW_BUS_ID.test(t) ? t : quote(t)
}

/**
 * `WINDOW GRANT <window> <app id>` or `WINDOW WITHDRAW <window|all> [<app>]`, or why not (nothing is sent). A grant names one window
 * and one show app by its Show Bus id, never every window: Stagewatch asks the operator for it on the desk, and the approved grant finds
 * its app by id alone. A withdraw names its window, or every window when it is empty, and its app (id or name) only when one is given.
 * Whether the app is heard, may show a window, or already holds it is Stagewatch's to say, in the receipt the log shows.
 */
export function windowGrantLine(what: string, window: string, app: string): { line: string } | { error: string } {
	const who = windowName(window)
	if (who === null) return { error: 'Windows: a window name uses letters, digits, - and _' }
	if (what !== 'grant' && what !== 'withdraw') return { error: `Windows: "${what}" is not grant or withdraw` }
	const named = app.trim()
	const word = named === '' ? '' : showAppWord(named)
	if (word === null) return { error: `Windows: a show app is named by its Show Bus id or its name, at most ${MAX_APP_NAME} characters with no control character` }
	if (what === 'grant') {
		if (who.toLowerCase() === 'all') return { error: 'Windows: grant one window at a time — name it' }
		if (word === '') return { error: 'Windows: a grant names the show app — its Show Bus id' }
		// A button proves no person, so Stagewatch asks the operator for the grant; the approved grant finds its app by id alone.
		if (!SHOW_BUS_ID.test(named)) return { error: 'Windows: a grant from a button names the show app by its Show Bus id' }
		return { line: `WINDOW GRANT ${who} ${word}` }
	}
	return { line: word === '' ? `WINDOW WITHDRAW ${who}` : `WINDOW WITHDRAW ${who} ${word}` }
}

/** The longest line Stagewatch's desk takes (Show Bus §11, LineFence.MaxChars): a longer one is refused, never cut. */
export const MAX_PEER_LINE = 2048

/** The longest reason a rejection keeps (ProposalDesk.MaxReason). */
export const MAX_REASON = 400

/** How long a high proposal's first press stays armed for the second, which approves it (cross-app proposals D12). */
export const HIGH_PRESS_MS = 3000

const CONTROL = /[\u0000-\u001f\u007f-\u009f]/

/**
 * `PEER <app id> "<line>"` — one line another show app lists, sent born approved as this deck's — or why not (nothing is sent): no
 * app chosen, an app that is no Show Bus id, no line, a line over MAX_PEER_LINE characters or one holding a control character.
 * Whether the app lists the line, collects, or proves its verbs is Stagewatch's to say, in the receipt the log shows.
 */
export function peerLine(app: string, line: string): { line: string } | { error: string } {
	const id = app.trim()
	if (id === '') return { error: 'Peer: choose the show app' }
	if (!/^[A-Za-z0-9_-]{1,40}$/.test(id)) return { error: 'Peer: a show app is named by its Show Bus id' }
	const text = line.trim()
	if (text === '') return { error: 'Peer: type the line the app is to run' }
	if (text.length > MAX_PEER_LINE) return { error: `Peer: a line is at most ${MAX_PEER_LINE} characters` }
	if (CONTROL.test(text)) return { error: 'Peer: a line holds no control characters' }
	return { line: `PEER ${id} ${quote(text)}` }
}

/** A window grant's line as Stagewatch's grammar reads it: `window grant <window> <app>`, any case. */
const GRANT_LINE = /^\s*window\s+grant\s/i

/**
 * What a press of desk_approve does with the oldest proposal waiting (`desk_next_id`): approves one that is not high
 * (`DESK APPROVE <id>`); for a high one, the first press arms it and says so, and a second press within HIGH_PRESS_MS of the first,
 * while the same proposal is still the oldest, sends `DESK APPROVE <id> HIGH` — a high proposal is approved by its id and the word,
 * never as "next". Nothing waiting is said, and nothing is sent; nor is a window grant waiting, which Stagewatch takes only from a
 * person who proved it (at its desk, or over HTTP or a WebSocket with the PIN or a control key) — a deck proves nobody, so its press
 * would be refused (Show Bus 12.4). A deck may still reject one.
 */
export function deskApprove(
	s: Snapshot | null,
	armed: { id: string; at: number } | null,
	now: number,
): { line: string } | { arm: { id: string; at: number }; note: string } | { error: string } {
	const next = deskNext(s)
	if (!next) return { error: 'Desk: nothing is waiting for a decision' }
	if (GRANT_LINE.test(next.line))
		return {
			error: `Desk: ${next.id.split('-')[0]} is a window grant (${next.line}) — a person approves it at Stagewatch's desk, or over HTTP or a WebSocket with the PIN; a deck proves nobody`,
		}
	if (next.risk !== 'high') return { line: `DESK APPROVE ${next.id}` }
	if (armed && armed.id === next.id && now - armed.at >= 0 && now - armed.at <= HIGH_PRESS_MS) return { line: `DESK APPROVE ${next.id} HIGH` }
	const short = next.id.split('-')[0]
	return { arm: { id: next.id, at: now }, note: `Desk: ${short} is high (${next.line}) — press again within ${HIGH_PRESS_MS / 1000} s to approve it` }
}

/** `DESK REJECT <id> ["<why>"]` for the oldest proposal waiting, or why not: nothing waiting, or a reason too long or with a control character. */
export function deskReject(s: Snapshot | null, why: string): { line: string } | { error: string } {
	const next = deskNext(s)
	if (!next) return { error: 'Desk: nothing is waiting for a decision' }
	const reason = why.trim()
	if (reason.length > MAX_REASON) return { error: `Desk: a reason is at most ${MAX_REASON} characters` }
	if (CONTROL.test(reason)) return { error: 'Desk: a reason holds no control characters' }
	return { line: reason === '' ? `DESK REJECT ${next.id}` : `DESK REJECT ${next.id} ${quote(reason)}` }
}

/**
 * Why Stagewatch would refuse this deck a person's line now, by its own `ACCESS` line (`what`: a peer line, a desk decision or a
 * window grant), or null: allowed, or Stagewatch has not said (a Stagewatch before 2.22, or before the first frame), which is
 * Stagewatch's to answer.
 */
export function accessRefuses(access: DeskAccess | null, what: 'peer' | 'desk' | 'grant'): string | null {
	if (!access || access[what]) return null
	return `Stagewatch takes no ${what === 'grant' ? 'window grant' : what === 'desk' ? 'desk decision' : 'peer line'} from this deck: ${access.why || 'its address is not listed'}`
}

/** The peer dropdown: the config's show apps by their names and kinds, each sent by its Show Bus id. */
export function peerChoices(peers: PeerInfo[] | undefined): DropdownChoice[] {
	return (peers ?? []).map((p) => ({ id: p.id, label: p.kind ? `${p.name} (${p.kind})` : p.name }))
}

/** The link dropdown: none named first (Stagewatch picks: the only link, else for the clip on air the VT's), then the config's links by their peers' names. */
export function playoutLinkChoices(links: PlayoutLinkInfo[] | undefined, none: string): DropdownChoice[] {
	return [{ id: '', label: none }, ...(links ?? []).map((l) => ({ id: l.name, label: l.label && l.label !== l.name ? `${l.name} — ${l.label}` : l.name }))]
}

export function UpdateActions(self: ModuleInstance): void {
	const cfg = self.showConfig
	const presetChoices: DropdownChoice[] = cfg.presets.map((p) => ({ id: p.slot, label: `${p.slot}: ${p.label}` }))
	if (presetChoices.length === 0)
		for (let i = 1; i <= 12; i++) presetChoices.push({ id: i, label: `Preset ${i}` })
	const itemChoices: DropdownChoice[] = cfg.rundown.map((r, i) => ({
		id: i + 1,
		label: `${i + 1}: ${r.title}${r.speaker ? ` — ${r.speaker}` : ''} (${formatShort(r.seconds)})`,
	}))
	const bankChoices: DropdownChoice[] = cfg.messages.map((m, i) => ({ id: i, label: m.label }))
	const deptChoices: DropdownChoice[] = (cfg.departments ?? []).map((d) => ({ id: d, label: d }))
	const holdingChoices: DropdownChoice[] = [
		{ id: 'toggle', label: 'Toggle (off / the last image)' },
		{ id: 'off', label: 'Off (back to the timer)' },
	].concat((cfg.holding ?? []).map((h, i) => ({ id: h.id, label: `${i + 1}: ${h.name}` })))
	const ladderChoices: DropdownChoice[] = [{ id: 'default', label: 'Show default' }].concat(
		(cfg.ladders ?? []).map((l) => ({ id: l.id, label: l.name })),
	)

	const send = (line: string): void => self.sendCommand(line)
	/** A line that drives another app's air: sent with a request id, so its answer reaches the log and `playout_answer`. */
	const playout = (line: string): void => self.sendPlayout(line)
	const built = (r: { line: string } | { error: string }): void => {
		if ('error' in r) {
			self.log('warn', r.error)
			return
		}
		playout(r.line)
	}
	const linkChoices = playoutLinkChoices(cfg.playoutLinks, 'None named: the only link, else the one feeding the VT')
	const startLinkChoices = playoutLinkChoices(cfg.playoutLinks, 'None named: the only link')
	const LINK_TIP = 'A playout link set up in Stagewatch (Settings → Integrations), or its name typed. From another PC the press also needs that PC\'s address in Stagewatch\'s Settings → Remote → May drive other show apps.'

	const actions: CompanionActionDefinitions = {
		// ---- transport
		toggle: {
			name: 'Transport: Start / Pause (toggle)',
			description: 'One button for the whole show: starts when stopped, pauses when running (also pauses overtime).',
			options: [],
			callback: () => send('TOGGLE'),
		},
		start: { name: 'Transport: Start', options: [], callback: () => send('START') },
		pause: { name: 'Transport: Pause', options: [], callback: () => send('PAUSE') },
		reset: { name: 'Transport: Reset', options: [], callback: () => send('RESET') },

		// ---- time
		nudge: {
			name: 'Time: Add / remove time (nudge)',
			description: 'Negative values remove time. Works live while running.',
			options: [
				{ type: 'number', id: 'seconds', label: 'Seconds (±)', default: 60, min: -86400, max: 86400, step: 1 },
			],
			callback: (ev) => {
				const s = num(ev.options.seconds)
				if (s !== 0) send(`NUDGE ${s}`)
			},
		},
		set_duration: {
			name: 'Time: Load duration',
			description: 'Loads a countdown without starting it. Accepts 5:00, 1:30:00 or seconds.',
			options: [
				{ type: 'textinput', id: 'duration', label: 'Duration', default: '05:00', useVariables: true },
				{ type: 'checkbox', id: 'start', label: 'Start immediately', default: false },
			],
			callback: (ev) => {
				const d = str(ev.options.duration)
				if (!/^\d+(:\d{1,2}){0,2}(\.\d+)?$/.test(d)) {
					self.log('warn', `Load duration: "${d}" is not a time (use mm:ss, hh:mm:ss or seconds)`)
					return
				}
				send(`SET ${d}`)
				if (ev.options.start === true) send('START')
			},
			learn: () => (self.snapshot && self.snapshot.total > 0 ? { duration: formatShort(self.snapshot.total) } : undefined),
		},
		end_at: {
			name: 'Time: Finish at a time of day',
			description:
				'Lands on zero at this time. Elastic: the loaded countdown speeds up or slows down to fit. Real time: counts down the actual time left at normal speed. Leave the time empty to clear.',
			options: [
				{ type: 'textinput', id: 'time', label: 'Time (HH:MM, 24h) — empty to clear', default: '', useVariables: true },
				{
					type: 'dropdown',
					id: 'pace',
					label: 'Pace',
					default: 'default',
					choices: [
						{ id: 'default', label: 'As set in Stagewatch' },
						{ id: 'elastic', label: 'Elastic (stretch the loaded duration)' },
						{ id: 'realtime', label: 'Real time (count the actual time left)' },
					],
				},
			],
			callback: (ev) => {
				const t = str(ev.options.time)
				const pace = str(ev.options.pace, 'default')
				if (t === '') send('ENDAT clear')
				else if (/^\d{1,2}:\d{2}(:\d{2})?$/.test(t)) send(pace === 'default' ? `ENDAT ${t}` : `ENDAT ${t} ${pace}`)
				else self.log('warn', `Finish at: "${t}" is not HH:MM`)
			},
		},
		mode: {
			name: 'Time: Clock mode',
			options: [
				{
					type: 'dropdown',
					id: 'mode',
					label: 'Mode',
					default: 'countdown',
					choices: [
						{ id: 'countdown', label: 'Countdown' },
						{ id: 'countup', label: 'Count up' },
						{ id: 'tod', label: 'Time of day' },
					],
				},
			],
			callback: (ev) => send(`MODE ${str(ev.options.mode, 'countdown')}`),
		},

		// ---- presets & running order
		preset: {
			name: 'Presets: Load preset',
			options: [
				{ type: 'dropdown', id: 'slot', label: 'Preset', default: 1, choices: presetChoices, allowCustom: true },
				{ type: 'checkbox', id: 'start', label: 'Start immediately', default: false },
			],
			callback: (ev) => {
				const slot = Math.trunc(num(ev.options.slot, 0))
				if (slot < 1 || slot > 12) return self.log('warn', `Preset ${String(ev.options.slot)} doesn't exist (1–12)`)
				send(`PRESET ${slot}${ev.options.start === true ? ' START' : ''}`)
			},
		},
		next: {
			name: 'Running order: Next item',
			description: 'Loads the next item; starts it straight away if the show is live.',
			options: [],
			callback: () => send('NEXT'),
		},
		previous: { name: 'Running order: Previous item', options: [], callback: () => send('PREV') },
		goto: {
			name: 'Running order: Go to item',
			options: [
				{
					type: 'dropdown',
					id: 'item',
					label: 'Item',
					default: 1,
					choices: itemChoices.length ? itemChoices : [{ id: 1, label: '1' }],
					allowCustom: true,
				},
				{ type: 'checkbox', id: 'start', label: 'Start immediately', default: false },
			],
			callback: (ev) => {
				const n = Math.trunc(num(ev.options.item, 0))
				if (n < 1) return
				send(`GOTO ${n}${ev.options.start === true ? ' start' : ''}`)
			},
			learn: () => (self.snapshot && self.snapshot.item >= 0 ? { item: self.snapshot.item + 1 } : undefined),
		},

		// ---- messages
		message: {
			name: 'Message: Show message to presenter',
			options: [
				{ type: 'textinput', id: 'text', label: 'Message', default: 'Please wrap up', useVariables: true },
				{ type: 'dropdown', id: 'urgency', label: 'Urgency', default: 'info', choices: URGENCY_CHOICES },
				{ type: 'dropdown', id: 'flash', label: 'Flash', default: 'off', choices: FLASH_CHOICES },
				{ type: 'checkbox', id: 'ack', label: 'Ask the speaker to acknowledge', default: false },
			],
			callback: (ev) => {
				const text = str(ev.options.text)
				if (!text) return send('CLEARMESSAGE')
				send(`MESSAGE ${quote(text)} ${str(ev.options.urgency, 'info')} ${str(ev.options.flash, 'off')}${ev.options.ack === true ? ' ack' : ''}`)
			},
			learn: () =>
				self.snapshot?.message
					? {
							text: self.snapshot.message,
							urgency: self.snapshot.urgency === 'warning' ? 'warn' : self.snapshot.urgency === 'critical' ? 'crit' : 'info',
						}
					: undefined,
		},
		quick_message: {
			name: 'Message: Send quick message',
			description: 'One of the quick messages configured in Stagewatch.',
			options: [
				{
					type: 'dropdown',
					id: 'index',
					label: 'Quick message',
					default: 0,
					choices: bankChoices.length ? bankChoices : [{ id: 0, label: '(none configured yet)' }],
				},
			],
			callback: (ev) => {
				const m = self.showConfig.messages[Math.trunc(num(ev.options.index, -1))]
				if (!m) return self.log('warn', 'That quick message no longer exists in Stagewatch')
				const urg = m.urgency === 'warning' ? 'warn' : m.urgency === 'critical' ? 'crit' : 'info'
				send(`MESSAGE ${quote(m.text)} ${urg} ${m.flash}`)
			},
		},
		clear_message: { name: 'Message: Clear', options: [], callback: () => send('CLEARMESSAGE') },
		flash: {
			name: 'Message: Flash message',
			options: [{ type: 'dropdown', id: 'rate', label: 'Flash', default: 'slow', choices: FLASH_CHOICES }],
			callback: (ev) => send(`FLASH ${str(ev.options.rate, 'off')}`),
		},
		flash_timer: {
			name: 'Display: Flash the timer',
			options: [
				{
					type: 'dropdown',
					id: 'on',
					label: 'Timer flash',
					default: 'on',
					choices: [
						{ id: 'on', label: 'On' },
						{ id: 'off', label: 'Off' },
					],
				},
			],
			callback: (ev) => send(`FLASH TIMER ${str(ev.options.on, 'off')}`),
		},

		// ---- display
		blackout: {
			name: 'Display: Blackout outputs',
			options: [
				{
					type: 'dropdown',
					id: 'state',
					label: 'Blackout',
					default: 'toggle',
					choices: [
						{ id: 'toggle', label: 'Toggle' },
						{ id: 'on', label: 'On (hide everything)' },
						{ id: 'off', label: 'Off (show outputs)' },
					],
				},
			],
			callback: (ev) => send(`BLACKOUT ${str(ev.options.state, 'toggle')}`),
		},

		// ---- 2.3: holding images
		holding: {
			name: 'Display: Holding image',
			description:
				'Put a still image full screen on every output, NDI and browser screen (over the timer; blackout still wins). Images are added on the Holding tab in Stagewatch or uploaded from the Showcaller page. Custom values: an image number, its name, off or toggle.',
			options: [{ type: 'dropdown', id: 'image', label: 'Image', default: holdingChoices[2]?.id ?? 'toggle', choices: holdingChoices, allowCustom: true }],
			callback: (ev) => send(`HOLDING ${quote(str(ev.options.image, 'toggle') || 'toggle')}`),
		},

		// ---- 2.4: display style
		style: {
			name: 'Display: Holographic 3D style',
			description: 'Switch the output and browser screens between the standard display and the holographic 3D look (Stagewatch Pro).',
			options: [
				{
					type: 'dropdown',
					id: 'style',
					label: 'Style',
					default: 'toggle',
					choices: [
						{ id: 'toggle', label: 'Toggle' },
						{ id: 'holo', label: 'Holographic 3D' },
						{ id: 'egg', label: '3D egg timer' },
						{ id: 'standard', label: 'Standard' },
					],
				},
			],
			callback: (ev) => send(`STYLE ${str(ev.options.style, 'toggle')}`),
		},

		// ---- 2.9: roll and running orders
		roll: {
			name: 'Running order: Roll (follow the clock)',
			description: 'Run the running order by its planned times with nobody at the desk. Any manual action takes over.',
			options: [
				{
					type: 'dropdown',
					id: 'state',
					label: 'Roll',
					default: 'toggle',
					choices: [
						{ id: 'toggle', label: 'Toggle' },
						{ id: 'on', label: 'On' },
						{ id: 'off', label: 'Off' },
					],
				},
			],
			callback: (ev) => send(`ROLL ${str(ev.options.state, 'toggle')}`),
		},
		rundown_switch: {
			name: 'Running order: make another live',
			description: "Switch to another of the show's running orders (e.g. Day 2). The current item ends cleanly.",
			options: [{ type: 'textinput', id: 'name', label: 'Running order name', default: 'Day 2', useVariables: true }],
			callback: (ev) => {
				const name = str(ev.options.name).trim()
				if (!name || name.length > 40) {
					self.log('warn', "Running order: give the name of one of the show's running orders")
					return
				}
				send(`RUNDOWN ${name}`)
			},
		},

		// ---- 2.8: extra timers and screens
		aux: {
			name: 'Extra timer: control',
			description: 'Start, pause, reset, count up/down, or show on stage one of the three extra timers (they never change the show).',
			options: [
				{ type: 'dropdown', id: 'n', label: 'Timer', default: '1', choices: [1, 2, 3].map((n) => ({ id: String(n), label: `Timer ${n}` })) },
				{
					type: 'dropdown',
					id: 'action',
					label: 'Action',
					default: 'toggle',
					choices: [
						{ id: 'toggle', label: 'Start / pause' },
						{ id: 'start', label: 'Start' },
						{ id: 'pause', label: 'Pause' },
						{ id: 'reset', label: 'Reset' },
						{ id: 'stage', label: 'Show on stage' },
						{ id: 'unstage', label: 'Hide from stage' },
						{ id: 'down', label: 'Count down' },
						{ id: 'up', label: 'Count up' },
					],
				},
			],
			callback: (ev) => send(`AUX ${str(ev.options.n, '1')} ${str(ev.options.action, 'toggle')}`),
		},
		aux_set: {
			name: 'Extra timer: load a time',
			options: [
				{ type: 'dropdown', id: 'n', label: 'Timer', default: '1', choices: [1, 2, 3].map((n) => ({ id: String(n), label: `Timer ${n}` })) },
				{ type: 'textinput', id: 'duration', label: 'Duration (5:00, 1:30:00 or seconds)', default: '3:00', useVariables: true },
				{ type: 'checkbox', id: 'start', label: 'Start it', default: false },
			],
			callback: (ev) => {
				const d = str(ev.options.duration).trim()
				if (!/^\d+(:\d{1,2}){0,2}$/.test(d)) {
					self.log('warn', `Extra timer: "${d}" is not a time (use mm:ss, hh:mm:ss or seconds)`)
					return
				}
				const n = str(ev.options.n, '1')
				send(`AUX ${n} SET ${d}`)
				if (ev.options.start === true) send(`AUX ${n} START`)
			},
		},
		aux_nudge: {
			name: 'Extra timer: add or remove time',
			options: [
				{ type: 'dropdown', id: 'n', label: 'Timer', default: '1', choices: [1, 2, 3].map((n) => ({ id: String(n), label: `Timer ${n}` })) },
				{ type: 'number', id: 'seconds', label: 'Seconds (negative removes)', default: 30, min: -3600, max: 3600 },
			],
			callback: (ev) => send(`AUX ${str(ev.options.n, '1')} NUDGE ${Number(ev.options.seconds) || 0}`),
		},
		screen_go: {
			name: 'Screens: send to a page or saved setup',
			description:
				'Switch browser screens to another page (e.g. /audience) or a setup saved in Stagewatch → Screens. Empty = all browser screens; show apps (Patterns, PlayMap, AGMessage) and unproven claims are never included.',
			options: [
				{ type: 'textinput', id: 'target', label: 'Setup name or page (/viewer?cue=0)', default: 'lobby', useVariables: true },
				{ type: 'textinput', id: 'who', label: 'Screen name or id (empty = all browser screens)', default: '', useVariables: true },
			],
			callback: (ev) => {
				const target = str(ev.options.target).trim()
				if (!/^([A-Za-z0-9_-]{1,40}|\/[^\s]{0,399})$/.test(target)) {
					self.log('warn', `Screens: "${target}" is not a setup name or a page`)
					return
				}
				send(`SCREEN GO ${target} ${str(ev.options.who).trim()}`.trim())
			},
		},
		screen_identify: {
			name: 'Screens: identify',
			description:
				'Show each screen\'s name full screen for 6 seconds, so you can find it in the room. Empty = all browser screens; show apps and unproven claims are never included.',
			options: [{ type: 'textinput', id: 'who', label: 'Screen name or id (empty = all browser screens)', default: '', useVariables: true }],
			callback: (ev) => send(`SCREEN IDENTIFY ${str(ev.options.who).trim()}`.trim()),
		},

		// ---- 2.7: LTC in
		ltc_chase: {
			name: 'LTC in: timer follows timecode',
			description: 'Chase: the timer starts when incoming LTC rolls and pauses when it stops (Stagewatch Production).',
			options: [
				{
					type: 'dropdown',
					id: 'state',
					label: 'Chase',
					default: 'toggle',
					choices: [
						{ id: 'toggle', label: 'Toggle' },
						{ id: 'on', label: 'On' },
						{ id: 'off', label: 'Off' },
					],
				},
			],
			callback: (ev) => send(`LTC CHASE ${str(ev.options.state, 'toggle')}`),
		},
		ltc_end: {
			name: 'LTC in: count down to a timecode',
			description: 'While chasing, keep the timer at end − timecode (e.g. the last frame of a video). Empty clears it.',
			options: [{ type: 'textinput', id: 'tc', label: 'End timecode (HH:MM:SS:FF)', default: '', useVariables: true }],
			callback: (ev) => {
				const tc = str(ev.options.tc).trim()
				if (tc.length && !/^\d{1,2}[:;.]\d{2}[:;.]\d{2}([:;.]\d{2})?$/.test(tc)) {
					self.log('warn', `LTC end: "${tc}" is not a timecode (use HH:MM:SS:FF)`)
					return
				}
				send(`LTC END ${tc.length ? tc : 'clear'}`)
			},
		},

		// ---- 2.2: outputs
		output: {
			name: 'Display: Output window',
			description: 'Open the speaker output full screen, keyed (for a vision mixer), as a floating mini timer, or close it.',
			options: [
				{
					type: 'dropdown',
					id: 'mode',
					label: 'Output',
					default: 'toggle',
					choices: [
						{ id: 'toggle', label: 'Toggle (full screen / closed)' },
						{ id: 'full', label: 'Full screen' },
						{ id: 'keyed', label: 'Keyed full screen' },
						{ id: 'mini', label: 'Floating mini' },
						{ id: 'closed', label: 'Close' },
					],
				},
			],
			callback: (ev) => send(`OUTPUT ${str(ev.options.mode, 'toggle')}`),
		},
		ndi: {
			name: 'Display: NDI output',
			description: 'Send the output as an NDI source (needs the output window open and the free NDI Runtime).',
			options: [
				{
					type: 'dropdown',
					id: 'state',
					label: 'NDI',
					default: 'toggle',
					choices: [
						{ id: 'toggle', label: 'Toggle' },
						{ id: 'on', label: 'On' },
						{ id: 'off', label: 'Off' },
					],
				},
			],
			callback: (ev) => send(`NDI ${str(ev.options.state, 'toggle')}`),
		},

		// ---- 2.0: acknowledged cues, departments, green room
		signal: {
			name: 'Cues: Send a cue (with acknowledgement)',
			description:
				'Speaker / moderator messages show on their screens with a "Got it" button; departments get a big STANDBY / GO page; backstage gets "call to stage". You see when they confirm.',
			options: [
				{
					type: 'dropdown',
					id: 'kind',
					label: 'Cue',
					default: 'message',
					choices: [
						{ id: 'message', label: 'Message' },
						{ id: 'standby', label: 'Standby' },
						{ id: 'go', label: 'GO' },
						{ id: 'call', label: 'Call to stage' },
						{ id: 'nudge', label: 'Pocket buzz (phone / watch)' },
					],
				},
				{ type: 'textinput', id: 'target', label: 'To (speaker, moderator, backstage, interpreter or a department)', default: 'speaker', useVariables: true },
				{ type: 'textinput', id: 'text', label: 'Text', default: 'Please wrap up', useVariables: true },
				{ type: 'checkbox', id: 'ack', label: 'Ask for acknowledgement', default: true },
			],
			callback: (ev) => {
				const target = str(ev.options.target, 'speaker') || 'speaker'
				send(`SIGNAL ${str(ev.options.kind, 'message')} ${quote(target)} ${quote(str(ev.options.text))}${ev.options.ack === false ? ' noack' : ''}`)
			},
		},
		dept_cue: {
			name: 'Cues: Department standby / GO',
			options: [
				{
					type: 'dropdown',
					id: 'dept',
					label: 'Department',
					default: deptChoices[0]?.id ?? 'Lighting',
					choices: deptChoices.length ? deptChoices : [{ id: 'Lighting', label: 'Lighting' }],
					allowCustom: true,
				},
				{
					type: 'dropdown',
					id: 'kind',
					label: 'Cue',
					default: 'go',
					choices: [
						{ id: 'standby', label: 'Standby' },
						{ id: 'go', label: 'GO' },
					],
				},
				{ type: 'textinput', id: 'text', label: 'Cue number / note', default: '', useVariables: true },
			],
			callback: (ev) => send(`SIGNAL ${str(ev.options.kind, 'go')} ${quote(str(ev.options.dept))} ${quote(str(ev.options.text))}`),
		},
		call_next: {
			name: 'Cues: Call the next speaker to stage',
			description: 'Sends "<next speaker> to stage" to the backstage page, which answers Found / In position.',
			options: [],
			callback: () => {
				const snap = self.snapshot
				const next = snap?.timings?.[snap.item + 1]
				const who = next ? next.speaker || next.title : snap?.next
				if (!who) return self.log('warn', 'No next item to call')
				send(`SIGNAL call backstage ${quote(`${who} to stage`)}`)
			},
		},
		ack: {
			name: 'Cues: Acknowledge ("Got it") — for a podium or green-room Stream Deck',
			description: 'Confirms the newest open cue for this target, as if they tapped "Got it" on their screen.',
			options: [
				{ type: 'textinput', id: 'target', label: 'For', default: 'speaker' },
				{
					type: 'dropdown',
					id: 'stage',
					label: 'Reply',
					default: 'done',
					choices: [
						{ id: 'seen', label: 'Seen / found / standing by' },
						{ id: 'done', label: 'Done / in position' },
					],
				},
			],
			callback: (ev) => {
				const g = latestOpenFor(self.snapshot, str(ev.options.target, 'speaker'))
				if (!g) return self.log('info', 'Nothing to acknowledge')
				send(`ACK ${g.id} ${str(ev.options.stage, 'done')}`)
			},
		},
		signal_resend: {
			name: 'Cues: Send an unanswered cue again',
			description: 'Re-sends the newest cue nobody has acknowledged yet (optionally for one target), so it flashes again on their screen.',
			options: [{ type: 'textinput', id: 'target', label: 'Target (blank = any)', default: '' }],
			callback: (ev) => {
				const g = newestUnanswered(self.snapshot, str(ev.options.target))
				if (!g) return self.log('info', 'No unanswered cue to send again')
				send(`SIGNAL resend ${g.id}`)
			},
		},
		all_go: {
			name: 'Cues: ALL GO (every department standing by)',
			description: 'GO to every department standing by, at the same moment: a cue that spans departments. Each still confirms its own GO.',
			options: [],
			callback: () => send('SIGNAL go all'),
		},
		signal_clear: {
			name: 'Cues: Clear cues',
			options: [{ type: 'textinput', id: 'target', label: 'Target (blank = all)', default: '' }],
			callback: (ev) => send(`SIGNAL clear ${quote(str(ev.options.target) || '*')}`),
		},

		// ---- 2.0: schedule recovery
		trim: {
			name: 'Schedule: Cut / extend an item',
			description: 'Changes the item\'s planned length (kept apart from the plan for the report). The live item\'s clock moves with it.',
			options: [
				{ type: 'dropdown', id: 'item', label: 'Item', default: 1, choices: itemChoices.length ? itemChoices : [{ id: 1, label: '1' }], allowCustom: true },
				{ type: 'number', id: 'seconds', label: 'Seconds (negative = cut)', default: -60, min: -86400, max: 86400 },
			],
			callback: (ev) => {
				const n = Math.trunc(num(ev.options.item, 0))
				const sec = Math.trunc(num(ev.options.seconds))
				if (n >= 1 && sec !== 0) send(`TRIM ${n} ${sec}`)
			},
		},
		trim_live: {
			name: 'Schedule: Cut / extend the live item',
			description: 'Like "Cut / extend an item", always for the item on now — one key works all show.',
			options: [{ type: 'number', id: 'seconds', label: 'Seconds (negative = cut)', default: -60, min: -86400, max: 86400 }],
			callback: (ev) => {
				const n = resolveItem(self.snapshot, 0)
				const sec = Math.trunc(num(ev.options.seconds))
				if (n < 1) return self.log('info', 'No live running-order item')
				if (sec !== 0) send(`TRIM ${n} ${sec}`)
			},
		},
		trim_undo: {
			name: 'Schedule: Undo trims on an item',
			description: 'Puts the item back to its planned length. Item 0 = the live item.',
			options: [{ type: 'number', id: 'item', label: 'Item (0 = live)', default: 0, min: 0, max: 999 }],
			callback: (ev) => {
				const n = resolveItem(self.snapshot, num(ev.options.item, 0))
				const adj = itemAdjust(self.snapshot, n)
				if (n < 1 || adj === 0) return self.log('info', 'Nothing to undo')
				send(`TRIM ${n} ${-adj}`)
			},
		},
		delay: {
			name: 'Schedule: Announce a delay before the next item',
			description: 'Ripples through the running order until a gap, buffer or fixed start absorbs it.',
			options: [
				{
					type: 'dropdown',
					id: 'how',
					label: 'How',
					default: 'add',
					choices: [
						{ id: 'add', label: 'Add to the current delay' },
						{ id: 'set', label: 'Set to' },
						{ id: 'clear', label: 'Clear' },
					],
				},
				{ type: 'number', id: 'seconds', label: 'Seconds', default: 60, min: 0, max: 86400 },
			],
			callback: (ev) => {
				if (ev.options.how === 'clear') return send('DELAY clear')
				const base = ev.options.how === 'add' ? (self.snapshot?.delay ?? 0) : 0
				send(`DELAY ${Math.max(0, base + Math.trunc(num(ev.options.seconds)))}`)
			},
		},
		catchup: {
			name: 'Schedule: Catch up (land the live item on its planned end)',
			description: 'Re-times the live item with elastic end-at so it finishes when the plan said it would.',
			options: [],
			callback: () => send('CATCHUP'),
		},
		advice_apply: {
			name: 'Assistant: Apply suggestion',
			description: 'Runs the command behind assistant suggestion 1–3 (shown in $(stagewatch:advice_1) etc.). Information-only suggestions do nothing.',
			options: [{ type: 'number', id: 'n', label: 'Suggestion', default: 1, min: 1, max: 3 }],
			callback: (ev) => {
				const cmd = adviceCommand(self.snapshot, Math.trunc(num(ev.options.n, 1)))
				if (!cmd) return self.log('info', 'That suggestion has nothing to apply')
				send(cmd)
			},
		},

		// ---- 2.0: speaker presentation
		preview: {
			name: 'Display: Briefing preview (cue ladder at 10×)',
			options: [
				{
					type: 'dropdown',
					id: 'state',
					label: 'Preview',
					default: 'toggle',
					choices: [
						{ id: 'toggle', label: 'Toggle' },
						{ id: 'on', label: 'Start' },
						{ id: 'off', label: 'Stop' },
					],
				},
			],
			callback: (ev) => send(`PREVIEW ${str(ev.options.state, 'toggle')}`),
		},
		ladder: {
			name: 'Display: Cue ladder',
			description: 'Keynote 10/5/2/0, panel, Toastmasters, awards, spoken… (as set up in Stagewatch).',
			options: [{ type: 'dropdown', id: 'ladder', label: 'Ladder', default: 'default', choices: ladderChoices, allowCustom: true }],
			callback: (ev) => send(`LADDER ${str(ev.options.ladder, 'default')}`),
		},
		speaker_format: {
			name: 'Display: Speaker clock format',
			options: [
				{
					type: 'dropdown',
					id: 'format',
					label: 'Format',
					default: 'standard',
					choices: [
						{ id: 'standard', label: 'Standard (MM:SS)' },
						{ id: 'calm', label: 'Calm ("12 MIN" until the last minute)' },
						{ id: 'progress', label: 'Progress bar and cue only' },
					],
				},
			],
			callback: (ev) => send(`FORMAT ${str(ev.options.format, 'standard')}`),
		},

		// ---- 2.0: Q&A, rehearsal, speech, report
		qa_next: { name: 'Q&A: Take the next question live', options: [], callback: () => send('Q next') },
		qa_live: {
			name: 'Q&A: Finish the question on air',
			description: 'Marks the live question answered (or dismisses it) without taking the next one.',
			options: [
				{
					type: 'dropdown',
					id: 'state',
					label: 'Mark as',
					default: 'answered',
					choices: [
						{ id: 'answered', label: 'Answered' },
						{ id: 'dismiss', label: 'Dismissed' },
					],
				},
			],
			callback: (ev) => {
				const q = liveQuestion(self.snapshot)
				if (!q) return self.log('info', 'No question on air')
				send(`Q ${q.id} ${str(ev.options.state, 'answered')}`)
			},
		},
		rehearse: {
			name: 'Presentation: Record rehearsal timings (toggle)',
			description: 'Times every PowerPoint slide; the live talk is then compared against it.',
			options: [],
			callback: () => send('REHEARSE'),
		},
		listen: {
			name: 'Presentation: Speech pace tracking (toggle)',
			description: 'Follows the speaker through their script with Windows\' offline speech recognition.',
			options: [],
			callback: () => send('LISTEN'),
		},
		report: {
			name: 'Show: Save the planned-vs-actual report',
			options: [
				{
					type: 'dropdown',
					id: 'format',
					label: 'Format',
					default: 'html',
					choices: [
						{ id: 'html', label: 'HTML (print to PDF)' },
						{ id: 'csv', label: 'CSV' },
					],
				},
			],
			callback: (ev) => send(`REPORT ${str(ev.options.format, 'html')}`),
		},
		note: {
			name: 'Show: Log a note in the show report',
			options: [{ type: 'textinput', id: 'text', label: 'Note', default: 'Stage reset', useVariables: true }],
			callback: (ev) => {
				const t = str(ev.options.text)
				if (t) send(`NOTE ${quote(t)}`)
			},
		},

		// ---- 2.10: screens, undo, end of show, loops
		screen_reload: {
			name: 'Screens: reload',
			description:
				'Reload the page on one screen (name or id) or every browser screen — e.g. after changing custom CSS. Show apps and unproven claims are never included.',
			options: [{ type: 'textinput', id: 'who', label: 'Screen name or id (empty = all browser screens)', default: '', useVariables: true }],
			callback: (ev) => send(`SCREEN RELOAD ${str(ev.options.who).trim()}`.trim()),
		},
		screen_disconnect: {
			name: 'Screens: disconnect',
			description:
				'Take a screen off Stagewatch (it shows "disconnected by the operator" and stays off until reloaded on the device). Empty = all browser screens; show apps and unproven claims are never included (revoking a show app\'s key is the wall).',
			options: [{ type: 'textinput', id: 'who', label: 'Screen name or id (empty = all browser screens)', default: '', useVariables: true }],
			callback: (ev) => send(`SCREEN DISCONNECT ${str(ev.options.who).trim()}`.trim()),
		},
		// ---- 2.17: windows Stagewatch controls (each at an address of its own, /w/<token>)
		window_show: {
			name: 'Windows: show a page',
			description:
				'Put a page on a window Stagewatch controls (or cue it behind the holding page, ready for Release). A page such as /clock, a saved setup, holding or black.',
			options: [
				{ type: 'textinput', id: 'window', label: 'Window name (empty = all windows)', default: 'lobby', useVariables: true },
				{ type: 'textinput', id: 'content', label: 'Page, setup, holding or black', default: '/clock', useVariables: true },
				{
					type: 'dropdown',
					id: 'how',
					label: 'How',
					default: 'show',
					choices: [
						{ id: 'show', label: 'Show it now' },
						{ id: 'cue', label: 'Cue it behind the holding page' },
					],
				},
			],
			callback: (ev) => {
				const who = windowName(str(ev.options.window))
				const content = str(ev.options.content)
				if (who === null) return self.log('warn', 'Windows: a window name uses letters, digits, - and _')
				if (!/^([A-Za-z0-9_-]{1,40}|\/[^\s]{0,399})$/.test(content)) return self.log('warn', `Windows: "${content}" is not a page, a setup, holding or black`)
				send(`WINDOW ${str(ev.options.how, 'show') === 'cue' ? 'CUE' : 'SHOW'} ${who} ${content}`)
			},
		},
		window_hold: {
			name: 'Windows: holding page',
			description: 'The holding page on a window (the page stays loaded underneath, so Release is instant): plain, toggled, with words, or with a holding image.',
			options: [
				{ type: 'textinput', id: 'window', label: 'Window name (empty = all windows)', default: '', useVariables: true },
				{
					type: 'dropdown',
					id: 'how',
					label: 'Holding page',
					default: 'on',
					choices: [
						{ id: 'on', label: 'On' },
						{ id: 'toggle', label: 'Toggle' },
						{ id: 'text', label: 'On, with these words' },
						{ id: 'image', label: 'On, with this holding image (number or name)' },
					],
				},
				{ type: 'textinput', id: 'value', label: 'Words or image', default: '', useVariables: true },
			],
			callback: (ev) => {
				const who = windowName(str(ev.options.window))
				if (who === null) return self.log('warn', 'Windows: a window name uses letters, digits, - and _')
				const how = str(ev.options.how, 'on')
				const value = str(ev.options.value)
				if (how === 'toggle') return send(`WINDOW HOLD ${who} TOGGLE`)
				if (how === 'text') return send(`WINDOW HOLD ${who} TEXT ${quote(value)}`)
				if (how === 'image') return value ? send(`WINDOW HOLD ${who} IMAGE ${quote(value)}`) : self.log('warn', 'Windows: name the holding image')
				send(`WINDOW HOLD ${who}`)
			},
		},
		window_release: {
			name: 'Windows: release the holding page',
			description: 'Back from the holding page to the page (shown at once: it was loaded underneath).',
			options: [{ type: 'textinput', id: 'window', label: 'Window name (empty = all windows)', default: '', useVariables: true }],
			callback: (ev) => {
				const who = windowName(str(ev.options.window))
				if (who === null) return self.log('warn', 'Windows: a window name uses letters, digits, - and _')
				send(`WINDOW RELEASE ${who}`)
			},
		},
		window_identify: {
			name: 'Windows: identify',
			description: 'Show each window\'s name on it for 6 seconds.',
			options: [{ type: 'textinput', id: 'window', label: 'Window name (empty = all windows)', default: '', useVariables: true }],
			callback: (ev) => {
				const who = windowName(str(ev.options.window))
				if (who === null) return self.log('warn', 'Windows: a window name uses letters, digits, - and _')
				send(`WINDOW IDENTIFY ${who}`)
			},
		},
		// ---- 2.22: a window granted to a show app (Show Bus 12.4); every press goes with a request id, so Stagewatch's answer is in the log
		window_grant: {
			name: 'Windows: grant to a show app, or withdraw',
			description:
				"Ask for one window to be granted to a show app on the show network (a Patterns desk shows it as a page, a PlayMap node draws it), or withdraw a window's grants. A grant is a person's act and a button proves no person, so Stagewatch puts the grant on its desk for the operator to approve: the log says it waits, then what the operator decided. Name the app by its Show Bus id. A withdraw runs from here, and Stagewatch's answer is in the log too.",
			options: [
				{
					type: 'dropdown',
					id: 'what',
					label: 'Grant or withdraw',
					default: 'withdraw',
					choices: [
						{ id: 'grant', label: 'Grant (one window, one app)' },
						{ id: 'withdraw', label: 'Withdraw' },
					],
				},
				{ type: 'textinput', id: 'window', label: 'Window name (empty = all windows, for a withdraw only)', default: 'lobby', useVariables: true },
				{ type: 'textinput', id: 'app', label: 'Show app: its Show Bus id (a withdraw also takes its name; empty = every app, for a withdraw only)', default: '', useVariables: true },
			],
			callback: (ev) => {
				const r = windowGrantLine(str(ev.options.what, 'withdraw'), str(ev.options.window), str(ev.options.app))
				if ('error' in r) return self.log('warn', r.error)
				const shut = r.line.startsWith('WINDOW GRANT ') ? accessRefuses(self.access ?? null, 'grant') : null
				if (shut) return self.log('warn', shut)
				self.sendWithReceipt(r.line)
			},
		},
		// ---- 2.22: a person's lines from a deck (Show Bus §11), this PC or an address Stagewatch lists; Stagewatch's answer is in the log
		peer_line: {
			name: 'Show apps: send a line to another app',
			description:
				"Sends one line another show app lists (PlayMap, Patterns, AGMessage) through Stagewatch's desk, approved by this press. Stagewatch sends only a line the app's verified verbs list, while the app collects; its answer — and the app's — is in the log. Taken only from Stagewatch's own PC or an address listed in Settings → Remote → May drive other show apps.",
			options: [
				{ type: 'dropdown', id: 'app', label: 'Show app', default: peerChoices(cfg.peers)[0]?.id ?? '', choices: peerChoices(cfg.peers), allowCustom: true },
				{ type: 'textinput', id: 'line', label: "Line, in the app's own words (e.g. PLAYLIST CUE \"Main show\" #3)", default: '', useVariables: true },
			],
			callback: (ev) => {
				const shut = accessRefuses(self.access ?? null, 'peer')
				if (shut) return self.log('warn', shut)
				const r = peerLine(str(ev.options.app), str(ev.options.line))
				if ('error' in r) return self.log('warn', r.error)
				self.sendWithReceipt(r.line)
			},
		},
		desk_approve: {
			name: 'Desk: approve the oldest proposal',
			description:
				'Approves the proposal $(stagewatch:desk_next_id) names, the oldest waiting on Stagewatch\'s desk. A high one takes a second press within 3 s, which approves it by its id with HIGH. Stagewatch\'s answer is in the log.',
			options: [],
			callback: () => {
				const shut = accessRefuses(self.access ?? null, 'desk')
				if (shut) return self.log('warn', shut)
				const r = deskApprove(self.snapshot, self.deskArmed ?? null, Date.now())
				if ('error' in r) return self.log('warn', r.error)
				if ('arm' in r) {
					self.deskArmed = r.arm
					return self.log('info', r.note)
				}
				self.deskArmed = null
				self.sendWithReceipt(r.line)
			},
		},
		desk_reject: {
			name: 'Desk: reject the oldest proposal',
			description: "Rejects the proposal $(stagewatch:desk_next_id) names, the oldest waiting on Stagewatch's desk, with an optional reason for its journal. Stagewatch's answer is in the log.",
			options: [{ type: 'textinput', id: 'why', label: 'Reason (optional)', default: '', useVariables: true }],
			callback: (ev) => {
				const shut = accessRefuses(self.access ?? null, 'desk')
				if (shut) return self.log('warn', shut)
				const r = deskReject(self.snapshot, str(ev.options.why))
				if ('error' in r) return self.log('warn', r.error)
				self.deskArmed = null
				self.sendWithReceipt(r.line)
			},
		},
		live_undo: {
			name: 'Undo the last live edit',
			description: 'Reverses the last trim, delay, nudge, department note, recovery or approved copilot plan (as one step).',
			options: [],
			callback: () => send('UNDO'),
		},
		live_redo: {
			name: 'Redo',
			description: 'Puts back the last live edit you undid.',
			options: [],
			callback: () => send('REDO'),
		},
		end_of_show: {
			name: 'Running order: show ends at',
			description: 'When the show must end (HH:MM). With no start time the plan is back-timed from it; it is always the last curfew. Empty clears it.',
			options: [{ type: 'textinput', id: 'time', label: 'End time (HH:MM, empty = clear)', default: '', useVariables: true }],
			callback: (ev) => {
				const t = str(ev.options.time).trim()
				if (t.length && !/^(\d{4}-\d{2}-\d{2} )?\d{1,2}[:.]\d{2}( ?[ap]m)?$/i.test(t)) {
					self.log('warn', `End of show: "${t}" is not a time (use 17:30)`)
					return
				}
				send(`ENDOFSHOW ${t.length ? t : 'clear'}`)
			},
		},
		loop: {
			name: 'Running order: loop',
			description: 'Run the running order again when it ends — a number of extra passes, for ever (exhibitions, walk-in loops), or off.',
			options: [
				{
					type: 'dropdown',
					id: 'mode',
					label: 'Loop',
					default: 'off',
					choices: [
						{ id: 'off', label: 'Off' },
						{ id: 'forever', label: 'For ever' },
						{ id: 'n', label: 'Extra passes…' },
					],
				},
				{ type: 'number', id: 'n', label: 'Extra passes (1-99, when "Extra passes…")', default: 1, min: 1, max: 99 },
			],
			callback: (ev) => {
				const mode = str(ev.options.mode, 'off')
				send(`LOOP ${mode === 'n' ? Math.min(99, Math.max(1, Math.trunc(num(ev.options.n)))) : mode}`)
			},
		},

		// ---- 2.10: Patterns video (Stagewatch forwards these to Patterns over its wire)
		patterns_end: {
			name: 'Patterns: clip to its last seconds',
			description:
				"The clip on air in Patterns jumps to its last N seconds (default 10); its end and whatever follows still play. Goes over Stagewatch's playout link called patterns (else its only link); Patterns' answer is in the log and $(stagewatch:playout_answer).",
			options: [{ type: 'number', id: 'seconds', label: 'Seconds before the end', default: 10, min: 1, max: 3600 }],
			callback: (ev) => playout(`PATTERNS END ${Math.min(3600, Math.max(1, Math.trunc(num(ev.options.seconds, 10))))}`),
		},
		patterns_restart: {
			name: 'Patterns: restart the clip',
			description: 'The clip on air in Patterns plays again from the start.',
			options: [],
			callback: () => playout('PATTERNS RESTART'),
		},
		patterns_transport: {
			name: 'Patterns: play / pause / stop the clip',
			description: 'The clip on air in Patterns (Patterns Round 86 or later). Stop puts it back to its top, held — it never turns outputs off.',
			options: [
				{
					type: 'dropdown',
					id: 'what',
					label: 'Action',
					default: 'pause',
					choices: [
						{ id: 'play', label: 'Play on' },
						{ id: 'pause', label: 'Pause' },
						{ id: 'stop', label: 'Stop (back to the top, held)' },
					],
				},
			],
			callback: (ev) => {
				const what = str(ev.options.what, 'pause')
				if (what === 'play' || what === 'pause' || what === 'stop') playout(`PATTERNS ${what.toUpperCase()}`)
			},
		},
		patterns_seek: {
			name: 'Patterns: move the clip to a time',
			description: 'Seek the clip on air in Patterns to a point from its start (m:ss or seconds; Patterns Round 86 or later).',
			options: [{ type: 'textinput', id: 'at', label: 'Point in the clip (m:ss or seconds)', default: '0:00', useVariables: true }],
			callback: (ev) => {
				const at = str(ev.options.at).trim()
				if (!/^(\d{1,2}:)?\d{1,2}:\d{2}$|^\d{1,5}$/.test(at)) {
					self.log('warn', `Patterns seek: "${at}" is not a time (use m:ss)`)
					return
				}
				playout(`PATTERNS SEEK ${at}`)
			},
		},

		// ---- 2.0: actions that return a value (for Companion's action results / logic)
		get_remaining: {
			name: 'Value: Seconds remaining',
			description: 'Returns the seconds left (negative in overtime) for use in logic and expressions.',
			options: [],
			hasResult: true,
			callback: () => self.snapshot?.seconds ?? 0,
		},
		get_item_expected: {
			name: 'Value: Expected start of an item',
			description: 'Returns the projected start time (HH:MM) of a running-order item.',
			options: [{ type: 'number', id: 'item', label: 'Item', default: 1, min: 1, max: 999 }],
			hasResult: true,
			callback: (ev) => self.snapshot?.timings?.find((t) => t.i + 1 === Math.trunc(num(ev.options.item)))?.expected ?? '',
		},
		get_curfew_margin: {
			name: 'Value: Seconds to spare at the next curfew',
			description: 'Negative when the show is on course to overrun it.',
			options: [],
			hasResult: true,
			callback: () => self.snapshot?.hardOutMargin ?? 0,
		},

		// ---- power users
		raw: {
			name: 'Advanced: Send raw command',
			description: 'Any Stagewatch protocol command, e.g. NUDGE -30 or GOTO 4 start. See the module help.',
			options: [{ type: 'textinput', id: 'command', label: 'Command', default: 'TOGGLE', useVariables: true }],
			callback: (ev) => {
				const c = str(ev.options.command)
				if (c) send(c)
			},
		},
	}

	// ---- 2.20: playout links (Patterns desks and PlayMap nodes), under the licence that runs them
	if (playoutOffered(cfg, self.configHeard)) {
		actions.playout_transport = {
			name: 'Playout: the clip on air (play, pause, stop, restart, last seconds, seek)',
			description:
				"The clip on air on one of Stagewatch's playout links — a Patterns desk or a PlayMap node — or, with none named, on the only link switched on, else the one feeding the VT. What Stagewatch and the peer answer is in this connection's log and $(stagewatch:playout_answer): a Patterns playlist item's pause, say, is refused in Patterns' own words.",
			options: [
				{ type: 'dropdown', id: 'link', label: 'Link', default: '', choices: linkChoices, allowCustom: true, tooltip: LINK_TIP },
				{
					type: 'dropdown',
					id: 'what',
					label: 'Action',
					default: 'pause',
					disableAutoExpression: true,
					choices: [
						{ id: 'play', label: 'Play on' },
						{ id: 'pause', label: 'Pause' },
						{ id: 'stop', label: 'Stop (back to the top, held)' },
						{ id: 'restart', label: 'Restart from the top' },
						{ id: 'end', label: 'Jump to its last seconds' },
						{ id: 'seek', label: 'Move to a point' },
					],
				},
				{ type: 'number', id: 'seconds', label: 'Seconds before the end', default: 10, min: 1, max: 3600, isVisibleExpression: "$(options:what) == 'end'" },
				{ type: 'textinput', id: 'at', label: 'Point in the clip (m:ss or seconds)', default: '0:00', useVariables: true, isVisibleExpression: "$(options:what) == 'seek'" },
			],
			callback: (ev) => built(playoutTransportLine(str(ev.options.link), str(ev.options.what, 'pause'), num(ev.options.seconds, 10), str(ev.options.at))),
		}
		actions.playout_go = {
			name: 'Playout: GO (a PlayMap playlist item or a Patterns cue)',
			description:
				"Starts something new on a playout link's air: on a PlayMap node a playlist's item (its next one when the item is empty), on a Patterns desk a cue (in the item field, playlist empty; Patterns' own standby decides). Together starts the GOs of every link asked together at one instant, where each clock allows. With two links, name one. The peer's answer is in the log and $(stagewatch:playout_answer).",
			options: [
				{ type: 'dropdown', id: 'link', label: 'Link', default: '', choices: startLinkChoices, allowCustom: true, tooltip: LINK_TIP },
				{ type: 'textinput', id: 'playlist', label: 'Playlist (PlayMap; empty for a Patterns cue)', default: '', useVariables: true },
				{ type: 'textinput', id: 'item', label: "Item (PlayMap; empty = the playlist's next) or cue (Patterns)", default: '', useVariables: true },
				{ type: 'checkbox', id: 'together', label: 'Together (at one instant with the other links asked together)', default: false },
			],
			callback: (ev) => built(playoutStartLine('go', str(ev.options.link), str(ev.options.playlist), str(ev.options.item), ev.options.together === true)),
		}
		actions.playout_cue = {
			name: 'Playout: cue (a PlayMap playlist item, or a Patterns cue to standby)',
			description:
				"Readies the next thing on a playout link: on a PlayMap node a playlist's item, cued only while that playlist is idle (a cued item shows its first frame on air); on a Patterns desk its own standby moves to the cue. The peer's answer is in the log and $(stagewatch:playout_answer).",
			options: [
				{ type: 'dropdown', id: 'link', label: 'Link', default: '', choices: startLinkChoices, allowCustom: true, tooltip: LINK_TIP },
				{ type: 'textinput', id: 'playlist', label: 'Playlist (PlayMap; empty for a Patterns cue)', default: '', useVariables: true },
				{ type: 'textinput', id: 'item', label: 'Item (PlayMap) or cue (Patterns)', default: '', useVariables: true },
			],
			callback: (ev) => built(playoutStartLine('cue', str(ev.options.link), str(ev.options.playlist), str(ev.options.item), false)),
		}
	}
	self.setActionDefinitions(actions)
}

export type { CompanionOptionValues }
