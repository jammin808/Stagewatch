// Pure state layer: no I/O, no Companion calls — so it is fast and unit-testable.
// Stagewatch pushes a whole SNAPSHOT frame on every visible change; we turn it into
// variable values, then diff against the previous frame so Companion only hears about
// what actually changed (one setVariableValues + one checkFeedbacks per frame).

export type TimerState = 'idle' | 'running' | 'paused' | 'expired'
export type TimerMode = 'countdown' | 'countup' | 'tod'
export type TimerPhase = 'normal' | 'warning' | 'wrapup' | 'overtime'

export interface SignalInfo {
	id: number
	kind: 'message' | 'standby' | 'go' | 'call' | 'nudge'
	target: string
	text: string
	state: 'sent' | 'seen' | 'done' | 'cancelled'
	escalated: boolean
	age: number
	by: string
}

export interface TimingInfo {
	i: number
	title: string
	speaker: string
	kind: string
	seconds: number
	adjust: number
	planned: string
	expected: string
	expectedEnd: string
	actual: string
	actualEnd: string
	status: 'done' | 'live' | 'loaded' | 'upcoming'
	delay: number
	conflict: boolean
	fixed: boolean
	hardOut: string
}

export interface AdviceInfo {
	key: string
	level: 'info' | 'suggest' | 'warn' | 'urgent'
	text: string
	command: string | null
	label: string | null
}

export interface RoomInfo {
	room: string
	host: string
	webPort: number
	title: string
	text: string
	state: string
	phase: string
	offset: number | null
	hardOut: string
	hardOutMargin: number | null
	openSignals: number
	stale: boolean
	self: boolean
}

export interface Snapshot {
	type: 'state'
	mode: TimerMode
	state: TimerState
	seconds: number
	text: string
	total: number
	phase: TimerPhase
	color: string
	title: string
	next: string
	item: number
	items: number
	preset?: number
	endTime: string
	timeOfDay: string
	offset: number | null
	schedule: string
	message: string
	urgency: 'info' | 'warning' | 'critical'
	flash: 'off' | 'slow' | 'fast'
	flashTimer: boolean
	blackout: boolean
	watermark?: string
	configVersion: number
	// ---- Stagewatch 2.0 (optional: older Stagewatch versions don't send them)
	speakerText?: string
	cue?: string
	finishBy?: string
	preview?: boolean
	stretching?: boolean
	room?: string
	messageId?: number
	messageAck?: '' | 'sent' | 'seen' | 'done'
	messageAckBy?: string
	signals?: SignalInfo[]
	slides?: { slide: number; count: number; next: string; notes: string; ghost: number; projected: number; pace: string; rehearsing: boolean } | null
	speech?: { percent: number; wpm: number; projected: number | null; listening: boolean } | null
	/** The clip feeding the VT countdown: vMix's, or a playout link's (2.20: `link` names it, `node` is its peer's Show Bus id). */
	playout?: { title: string; remaining: number; duration: number; running: boolean; source: string; link?: string; node?: string } | null
	qaLive?: string
	qaPending?: number
	qaQueued?: number
	qaFit?: number
	hardOut?: string
	hardOutMargin?: number | null
	forecast?: string
	delay?: number
	clockOffsetMs?: number | null
	// detail frames
	timings?: TimingInfo[]
	advice?: AdviceInfo[]
	questions?: { id: number; text: string; name: string; state: string }[]
	rooms?: RoomInfo[]
	// ---- Stagewatch 2.2
	output?: OutputMode
	ndi?: boolean
	// ---- Stagewatch 2.3
	holding?: string
	holdingName?: string
	holdingFill?: boolean
	// ---- Stagewatch 2.4
	style?: 'standard' | 'holo'
	holoFloor?: boolean
	scene?: 'holo' | 'egg'
	animate?: boolean
	particles?: boolean
	// ---- Stagewatch 2.7
	ltc?: string
	ltcRunning?: boolean
	ltcChase?: boolean
	// ---- Stagewatch 2.8
	aux?: AuxTimerView[]
	auxLine?: string
	// ---- Stagewatch 2.9
	roll?: boolean
	rundown?: string
	rundowns?: string[]
	notesVersion?: number
}

