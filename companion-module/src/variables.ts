import type { CompanionVariableDefinitions } from '@companion-module/base'
import { slug } from './state.js'
import type ModuleInstance from './main.js'

const STATIC: Record<string, string> = {
	time: 'Timer as shown (e.g. 04:59, -00:12)',
	time_short: 'Timer, compact (e.g. 4:59 / 1:02:03)',
	time_hms: 'Timer, always HH:MM:SS',
	hours: 'Hours (2 digits)',
	minutes: 'Minutes (2 digits)',
	seconds_part: 'Seconds (2 digits)',
	sign: '"-" in overtime, otherwise empty',
	seconds: 'Remaining seconds (negative in overtime; elapsed in count-up)',
	total: 'Loaded duration',
	total_seconds: 'Loaded duration in seconds',
	elapsed: 'Elapsed time of the current countdown',
	progress: 'Percent of the countdown left (0–100)',
	state: 'idle / running / paused / expired',
	mode: 'countdown / countup / tod',
	phase: 'normal / warning / wrapup / overtime',
	running: 'Clock is running (true/false)',
	overtime: 'At or past zero (true/false)',
	color: 'Timer colour as hex (#RRGGBB)',
	title: 'Current item or preset title',
	next: 'Next item in the running order',
	item: 'Current running-order item number (0 = none)',
	items: 'Number of running-order items',
	preset: 'Loaded preset slot (0 = none)',
	end_time: 'Projected end time (HH:MM:SS)',
	time_of_day: 'Time of day on the Stagewatch PC',
	offset_seconds: 'Seconds behind schedule (negative = ahead)',
	offset: 'Behind/ahead as text (e.g. +03:12)',
	schedule: 'Schedule summary (e.g. "+03:12 behind · show ends 16:45")',
	message: 'Message currently shown to the presenter',
	message_urgency: 'info / warning / critical',
	blackout: 'Outputs blacked out (true/false)',
	connected: 'Connected to Stagewatch (true/false)',
	// 2.2
	output: 'Output window: closed / full / keyed / mini',
	ndi: 'NDI is sending (true/false)',
	licence: 'Stagewatch licence (Standard, Pro, Production, Trial…)',
	live_kind: 'Kind of the live item (talk, panel, qa, break…)',
	live_trim: 'How much the live item has been trimmed (e.g. -01:00)',
	qa_live_id: 'Id of the question on air (0 = none)',
	// 2.3
	holding: 'Name of the holding image on air (empty = none)',
	holding_on: 'A holding image is on air (true/false)',
	// 2.4
	style: 'Display style: standard / holo / egg',
	// 2.7
	ltc: 'Incoming LTC timecode (HH:MM:SS:FF, empty = none)',
	ltc_rolling: 'Incoming LTC is rolling (true/false)',
	ltc_chase: 'The timer follows LTC (true/false)',
	// 2.8
	aux_1: 'Extra timer 1 (e.g. 02:13, -00:15 past zero)',
	aux_1_name: 'Extra timer 1 name',
	aux_1_running: 'Extra timer 1 is running (true/false)',
	aux_2: 'Extra timer 2',
	aux_2_name: 'Extra timer 2 name',
	aux_2_running: 'Extra timer 2 is running (true/false)',
	aux_3: 'Extra timer 3',
	aux_3_name: 'Extra timer 3 name',
	aux_3_running: 'Extra timer 3 is running (true/false)',
	aux_on_stage: 'The extra timer shown on stage ("VT  02:13"; empty = none)',
	// 2.9
	roll: 'Roll is on: the running order follows the clock (true/false)',
	rundown: 'The live running order (e.g. Day 1)',
	rundowns: "All of the show's running orders, comma separated",
	// 2.0
	speaker_time: 'Timer as the speaker sees it (calm "12 MIN", honest clock…)',
	cue: 'Cue-ladder label in force ("5 MIN", "WRAP UP")',
	finish_by: 'Speaker finish-by time (HH:MM)',
	preview: 'Briefing preview playing (true/false)',
	stretching: 'Elastic end-at is stretching time (true/false)',
	room: 'Room name',
	ladder: 'Active cue ladder',
	message_ack: 'Speaker acknowledgement of the message: "" / sent / seen / done',
	message_ack_by: 'Who acknowledged the message',
	cues_open: 'Cues sent and not yet finished',
	cues_waiting: 'Cues nobody has acknowledged yet',
	cues_unanswered: 'Cues that escalated (no acknowledgement in time)',
	last_cue: 'Newest open cue (kind and target)',
	standing_by: 'Departments standing by (waiting for GO)',
	standing_by_list: 'Departments standing by, by name',
	slide: 'PowerPoint slide number',
	slides: 'PowerPoint slide count',
	slide_next: 'Title of the next slide',
	pace: 'ahead / onpace / behind against the rehearsal',
	ghost: 'Time behind (+) or ahead (-) of the rehearsal',
	script_percent: 'Percent of the script delivered (speech tracking)',
	wpm: 'Speaking pace, words per minute',
	vt_title: 'Video (VT) playing in vMix',
	vt_remaining: 'Video time left (MM:SS)',
	vt_seconds: 'Video seconds left',
	hard_out: 'Next curfew and how the show is tracking against it',
	hard_out_margin: 'Time to spare (+) or over (-) at the next curfew',
	forecast: 'Likely end of show (P50 / P80) and curfew risk',
	delay: 'Delay announced before the next item',
	next_expected: 'Expected start of the next item',
	clock_offset_ms: 'How far the Stagewatch PC clock is from the time server (ms)',
	qa_live: 'Question being answered now',
	qa_new: 'New audience questions waiting for approval',
	qa_queued: 'Approved questions queued',
	qa_fit: 'How many more answers fit in the time left',
	advice_count: 'Number of assistant suggestions',
	advice_1: 'Assistant suggestion 1',
	advice_1_action: 'Button label for suggestion 1 ("" = information only)',
	advice_2: 'Assistant suggestion 2',
	advice_2_action: 'Button label for suggestion 2',
	advice_3: 'Assistant suggestion 3',
	advice_3_action: 'Button label for suggestion 3',
	rooms: 'Other Stagewatch rooms on the network',
	rooms_late: 'Rooms running late or at curfew risk',
}

