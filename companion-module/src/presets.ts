import { ButtonGraphicsDecorationType } from '@companion-module/base'
import type {
	CompanionButtonStyleProps,
	CompanionLayeredButtonPresetDefinition,
	CompanionPresetDefinitions,
	CompanionPresetSection,
	CompanionSimplePresetDefinition,
} from '@companion-module/base'
import { COLORS } from './feedbacks.js'
import type ModuleInstance from './main.js'
import type { ModuleSchema } from './main.js'

const HOLD_MS = 1000

export function UpdatePresets(self: ModuleInstance): void {
	const v = (name: string): string => `$(${self.label}:${name})`
	const presets: CompanionPresetDefinitions<ModuleSchema> = {}
	const style = (text: string, extra: Partial<CompanionButtonStyleProps> = {}): CompanionButtonStyleProps => ({
		text,
		size: 'auto',
		color: COLORS.white,
		bgcolor: COLORS.black,
		show_topbar: false,
		...extra,
	})
	const simple = (
		id: string,
		name: string,
		def: Omit<CompanionSimplePresetDefinition<ModuleSchema>, 'type' | 'name'>,
	): string => {
		presets[id] = { type: 'simple', name, ...def }
		return id
	}
	const press = (actionId: string, options: Record<string, unknown> = {}) => [
		{ down: [{ actionId, options }], up: [] },
	] as CompanionSimplePresetDefinition<ModuleSchema>['steps']

	// ---------------------------------------------------------------- show control
	const control = [
		simple('timer_toggle', 'Timer — press to start / pause', {
			style: style(`${v('time')}`, { size: 'auto' }),
			steps: press('toggle'),
			feedbacks: [
				{ feedbackId: 'phase_color', options: { target: 'bg' } },
				{ feedbackId: 'state', options: { state: 'paused' }, style: { bgcolor: COLORS.blue, color: COLORS.white } },
			],
			keywords: ['timer', 'start', 'pause', 'toggle', 'countdown'],
		}),
		simple('start', 'Start', {
			style: style('▶\nSTART', { size: '18' }),
			steps: press('start'),
			feedbacks: [{ feedbackId: 'running', options: {}, style: { bgcolor: COLORS.green, color: COLORS.white } }],
		}),
		simple('pause', 'Pause', {
			style: style('⏸\nPAUSE', { size: '18' }),
			steps: press('pause'),
			feedbacks: [
				{ feedbackId: 'state', options: { state: 'paused' }, style: { bgcolor: COLORS.blue, color: COLORS.white } },
			],
		}),
		simple('reset', 'Reset', { style: style('↺\nRESET', { size: '18' }), steps: press('reset'), feedbacks: [] }),
		simple('next', 'Next item', {
			style: style(`NEXT ▶\n${v('next')}`, { size: '14' }),
			steps: press('next'),
			feedbacks: [],
		}),
		simple('previous', 'Previous item', { style: style('◀\nPREV', { size: '18' }), steps: press('previous'), feedbacks: [] }),
		simple('blackout', 'Blackout (toggle)', {
			style: style('BLACK\nOUT', { size: '18', bgcolor: COLORS.darkgrey }),
			steps: press('blackout', { state: 'toggle' }),
			feedbacks: [
				{ feedbackId: 'blackout', options: {}, style: { bgcolor: COLORS.red, color: COLORS.white, text: 'BLACKOUT\nON' } },
			],
		}),
	]

	// ---------------------------------------------------------------- live displays
	const display = [
		simple('show_timer', 'Timer (display, Stagewatch colours)', {
			style: style(v('time'), { size: 'auto' }),
			steps: [],
			feedbacks: [{ feedbackId: 'phase_color', options: { target: 'text' } }],
		}),
		simple('show_title', 'Current title', { style: style(v('title'), { size: '14' }), steps: [], feedbacks: [] }),
		simple('show_next', 'Next up', { style: style(`NEXT\n${v('next')}`, { size: '14' }), steps: [], feedbacks: [] }),
		simple('show_schedule', 'Ahead / behind schedule', {
			style: style(`SCHEDULE\n${v('offset')}`, { size: '14' }),
			steps: [],
			feedbacks: [{ feedbackId: 'behind_schedule', options: { seconds: 60 }, style: { bgcolor: COLORS.red } }],
		}),
		simple('show_end', 'Ends at', { style: style(`ENDS\n${v('end_time')}`, { size: '14' }), steps: [], feedbacks: [] }),
		simple('show_progress', 'Percent left', {
			style: style(`${v('progress')}%\nLEFT`, { size: '18' }),
			steps: [],
			feedbacks: [{ feedbackId: 'phase_color', options: { target: 'bg_alert' } }],
		}),
		simple('show_tod', 'Time of day', { style: style(v('time_of_day'), { size: '14' }), steps: [], feedbacks: [] }),
		simple('show_link', 'Connection status', {
			style: style('STAGE\nWATCH\nOFFLINE', { size: '14', bgcolor: COLORS.red }),
			steps: [],
			feedbacks: [
				{ feedbackId: 'connected', options: {}, style: { bgcolor: COLORS.green, text: 'STAGE\nWATCH\nOK' } },
			],
		}),
	]

	// ---------------------------------------------------------------- nudge
	const nudges: [number, string][] = [
		[-300, '-5 min'],
		[-60, '-1 min'],
		[-10, '-10 s'],
		[10, '+10 s'],
		[60, '+1 min'],
		[300, '+5 min'],
	]
	const nudge = nudges.map(([s, label]) =>
		simple(`nudge_${s < 0 ? 'm' : 'p'}${Math.abs(s)}`, `Nudge ${label}`, {
			style: style(label.replace(' ', '\n'), { size: '18', bgcolor: s < 0 ? COLORS.darkgrey : COLORS.grey }),
			steps: press('nudge', { seconds: s }),
			feedbacks: [],
		}),
	)

	// ---------------------------------------------------------------- messages
	const cfg = self.showConfig
	const messages = [
		simple('message_clear', 'Clear message', {
			style: style('CLEAR\nMSG', { size: '18' }),
			steps: press('clear_message'),
			feedbacks: [{ feedbackId: 'message_active', options: { urgency: 'any' }, style: { bgcolor: COLORS.amber, color: COLORS.black } }],
		}),
		simple('message_wrap', 'Message: Please wrap up (flashing)', {
			style: style('WRAP\nUP', { size: '18', bgcolor: COLORS.darkgrey }),
			steps: press('message', { text: 'Please wrap up', urgency: 'warn', flash: 'slow' }),
			feedbacks: [],
		}),
		...cfg.messages.map((m, i) =>
			simple(`quick_${i}`, `Quick message: ${m.label}`, {
				style: style(m.label, {
					size: '14',
					bgcolor: m.urgency === 'critical' ? COLORS.red : m.urgency === 'warning' ? COLORS.orange : COLORS.darkgrey,
				}),
				steps: press('quick_message', { index: i }),
				feedbacks: [],
			}),
		),
	]

	// ---------------------------------------------------------------- presets (tap = load, hold = load & start)
	const presetIds = (cfg.presets.length ? cfg.presets.map((p) => p.slot) : Array.from({ length: 12 }, (_, i) => i + 1)).map(
		(slot) =>
			simple(`preset_${slot}`, `Preset ${slot} (tap load · hold start)`, {
				style: style(`${v(`preset_${slot}_label`)}\n${v(`preset_${slot}_duration`)}`, { size: '14', bgcolor: COLORS.darkgrey }),
				steps: [
					{
						down: [{ actionId: 'preset', options: { slot, start: false } }],
						up: [],
						[HOLD_MS]: { options: { runWhileHeld: true }, actions: [{ actionId: 'preset', options: { slot, start: true } }] },
					},
				],
				feedbacks: [{ feedbackId: 'current_preset', options: { slot }, style: { bgcolor: COLORS.blue, color: COLORS.white } }],
			}),
	)

	// ---------------------------------------------------------------- running order (tap = load, hold = load & start)
	const itemIds = cfg.rundown.map((_, i) => {
		const n = i + 1
		return simple(`item_${n}`, `Item ${n} (tap load · hold start)`, {
			style: style(`${n}. ${v(`item_${n}_title`)}\n${v(`item_${n}_duration`)}`, { size: '14', bgcolor: COLORS.darkgrey }),
			steps: [
				{
					down: [{ actionId: 'goto', options: { item: n, start: false } }],
					up: [],
					[HOLD_MS]: { options: { runWhileHeld: true }, actions: [{ actionId: 'goto', options: { item: n, start: true } }] },
				},
			],
			feedbacks: [
				{ feedbackId: 'item_done', options: { item: n }, style: { bgcolor: COLORS.black, color: COLORS.grey } },
				{ feedbackId: 'current_item', options: { item: n }, style: { bgcolor: COLORS.green, color: COLORS.white } },
			],
		})
	})

	// ---------------------------------------------------------------- modes
	const modes = (
		[
			['countdown', 'COUNT\nDOWN'],
			['countup', 'COUNT\nUP'],
			['tod', 'CLOCK'],
		] as const
	).map(([mode, text]) =>
		simple(`mode_${mode}`, `Mode: ${text.replace('\n', ' ').toLowerCase()}`, {
			style: style(text, { size: '14' }),
			steps: press('mode', { mode }),
			feedbacks: [{ feedbackId: 'mode', options: { mode }, style: { bgcolor: COLORS.blue, color: COLORS.white } }],
		}),
	)

	// ================================================================ 2.0
	const expr = (value: string) => ({ isExpression: true as const, value })

	// The timer as a layered key: a progress ring that drains, the time, and the cue label.
	// Hosts that can't draw layered keys fall back to the simple timer key.
	const layeredTimer: CompanionLayeredButtonPresetDefinition<ModuleSchema> = {
		type: 'layered',
		name: 'Timer ring — progress ring, time and cue (press to start / pause)',
		keywords: ['timer', 'gauge', 'ring', 'progress', 'countdown'],
		canvas: { decoration: ButtonGraphicsDecorationType.None },
		elements: [
			{ id: 'bg', type: 'box', x: 0, y: 0, width: 100, height: 100, color: COLORS.black },
			{
				id: 'ring',
				type: 'gauge',
				orientation: 'ring',
				x: 3, y: 3, width: 94, height: 94,
				min: 0, max: 100,
				value: expr(`$(${self.label}:progress)`),
				ringWidth: 9,
				roundedEnds: true,
				trackStyle: 'dimmed',
				stops: [
					{ value: 0, color: COLORS.red, gradient: false },
					{ value: 10, color: COLORS.orange, gradient: false },
					{ value: 25, color: COLORS.amber, gradient: false },
					{ value: 50, color: COLORS.green, gradient: false },
				],
			},
			{ id: 'time', type: 'text', x: 12, y: 30, width: 76, height: 30, text: expr(`$(${self.label}:time_short)`), fontsize: 24, weight: 'bold', color: COLORS.white, halign: 'center', valign: 'center' },
			{ id: 'cue', type: 'text', x: 12, y: 60, width: 76, height: 16, text: expr(`$(${self.label}:cue)`), fontsize: 11, color: COLORS.white, halign: 'center', valign: 'center' },
		],
		feedbacks: [
			{ feedbackId: 'phase', options: { phase: 'warning' }, styleOverrides: [{ elementId: 'time', elementProperty: 'color', override: COLORS.amber }] },
			{ feedbackId: 'phase', options: { phase: 'wrapup' }, styleOverrides: [{ elementId: 'time', elementProperty: 'color', override: COLORS.orange }] },
			{ feedbackId: 'phase', options: { phase: 'overtime' }, styleOverrides: [{ elementId: 'time', elementProperty: 'color', override: COLORS.red }, { elementId: 'bg', elementProperty: 'color', override: 0x3b0d0d }] },
		],
		steps: press('toggle'),
	}
	presets['timer_ring'] = {
		type: 'alternatives',
		variants: [
			layeredTimer,
			{
				type: 'simple',
				name: 'Timer ring (simple fallback)',
				style: style(`${v('time')}\n${v('cue')}`, { size: 'auto' }),
				steps: press('toggle'),
				feedbacks: [{ feedbackId: 'phase_color', options: { target: 'bg' } }],
			},
		],
	}

	// ---------------------------------------------------------------- cues & departments
	const ackCue = (id: string, name: string, text: string, label: string) =>
		simple(id, name, {
			style: style(label, { size: '14', bgcolor: COLORS.darkgrey }),
			steps: press('signal', { kind: 'message', target: 'speaker', text, ack: true }),
			feedbacks: [
				{ feedbackId: 'message_ack', options: { state: 'sent' }, style: { bgcolor: COLORS.amber, color: COLORS.black, text: `${label}\n⏳` } },
				{ feedbackId: 'message_ack', options: { state: 'done' }, style: { bgcolor: COLORS.green, color: COLORS.white, text: `${label}\n✓` } },
			],
			keywords: ['cue', 'ack', 'acknowledge', 'speaker'],
		})
	const cueKeys = [
		ackCue('cue_wrap', 'Speaker: wrap up (with acknowledgement)', 'Please start to wrap up', 'WRAP\nUP'),
		ackCue('cue_finish', 'Speaker: finish by… (with acknowledgement)', 'Please finish by {FINISH_BY}', 'FINISH\nBY'),
		ackCue('cue_slow', 'Speaker: slow down (interpreters)', 'Slow down a little', 'SLOW\nDOWN'),
		simple('cue_buzz', 'Speaker: pocket buzz (phone / watch)', {
			style: style('BUZZ\n📳', { size: '14', bgcolor: COLORS.darkgrey }),
			steps: press('signal', { kind: 'nudge', target: 'speaker', text: 'Look at the monitor', ack: true }),
			feedbacks: [{ feedbackId: 'cue_waiting', options: { target: 'speaker', escalated: false }, style: { bgcolor: COLORS.amber, color: COLORS.black } }],
		}),
		simple('cue_call_next', 'Backstage: call the next speaker to stage', {
			style: style(`CALL\n${v('next')}`, { size: '14', bgcolor: COLORS.darkgrey }),
			steps: press('call_next'),
			feedbacks: [{ feedbackId: 'cue_waiting', options: { target: 'backstage', escalated: false }, style: { bgcolor: COLORS.amber, color: COLORS.black } }],
			keywords: ['green room', 'call', 'backstage', 'wrangler'],
		}),
		simple('cue_podium_ack', 'Podium "Got it" key (for the speaker\'s own Stream Deck)', {
			style: style('GOT\nIT ✓', { size: '18', bgcolor: COLORS.darkgrey }),
			steps: press('ack', { target: 'speaker', stage: 'done' }),
			feedbacks: [{ feedbackId: 'cue_waiting', options: { target: 'speaker', escalated: false }, style: { bgcolor: COLORS.amber, color: COLORS.black } }],
		}),
		simple('cue_waiting', 'Cues waiting / unanswered (display)', {
			style: style(`CUES\n${v('cues_waiting')} open`, { size: '14' }),
			steps: press('signal_clear', { target: '' }),
			feedbacks: [
				{ feedbackId: 'cue_waiting', options: { target: '', escalated: false }, style: { bgcolor: COLORS.amber, color: COLORS.black } },
				{ feedbackId: 'cue_waiting', options: { target: '', escalated: true }, style: { bgcolor: COLORS.red, color: COLORS.white, text: `NO ACK\n${v('last_cue')}` } },
			],
		}),
	]
	// One standby and one GO key per department, built from Stagewatch's department list (template groups).
	presets['dept_standby_t'] = {
		type: 'simple',
		name: 'Department standby',
		style: style('SB\n$(local:dept)', { size: '14', bgcolor: COLORS.darkgrey }),
		steps: [{ down: [{ actionId: 'dept_cue', options: { dept: expr('$(local:dept)'), kind: 'standby', text: '' } }], up: [] }],
		feedbacks: [
			{ feedbackId: 'dept_state', options: { dept: expr('$(local:dept)'), state: 'standby' }, style: { bgcolor: COLORS.amber, color: COLORS.black } },
			{ feedbackId: 'dept_state', options: { dept: expr('$(local:dept)'), state: 'standby_ack' }, style: { bgcolor: COLORS.amber, color: COLORS.black, text: 'SB ✓\n$(local:dept)' } },
		],
		localVariables: [{ variableType: 'simple', variableName: 'dept', startupValue: 'Lighting' }],
	}
	presets['dept_go_t'] = {
		type: 'simple',
		name: 'Department GO',
		style: style('GO\n$(local:dept)', { size: '14', bgcolor: COLORS.darkgrey }),
		steps: [{ down: [{ actionId: 'dept_cue', options: { dept: expr('$(local:dept)'), kind: 'go', text: '' } }], up: [] }],
		feedbacks: [
			{ feedbackId: 'dept_state', options: { dept: expr('$(local:dept)'), state: 'go' }, style: { bgcolor: COLORS.green, color: COLORS.white } },
			{ feedbackId: 'dept_state', options: { dept: expr('$(local:dept)'), state: 'go_ack' }, style: { bgcolor: COLORS.darkgrey, color: COLORS.green, text: 'GO ✓\n$(local:dept)' } },
		],
		localVariables: [{ variableType: 'simple', variableName: 'dept', startupValue: 'Lighting' }],
	}
	presets['all_go'] = {
		type: 'simple',
		name: 'ALL GO (every department standing by)',
		style: style(`ALL GO
${v('standing_by')} SB`, { size: '14', bgcolor: COLORS.darkgrey }),
		steps: [{ down: [{ actionId: 'all_go', options: {} }], up: [] }],
		feedbacks: [{ feedbackId: 'standing_by', options: { count: 1 }, style: { bgcolor: COLORS.green, color: COLORS.white } }],
	}
	const deptValues = (cfg.departments?.length ? cfg.departments : ['Lighting', 'Sound', 'Video', 'Stage']).map((d) => ({ name: d, value: d }))

	// ---------------------------------------------------------------- schedule recovery & assistant
	const recovery = [
		simple('show_curfew', 'Curfew status (display)', {
			style: style(`CURFEW\n${v('hard_out_margin')}`, { size: '14' }),
			steps: [],
			feedbacks: [
				{ feedbackId: 'hard_out_risk', options: { seconds: 300 }, style: { bgcolor: COLORS.amber, color: COLORS.black } },
				{ feedbackId: 'hard_out_risk', options: { seconds: 0 }, style: { bgcolor: COLORS.red, color: COLORS.white } },
			],
		}),
		simple('show_forecast', 'Likely end of show (display)', { style: style(`END\n${v('forecast')}`, { size: '7' }), steps: [], feedbacks: [] }),
		simple('catchup', 'Catch up: land the live item on its planned end', {
			style: style('CATCH\nUP', { size: '18', bgcolor: COLORS.darkgrey }),
			steps: press('catchup'),
			feedbacks: [{ feedbackId: 'stretching', options: {}, style: { bgcolor: COLORS.amber, color: COLORS.black, text: 'CATCH\nUP ⇢' } }],
		}),
		simple('delay_1', 'Delay next item +1 min', { style: style(`DELAY\n+1m\n${v('delay')}`, { size: '14', bgcolor: COLORS.darkgrey }), steps: press('delay', { how: 'add', seconds: 60 }), feedbacks: [] }),
		simple('delay_5', 'Delay next item +5 min', { style: style(`DELAY\n+5m\n${v('delay')}`, { size: '14', bgcolor: COLORS.darkgrey }), steps: press('delay', { how: 'add', seconds: 300 }), feedbacks: [] }),
		simple('delay_clear', 'Clear the delay', { style: style('DELAY\nCLEAR', { size: '14' }), steps: press('delay', { how: 'clear', seconds: 0 }), feedbacks: [] }),
		simple('next_expected', 'Next item and its expected start (display)', { style: style(`NEXT\n${v('next_expected')}`, { size: '14' }), steps: [], feedbacks: [] }),
	]
	const advice = [1, 2, 3].map((n) =>
		simple(`advice_${n}`, `Assistant suggestion ${n} (press to apply)`, {
			style: style(`${v(`advice_${n}`)}`, { size: '7', bgcolor: COLORS.darkgrey }),
			steps: press('advice_apply', { n }),
			feedbacks: [{ feedbackId: 'advice_waiting', options: { level: n === 1 ? 'urgent' : 'warn' }, style: { bgcolor: n === 1 ? COLORS.red : COLORS.blue, color: COLORS.white } }],
			keywords: ['assistant', 'ai', 'suggestion', 'trim', 'producer'],
		}),
	)

	// ---------------------------------------------------------------- speaker presentation
	const speakerKeys = [
		simple('preview', 'Briefing preview: the cue ladder at 10× on every output', {
			style: style('BRIEF\nPREVIEW', { size: '14', bgcolor: COLORS.darkgrey }),
			steps: press('preview', { state: 'toggle' }),
			feedbacks: [{ feedbackId: 'preview', options: {}, style: { bgcolor: COLORS.amber, color: COLORS.black, text: 'PREVIEW\nON' } }],
		}),
		simple('format_calm', 'Speaker clock: calm ("12 MIN")', { style: style('CALM\nCLOCK', { size: '14' }), steps: press('speaker_format', { format: 'calm' }), feedbacks: [] }),
		simple('format_standard', 'Speaker clock: standard (MM:SS)', { style: style('MM:SS\nCLOCK', { size: '14' }), steps: press('speaker_format', { format: 'standard' }), feedbacks: [] }),
		simple('show_slide', 'Slide n / N and pace (display)', {
			style: style(`SLIDE\n${v('slide')}/${v('slides')}\n${v('ghost')}`, { size: '14' }),
			steps: press('rehearse'),
			feedbacks: [
				{ feedbackId: 'pace', options: { pace: 'behind' }, style: { bgcolor: COLORS.orange, color: COLORS.black } },
				{ feedbackId: 'pace', options: { pace: 'ahead' }, style: { bgcolor: COLORS.blue, color: COLORS.white } },
			],
		}),
		simple('show_vt', 'Video (VT) countdown from vMix (display)', {
			style: style(`VT\n${v('vt_remaining')}`, { size: '18' }),
			steps: [],
			feedbacks: [
				{ feedbackId: 'vt_running', options: { last: 0 }, style: { bgcolor: COLORS.blue, color: COLORS.white } },
				{ feedbackId: 'vt_running', options: { last: 10 }, style: { bgcolor: COLORS.red, color: COLORS.white } },
			],
		}),
		simple('listen', 'Speech pace tracking (toggle)', { style: style(`SCRIPT\n${v('script_percent')}%\n${v('wpm')} wpm`, { size: '14' }), steps: press('listen'), feedbacks: [] }),
		...(cfg.ladders ?? []).map((l) =>
			simple(`ladder_${l.id}`, `Cue ladder: ${l.name}`, { style: style(l.name, { size: '7', bgcolor: COLORS.darkgrey }), steps: press('ladder', { ladder: l.id }), feedbacks: [] }),
		),
	]

	// ---------------------------------------------------------------- Q&A and show
	const qa = [
		simple('qa_next', 'Q&A: next question live', {
			style: style(`Q&A\n${v('qa_queued')} queued\n~${v('qa_fit')} fit`, { size: '14', bgcolor: COLORS.darkgrey }),
			steps: press('qa_next'),
			feedbacks: [
				{ feedbackId: 'qa_waiting', options: { which: 'queued' }, style: { bgcolor: COLORS.blue, color: COLORS.white } },
				{ feedbackId: 'qa_waiting', options: { which: 'new' }, style: { bgcolor: COLORS.amber, color: COLORS.black } },
			],
		}),
		simple('report', 'Save the show report', { style: style('SHOW\nREPORT', { size: '14' }), steps: press('report', { format: 'html' }), feedbacks: [] }),
		simple('rooms', 'Other rooms (display)', {
			style: style(`ROOMS\n${v('rooms_late')}/${v('rooms')} late`, { size: '14' }),
			steps: [],
			feedbacks: [{ feedbackId: 'room_late', options: { room: '', seconds: 60 }, style: { bgcolor: COLORS.red, color: COLORS.white } }],
		}),
	]

	// ---------------------------------------------------------------- 2.2: outputs
	const outputs = [
		simple('output_full', 'Output: full screen (toggle)', {
			style: style('OUTPUT\nFULL', { size: '14', bgcolor: COLORS.darkgrey }),
			steps: press('output', { mode: 'toggle' }),
			feedbacks: [{ feedbackId: 'output', options: { mode: 'full' }, style: { bgcolor: COLORS.green, color: COLORS.white, text: 'OUTPUT\nFULL ●' } }],
			keywords: ['output', 'screen', 'monitor', 'confidence'],
		}),
		simple('output_keyed', 'Output: keyed full screen (vision mixer)', {
			style: style('OUTPUT\nKEYED', { size: '14', bgcolor: COLORS.darkgrey }),
			steps: press('output', { mode: 'keyed' }),
			feedbacks: [
				{ feedbackId: 'output', options: { mode: 'keyed' }, style: { bgcolor: COLORS.blue, color: COLORS.white, text: 'KEYED\n●' } },
				{ feedbackId: 'feature_locked', options: { feature: 'keyed' }, style: { bgcolor: COLORS.darkgrey, color: COLORS.grey } },
			],
			keywords: ['output', 'chroma', 'key', 'vision mixer'],
		}),
		simple('output_mini', 'Output: floating mini timer', {
			style: style('OUTPUT\nMINI', { size: '14', bgcolor: COLORS.darkgrey }),
			steps: press('output', { mode: 'mini' }),
			feedbacks: [
				{ feedbackId: 'output', options: { mode: 'mini' }, style: { bgcolor: COLORS.blue, color: COLORS.white, text: 'MINI\n●' } },
				{ feedbackId: 'feature_locked', options: { feature: 'keyed' }, style: { bgcolor: COLORS.darkgrey, color: COLORS.grey } },
			],
		}),
		simple('output_close', 'Output: close', {
			style: style('OUTPUT\nCLOSE', { size: '14' }),
			steps: press('output', { mode: 'closed' }),
			feedbacks: [{ feedbackId: 'output', options: { mode: 'closed' }, style: { bgcolor: COLORS.red, color: COLORS.white, text: 'OUTPUT\nOFF' } }],
		}),
		simple('ndi_toggle', 'NDI on / off', {
			style: style('NDI', { size: '18', bgcolor: COLORS.darkgrey }),
			steps: press('ndi', { state: 'toggle' }),
			feedbacks: [
				{ feedbackId: 'ndi', options: {}, style: { bgcolor: COLORS.blue, color: COLORS.white, text: 'NDI\n● LIVE' } },
				{ feedbackId: 'feature_locked', options: { feature: 'ndi' }, style: { bgcolor: COLORS.darkgrey, color: COLORS.grey } },
			],
			keywords: ['ndi', 'stream', 'vmix', 'obs'],
		}),
		simple('style_holo', 'Holographic 3D display (toggle)', {
			style: style('3D\nHOLO', { size: '14', bgcolor: COLORS.darkgrey }),
			steps: press('style', { style: 'toggle' }),
			feedbacks: [
				{ feedbackId: 'holographic', options: {}, style: { bgcolor: COLORS.violet, color: COLORS.white, text: '3D\nHOLO ●' } },
				{ feedbackId: 'feature_locked', options: { feature: 'holo' }, style: { bgcolor: COLORS.darkgrey, color: COLORS.grey } },
			],
			keywords: ['holographic', '3d', 'hologram', 'style', 'three'],
		}),
		simple('style_egg', '3D egg timer', {
			style: style('3D\nEGG ⏳', { size: '14', bgcolor: COLORS.darkgrey }),
			steps: press('style', { style: 'egg' }),
			feedbacks: [
				{ feedbackId: 'holographic', options: { scene: 'egg' }, style: { bgcolor: COLORS.violet, color: COLORS.white } },
				{ feedbackId: 'feature_locked', options: { feature: 'holo' }, style: { bgcolor: COLORS.darkgrey, color: COLORS.grey } },
			],
			keywords: ['egg', 'hourglass', 'sand', '3d'],
		}),
		...[1, 2, 3].map((n) =>
			simple(`aux_${n}_toggle`, `Extra timer ${n} (start / pause)`, {
				style: style(`${v(`aux_${n}_name`)}\n${v(`aux_${n}`)}`, { size: '14', bgcolor: COLORS.darkgrey }),
				steps: press('aux', { n: String(n), action: 'toggle' }),
				feedbacks: [
					{ feedbackId: 'aux_running', options: { n: String(n), what: 'running' }, style: { bgcolor: COLORS.green, color: COLORS.white } },
					{ feedbackId: 'aux_running', options: { n: String(n), what: 'over' }, style: { bgcolor: COLORS.red, color: COLORS.white } },
				],
				keywords: ['aux', 'extra', 'timer', 'vt', 'changeover', 'second'],
			}),
		),
		...[1, 2, 3].map((n) =>
			simple(`aux_${n}_stage`, `Extra timer ${n}: show on stage`, {
				style: style(`STAGE\n${v(`aux_${n}_name`)}`, { size: '14', bgcolor: COLORS.darkgrey }),
				steps: press('aux', { n: String(n), action: 'stage' }),
				feedbacks: [{ feedbackId: 'aux_running', options: { n: String(n), what: 'stage' }, style: { bgcolor: COLORS.violet, color: COLORS.white } }],
				keywords: ['aux', 'stage', 'second line'],
			}),
		),
		simple('roll_toggle', 'Running order: Roll (toggle)', {
			style: style(`ROLL\n${v('rundown')}`, { size: '14', bgcolor: COLORS.darkgrey }),
			steps: press('roll', { state: 'toggle' }),
			feedbacks: [{ feedbackId: 'roll', options: {}, style: { bgcolor: COLORS.green, color: COLORS.white, text: `ROLLING\n${v('rundown')}` } }],
			keywords: ['roll', 'auto', 'clock', 'unattended', 'running order'],
		}),
		simple('screens_identify', 'Screens: identify all', {
			style: style('FIND\nSCREENS', { size: '14', bgcolor: COLORS.darkgrey }),
			steps: press('screen_identify', { who: '' }),
			feedbacks: [],
			keywords: ['screens', 'identify', 'find', 'tv', 'tablet'],
		}),
		simple('ltc_chase', 'LTC in: timer follows timecode (toggle)', {
			style: style(`LTC
${v('ltc')}`, { size: '7', bgcolor: COLORS.darkgrey }),
			steps: press('ltc_chase', { state: 'toggle' }),
			feedbacks: [
				{ feedbackId: 'ltc_chase', options: {}, style: { bgcolor: COLORS.violet, color: COLORS.white } },
				{ feedbackId: 'ltc_running', options: { chase: true }, style: { bgcolor: COLORS.amber, color: COLORS.black } },
				{ feedbackId: 'feature_locked', options: { feature: 'timecode' }, style: { bgcolor: COLORS.darkgrey, color: COLORS.grey } },
			],
			keywords: ['ltc', 'timecode', 'smpte', 'chase', 'video'],
		}),
		simple('show_licence', 'Licence (display)', { style: style(`LICENCE\n${v('licence')}`, { size: '14' }), steps: [], feedbacks: [] }),
	]

	// 2.3: holding images — one key per image (lit violet while on air), plus off / toggle
	const holdingKeys = [
		simple('holding_off', 'Holding: off (back to the timer)', {
			style: style('HOLD\nOFF', { size: '14' }),
			steps: press('holding', { image: 'off' }),
			feedbacks: [{ feedbackId: 'holding', options: { image: 'any' }, style: { bgcolor: COLORS.amber, color: COLORS.black, text: `HOLD OFF\n${v('holding')}` } }],
			keywords: ['holding', 'slide', 'image', 'logo', 'walk-in'],
		}),
		simple('holding_toggle', 'Holding: toggle the last image', {
			style: style('HOLD\n⏯', { size: '14', bgcolor: COLORS.darkgrey }),
			steps: press('holding', { image: 'toggle' }),
			feedbacks: [{ feedbackId: 'holding', options: { image: 'any' }, style: { bgcolor: COLORS.violet, color: COLORS.white, text: `ON AIR\n${v('holding')}` } }],
		}),
		...(cfg.holding ?? []).map((h, i) =>
			simple(`holding_${h.id}`, `Holding: ${h.name}`, {
				style: style(`HOLD\n${v(`holding_${i + 1}_name`)}`, { size: '7', bgcolor: COLORS.darkgrey }),
				steps: press('holding', { image: h.id }),
				feedbacks: [{ feedbackId: 'holding', options: { image: h.id }, style: { bgcolor: COLORS.violet, color: COLORS.white } }],
			}),
		),
	]

	// 2.2: recovery extras and cue resend
	const recovery22 = [
		simple('trim_live_m60', 'Cut the live item by 1 min', {
			style: style(`CUT\n−1m\n${v('live_trim')}`, { size: '14', bgcolor: COLORS.darkgrey }),
			steps: press('trim_live', { seconds: -60 }),
			feedbacks: [{ feedbackId: 'item_trimmed', options: { item: 0, how: 'cut' }, style: { bgcolor: COLORS.orange, color: COLORS.black } }],
			keywords: ['trim', 'cut', 'shorten'],
		}),
		simple('trim_live_p60', 'Extend the live item by 1 min', {
			style: style(`EXTEND\n+1m\n${v('live_trim')}`, { size: '14', bgcolor: COLORS.darkgrey }),
			steps: press('trim_live', { seconds: 60 }),
			feedbacks: [{ feedbackId: 'item_trimmed', options: { item: 0, how: 'extended' }, style: { bgcolor: COLORS.blue, color: COLORS.white } }],
		}),
		simple('trim_undo', 'Undo trims on the live item', {
			style: style('UNDO\nTRIM', { size: '14' }),
			steps: press('trim_undo', { item: 0 }),
			feedbacks: [{ feedbackId: 'item_trimmed', options: { item: 0, how: 'any' }, style: { bgcolor: COLORS.orange, color: COLORS.black } }],
		}),
		simple('live_kind', 'What\'s on now (display, coloured by kind)', {
			style: style(`ON NOW\n${v('live_kind')}`, { size: '14' }),
			steps: [],
			feedbacks: [
				{ feedbackId: 'item_kind', options: { kind: 'qa' }, style: { bgcolor: COLORS.blue, color: COLORS.white } },
				{ feedbackId: 'item_kind', options: { kind: 'break' }, style: { bgcolor: COLORS.grey, color: COLORS.white } },
				{ feedbackId: 'item_kind', options: { kind: 'panel' }, style: { bgcolor: COLORS.orange, color: COLORS.black } },
			],
		}),
	]
	const cueResend = simple('cue_resend', 'Resend the unanswered cue', {
		style: style('RESEND\nCUE', { size: '14', bgcolor: COLORS.darkgrey }),
		steps: press('signal_resend', { target: '' }),
		feedbacks: [
			{ feedbackId: 'cue_waiting', options: { target: '', escalated: true }, style: { bgcolor: COLORS.red, color: COLORS.white, text: `RESEND\n${v('last_cue')}` } },
			{ feedbackId: 'feature_locked', options: { feature: 'crewcues' }, style: { bgcolor: COLORS.darkgrey, color: COLORS.grey } },
		],
		keywords: ['cue', 'resend', 'escalated', 'radio'],
	})
	const qaAnswered = simple('qa_answered', 'Q&A: question on air answered', {
		style: style('Q&A\nDONE ✓', { size: '14' }),
		steps: press('qa_live', { state: 'answered' }),
		feedbacks: [],
	})

	// Dim Production-only keys on lower licences, so nobody presses a key that won't work.
	const lockAll = (ids: string[], feature: string): void => {
		for (const id of ids) {
			const p = presets[id] as CompanionSimplePresetDefinition<ModuleSchema> | undefined
			if (p?.type !== 'simple') continue
			p.feedbacks = [...(p.feedbacks ?? []), { feedbackId: 'feature_locked', options: { feature }, style: { bgcolor: COLORS.darkgrey, color: COLORS.grey } }]
		}
	}
	lockAll(['cue_wrap', 'cue_finish', 'cue_slow', 'cue_buzz', 'cue_call_next', 'cue_podium_ack', 'cue_waiting', 'dept_standby_t', 'dept_go_t', 'all_go'], 'crewcues')
	lockAll(['show_forecast'], 'forecast')
	lockAll(['advice_1', 'advice_2', 'advice_3'], 'assistant')
	lockAll(['show_vt'], 'vmix')
	lockAll(['listen'], 'speech')
	lockAll(['rooms'], 'multiroom')
	lockAll(['report'], 'fullreport')
	lockAll(holdingKeys, 'holding')

	// ---------------------------------------------------------------- the Showcaller / Producer page (8 × 4)
	// Laid out in reading order for a 32-key page: drag the keys onto a page left to right.
	const showcallerPage = [
		'timer_ring', 'show_title', 'show_schedule', 'show_curfew', 'show_forecast', 'advice_1', 'advice_2', 'show_link',
		'timer_toggle', 'previous', 'next', 'catchup', 'nudge_m60', 'nudge_p60', 'delay_1', 'delay_5',
		'cue_wrap', 'cue_finish', 'cue_buzz', 'cue_call_next', 'cue_waiting', 'preview', 'show_slide', 'show_vt',
		'qa_next', 'next_expected', 'advice_3', 'rooms', 'report', 'blackout', 'reset', 'show_tod',
	].filter((id) => presets[id] !== undefined)

	const structure: CompanionPresetSection<ModuleSchema>[] = [
		{
			id: 'showcaller',
			name: 'Showcaller / Producer page',
			description:
				'A complete 32-key page for the show caller, in reading order (8 across, 4 down): the timer ring, schedule, curfew, assistant, transport, recovery, speaker cues, green room, Q&A and blackout.',
			definitions: showcallerPage,
		},
		{
			id: 'control',
			name: 'Show control',
			definitions: [
				{ id: 'transport', type: 'simple', name: 'Transport', presets: control },
				{ id: 'nudge', type: 'simple', name: 'Add / remove time', presets: nudge },
				{ id: 'modes', type: 'simple', name: 'Clock mode', presets: modes },
			],
		},
		{
			id: 'display',
			name: 'Live displays',
			description: 'Keys that show Stagewatch state — they update live.',
			definitions: display,
		},
		{
			id: 'running_order',
			name: 'Running order',
			description: itemIds.length
				? 'One key per item. Tap to load, hold 1 s to load and start. Green = on now, grey = done.'
				: 'Add items to the running order in Stagewatch and they appear here.',
			definitions: itemIds,
		},
		{
			id: 'presets',
			name: 'Presets',
			description: 'Tap to load, hold 1 s to load and start. Labels follow Stagewatch live.',
			definitions: presetIds,
		},
		{ id: 'messages', name: 'Messages', definitions: messages },
		{
			id: 'holding',
			name: 'Holding images',
			description: (cfg.holding ?? []).length
				? 'One key per holding image — press to put it on every screen (violet = on air), Off to go back to the timer.'
				: 'Add holding images in Stagewatch (Holding tab) and a key appears here for each.',
			definitions: holdingKeys,
		},
		{
			id: 'outputs',
			name: 'Outputs, NDI & 3D',
			description: 'Open, key or close the speaker output and switch NDI — lit while active. Greyed when the licence doesn\'t include it.',
			definitions: outputs,
		},
		{
			id: 'cues',
			name: 'Cues & departments',
			description: 'Cues the target confirms: amber = waiting, green = confirmed, red = nobody answered. Department keys follow the departments set in Stagewatch.',
			definitions: [
				{ id: 'cue_keys', type: 'simple', name: 'Speaker & green room', presets: [...cueKeys, cueResend] },
				{
					id: 'dept_standby',
					type: 'template',
					name: 'Department standby',
					presetId: 'dept_standby_t',
					templateVariableName: 'dept',
					templateValues: deptValues,
				},
				{ id: 'dept_go', type: 'template', name: 'Department GO', presetId: 'dept_go_t', templateVariableName: 'dept', templateValues: deptValues },
				{ id: 'all_go', type: 'simple', name: 'ALL GO (every department standing by)', presets: ['all_go'] },
			],
		},
		{
			id: 'recovery',
			name: 'Schedule recovery & assistant',
			description: 'Curfew, likely end, catch up, delays, and the assistant\'s suggestions (press to apply).',
			definitions: [...recovery, ...recovery22, ...advice],
		},
		{ id: 'speaker', name: 'Speaker cues & presentation', definitions: [...speakerKeys, 'timer_ring'] },
		{ id: 'qa', name: 'Q&A, report & rooms', definitions: [...qa, qaAnswered] },
	]
	self.setPresetDefinitions(structure, presets)
}
