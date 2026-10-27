import {
	InstanceBase,
	InstanceStatus,
	type CompanionHTTPRequest,
	type CompanionHTTPResponse,
	type CompanionVariableValues,
	type InstanceTypes,
	type SomeCompanionConfigField,
} from '@companion-module/base'
import { UpdateActions } from './actions.js'
import { DEFAULT_PORT, GetConfigFields, type ModuleConfig } from './config.js'
import { answerShown, receiptWords, StagewatchConnection, type LinkStatus, type Receipt } from './connection.js'
import { UpdateFeedbacks } from './feedbacks.js'
import { UpdatePresets } from './presets.js'
import {
	EMPTY_CONFIG,
	configVariables,
	deriveVariables,
	diffValues,
	feedbacksAffected,
	playoutOffered,
	readPlayoutLinks,
	type ShowConfig,
	type Snapshot,
	type VariableValues,
} from './state.js'
import { UpgradeScripts } from './upgrades.js'
import { UpdateVariableDefinitions } from './variables.js'

export type ModuleSchema = {
	config: ModuleConfig
	secrets: undefined
	actions: InstanceTypes['actions']
	feedbacks: InstanceTypes['feedbacks']
	variables: CompanionVariableValues
}

export { UpgradeScripts }

/**
 * Stagewatch ↔ Companion.
 *
 * Data flow: Stagewatch pushes a full SNAPSHOT frame whenever anything visible changes (~1/s
 * while running) and a CONFIG frame when presets / quick messages / the running order change.
 * Per frame we send Companion exactly one batched setVariableValues (changed values only) and one
 * checkFeedbacks (only the feedbacks whose inputs changed). Definitions (actions, presets,
 * variable list) are rebuilt only when the *shape* of the show changes, debounced.
 */
export default class ModuleInstance extends InstanceBase<ModuleSchema> {
	config!: ModuleConfig
	snapshot: Snapshot | null = null
	showConfig: ShowConfig = EMPTY_CONFIG
	/** A CONFIG has been heard from Stagewatch since this instance began: until then nothing is known of its grammar or licence. */
	configHeard = false
	isConnected = false
	/** The latest answer Stagewatch gave a playout press, or why the latest was not sent (`playout_answer`). */
	#playoutAnswer = ''
	/** The id the latest playout press went with; null when it was not sent. */
	#latestPlayout: string | null = null

	#conn: StagewatchConnection | null = null
	#vars: VariableValues = {}
	#shape = ''
	#defsTimer: NodeJS.Timeout | null = null
	#lastStatus = ''
	#recording = false

	constructor(internal: unknown) {
		super(internal)
	}

	async init(config: ModuleConfig): Promise<void> {
		this.config = config
		this.#publishDefinitions()
		this.#pushVariables()
		this.#connect() // never awaited: init must return promptly
	}

	async configUpdated(config: ModuleConfig): Promise<void> {
		const changed = config.host !== this.config?.host || config.port !== this.config?.port
		this.config = config
		if (changed) this.#connect()
	}

	async destroy(): Promise<void> {
		if (this.#defsTimer) clearTimeout(this.#defsTimer)
		this.#conn?.destroy()
		this.#conn = null
	}

	getConfigFields(): SomeCompanionConfigField[] {
		return GetConfigFields()
	}

	/** Send a protocol line now, or drop it (never queue stale presses while offline). */
	sendCommand(line: string): void {
		if (!this.#conn?.send(line)) this.log('warn', `Not connected to Stagewatch — "${line}" was not sent`)
		else this.log('debug', `→ ${line}`)
	}

	/**
	 * Send a line that drives another app's air (`PLAYOUT …`, `PATTERNS …`) with a request id, so what Stagewatch — and through it the
	 * peer — answers reaches the log and `playout_answer`: a refusal at Stagewatch's door, the peer's own words, or the answer lost
	 * with the link. Dropped, never queued, while offline.
	 */
	sendPlayout(line: string): void {
		const id = this.#conn?.request(line) ?? null
		this.#latestPlayout = id
		if (id === null) {
			this.log('warn', `Not connected to Stagewatch — "${line}" was not sent`)
			this.#setPlayoutAnswer('not sent — not connected to Stagewatch')
			return
		}
		this.log('debug', `→ @${id} ${line}`)
	}

	#onReceipt(r: Receipt): void {
		this.log(r.kind === 'answered' && r.ok ? 'info' : 'warn', `${r.line}: ${receiptWords(r)}`)
		this.#setPlayoutAnswer(answerShown(r, this.#latestPlayout))
	}

	#setPlayoutAnswer(words: string): void {
		this.#playoutAnswer = words
		this.#pushVariables()
	}