export function UpdateVariableDefinitions(self: ModuleInstance): void {
	const defs: CompanionVariableDefinitions = {}
	for (const [id, name] of Object.entries(STATIC)) defs[id] = { name }
	const cfg = self.showConfig
	const slots = cfg.presets.length ? cfg.presets.map((p) => p.slot) : Array.from({ length: 12 }, (_, i) => i + 1)
	for (const slot of slots) {
		defs[`preset_${slot}_label`] = { name: `Preset ${slot}: label` }
		defs[`preset_${slot}_duration`] = { name: `Preset ${slot}: duration` }
	}
	cfg.rundown.forEach((_, i) => {
		const n = i + 1
		defs[`item_${n}_title`] = { name: `Item ${n}: title` }
		defs[`item_${n}_speaker`] = { name: `Item ${n}: speaker` }
		defs[`item_${n}_duration`] = { name: `Item ${n}: duration` }
		defs[`item_${n}_planned`] = { name: `Item ${n}: planned start` }
		defs[`item_${n}_expected`] = { name: `Item ${n}: expected start` }
		defs[`item_${n}_actual`] = { name: `Item ${n}: actual start` }
		defs[`item_${n}_status`] = { name: `Item ${n}: done / live / loaded / upcoming` }
		defs[`item_${n}_delay`] = { name: `Item ${n}: delay against plan` }
	})
	;(cfg.departments ?? []).forEach((d, i) => {
		defs[`dept_name_${i + 1}`] = { name: `Department ${i + 1}: name` }
		defs[`dept_${slug(d)}`] = { name: `${d}: clear / standby / standby_ack / go / go_ack` }
	})
	self.setVariableDefinitions(defs)
}