/** One extra (aux) timer: it runs beside the show without changing it. */
export interface AuxTimerView {
	n: number
	name: string
	text: string
	seconds: number
	duration: number
	running: boolean
	up: boolean
	stage: boolean
}

export type OutputMode = 'closed' | 'full' | 'keyed' | 'mini'


export interface ShowConfig {
	presets: { slot: number; label: string; seconds: number }[]
	messages: { label: string; text: string; urgency: string; flash: string }[]
	rundown: { title: string; speaker: string | null; seconds: number }[]
	ladders?: { id: string; name: string }[]
	departments?: string[]
	room?: string
	ladder?: string
	/** 2.2: licence name and the feature keys it unlocks (absent on older Stagewatch = everything allowed). */
	tier?: string
	features?: string[]
	/** 2.3: the holding-image library. */
	holding?: { id: string; name: string }[]
	/**
	 * 2.20: the playout links a `PLAYOUT <link> …` line may name — each switched on under the licence (none without it), its peer's
	 * kind once heard or set (`playmap`, `patterns`, "" while neither) and its peer as people read it. Absent from a Stagewatch older
	 * than 2.20, whose grammar has no `PLAYOUT`.
	 */
	playoutLinks?: PlayoutLinkInfo[]
}

export interface PlayoutLinkInfo {
	name: string
	kind: string
	label: string
}

/** The licence key that carries playout (Stagewatch's FeatureGate.AllowsPlayout): the vMix VT countdown and the playout links. */
export const PLAYOUT_FEATURE = 'vmix'

/**
 * Whether the playout actions are offered: until a CONFIG is heard (so buttons keep their actions while Stagewatch is away), then
 * only from a Stagewatch whose grammar has `PLAYOUT` (its CONFIG carries `playoutLinks`) and whose licence carries playout. An empty
 * list keeps them: with every link switched off a press is still Stagewatch's to refuse, in its words on the log, where an action
 * Companion no longer knows would do nothing and say nothing.
 */
export function playoutOffered(config: ShowConfig, heard: boolean): boolean {
	return !heard || (Array.isArray(config.playoutLinks) && hasFeature(config, PLAYOUT_FEATURE))
}

/** The config's playout links, each a name Stagewatch would read; anything else in the list is left out. */
export function readPlayoutLinks(v: unknown): PlayoutLinkInfo[] | undefined {
	if (!Array.isArray(v)) return undefined
	return v
		.filter((l): l is PlayoutLinkInfo => !!l && typeof l.name === 'string' && /^[a-z0-9][a-z0-9-]{0,23}$/.test(l.name))
		.map((l) => ({ name: l.name, kind: typeof l.kind === 'string' ? l.kind : '', label: typeof l.label === 'string' && l.label ? l.label : l.name }))
}

/**
 * Whether the clip feeding the VT comes from `from`: a link's name, `vmix`, or "" for any playout link — and, with `runningOnly`,
 * whether it runs, and with `last` above 0, whether it is in its last `last` seconds. A frame from before 2.20 names no link: a clip
 * whose source is not vMix then counts as a link's.
 */
export function vtFrom(s: Snapshot | null, from: string, runningOnly = false, last = 0): boolean {
	const p = s?.playout
	if (!p || (runningOnly && !p.running) || (last > 0 && !(p.remaining <= last))) return false
	const link = p.link ?? ''
	if (from === 'vmix') return link === '' && p.source === 'vMix'
	if (from === '') return link !== '' || p.source !== 'vMix'
	return link === from
}

