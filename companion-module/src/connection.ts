// Resilient line-protocol client for the Stagewatch Companion server (TCP, default port 9923).
//
//  * Reconnects forever with capped exponential backoff (1 s → 10 s), resets on success.
//  * Heartbeat: PING every 4 s; if nothing at all arrives for 12 s the socket is presumed
//    dead (cable pulled, laptop asleep, Wi-Fi roam) and is torn down and redialled.
//  * Commands are never queued while offline: replaying stale "START"/"NEXT" presses after a
//    reconnect would be dangerous mid-show. A press while offline is dropped and logged.
//  * Inbound data is reassembled into lines with a hard length cap.

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

export interface ConnectionEvents {
	status: [LinkStatus]
	snapshot: [Snapshot]
	config: [ShowConfig]
	/** An operator action in Stagewatch, as a protocol line (for Companion's action recorder). */
	record: [string]
	protocolError: [string]
}

const MAX_LINE = 1024 * 1024

export interface ConnectionTimings {
	pingEveryMs: number
	deadAfterMs: number
	connectTimeoutMs: number
	deniedRetryMs: number
	minBackoffMs: number
	maxBackoffMs: number
}

export const DEFAULT_TIMINGS: ConnectionTimings = {
	pingEveryMs: 4000,
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

	constructor(
		readonly host: string,
		readonly port: number,
		timings: Partial<ConnectionTimings> = {},
	) {
		super()
		this.#t = { ...DEFAULT_TIMINGS, ...timings }
		this.#backoffMs = this.#t.minBackoffMs
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
		if (s) {
			s.removeAllListeners()
			s.on('error', () => {})
			s.destroy()
		}
	}
}

/** Quote a string argument for the Stagewatch line protocol. */
export function quote(text: string): string {
	return '"' + text.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\r?\n/g, '\\n') + '"'
}
