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
#   interop  not a default lane: the rig, Patterns' bench host, PlayMap's node and AGMessage at the commits
#            tools/interop/siblings.json pins, built in clones of the lane's own (tools/interop/build.sh), run on each
#            topology in turn, each in a user, network and PID namespace of its own that ends with its run
#            (tools/interop/run.sh): lan (each app a host of its own on one switch) and laptop (the four on one host with
#            its Wi-Fi up); SW_INTEROP_TOPOLOGY=lan or laptop runs one. Each topology's matrix goes to
#            artifacts/interop/<topology>. Where a sibling's repository is absent, a pin or the Node AGMessage needs cannot
#            be fetched, or the kernel gives no such namespaces, the lane is "not run", never passed, and
#            SW_INTEROP_REQUIRED=1 makes that a failure. SW_INTEROP_HEADS=1 runs each sibling at its repository's HEAD
#            instead of its pin (CI's nightly drift report); tools/interop/summary.py prints what a run built and its rows,
#            and tools/interop/publish.py copies what may leave the machine (CI's artifact). The lane's own scripts are
#            checked first (tools/interop/selfcheck.py: pins.py, build.sh, summary.py and publish.py on fixtures of their
#            own), and the lane does not run when they fail
# First time on a machine: tools/setup-dev.sh. The Windows-only lanes (UI smoke, the installer) are tools/smoke.ps1 and
# tools/release.ps1 on the Windows job.
set -uo pipefail
cd "$(dirname "$0")/.."
root=$(pwd)
lanes=("$@")
[ ${#lanes[@]} -eq 0 ] && lanes=(build unit module holo e2e)
failed=()
notrun=()
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
if [ -z "${PLAYWRIGHT_BROWSERS_PATH:-}" ] && [ -d /opt/pw-browsers ]; then export PLAYWRIGHT_BROWSERS_PATH=/opt/pw-browsers; fi

lane() { printf '\n\033[36m==> %s\033[0m\n' "$1"; }
built=0
build() {
  [ $built -eq 1 ] && return 0
  lane build
  # One build at a time: the lanes after it run what it built (--no-build). No MSBuild node outlives a build
  # (-nodeReuse:false), so none is left idle after the lanes or taken up by a build in another tree.
  dotnet build Timer.slnf -c Debug -v q -nologo -nodeReuse:false &&
    dotnet build tests/Stagewatch.PageShot -c Debug -v q -nologo -nodeReuse:false &&
    dotnet build tools/Stagewatch.Rig -c Debug -v q -nologo -nodeReuse:false &&
    dotnet build tools/BusCarrierProbe -c Debug -v q -nologo -nodeReuse:false && built=1
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
    interop)
      build || { failed+=(build); continue; }
      lane interop
      python3 -I tools/interop/selfcheck.py || { failed+=("interop (self-check)"); continue; }
      case "${SW_INTEROP_TOPOLOGY:-all}" in
        all) topologies=(lan laptop) ;;
        lan|laptop) topologies=("$SW_INTEROP_TOPOLOGY") ;;
        *) echo "interop: SW_INTEROP_TOPOLOGY is lan, laptop or all (given: \"$SW_INTEROP_TOPOLOGY\"); offline has no rows yet" >&2
           failed+=(interop); continue ;;
      esac
      tools/interop/build.sh
      rc=$?
      if [ $rc -eq 0 ] && ! unshare --user --map-root-user --net --pid --fork --mount-proc --kill-child true 2>/dev/null; then
        echo "interop: not run (this machine gives no user, network and PID namespaces to its user)" >&2
        rc=3
      fi
      if [ $rc -eq 0 ]; then
        # One topology at a time, each in namespaces of its own (lan.py builds only in a fresh one). In the foreground, so
        # Ctrl+C reaches the run's processes. unshare holds off SIGTERM while it waits, so the end of the 20 minutes is its
        # SIGKILL 10 s later, and --kill-child then ends run.sh, the PID namespace's first process, and with it every process
        # of the run. A failed topology fails the lane; one not run leaves it not run unless another failed.
        for topology in "${topologies[@]}"; do
          printf '\n\033[36m==> interop: %s\033[0m\n' "$topology"
          timeout --foreground -k 10 1200 unshare --user --map-root-user --net --pid --fork --mount-proc --kill-child \
            tools/interop/run.sh "$topology"
          one=$?
          if [ $one -ne 0 ] && [ $one -ne 3 ]; then rc=1; elif [ $one -eq 3 ] && [ $rc -eq 0 ]; then rc=3; fi
        done
      fi
      case $rc in
        0) ;;
        3) if [ "${SW_INTEROP_REQUIRED:-}" = 1 ]; then failed+=("interop (not run)"); else notrun+=(interop); fi ;;
        *) failed+=(interop) ;;
      esac ;;
    rig)
      build || { failed+=(build); continue; }
      lane rig
      read -ra rig_args <<< "${SW_RIG_ARGS:-}"
      dotnet tools/Stagewatch.Rig/bin/Debug/net10.0-windows/Stagewatch.Rig.dll --state "$root/artifacts/rig-windows.json" \
        ${rig_args[@]+"${rig_args[@]}"} ;;
    *) echo "unknown lane: $l (build unit module holo e2e pageshot rig interop)" >&2; failed+=("$l") ;;
  esac
done

if [ ${#failed[@]} -gt 0 ]; then
  printf '\n\033[31mFailed: %s\033[0m\n' "${failed[*]}"
  exit 1
fi
if [ ${#notrun[@]} -gt 0 ]; then
  passed=()
  for l in "${lanes[@]}"; do [[ " ${notrun[*]} " == *" $l "* ]] || passed+=("$l"); done
  printf '\n\033[33mNot run: %s (why is said above); passed: %s\033[0m\n' "${notrun[*]}" "${passed[*]:-none}"
  exit 0
fi
printf '\n\033[32mAll lanes passed: %s\033[0m\n' "${lanes[*]}"