/** Licence features Companion can check (keys sent by Stagewatch in CONFIG). */
export const FEATURE_CHOICES: { id: string; label: string }[] = [
	{ id: 'crewcues', label: 'Crew cues & acknowledgements (Production)' },
	{ id: 'forecast', label: 'Forecast & curfew suggestions (Production)' },
	{ id: 'assistant', label: 'Producer assistant (Production)' },
	{ id: 'multiroom', label: 'Multi-room (Production)' },
	{ id: 'cuelights', label: 'Cue lights (Production)' },
	{ id: 'timecode', label: 'LTC & time server (Production)' },
	{ id: 'vmix', label: 'vMix VT countdown & playout links (Production)' },
	{ id: 'speech', label: 'Speech pace (Production)' },
	{ id: 'fullreport', label: 'Full show report (Production)' },
	{ id: 'mcp', label: 'MCP / AI tools (Production)' },
	{ id: 'runningorder', label: 'Running order (Pro)' },
	{ id: 'powerpoint', label: 'PowerPoint (Pro)' },
	{ id: 'csvreport', label: 'CSV report (Pro)' },
	{ id: 'keyed', label: 'Keyed & floating outputs (Standard)' },
	{ id: 'ndi', label: 'NDI (Standard)' },
	{ id: 'endat', label: 'End-at timing (Standard)' },
	{ id: 'quickmessages', label: 'Quick messages (Standard)' },
	{ id: 'holding', label: 'Holding images (Standard)' },
	{ id: 'holo', label: 'Holographic 3D display (Pro)' },
]

/** True when the licence includes the feature (older Stagewatch versions don't say: assume yes). */
export function hasFeature(config: ShowConfig, key: string): boolean {
	return !Array.isArray(config.features) || config.features.includes(key)
}

/** The running-order item a "live item" option means: 0 / blank = the live one. Returns 1-based, or 0. */
export function resolveItem(s: Snapshot | null, n: number): number {
	if (n >= 1) return Math.trunc(n)
	return s && s.item >= 0 ? s.item + 1 : 0
}

/** Seconds an item has been trimmed (+ extended, − cut). */
export function itemAdjust(s: Snapshot | null, item: number): number {
	return s?.timings?.find((t) => t.i + 1 === item)?.adjust ?? 0
}

/** The newest cue still waiting for an answer (optionally for one target). */
export function newestUnanswered(s: Snapshot | null, target = ''): SignalInfo | undefined {
	const t = target.trim().toLowerCase()
	const mine = openSignals(s).filter((g) => g.state === 'sent' && (!t || g.target.toLowerCase() === t))
	return mine[mine.length - 1]
}

/** The question currently live, if any. */
export function liveQuestion(s: Snapshot | null): { id: number; text: string } | undefined {
	return s?.questions?.find((q) => q.state === 'live')
}

export const EMPTY_CONFIG: ShowConfig = { presets: [], messages: [], rundown: [], ladders: [], departments: [] }

export type VariableValues = Record<string, string | number | boolean>

/** Every feedback id the module defines, so state changes map to exact rechecks. */
export const FEEDBACK_IDS = [
	'state',
	'running',
	'overtime',
	'phase',
	'phase_color',
	'blackout',
	'message_active',
	'current_item',
	'item_done',
	'current_preset',
	'mode',
	'time_below',
	'behind_schedule',
	'connected',
	// 2.0
	'message_ack',
	'cue_waiting',
	'dept_state',
	'standing_by',
	'hard_out_risk',
	'pace',
	'vt_running',
	'preview',
	'qa_waiting',
	'item_late',
	'cue_label',
	'stretching',
	'room_late',
	'advice_waiting',
	'remaining',
	'progress_value',
	'clock_drift',
	// 2.2
	'output',
	'ndi',
	'feature_locked',
	'item_kind',
	'item_trimmed',
	// 2.3
	'holding',
	// 2.4
	'holographic',
	'ltc_running',
	'ltc_chase',
	'aux_running',
	'roll',
	'rundown_live',
	// 2.20
	'vt_link',
] as const
export type FeedbackId = (typeof FEEDBACK_IDS)[number]