	// ------------------------------------------------------------------ connection

	#connect(): void {
		this.#conn?.destroy()
		this.#setConnected(false)

		const host = (this.config.host || '').trim()
		const port = Number(this.config.port) || DEFAULT_PORT
		if (!host) {
			this.updateStatus(InstanceStatus.BadConfig, 'Enter the address of the Stagewatch PC')
			return
		}

		const conn = new StagewatchConnection(host, port)
		this.#conn = conn
		conn.on('status', (s) => this.#onLinkStatus(s))
		conn.on('snapshot', (s) => this.#onSnapshot(s))
		conn.on('config', (c) => this.#onConfig(c))
		conn.on('record', (line) => this.#onRecord(line))
		conn.on('protocolError', (m) => this.log('warn', m))
		conn.on('receipt', (r) => this.#onReceipt(r))
		conn.start()
	}

	#onLinkStatus(s: LinkStatus): void {
		const key = JSON.stringify(s)
		if (key === this.#lastStatus) return
		this.#lastStatus = key
		switch (s.kind) {
			case 'connecting':
				this.updateStatus(InstanceStatus.Connecting)
				break
			case 'connected':
				this.updateStatus(InstanceStatus.Ok)
				this.log('info', `Connected to Stagewatch at ${this.config.host}:${this.config.port}`)
				this.#setConnected(true)
				break
			case 'disconnected':
				this.updateStatus(InstanceStatus.ConnectionFailure, s.reason)
				this.#setConnected(false)
				break
			case 'stuck':
				this.updateStatus(InstanceStatus.UnknownWarning, 'Stagewatch has stopped updating — the time on keys may be frozen')
				this.log('warn', 'Stagewatch is connected but has stopped updating')
				this.#setConnected(false)
				break
			case 'denied':
				this.updateStatus(InstanceStatus.InsufficientPermissions, s.reason)
				this.log('warn', s.reason)
				this.#setConnected(false)
				break
		}
	}

	#setConnected(connected: boolean): void {
		const wasConnected = this.isConnected
		this.isConnected = connected
		const prev = this.snapshot
		// Offline means unknown: blank the timer rather than show a frozen, wrong time.
		if (!connected) this.snapshot = null
		if (wasConnected !== connected || prev !== this.snapshot) {
			this.#pushVariables()
			this.checkFeedbacks('connected', ...feedbacksAffected(prev, this.snapshot, true))
		}
	}

	// ------------------------------------------------------------------ frames

	#onSnapshot(s: Snapshot): void {
		const prev = this.snapshot
		this.snapshot = s
		this.#pushVariables()
		const changed = feedbacksAffected(prev, s, false)
		if (changed.length > 0) this.checkFeedbacks(changed[0], ...changed.slice(1))
	}

	#licence = ''

	#onConfig(c: ShowConfig): void {
		this.showConfig = {
			presets: Array.isArray(c.presets) ? c.presets : [],
			messages: Array.isArray(c.messages) ? c.messages : [],
			rundown: Array.isArray(c.rundown) ? c.rundown : [],
			ladders: Array.isArray(c.ladders) ? c.ladders : [],
			departments: Array.isArray(c.departments) ? c.departments : [],
			room: typeof c.room === 'string' ? c.room : '',
			ladder: typeof c.ladder === 'string' ? c.ladder : '',
			tier: typeof c.tier === 'string' ? c.tier : undefined,
			features: Array.isArray(c.features) ? c.features.filter((f) => typeof f === 'string') : undefined,
			holding: Array.isArray(c.holding) ? c.holding.filter((h) => h && typeof h.id === 'string') : [],
			playoutLinks: readPlayoutLinks(c.playoutLinks),
		}
		this.configHeard = true
		this.#pushVariables()
		// The licence can change while connected (activated mid-show): recheck the dimmed keys.
		const licence = JSON.stringify([this.showConfig.tier, this.showConfig.features])
		if (licence !== this.#licence) {
			this.#licence = licence
			this.checkFeedbacks('feature_locked')
		}

		// Labels are variables, so they update live. Only the *shape* (counts, quick-message
		// labels, dropdown text) needs new definitions — and that is debounced, since redefining
		// actions/presets is comparatively expensive for Companion.
		const shape = JSON.stringify([
			this.showConfig.presets.map((p) => [p.slot, p.label, p.seconds]),
			this.showConfig.messages.map((m) => [m.label, m.urgency]),
			this.showConfig.rundown.map((r) => [r.title, r.speaker, r.seconds]),
			this.showConfig.departments,
			(this.showConfig.ladders ?? []).map((l) => l.id),
			(this.showConfig.holding ?? []).map((h) => [h.id, h.name]),
			// The playout actions and keys come and go with the licence and the links (each link's label is in its dropdown).
			playoutOffered(this.showConfig, this.configHeard),
			(this.showConfig.playoutLinks ?? []).map((l) => [l.name, l.kind, l.label]),
		])
		if (shape === this.#shape) return
		this.#shape = shape
		if (this.#defsTimer) clearTimeout(this.#defsTimer)
		this.#defsTimer = setTimeout(() => {
			this.#defsTimer = null
			this.#publishDefinitions()
			this.#pushVariables()
		}, 300)
	}

	#publishDefinitions(): void {
		UpdateActions(this)
		UpdateFeedbacks(this)
		UpdateVariableDefinitions(this)
		UpdatePresets(this) // after actions & feedbacks
	}

