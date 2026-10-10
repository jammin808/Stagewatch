import { combineRgb, type CompanionFeedbackDefinitions, type DropdownChoice } from '@companion-module/base'
import { contrastText, deptState, FEATURE_CHOICES, hasFeature, hexToNumber, isCounting, itemAdjust, openSignals, resolveItem } from './state.js'
import type ModuleInstance from './main.js'

// Every callback reads the cached frame only: no I/O, constant time.
// Rechecks are triggered per frame for exactly the feedbacks whose inputs changed.

export const COLORS = {
	white: combineRgb(255, 255, 255),
	black: combineRgb(0, 0, 0),
	green: combineRgb(0, 160, 60),
	amber: combineRgb(245, 158, 11),
	orange: combineRgb(249, 115, 22),
	red: combineRgb(220, 38, 38),
	blue: combineRgb(37, 99, 235),
	grey: combineRgb(64, 64, 64),
	darkgrey: combineRgb(32, 32, 32),
	violet: combineRgb(139, 92, 246),
}

const STATE_CHOICES: DropdownChoice[] = [
	{ id: 'running', label: 'Running' },
	{ id: 'paused', label: 'Paused' },
	{ id: 'idle', label: 'Stopped / loaded' },
	{ id: 'expired', label: 'Reached zero (overtime or held)' },
]
const PHASE_CHOICES: DropdownChoice[] = [
	{ id: 'normal', label: 'Normal' },
	{ id: 'warning', label: 'Warning' },
	{ id: 'wrapup', label: 'Wrap-up' },
	{ id: 'overtime', label: 'Overtime' },
]