const pad = (n: number): string => String(n).padStart(2, '0')

/** "-01:02:03" style, always with hours. */
export function formatHms(totalSeconds: number): string {
	const neg = totalSeconds < 0
	const s = Math.abs(Math.trunc(totalSeconds))
	return `${neg ? '-' : ''}${pad(Math.floor(s / 3600))}:${pad(Math.floor(s / 60) % 60)}:${pad(s % 60)}`
}

/** "04:59" or "1:02:03" — what fits on a 72px key. */
export function formatShort(totalSeconds: number): string {
	const neg = totalSeconds < 0
	const s = Math.abs(Math.trunc(totalSeconds))
	const h = Math.floor(s / 3600)
	const body = h > 0 ? `${h}:${pad(Math.floor(s / 60) % 60)}:${pad(s % 60)}` : `${pad(Math.floor(s / 60))}:${pad(s % 60)}`
	return (neg ? '-' : '') + body
}

/** "+03:12" / "-01:00" / "±00:00". */
export function signed(seconds: number | null | undefined): string {
	if (seconds === null || seconds === undefined) return ''
	return (seconds > 0 ? '+' : seconds < 0 ? '-' : '±') + formatShort(Math.abs(seconds))
}

export function isCounting(s: Snapshot | null): boolean {
	return !!s && (s.state === 'running' || (s.state === 'expired' && s.seconds < 0))
}

/** Variable-safe slug for department names ("Video / LED" → "video_led"). */
export function slug(name: string): string {
	return name
		.toLowerCase()
		.replace(/[^a-z0-9]+/g, '_')
		.replace(/^_+|_+$/g, '')
}

/** The open (unanswered or seen-but-not-done) signals. */
export function openSignals(s: Snapshot | null): SignalInfo[] {
	return (s?.signals ?? []).filter((g) => g.state === 'sent' || g.state === 'seen')
}

export type DeptState = 'clear' | 'standby' | 'standby_ack' | 'go' | 'go_ack'

/** Where a department's latest cue stands. */
/** Departments standing by (a standby not yet answered with a GO), in the order they were stood by. */
export function standingBy(s: Snapshot | null): NonNullable<Snapshot['signals']> {
	return (s?.signals ?? []).filter((g) => g.kind === 'standby' && g.state !== 'cancelled')
}

export function deptState(s: Snapshot | null, dept: string): DeptState {
	const mine = (s?.signals ?? []).filter((g) => g.target.toLowerCase() === dept.toLowerCase() && g.state !== 'cancelled')
	const g = mine[mine.length - 1]
	if (!g) return 'clear'
	const acked = g.state === 'seen' || g.state === 'done'
	if (g.kind === 'standby') return acked ? 'standby_ack' : 'standby'
	if (g.kind === 'go') return acked ? 'go_ack' : 'go'
	return 'clear'
}

