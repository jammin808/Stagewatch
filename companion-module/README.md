# companion-module-stagewatch

Bitfocus Companion module for the **Stagewatch** stage timer. Requires Companion 5.0+ (module API 2.1).

- User help: [companion/HELP.md](companion/HELP.md)
- Protocol and architecture: [../docs/COMPANION.md](../docs/COMPANION.md)

```pwsh
npm install
npm test              # tsc build + node:test (state layer + scripted fake server)
npm run package       # stagewatch-<version>.tgz for Companion → Modules → Import
npx companion-module-check
```
