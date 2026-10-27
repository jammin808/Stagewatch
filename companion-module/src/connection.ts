// Resilient line-protocol client for the Stagewatch Companion server (TCP, default port 9923).
//
//  * Reconnects forever with capped exponential backoff (1 s → 10 s), resets on success.
//  * Heartbeat (Show Bus 1.1 liveness: 2 s / 12 s): PING every 2 s; if nothing at all arrives for 12 s the socket is presumed
//    dead (cable pulled, laptop asleep, Wi-Fi roam) and is torn down and redialled.
//  * Commands are never queued while offline: replaying stale "START"/"NEXT" presses after a
//    reconnect would be dangerous mid-show. A press while offline is dropped and logged.
//  * Inbound data is reassembled into lines with a hard length cap.
//  * A line sent with a request id (`@c<n> …`, request()) is answered by Stagewatch's own receipt: `OK @id <stage> [words]` or
//    `ERROR @id <words>`, and after an `accepted` that waits on another node its one follow-up, the word `final` after the id, within
//    30 s of the send. Each answer is a 'receipt' event naming the line it answers. A line still waiting when the socket closes — no
//    answer yet, or an `accepted` within FINAL_WAIT_MS of its send — is one 'receipt' of kind `lost`, never a success the module did
//    not hear; one older than that is forgotten without a word, for no answer to it could still come.

import { EventEmitter } from 'node:events'
import net from 'node:net'
import type { ShowConfig, Snapshot } from './state.js'

export type LinkStatus =
	| { kind: 'connecting' }
	| { kind: 'connected' }
	| { kind: 'disconnected'; reason: string }
	| { kind: 'denied'; reason: string }
	/** The link is fine but Stagewatch itself has stopped updating (its show thread is stuck). */
	| { kind: 'stuck' }

/**
 * Stagewatch's answer to a line sent with a request id (Show Bus §3 on Companion TCP). `line` is the line as sent, without its id.
 * `answered` an `OK` or `ERROR` line for that id; `lost` the socket closed while the line still waited — with no answer yet, so it may
 * or may not have run, or after an `accepted` and before any `final`, which may or may not have been coming.
 */
export interface Receipt {
	id: string
	line: string
	kind: 'answered' | 'lost'
	ok: boolean
	/** `accepted` or `applied` on an OK; "" on an ERROR, on a bare `OK @id`, and on `lost`. */
	stage: string
	/** The follow-up after an `accepted` that waited on another node: that node's final word, or Stagewatch's own. */
	final: boolean
	/** Stagewatch's words: the effect, or why not. */
	words: string
	/** On `lost`: whether an `accepted` had come before the socket closed (a `final` may or may not have been coming). */
	wasAccepted: boolean
}

export interface ConnectionEvents {
	status: [LinkStatus]
	snapshot: [Snapshot]
	config: [ShowConfig]
	/** An operator action in Stagewatch, as a protocol line (for Companion's action recorder). */
	record: [string]
	protocolError: [string]
	receipt: [Receipt]
}

const MAX_LINE = 1024 * 1024

/** Lines waiting for Stagewatch's answer at most; the oldest is forgotten (its answer then goes unread), never resent. */
export const MAX_PENDING = 64
/**
 * A line waits this long from its send, then is forgotten without a word: Stagewatch answers within its 3 s deadline and sends a
 * follow-up, when one is coming, within 30 s of the send — so past this no answer can come, and none is reported lost.
 */
export const FINAL_WAIT_MS = 40000

/**
 * The words an `accepted` begins with when Stagewatch already knows no follow-up will come (PlayoutReceipts.Unconfirmed: the peer took
 * the line and said nothing final, or never answered): that answer settles the line.
 */
const UNCONFIRMED = 'unconfirmed'

const STAGES = ['accepted', 'applied']

/**
 * One receipt line read: `OK @id [final] [<stage>] [words]` or `ERROR @id [final] <words>`; null for any other line. The word
 * `final` after the id marks the follow-up only when the id is waiting for one (its first answer was `accepted`), so an ERROR
 * whose own words begin with "final" is never taken for it.
 */