export function deriveVariables(s: Snapshot | null, connected: boolean, config: ShowConfig = EMPTY_CONFIG): VariableValues {
	if (!s) {
		return {
			connected,
			time: connected ? '--:--' : 'OFFLINE',
			time_short: connected ? '--:--' : 'OFF',
			state: connected ? 'unknown' : 'offline',
		}
	}
	const abs = Math.abs(s.seconds)
	const progress = s.total > 0 && s.mode === 'countdown' ? Math.round(Math.min(1, Math.max(0, s.seconds / s.total)) * 100) : 0
	const open = openSignals(s)
	const advice = s.advice ?? []
	const timings = s.timings ?? []
	const nextTiming = s.item >= 0 && s.item + 1 < timings.length ? timings[s.item + 1] : undefined
	const v: VariableValues = {
		connected,
		time: s.text,
		time_short: formatShort(s.seconds),
		time_hms: s.mode === 'tod' ? s.text : formatHms(s.seconds),
		hours: pad(Math.floor(abs / 3600)),
		minutes: pad(Math.floor(abs / 60) % 60),
		seconds_part: pad(abs % 60),
		sign: s.seconds < 0 ? '-' : '',
		seconds: s.seconds,
		total: s.total > 0 ? formatShort(s.total) : '',
		total_seconds: s.total,
		progress,
		elapsed: s.mode === 'countdown' && s.total > 0 ? formatShort(s.total - s.seconds) : '',
		state: s.state,
		mode: s.mode,
		phase: s.phase,
		running: isCounting(s),
		overtime: s.phase === 'overtime',
		color: s.color,
		title: s.title,
		next: s.next,
		item: s.item >= 0 ? s.item + 1 : 0,
		items: s.items,
		preset: s.preset != null && s.preset > 0 ? s.preset : 0,
		end_time: s.endTime.replace(/^(ENDS|ends)\s*/, ''),
		time_of_day: s.timeOfDay,
		offset_seconds: s.offset ?? 0,
		offset: s.offset == null ? '' : signed(s.offset),
		schedule: s.schedule,
		message: s.message,
		message_urgency: s.urgency,
		blackout: s.blackout,
		// ---- 2.0: speaker face
		speaker_time: s.speakerText ?? s.text,
		cue: s.cue ?? '',
		finish_by: (s.finishBy ?? '').replace(/^FINISH BY\s*/, ''),
		preview: s.preview === true,
		stretching: s.stretching === true,
		room: s.room ?? config.room ?? '',
		ladder: config.ladders?.find((l) => l.id === config.ladder)?.name ?? config.ladder ?? '',
		// ---- acknowledged cues
		message_ack: s.messageAck ?? '',
		message_ack_by: s.messageAckBy ?? '',
		cues_open: open.length,
		cues_waiting: open.filter((g) => g.state === 'sent').length,
		cues_unanswered: open.filter((g) => g.escalated && g.state === 'sent').length,
		last_cue: open.length ? `${open[open.length - 1].kind} ${open[open.length - 1].target}` : '',
		standing_by: standingBy(s).length,
		standing_by_list: standingBy(s).map((g) => g.target).join(', '),
		// ---- presentation
		slide: s.slides?.slide ?? 0,
		slides: s.slides?.count ?? 0,
		slide_next: s.slides?.next ?? '',
		pace: s.slides?.pace && s.slides.pace !== 'unknown' ? s.slides.pace : '',
		ghost: s.slides && s.slides.pace !== 'unknown' ? signed(s.slides.ghost) : '',
		script_percent: s.speech?.percent ?? 0,
		wpm: s.speech?.wpm ?? 0,
		vt_title: s.playout?.title ?? '',
		vt_remaining: s.playout ? formatShort(s.playout.remaining) : '',
		vt_seconds: s.playout?.remaining ?? 0,
		vt_source: s.playout?.source ?? '',
		vt_link: s.playout?.link ?? '',
		// ---- schedule recovery
		hard_out: s.hardOut ?? '',
		hard_out_margin: s.hardOutMargin == null ? '' : signed(s.hardOutMargin),
		forecast: s.forecast ?? '',
		delay: s.delay ? formatShort(s.delay) : '',
		next_expected: nextTiming?.expected ?? '',
		clock_offset_ms: s.clockOffsetMs ?? 0,
		// ---- Q&A
		qa_live: s.qaLive ?? '',
		qa_new: s.qaPending ?? 0,
		qa_queued: s.qaQueued ?? 0,
		qa_fit: s.qaFit ?? 0,
		// ---- assistant & rooms
		advice_count: advice.length,
		rooms: (s.rooms ?? []).length,
		rooms_late: (s.rooms ?? []).filter((r) => (r.offset ?? 0) > 60 || (r.hardOutMargin ?? 0) < 0).length,
		// ---- 2.2: outputs, licence, live item detail
		output: s.output ?? '',
		ndi: s.ndi === true,
		licence: config.tier ?? '',
		live_kind: s.item >= 0 ? (timings[s.item]?.kind ?? '') : '',
		live_trim: s.item >= 0 && timings[s.item] && timings[s.item].adjust ? signed(timings[s.item].adjust) : '',
		qa_live_id: liveQuestion(s)?.id ?? 0,
		// ---- 2.3: holding image
		holding: s.holding ? (s.holdingName ?? '') : '',
		holding_on: !!s.holding,
		// ---- 2.4: display style
		style: s.style !== 'holo' ? 'standard' : s.scene === 'egg' ? 'egg' : 'holo',
		// ---- 2.7: LTC in
		ltc: s.ltc ?? '',
		ltc_rolling: s.ltcRunning === true,
		ltc_chase: s.ltcChase === true,
		// ---- 2.8: extra timers
		aux_on_stage: s.auxLine ?? '',
		// ---- 2.9: roll and running orders
		roll: s.roll === true,
		rundown: s.rundown ?? '',
		rundowns: (s.rundowns ?? []).join(', '),
	}
	for (let i = 0; i < 3; i++) {
		const a = (s.aux ?? []).find((x) => x.n === i + 1)
		v[`aux_${i + 1}`] = a?.text ?? ''
		v[`aux_${i + 1}_name`] = a?.name ?? ''
		v[`aux_${i + 1}_running`] = a?.running === true
	}
	for (let i = 0; i < 3; i++) {
		v[`advice_${i + 1}`] = advice[i]?.text ?? ''
		v[`advice_${i + 1}_action`] = advice[i]?.label ?? ''
	}
	timings.forEach((t) => {
		const n = t.i + 1
		v[`item_${n}_planned`] = t.planned
		v[`item_${n}_expected`] = t.status === 'done' ? '' : t.expected
		v[`item_${n}_actual`] = t.actual
		v[`item_${n}_status`] = t.status
		v[`item_${n}_delay`] = Math.abs(t.delay) < 30 ? '' : signed(t.delay)
	})
	for (const d of config.departments ?? []) v[`dept_${slug(d)}`] = deptState(s, d)
	return v
}

