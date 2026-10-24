#!/usr/bin/env bash
# A Stagewatch build and test machine off Windows (Ubuntu 24.04, run as root; safe to run again): the .NET 10 SDK, the
# Node packages of the Companion module, the 3D display and the browser workflows, and a headless Chromium for them
# (Playwright's headless shell: the one in PLAYWRIGHT_BROWSERS_PATH when the machine has it, else downloaded with its
# system libraries). WPF, WMI, COM and PowerPoint are Windows-only: their tests run on the Windows job, and the suite
# here names the checks it leaves to it (tests/Stagewatch.Tests/Bench.cs).
#   tools/setup-dev.sh          install
#   tools/setup-dev.sh start    install, then print the variables the lanes read (eval "$(tools/setup-dev.sh start)")
# Then: tools/test.sh (every lane) or tools/test.sh <lane…>.
set -euo pipefail
cd "$(dirname "$0")/.."

PLAYWRIGHT_VERSION=1.63.0

have_shell() {
  local root="${PLAYWRIGHT_BROWSERS_PATH:-$HOME/.cache/ms-playwright}"
  compgen -G "$root/chromium_headless_shell-*/chrome-headless-shell-linux64/chrome-headless-shell" >/dev/null
}

install() {
  export DEBIAN_FRONTEND=noninteractive
  if ! command -v dotnet >/dev/null || ! dotnet --list-sdks | grep -q '^10\.'; then
    apt-get update -qq
    apt-get install -y -qq --no-install-recommends dotnet-sdk-10.0 fonts-dejavu-core curl ca-certificates
  fi
  if ! command -v node >/dev/null || [ "$(node -p 'process.versions.node.split(".")[0]')" -lt 22 ]; then
    echo "Node 22 or later is needed (the Companion module's engines field): install it, then run this again." >&2
    exit 1
  fi
  npm ci --no-audit --no-fund --prefix companion-module >/dev/null
  npm ci --no-audit --no-fund --prefix src/Timer.Remote/holo >/dev/null
  # The browser workflows' Playwright, pinned; its browser is the machine's own when there is one.
  PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 npm ci --no-audit --no-fund --prefix tests/e2e >/dev/null
  if ! have_shell; then
    (cd tests/e2e && npx --yes "playwright@$PLAYWRIGHT_VERSION" install --with-deps --only-shell chromium)
  fi
  dotnet restore Timer.slnf >/dev/null
}

install
if [ "${1:-}" = "start" ]; then
  root="${PLAYWRIGHT_BROWSERS_PATH:-$HOME/.cache/ms-playwright}"
  shell=$(compgen -G "$root/chromium_headless_shell-*/chrome-headless-shell-linux64/chrome-headless-shell" | sort | tail -1)
  echo "export PLAYWRIGHT_BROWSERS_PATH=$root STAGEWATCH_BROWSER=$shell DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1"
fi
