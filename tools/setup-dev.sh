#!/usr/bin/env bash
# A Stagewatch build and test machine off Windows (Ubuntu 24.04, run as root; safe to run again): the .NET 10 SDK, the
# Node packages of the Companion module, the 3D display and the browser workflows, and a headless Chromium for them
# (Playwright's headless shell: the one in PLAYWRIGHT_BROWSERS_PATH when the machine has it, else downloaded with its
# system libraries). WPF, WMI, COM and PowerPoint are Windows-only: their tests run on the Windows job, and the suite
# here names the checks it leaves to it (tests/Stagewatch.Tests/Bench.cs). The interop lane's needs are checked too, and a .NET
# SDK a pinned sibling's global.json names that this machine lacks is said, with the SDK to install.
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
  # The interop lane (tools/test.sh interop): Python 3.12 or later for tools/interop/lan.py (os.unshare, os.setns),
  # util-linux's unshare and nsenter, mount (the run's secrets folder is a tmpfs of its own), and its driver's packages,
  # installed with no install script run.
  if ! command -v python3 >/dev/null || ! command -v unshare >/dev/null || ! command -v nsenter >/dev/null \
      || ! command -v mount >/dev/null; then
    apt-get update -qq
    apt-get install -y -qq --no-install-recommends python3 util-linux mount
  fi
  if ! python3 -c 'import sys; sys.exit(0 if sys.version_info >= (3, 12) else 1)'; then
    echo "The interop lane needs Python 3.12 or later (tools/interop/lan.py): install it, then run this again." >&2
    exit 1
  fi
  npm ci --ignore-scripts --no-audit --no-fund --prefix tests/interop >/dev/null
  sibling_sdks
  dotnet restore Timer.slnf >/dev/null
}

# The interop lane builds a sibling that pins its .NET SDK by global.json (Patterns does) with that SDK only, which apt's
# dotnet-sdk-10.0 (one feature band) may not be: each pinned sibling's global.json, read at its pin from its repository beside
# this one, is checked against this machine's SDKs, and one missing is said with the SDK to install beside the others (the lane
# says it too, and is not run without it). Nothing is installed for it here.
sibling_sdks() {
  local app repo ref build dir tmp said
  while read -r app repo ref build; do
    case $app in
      patterns) dir=${SW_PATTERNS_DIR:-../Patterns} ;;
      mapper) dir=${SW_MAPPER_DIR:-../Mapper} ;;
      *) continue ;;
    esac
    if ! git -C "$dir" rev-parse --git-dir >/dev/null 2>&1; then
      echo "The interop lane at its pins: no $app repository at $dir; its .NET SDK is not checked here (the lane checks it in its clone)" >&2
      continue
    fi
    if ! git -C "$dir" cat-file -e "$ref^{commit}" 2>/dev/null; then
      echo "The interop lane at its pins: $app: its pin $ref is not fetched in $dir; its .NET SDK is not checked here (the lane checks it in its clone)" >&2
      continue
    fi
    git -C "$dir" cat-file -e "$ref:global.json" 2>/dev/null || continue
    tmp=$(mktemp -d)
    git -C "$dir" show "$ref:global.json" > "$tmp/global.json"
    said=$(tools/interop/sdk.sh "$app" "$tmp") || echo "The interop lane at its pins: $said" >&2
    rm -rf "$tmp"
  done < <(python3 -I tools/interop/pins.py tools/interop/siblings.json 2>/dev/null)
}

install
if [ "${1:-}" = "start" ]; then
  root="${PLAYWRIGHT_BROWSERS_PATH:-$HOME/.cache/ms-playwright}"
  shell=$(compgen -G "$root/chromium_headless_shell-*/chrome-headless-shell-linux64/chrome-headless-shell" | sort | tail -1)
  echo "export PLAYWRIGHT_BROWSERS_PATH=$root STAGEWATCH_BROWSER=$shell DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1"
fi