export function configVariables(c: ShowConfig): VariableValues {
	const v: VariableValues = {}
	for (const p of c.presets) {
		v[`preset_${p.slot}_label`] = p.label
		v[`preset_${p.slot}_duration`] = p.seconds < 0 ? 'count up' : formatShort(p.seconds)
	}
	c.rundown.forEach((r, i) => {
		v[`item_${i + 1}_title`] = r.title
		v[`item_${i + 1}_speaker`] = r.speaker ?? ''
		v[`item_${i + 1}_duration`] = formatShort(r.seconds)
	})
	;(c.departments ?? []).forEach((d, i) => (v[`dept_name_${i + 1}`] = d))
	;(c.holding ?? []).forEach((h, i) => (v[`holding_${i + 1}_name`] = h.name))
	return v
}

/** Only the entries whose value changed or are new; entries that disappeared are blanked. */
export function diffValues(prev: VariableValues, next: VariableValues): VariableValues {
	const out: VariableValues = {}
	for (const k of Object.keys(next)) if (prev[k] !== next[k]) out[k] = next[k]
	for (const k of Object.keys(prev)) if (!(k in next) && prev[k] !== '') out[k] = ''
	return out
}

const same = (a: unknown, b: unknown): boolean => JSON.stringify(a) === JSON.stringify(b)