export function parseReceipt(line: string, awaitingFinal: boolean): { id: string; ok: boolean; stage: string; final: boolean; words: string } | null {
	const m = /^(OK|ERROR) @([A-Za-z0-9_-]{1,32})(?: (.*))?$/.exec(line)
	if (!m) return null
	let rest = m[3] ?? ''
	let final = false
	if (awaitingFinal && (rest === 'final' || rest.startsWith('final '))) {
		final = true
		rest = rest.slice(6)
	}
	const ok = m[1] === 'OK'
	let stage = ''
	if (ok) {
		const sp = rest.indexOf(' ')
		const first = sp < 0 ? rest : rest.slice(0, sp)
		if (STAGES.includes(first)) {
			stage = first
			rest = sp < 0 ? '' : rest.slice(sp + 1)
		}
	}
	return { id: m[2], ok, stage, final, words: rest.trim() }
}

export interface ConnectionTimings {
	pingEveryMs: number
	deadAfterMs: number
	connectTimeoutMs: number
	deniedRetryMs: number
	minBackoffMs: number
	maxBackoffMs: number
}

export const DEFAULT_TIMINGS: ConnectionTimings = {
	pingEveryMs: 2000,
	deadAfterMs: 12000,
	connectTimeoutMs: 5000,
	deniedRetryMs: 30000,
	minBackoffMs: 1000,
	maxBackoffMs: 10000,
}

export class StagewatchConnection extends EventEmitter<ConnectionEvents> {
	#socket: net.Socket | null = null
	#buffer = ''
	#backoffMs: number
	readonly #t: ConnectionTimings
	#reconnectTimer: NodeJS.Timeout | null = null
	#pingTimer: NodeJS.Timeout | null = null
	#lastRx = 0
	#pingSeq = 0
	#stuck = false
	#destroyed = false
	#ready = false
	#denied = false
	#requestSeq = 0
	/** Lines sent with an id and not yet answered for good, oldest first: id → the line, when, and whether `accepted` came. */
	readonly #pending = new Map<string, { line: string; at: number; accepted: boolean }>()

	/** The clock a line's age is read on: the wall clock, or a test's. */
	readonly #now: () => number

	constructor(
		readonly host: string,
		readonly port: number,
		timings: Partial<ConnectionTimings> = {},
		now: () => number = Date.now,
	) {
		super()
		this.#t = { ...DEFAULT_TIMINGS, ...timings }
		this.#backoffMs = this.#t.minBackoffMs
		this.#now = now
	}

	get isReady(): boolean {
		return this.#ready
	}

	start(): void {
		this.#dial()
	}

