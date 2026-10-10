import { Regex, type SomeCompanionConfigField } from '@companion-module/base'

export type ModuleConfig = {
	host: string
	port: number
	/** Stagewatch's web port (browser pages), for the /producer shortcut. */
	webPort: number
}

export const DEFAULT_PORT = 9923

export function GetConfigFields(): SomeCompanionConfigField[] {
	return [
		{
			type: 'static-text',
			id: 'info',
			width: 12,
			label: 'Stagewatch',
			value:
				'Connects to the Stagewatch timer (Settings → Companion in Stagewatch). Use 127.0.0.1 when Companion runs on the same PC; ' +
				'for another PC, tick "Bind all interfaces" in Stagewatch and enter its IP address.',
		},
		{
			type: 'textinput',
			id: 'host',
			label: 'Stagewatch PC address',
			width: 8,
			default: '127.0.0.1',
			regex: Regex.HOSTNAME,
		},
		{
			type: 'number',
			id: 'port',
			label: 'Port',
			width: 4,
			min: 1,
			max: 65535,
			default: DEFAULT_PORT,
			asInteger: true,
			clampValues: true,
		},
		{
			type: 'number',
			id: 'webPort',
			label: 'Stagewatch web port (for the Showcaller page link)',
			width: 6,
			min: 1,
			max: 65535,
			default: 9924,
			asInteger: true,
			clampValues: true,
		},
	]
}