export function UpdateFeedbacks(self: ModuleInstance): void {
	const s = () => self.snapshot
	const depts: DropdownChoice[] = (self.showConfig.departments ?? []).map((d) => ({ id: d, label: d }))

	const feedbacks: CompanionFeedbackDefinitions = {
		state: {
			type: 'boolean',
			name: 'Timer: State is…',
			defaultStyle: { bgcolor: COLORS.green, color: COLORS.white },
			options: [{ type: 'dropdown', id: 'state', label: 'State', default: 'running', choices: STATE_CHOICES }],
			callback: (fb) => s()?.state === fb.options.state,
		},
		running: {
			type: 'boolean',
			name: 'Timer: Clock is running (incl. overtime)',
			defaultStyle: { bgcolor: COLORS.green, color: COLORS.white },
			options: [],
			callback: () => isCounting(s()),
		},
		overtime: {
			type: 'boolean',
			name: 'Timer: Over time',
			description: 'True from zero onwards — counting negative, or held at 00:00.',
			defaultStyle: { bgcolor: COLORS.red, color: COLORS.white },
			options: [],
			callback: () => s()?.phase === 'overtime',
		},
		phase: {
			type: 'boolean',
			name: 'Timer: Warning stage is…',
			defaultStyle: { bgcolor: COLORS.amber, color: COLORS.black },
			options: [{ type: 'dropdown', id: 'phase', label: 'Stage', default: 'warning', choices: PHASE_CHOICES }],
			callback: (fb) => s()?.phase === fb.options.phase,
		},
		phase_color: {
			type: 'advanced',
			name: 'Timer: Mirror Stagewatch timer colour',
			description:
				'Paints the button in the exact colour the timer shows in Stagewatch (normal / warning / wrap-up / overtime), so your settings carry over.',
			affectedProperties: ['bgcolor', 'color'],
			options: [
				{
					type: 'dropdown',
					id: 'target',
					label: 'Apply to',
					default: 'text',
					choices: [
						{ id: 'text', label: 'Text colour' },
						{ id: 'bg', label: 'Background (text auto black/white)' },
						{ id: 'bg_alert', label: 'Background, only when not normal' },
					],
				},
			],
			callback: (fb) => {
				const snap = s()
				if (!snap) return {}
				const c = hexToNumber(snap.color)
				switch (fb.options.target) {
					case 'bg':
						return { bgcolor: c, color: contrastText(c) }
					case 'bg_alert':
						return snap.phase === 'normal' ? {} : { bgcolor: c, color: contrastText(c) }
					default:
						return { color: c }
				}
			},
		},
		time_below: {
			type: 'boolean',
			name: 'Timer: Remaining at or below…',
			description: 'For your own early warnings (e.g. light a key at 5:00).',
			defaultStyle: { bgcolor: COLORS.amber, color: COLORS.black },
			options: [
				{ type: 'number', id: 'seconds', label: 'Seconds', default: 300, min: 0, max: 86400 },
				{ type: 'checkbox', id: 'include_over', label: 'Stay on in overtime', default: true },
			],
			callback: (fb) => {
				const snap = s()
				if (!snap || snap.mode !== 'countdown' || snap.state === 'idle') return false
				if (snap.seconds < 0 || snap.phase === 'overtime') return fb.options.include_over === true
				return snap.seconds <= Number(fb.options.seconds)
			},
		},
		mode: {
			type: 'boolean',
			name: 'Timer: Clock mode is…',
			defaultStyle: { bgcolor: COLORS.blue, color: COLORS.white },
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
			callback: (fb) => s()?.mode === fb.options.mode,
		},
		blackout: {
			type: 'boolean',
			name: 'Display: Blackout is on',
			defaultStyle: { bgcolor: COLORS.red, color: COLORS.white },
			options: [],
			callback: () => s()?.blackout === true,
		},
		message_active: {
			type: 'boolean',
			name: 'Message: A message is showing',
			defaultStyle: { bgcolor: COLORS.amber, color: COLORS.black },
			options: [
				{
					type: 'dropdown',
					id: 'urgency',
					label: 'Urgency',
					default: 'any',
					choices: [
						{ id: 'any', label: 'Any' },
						{ id: 'info', label: 'Info' },
						{ id: 'warning', label: 'Warning' },
						{ id: 'critical', label: 'Critical' },
					],
				},
			],
			callback: (fb) => {
				const snap = s()
				if (!snap?.message) return false
				return fb.options.urgency === 'any' || snap.urgency === fb.options.urgency
			},
		},
		current_item: {
			type: 'boolean',
			name: 'Running order: Item is current',
			defaultStyle: { bgcolor: COLORS.green, color: COLORS.white },
			options: [{ type: 'number', id: 'item', label: 'Item number', default: 1, min: 1, max: 999 }],
			callback: (fb) => (s()?.item ?? -1) + 1 === Number(fb.options.item),
		},
		item_done: {
			type: 'boolean',
			name: 'Running order: Item is finished',
			defaultStyle: { bgcolor: COLORS.darkgrey, color: COLORS.grey },
			options: [{ type: 'number', id: 'item', label: 'Item number', default: 1, min: 1, max: 999 }],
			callback: (fb) => {
				const cur = s()?.item ?? -1
				return cur >= 0 && Number(fb.options.item) < cur + 1
			},
		},
		current_preset: {
			type: 'boolean',
			name: 'Presets: Preset is loaded',
			defaultStyle: { bgcolor: COLORS.blue, color: COLORS.white },
			options: [{ type: 'number', id: 'slot', label: 'Preset', default: 1, min: 1, max: 12 }],
			callback: (fb) => (s()?.preset ?? 0) === Number(fb.options.slot),
		},
		behind_schedule: {
			type: 'boolean',
			name: 'Running order: Behind schedule by at least…',
			defaultStyle: { bgcolor: COLORS.red, color: COLORS.white },
			options: [{ type: 'number', id: 'seconds', label: 'Seconds late', default: 60, min: 0, max: 86400 }],
			callback: (fb) => {
				const off = s()?.offset
				return off != null && off >= Number(fb.options.seconds)
			},
		},
		// ---------------------------------------------------------------- 2.2: outputs, licence, live item
		holographic: {
			type: 'boolean',
			name: 'Display: 3D display is on',
			description: 'Optionally only the hologram or only the egg timer.',
			defaultStyle: { bgcolor: COLORS.violet, color: COLORS.white },
			options: [
				{
					type: 'dropdown',
					id: 'scene',
					label: 'Scene',
					default: 'any',
					choices: [
						{ id: 'any', label: 'Either' },
						{ id: 'holo', label: 'Hologram' },
						{ id: 'egg', label: 'Egg timer' },
					],
				},
			],
			callback: (fb) => {
				const snap = s()
				if (snap?.style !== 'holo') return false
				const want = String(fb.options.scene ?? 'any')
				return want === 'any' || (snap.scene === 'egg' ? 'egg' : 'holo') === want
			},
		},
		ltc_running: {
			type: 'boolean',
			name: 'LTC in: timecode is rolling',
			description: 'Optionally only while the timer is following it (chase).',
			defaultStyle: { bgcolor: COLORS.amber, color: COLORS.black },
			options: [{ type: 'checkbox', id: 'chase', label: 'Only while chasing', default: false }],
			callback: (fb) => {
				const snap = s()
				return snap?.ltcRunning === true && (!fb.options.chase || snap.ltcChase === true)
			},
		},
		roll: {
			type: 'boolean',
			name: 'Running order: Roll is on',
			defaultStyle: { bgcolor: COLORS.green, color: COLORS.white },
			options: [],
			callback: () => s()?.roll === true,
		},
		rundown_live: {
			type: 'boolean',
			name: 'Running order: this one is live',
			defaultStyle: { bgcolor: COLORS.violet, color: COLORS.white },
			options: [{ type: 'textinput', id: 'name', label: 'Running order name', default: 'Day 1' }],
			callback: (fb) => (s()?.rundown ?? '').toLowerCase() === String(fb.options.name ?? '').trim().toLowerCase(),
		},
		aux_running: {
			type: 'boolean',
			name: 'Extra timer: running / on stage / past zero',
			defaultStyle: { bgcolor: COLORS.green, color: COLORS.white },
			options: [
				{ type: 'dropdown', id: 'n', label: 'Timer', default: '1', choices: [1, 2, 3].map((n) => ({ id: String(n), label: `Timer ${n}` })) },
				{
					type: 'dropdown',
					id: 'what',
					label: 'When',
					default: 'running',
					choices: [
						{ id: 'running', label: 'Running' },
						{ id: 'stage', label: 'On stage' },
						{ id: 'over', label: 'Past zero (counting down)' },
					],
				},
			],
			callback: (fb) => {
				const a = (s()?.aux ?? []).find((x) => x.n === Number(fb.options.n))
				if (!a) return false
				const what = String(fb.options.what ?? 'running')
				return what === 'stage' ? a.stage : what === 'over' ? !a.up && a.seconds < 0 : a.running
			},
		},
		ltc_chase: {
			type: 'boolean',
			name: 'LTC in: timer follows timecode',
			defaultStyle: { bgcolor: COLORS.violet, color: COLORS.white },
			options: [],
			callback: () => s()?.ltcChase === true,
		},
		holding: {
			type: 'boolean',
			name: 'Display: Holding image is on air',
			description: 'Any holding image, or one in particular.',
			defaultStyle: { bgcolor: COLORS.violet, color: COLORS.white },
			options: [
				{
					type: 'dropdown',
					id: 'image',
					label: 'Image',
					default: 'any',
					choices: [{ id: 'any', label: 'Any' }].concat((self.showConfig.holding ?? []).map((h, i) => ({ id: h.id, label: `${i + 1}: ${h.name}` }))),
					allowCustom: true,
				},
			],
			callback: (fb) => {
				const on = s()?.holding ?? ''
				const want = String(fb.options.image ?? 'any')
				if (!on) return false
				if (want === 'any') return true
				const list = self.showConfig.holding ?? []
				const n = Number(want)
				const target = Number.isInteger(n) && n >= 1 ? list[n - 1]?.id : list.find((h) => h.id === want || h.name.toLowerCase() === want.toLowerCase())?.id
				return target === on
			},
		},
		output: {
			type: 'boolean',
			name: 'Display: Output window is…',
			defaultStyle: { bgcolor: COLORS.green, color: COLORS.white },
			options: [
				{
					type: 'dropdown',
					id: 'mode',
					label: 'Output',
					default: 'open',
					choices: [
						{ id: 'open', label: 'Open (any mode)' },
						{ id: 'full', label: 'Full screen' },
						{ id: 'keyed', label: 'Keyed full screen' },
						{ id: 'mini', label: 'Floating mini' },
						{ id: 'closed', label: 'Closed' },
					],
				},
			],
			callback: (fb) => {
				const o = s()?.output ?? 'closed'
				return fb.options.mode === 'open' ? o !== 'closed' : o === fb.options.mode
			},
		},
		ndi: {
			type: 'boolean',
			name: 'Display: NDI is sending',
			defaultStyle: { bgcolor: COLORS.blue, color: COLORS.white },
			options: [],
			callback: () => s()?.ndi === true,
		},
		feature_locked: {
			type: 'boolean',
			name: 'Licence: Feature not included',
			description:
				'True when this Stagewatch licence doesn\'t include the feature — dim the key so operators know it won\'t work. Older Stagewatch versions always report "included".',
			defaultStyle: { bgcolor: COLORS.darkgrey, color: COLORS.grey },
			options: [{ type: 'dropdown', id: 'feature', label: 'Feature', default: 'crewcues', choices: FEATURE_CHOICES }],
			callback: (fb) => !hasFeature(self.showConfig, String(fb.options.feature ?? '')),
		},
		item_kind: {
			type: 'boolean',
			name: 'Running order: Live item is a…',
			description: 'Colour keys by what is on now — e.g. blue for Q&A, grey for breaks.',
			defaultStyle: { bgcolor: COLORS.blue, color: COLORS.white },
			options: [
				{
					type: 'dropdown',
					id: 'kind',
					label: 'Kind',
					default: 'qa',
					choices: [
						{ id: 'talk', label: 'Talk' },
						{ id: 'panel', label: 'Panel' },
						{ id: 'qa', label: 'Q&A' },
						{ id: 'break', label: 'Break' },
						{ id: 'award', label: 'Award' },
						{ id: 'video', label: 'Video' },
						{ id: 'buffer', label: 'Buffer' },
					],
					allowCustom: true,
				},
			],
			callback: (fb) => {
				const snap = s()
				const t = snap && snap.item >= 0 ? snap.timings?.[snap.item] : undefined
				return !!t && t.kind.toLowerCase() === String(fb.options.kind ?? '').toLowerCase()
			},
		},
		item_trimmed: {
			type: 'boolean',
			name: 'Running order: Item has been trimmed',
			description: 'Cut or extended from its planned length. Item 0 = the live item.',
			defaultStyle: { bgcolor: COLORS.orange, color: COLORS.black },
			options: [
				{ type: 'number', id: 'item', label: 'Item (0 = live)', default: 0, min: 0, max: 999 },
				{
					type: 'dropdown',
					id: 'how',
					label: 'Trimmed',
					default: 'any',
					choices: [
						{ id: 'any', label: 'Either way' },
						{ id: 'cut', label: 'Cut (shorter)' },
						{ id: 'extended', label: 'Extended (longer)' },
					],
				},
			],
			callback: (fb) => {
				const adj = itemAdjust(s(), resolveItem(s(), Number(fb.options.item) || 0))
				return fb.options.how === 'cut' ? adj < 0 : fb.options.how === 'extended' ? adj > 0 : adj !== 0
			},
		},

		connected: {
			type: 'boolean',
			name: 'Connection: Connected to Stagewatch',
			description: 'Invert it to light a key when the link is down.',
			defaultStyle: { bgcolor: COLORS.green, color: COLORS.white },
			options: [],
			callback: () => self.isConnected,
		},

		// ---------------------------------------------------------------- 2.0: acknowledged cues
		message_ack: {
			type: 'boolean',
			name: 'Cues: Speaker message is…',
			description: 'For messages sent with "ask to acknowledge".',
			defaultStyle: { bgcolor: COLORS.green, color: COLORS.white },
			options: [
				{
					type: 'dropdown',
					id: 'state',
					label: 'State',
					default: 'done',
					choices: [
						{ id: 'sent', label: 'Waiting for the speaker' },
						{ id: 'done', label: 'Acknowledged' },
					],
				},
			],
			callback: (fb) => {
				const a = s()?.messageAck ?? ''
				return fb.options.state === 'sent' ? a === 'sent' : a === 'seen' || a === 'done'
			},
		},
		cue_waiting: {
			type: 'boolean',
			name: 'Cues: A cue is waiting for acknowledgement',
			description: 'Optionally for one target (speaker, moderator, backstage, Lighting…). "Only unanswered" = escalated cues.',
			defaultStyle: { bgcolor: COLORS.amber, color: COLORS.black },
			options: [
				{ type: 'textinput', id: 'target', label: 'Target (blank = any)', default: '' },
				{ type: 'checkbox', id: 'escalated', label: 'Only unanswered (escalated)', default: false },
			],
			callback: (fb) => {
				const t = String(fb.options.target ?? '').trim().toLowerCase()
				return openSignals(s()).some(
					(g) => g.state === 'sent' && (!t || g.target.toLowerCase() === t) && (fb.options.escalated !== true || g.escalated),
				)
			},
		},
		dept_state: {
			type: 'boolean',
			name: 'Cues: Department is…',
			defaultStyle: { bgcolor: COLORS.amber, color: COLORS.black },
			options: [
				{
					type: 'dropdown',
					id: 'dept',
					label: 'Department',
					default: depts[0]?.id ?? 'Lighting',
					choices: depts.length ? depts : [{ id: 'Lighting', label: 'Lighting' }],
					allowCustom: true,
				},
				{
					type: 'dropdown',
					id: 'state',
					label: 'State',
					default: 'standby',
					choices: [
						{ id: 'standby', label: 'Standby sent (not yet confirmed)' },
						{ id: 'standby_ack', label: 'Standing by (confirmed)' },
						{ id: 'go', label: 'GO sent (not yet confirmed)' },
						{ id: 'go_ack', label: 'GO done (confirmed)' },
						{ id: 'clear', label: 'Clear' },
					],
				},
			],
			callback: (fb) => deptState(s(), String(fb.options.dept ?? '')) === fb.options.state,
		},

		// ---------------------------------------------------------------- 2.0: schedule recovery
		hard_out_risk: {
			type: 'boolean',
			name: 'Schedule: Curfew margin below…',
			description: 'Lights when the show is on course to finish less than N seconds before the next curfew (0 = over it).',
			defaultStyle: { bgcolor: COLORS.red, color: COLORS.white },
			options: [{ type: 'number', id: 'seconds', label: 'Seconds to spare', default: 0, min: -86400, max: 86400 }],
			callback: (fb) => {
				const m = s()?.hardOutMargin
				return m !== null && m !== undefined && m < Number(fb.options.seconds)
			},
		},
		item_late: {
			type: 'boolean',
			name: 'Running order: Item expected late by at least…',
			description: 'The projected start of a coming item, compared with its plan.',
			defaultStyle: { bgcolor: COLORS.orange, color: COLORS.black },
			options: [
				{ type: 'number', id: 'item', label: 'Item number', default: 1, min: 1, max: 999 },
				{ type: 'number', id: 'seconds', label: 'Seconds late', default: 120, min: 0, max: 86400 },
			],
			callback: (fb) => {
				const t = s()?.timings?.find((x) => x.i + 1 === Number(fb.options.item))
				return !!t && t.status !== 'done' && (t.delay >= Number(fb.options.seconds) || t.conflict)
			},
		},
		advice_waiting: {
			type: 'boolean',
			name: 'Assistant: Has a suggestion',
			defaultStyle: { bgcolor: COLORS.blue, color: COLORS.white },
			options: [
				{
					type: 'dropdown',
					id: 'level',
					label: 'At least',
					default: 'suggest',
					choices: [
						{ id: 'info', label: 'Information' },
						{ id: 'suggest', label: 'Suggestion' },
						{ id: 'warn', label: 'Warning' },
						{ id: 'urgent', label: 'Urgent' },
					],
				},
			],
			callback: (fb) => {
				const rank = (l: string): number => ['info', 'suggest', 'warn', 'urgent'].indexOf(l)
				return (s()?.advice ?? []).some((a) => rank(a.level) >= rank(String(fb.options.level)))
			},
		},
		stretching: {
			type: 'boolean',
			name: 'Time: Elastic end-at is stretching',
			defaultStyle: { bgcolor: COLORS.amber, color: COLORS.black },
			options: [],
			callback: () => s()?.stretching === true,
		},
		clock_drift: {
			type: 'boolean',
			name: 'Time: Stagewatch PC clock is off',
			description: 'Compared with the time server (Settings → Integrations in Stagewatch).',
			defaultStyle: { bgcolor: COLORS.red, color: COLORS.white },
			options: [{ type: 'number', id: 'ms', label: 'More than (ms)', default: 1000, min: 0, max: 600000 }],
			callback: (fb) => Math.abs(s()?.clockOffsetMs ?? 0) > Number(fb.options.ms),
		},

		// ---------------------------------------------------------------- 2.0: speaker & presentation
		cue_label: {
			type: 'boolean',
			name: 'Timer: Cue label is…',
			description: 'The cue-ladder label in force, e.g. "5 MIN" or "WRAP UP" (blank = any cue).',
			defaultStyle: { bgcolor: COLORS.amber, color: COLORS.black },
			options: [{ type: 'textinput', id: 'label', label: 'Label', default: '' }],
			callback: (fb) => {
				const want = String(fb.options.label ?? '').trim().toUpperCase()
				const cue = (s()?.cue ?? '').toUpperCase()
				return want ? cue === want : cue.length > 0
			},
		},
		pace: {
			type: 'boolean',
			name: 'Presentation: Pace against rehearsal is…',
			defaultStyle: { bgcolor: COLORS.orange, color: COLORS.black },
			options: [
				{
					type: 'dropdown',
					id: 'pace',
					label: 'Pace',
					default: 'behind',
					choices: [
						{ id: 'behind', label: 'Behind' },
						{ id: 'onpace', label: 'On pace' },
						{ id: 'ahead', label: 'Ahead' },
					],
				},
			],
			callback: (fb) => s()?.slides?.pace === fb.options.pace,
		},
		vt_running: {
			type: 'boolean',
			name: 'Playout: Video is running',
			description: 'vMix clip in Program, optionally only in its last N seconds.',
			defaultStyle: { bgcolor: COLORS.blue, color: COLORS.white },
			options: [{ type: 'number', id: 'last', label: 'Only in the last (seconds, 0 = always)', default: 0, min: 0, max: 86400 }],
			callback: (fb) => {
				const p = s()?.playout
				if (!p || !p.running) return false
				const last = Number(fb.options.last)
				return last <= 0 || p.remaining <= last
			},
		},
		preview: {
			type: 'boolean',
			name: 'Display: Briefing preview is playing',
			defaultStyle: { bgcolor: COLORS.amber, color: COLORS.black },
			options: [],
			callback: () => s()?.preview === true,
		},
		qa_waiting: {
			type: 'boolean',
			name: 'Q&A: Questions waiting',
			defaultStyle: { bgcolor: COLORS.blue, color: COLORS.white },
			options: [
				{
					type: 'dropdown',
					id: 'which',
					label: 'Which',
					default: 'new',
					choices: [
						{ id: 'new', label: 'New (need approval)' },
						{ id: 'queued', label: 'Approved and queued' },
					],
				},
			],
			callback: (fb) => (fb.options.which === 'queued' ? (s()?.qaQueued ?? 0) : (s()?.qaPending ?? 0)) > 0,
		},
		room_late: {
			type: 'boolean',
			name: 'Rooms: A room is late or at curfew risk',
			defaultStyle: { bgcolor: COLORS.red, color: COLORS.white },
			options: [
				{ type: 'textinput', id: 'room', label: 'Room (blank = any)', default: '' },
				{ type: 'number', id: 'seconds', label: 'Late by at least (s)', default: 60, min: 0, max: 86400 },
			],
			callback: (fb) => {
				const want = String(fb.options.room ?? '').trim().toLowerCase()
				return (s()?.rooms ?? []).some(
					(r) =>
						(!want || r.room.toLowerCase() === want) &&
						((r.offset ?? 0) >= Number(fb.options.seconds) || (r.hardOutMargin ?? 0) < 0),
				)
			},
		},

		// ---------------------------------------------------------------- 2.0: value feedbacks (gauges, expressions)
		remaining: {
			type: 'value',
			name: 'Value: Seconds remaining',
			description: 'For expressions and layered button graphics (negative in overtime).',
			options: [],
			callback: () => s()?.seconds ?? 0,
		},
		progress_value: {
			type: 'value',
			name: 'Value: Percent of the countdown left (0–100)',
			description: 'Drives the ring gauge on the layered timer key.',
			options: [],
			callback: () => {
				const snap = s()
				if (!snap || snap.mode !== 'countdown' || snap.total <= 0) return 0
				return Math.round(Math.max(0, Math.min(1, snap.seconds / snap.total)) * 100)
			},
		},
	}
	self.setFeedbackDefinitions(feedbacks)
}