/** Which feedbacks need re-evaluating after a frame change. */
export function feedbacksAffected(prev: Snapshot | null, next: Snapshot | null, connectionChanged: boolean): FeedbackId[] {
	if (!prev || !next || connectionChanged) return [...FEEDBACK_IDS]
	const out = new Set<FeedbackId>()
	const add = (...ids: FeedbackId[]): void => ids.forEach((f) => out.add(f))
	if (prev.state !== next.state) add('state', 'running', 'overtime', 'time_below', 'remaining')
	if (prev.phase !== next.phase) add('phase', 'phase_color', 'overtime')
	if (prev.color !== next.color) add('phase_color')
	if (prev.seconds !== next.seconds) add('time_below', 'running', 'remaining', 'progress_value')
	if (prev.total !== next.total) add('progress_value')
	if (prev.blackout !== next.blackout) add('blackout')
	if (prev.message !== next.message || prev.urgency !== next.urgency) add('message_active')
	if (prev.item !== next.item) add('current_item', 'item_done', 'item_late')
	if (prev.preset !== next.preset) add('current_preset')
	if (prev.mode !== next.mode) add('mode', 'running')
	if (prev.offset !== next.offset) add('behind_schedule')
	if (prev.messageAck !== next.messageAck) add('message_ack')
	if (!same(prev.signals, next.signals)) add('cue_waiting', 'dept_state', 'standing_by', 'message_ack')
	if (prev.hardOutMargin !== next.hardOutMargin) add('hard_out_risk')
	if (!same(prev.slides, next.slides)) add('pace')
	if (!same(prev.playout, next.playout)) add('vt_running', 'vt_link')
	if (prev.preview !== next.preview) add('preview')
	if (prev.qaPending !== next.qaPending || prev.qaQueued !== next.qaQueued) add('qa_waiting')
	if (!same(prev.timings, next.timings)) add('item_late')
	if (prev.cue !== next.cue) add('cue_label')
	if (prev.stretching !== next.stretching) add('stretching')
	if (!same(prev.rooms, next.rooms)) add('room_late')
	if (!same(prev.advice, next.advice)) add('advice_waiting')
	if (prev.clockOffsetMs !== next.clockOffsetMs) add('clock_drift')
	if (prev.output !== next.output) add('output')
	if (prev.ndi !== next.ndi) add('ndi')
	if (prev.item !== next.item || !same(prev.timings, next.timings)) add('item_kind', 'item_trimmed')
	if (prev.holding !== next.holding) add('holding')
	if (prev.style !== next.style || prev.scene !== next.scene) add('holographic')
	if (prev.ltcRunning !== next.ltcRunning || prev.ltcChase !== next.ltcChase) add('ltc_running')
	if (prev.ltcChase !== next.ltcChase) add('ltc_chase')
	if (prev.roll !== next.roll) add('roll')
	if (prev.rundown !== next.rundown) add('rundown_live')
	if (JSON.stringify((prev.aux ?? []).map((a) => [a.running, a.stage, a.seconds < 0])) !== JSON.stringify((next.aux ?? []).map((a) => [a.running, a.stage, a.seconds < 0])))
		add('aux_running')
	return [...out]
}

/** "#F97316" → Companion colour number. Bad input → white. */
export function hexToNumber(hex: string): number {
	const m = /^#?([0-9a-f]{6})$/i.exec(hex.trim())
	return m ? parseInt(m[1], 16) : 0xffffff
}

/** Black or white text, whichever reads better on this background. */
export function contrastText(bg: number): number {
	const r = (bg >> 16) & 0xff
	const g = (bg >> 8) & 0xff
	const b = bg & 0xff
	return 0.299 * r + 0.587 * g + 0.114 * b > 150 ? 0x000000 : 0xffffff
}

/**
 * The command behind assistant suggestion N (1-based), as a Stagewatch protocol line, or null.
 * Assistant commands were already validated by Stagewatch; we only pass them back.
 */
export function adviceCommand(s: Snapshot | null, n: number): string | null {
	const a = s?.advice?.[n - 1]
	return a?.command ?? null
}

/** The newest open signal for a target (for a podium "Got it" button). */
export function latestOpenFor(s: Snapshot | null, target: string): SignalInfo | undefined {
	const mine = openSignals(s).filter((g) => g.target.toLowerCase() === target.toLowerCase())
	return mine[mine.length - 1]
}
