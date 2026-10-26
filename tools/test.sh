#!/usr/bin/env bash
# Stagewatch's test lanes off Windows (the cloud bench, CI's Linux job). One build, then each lane in turn; the summary
# names every lane that failed and the exit code is non-zero if any did.
#   tools/test.sh                 every lane
#   tools/test.sh unit e2e        some lanes
# Lanes:
#   build    dotnet build: the solution (WPF compiles here too), PageShot, the rig and LB-1's carrier probe (run by hand:
#            dotnet run --project tools/BusCarrierProbe -c Debug --no-build)
#   unit     tests/Stagewatch.Tests (the suite names the checks it leaves to the Windows job)
#   module   the Companion module's tests (companion-module, npm test)
#   holo     the 3D display's tests (src/Timer.Remote/holo, npm test)
#   e2e      browser workflows against the rig: windows, pages, every transport (tests/e2e; screenshots to $SW_E2E_OUT)
#   pageshot PageShot's screenshots of the pages with the real server (artifacts/pages)
#   rig      not a test: start the rig in the foreground and print its addresses (Ctrl+C stops it); $SW_RIG_ARGS adds
#            options (SW_RIG_ARGS="--node-keys 2"); type "stall <ms>" to stop its show thread that long, as a stuck show
# First time on a machine: tools/setup-dev.sh. The Windows-only lanes (UI smoke, the installer) are tools/smoke.ps1 and
# tools/release.ps1 on the Windows job.
set -uo pipefail
cd "$(dirname "$0")/.."
root=$(pwd)
lanes=("$@")
[ ${#lanes[@]} -eq 0 ] && lanes=(build unit module holo e2e)
failed=()
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
if [ -z "${PLAYWRIGHT_BROWSERS_PATH:-}" ] && [ -d /opt/pw-browsers ]; then export PLAYWRIGHT_BROWSERS_PATH=/opt/pw-browsers; fi

lane() { printf '\n\033[36m==> %s\033[0m\n' "$1"; }
built=0
build() {
  [ $built -eq 1 ] && return 0
  lane build
  # One build at a time: the lanes after it run what it built (--no-build).
  dotnet build Timer.slnf -c Debug -v q -nologo &&
    dotnet build tests/Stagewatch.PageShot -c Debug -v q -nologo &&
    dotnet build tools/Stagewatch.Rig -c Debug -v q -nologo &&
    dotnet build tools/BusCarrierProbe -c Debug -v q -nologo && built=1
}

for l in "${lanes[@]}"; do
  case "$l" in
    build) build || failed+=(build) ;;
    unit)
      build || { failed+=(build); continue; }
      lane unit
      dotnet run --project tests/Stagewatch.Tests -c Debug --no-build || failed+=(unit) ;;
    module)
      lane module
      (cd companion-module && npm test) || failed+=(module) ;;
    holo)
      lane holo
      (cd src/Timer.Remote/holo && npm test) || failed+=(holo) ;;
    e2e)
      build || { failed+=(build); continue; }
      lane e2e
      (cd tests/e2e && npm test) || failed+=(e2e) ;;
    pageshot)
      build || { failed+=(build); continue; }
      lane pageshot
      dotnet run --project tests/Stagewatch.PageShot -c Debug --no-build -- "$root/artifacts/pages" || failed+=(pageshot) ;;
    rig)
      build || { failed+=(build); continue; }
      lane rig
      read -ra rig_args <<< "${SW_RIG_ARGS:-}"
      dotnet tools/Stagewatch.Rig/bin/Debug/net10.0-windows/Stagewatch.Rig.dll --state "$root/artifacts/rig-windows.json" \
        ${rig_args[@]+"${rig_args[@]}"} ;;
    *) echo "unknown lane: $l (build unit module holo e2e pageshot rig)" >&2; failed+=("$l") ;;
  esac
done

if [ ${#failed[@]} -gt 0 ]; then
  printf '\n\033[31mFailed: %s\033[0m\n' "${failed[*]}"
  exit 1
fi
printf '\n\033[32mAll lanes passed: %s\033[0m\n' "${lanes[*]}"