	/** Send one protocol line. Returns false (and does nothing) when not connected. */
	send(line: string): boolean {
		const s = this.#socket
		if (!s || !this.#ready || s.destroyed) return false
		if (/[\r\n]/.test(line)) line = line.replace(/[\r\n]+/g, ' ')
		s.write(line + '\n')
		return true
	}

	/**
	 * Send one protocol line with a request id, so Stagewatch answers it with a receipt ('receipt' events). Returns the id, or null
	 * (and does nothing) when not connected: a press is never queued for later.
	 */
	request(line: string): string | null {
		const s = this.#socket
		if (!s || !this.#ready || s.destroyed) return null
		if (/[\r\n]/.test(line)) line = line.replace(/[\r\n]+/g, ' ')
		const now = this.#now()
		this.#expire(now)
		while (this.#pending.size >= MAX_PENDING) this.#pending.delete(this.#pending.keys().next().value as string)
		const id = `c${++this.#requestSeq}`
		this.#pending.set(id, { line, at: now, accepted: false })
		s.write(`@${id} ${line}\n`)
		return id
	}

	destroy(): void {
		this.#destroyed = true
		this.#clearTimers()
		this.#teardown()
		this.removeAllListeners()
	}

	#dial(): void {
		if (this.#destroyed) return
		this.#teardown()
		this.#denied = false
		this.emit('status', { kind: 'connecting' })

		const socket = net.createConnection({ host: this.host, port: this.port })
		this.#socket = socket
		socket.setNoDelay(true)
		socket.setKeepAlive(true, 5000)
		socket.setEncoding('utf8')
		socket.setTimeout(this.#t.connectTimeoutMs)

		socket.once('connect', () => {
			socket.setTimeout(0)
			this.#lastRx = Date.now()
			// v2 stream: whole JSON frames on change instead of 20 Hz TICK lines.
			socket.write('SUBSCRIBE JSON\n')
			this.#startHeartbeat()
		})
		socket.on('timeout', () => socket.destroy(new Error('connection timed out')))
		socket.on('data', (chunk: string) => this.#onData(chunk))
		socket.on('error', () => {
			/* 'close' follows; reason is reported there */
		})
		socket.on('close', (hadError) => {
			if (this.#socket !== socket) return
			const wasReady = this.#ready
			this.#ready = false
			this.#stuck = false
			this.#socket = null
			this.#stopHeartbeat()
			this.#forgetPending()
			if (this.#destroyed) return
			if (!this.#denied) {
				this.emit('status', {
					kind: 'disconnected',
					reason: wasReady ? 'Connection lost' : hadError ? `Can't reach Stagewatch at ${this.host}:${this.port}` : 'Closed by Stagewatch',
				})
			}
			this.#scheduleReconnect(this.#denied ? this.#t.deniedRetryMs : undefined)
		})
	}

	#onData(chunk: string): void {
		this.#lastRx = Date.now()
		this.#buffer += chunk
		if (this.#buffer.length > MAX_LINE) {
			this.emit('protocolError', 'Oversized line from Stagewatch — resetting connection')
			this.#buffer = ''
			this.#socket?.destroy()
			return
		}
		let nl: number
		while ((nl = this.#buffer.indexOf('\n')) >= 0) {
			const line = this.#buffer.slice(0, nl).replace(/\r$/, '')
			this.#buffer = this.#buffer.slice(nl + 1)
			if (line.length > 0) this.#onLine(line)
		}
	}

	#onLine(line: string): void {
		if (line.startsWith('OK @') || line.startsWith('ERROR @')) {
			const id = /^\S+ @([A-Za-z0-9_-]{1,32})/.exec(line)?.[1]
			const p = id === undefined ? undefined : this.#pending.get(id)
			if (id !== undefined && p) {
				const r = parseReceipt(line, p.accepted)
				if (r) {
					// An accepted that is not final waits for its follow-up, unless its words say none is coming.
					if (!r.final && r.ok && r.stage === 'accepted' && !(r.words === UNCONFIRMED || r.words.startsWith(UNCONFIRMED + ' '))) p.accepted = true
					else this.#pending.delete(id)
					this.emit('receipt', { id, line: p.line, kind: 'answered', ok: r.ok, stage: r.stage, final: r.final, words: r.words, wasAccepted: p.accepted })
					return
				}
			}
		}
		const space = line.indexOf(' ')
		const verb = space < 0 ? line : line.slice(0, space)
		const rest = space < 0 ? '' : line.slice(space + 1)
		switch (verb) {
			case 'SNAPSHOT': {
				const snap = this.#parse<Snapshot>(rest)
				if (!snap) return
				if (!this.#ready) {
					this.#ready = true
					this.#backoffMs = this.#t.minBackoffMs
					this.emit('status', { kind: 'connected' })
				}
				this.emit('snapshot', snap)
				return
			}
			case 'CONFIG': {
				const cfg = this.#parse<ShowConfig>(rest)
				if (cfg) this.emit('config', cfg)
				return
			}
			case 'DENIED': {
				this.#denied = true
				const m = /"(.*)"/.exec(rest)
				this.emit('status', { kind: 'denied', reason: m ? m[1] : 'Stagewatch refused the connection' })
				return
			}
			case 'RECORD':
				if (rest.length > 0) this.emit('record', rest)
				return
			case 'ERROR':
				this.emit('protocolError', rest)
				return
			case 'PONG': {
				// "PONG <token> STUCK": Stagewatch answers but its show has stopped turning over.
				if (!this.#ready) return
				const stuck = /\bSTUCK\b/.test(rest)
				if (stuck !== this.#stuck) {
					this.#stuck = stuck
					this.emit('status', stuck ? { kind: 'stuck' } : { kind: 'connected' })
				}
				return
			}
			default:
				// READY, legacy v1 lines: nothing to do (any line already refreshed the heartbeat).
				return
		}
	}

	#parse<T>(json: string): T | null {
		try {
			return JSON.parse(json) as T
		} catch {
			this.emit('protocolError', 'Malformed JSON from Stagewatch')
			return null
		}
	}

	#startHeartbeat(): void {
		this.#stopHeartbeat()
		this.#pingTimer = setInterval(() => {
			const s = this.#socket
			if (!s) return
			if (Date.now() - this.#lastRx > this.#t.deadAfterMs) {
				this.emit('protocolError', `No response from Stagewatch for ${Math.round(this.#t.deadAfterMs / 1000)} s — reconnecting`)
				s.destroy()
				return
			}
			s.write(`PING ${++this.#pingSeq}\n`)
		}, this.#t.pingEveryMs)
	}

	#stopHeartbeat(): void {
		if (this.#pingTimer) clearInterval(this.#pingTimer)
		this.#pingTimer = null
	}

	#scheduleReconnect(delayOverride?: number): void {
		if (this.#destroyed || this.#reconnectTimer) return
		const delay = delayOverride ?? this.#backoffMs
		this.#backoffMs = Math.min(this.#backoffMs * 2, this.#t.maxBackoffMs)
		this.#reconnectTimer = setTimeout(() => {
			this.#reconnectTimer = null
			this.#dial()
		}, delay)
	}

	/** Forgets, without a word, every line sent more than FINAL_WAIT_MS ago: no answer to it can still come. */
	#expire(now: number): void {
		for (const [id, p] of this.#pending) if (now - p.at > FINAL_WAIT_MS) this.#pending.delete(id)
	}

	/**
	 * The socket is gone, and with it any answer still to come on it: each line still waiting — within FINAL_WAIT_MS of its send — is
	 * one `lost` receipt; an older one is forgotten without a word.
	 */
	#forgetPending(): void {
		if (this.#pending.size === 0) return
		this.#expire(this.#now())
		const waiting = [...this.#pending]
		this.#pending.clear()
		if (this.#destroyed) return
		for (const [id, p] of waiting)
			this.emit('receipt', { id, line: p.line, kind: 'lost', ok: false, stage: '', final: false, words: '', wasAccepted: p.accepted })
	}

	#clearTimers(): void {
		this.#stopHeartbeat()
		if (this.#reconnectTimer) clearTimeout(this.#reconnectTimer)
		this.#reconnectTimer = null
	}

	#teardown(): void {
		const s = this.#socket
		this.#socket = null
		this.#ready = false
		this.#stuck = false
		this.#buffer = ''
		this.#forgetPending()
		if (s) {
			s.removeAllListeners()
			s.on('error', () => {})
			s.destroy()
		}
	}
}

/**
 * What a receipt says, after the line it answers: its stage (or "refused") and Stagewatch's words — the peer's own when the line
 * went on to one. A line whose answer was lost says so, and never that it ran or did not, nor that a final answer was owed.
 */
export function receiptWords(r: Receipt): string {
	if (r.kind === 'lost')
		return r.wasAccepted
			? 'accepted; the link to Stagewatch dropped before any final answer came (look at Stagewatch)'
			: 'no answer — the link to Stagewatch dropped first, so it may or may not have run (look at Stagewatch)'
	const head = (r.final ? 'final ' : '') + (r.ok ? r.stage || 'ok' : 'refused')
	return r.words ? `${head} — ${r.words}` : head
}

/**
 * What `playout_answer` shows for a receipt: its words when it answers the latest press (`latest`, the id that press went with, or
 * null when it was not sent), else the line it answers before them — an earlier press's late answer never reads as the latest's.
 */
export function answerShown(r: Receipt, latest: string | null): string {
	const words = receiptWords(r)
	return r.id === latest ? words : `${r.line}: ${words}`
}

/** Quote a string argument for the Stagewatch line protocol. */
export function quote(text: string): string {
	return '"' + text.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\r?\n/g, '\\n') + '"'
}
