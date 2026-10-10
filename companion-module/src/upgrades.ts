import type { CompanionStaticUpgradeScript } from '@companion-module/base'
import type { ModuleConfig } from './config.js'

// Never remove or reorder entries: Companion counts how many have already run.
export const UpgradeScripts: CompanionStaticUpgradeScript<ModuleConfig>[] = []