	// ------------------------------------------------------------------ action recording
	// Press "record" on a Companion button, then do it in Stagewatch: every operator action arrives
	// as a RECORD line and becomes a raw-command action on the button, in order.

	handleStartStopRecordActions(isRecording: boolean): void {
		this.#recording = isRecording
		this.log('info', isRecording ? 'Recording: do it in Stagewatch and it lands on the button' : 'Recording stopped')
	}

	#onRecord(line: string): void {
		if (!this.#recording) return
		this.recordAction({ actionId: 'raw', options: { command: line } })
	}

	// ------------------------------------------------------------------ HTTP (Companion serves /instance/<label>/…)
	// /producer and /speaker jump to Stagewatch's own pages; /state gives the live frame as JSON
	// (handy for other tools on the Companion machine).

	handleHttpRequest(request: CompanionHTTPRequest): CompanionHTTPResponse {
		const path = request.path.replace(/\/+$/, '') || '/'
		const host = (this.config.host || '127.0.0.1').trim()
		const web = Number(this.config.webPort) || 9924
		const pages: Record<string, string> = {
			'/': '/', '/producer': '/producer', '/showcaller': '/producer', '/speaker': '/viewer',
			'/backstage': '/backstage', '/moderator': '/moderator', '/rooms': '/rooms', '/audience': '/audience',
		}
		if (path === '/state') {
			return {
				status: 200,
				headers: { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' },
				body: JSON.stringify(this.snapshot ?? { connected: this.isConnected }),
			}
		}
		const target = pages[path]
		if (target === undefined) return { status: 404, body: 'Not found. Try /producer, /speaker, /backstage, /moderator, /rooms, /audience or /state.' }
		return { status: 302, headers: { Location: `http://${host}:${web}${target}` } }
	}

	/** One batched IPC message with only the values that changed. */
	#pushVariables(): void {
		const next = {
			...deriveVariables(this.snapshot, this.isConnected, this.showConfig),
			...configVariables(this.showConfig),
			playout_answer: this.#playoutAnswer,
		}
		const changed = diffValues(this.#vars, next)
		this.#vars = next
		if (Object.keys(changed).length > 0) this.setVariableValues(changed)
	}
}
