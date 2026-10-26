#!/usr/bin/env bash
# The interop lane inside its namespaces. tools/test.sh interop starts it as
#   unshare --user --map-root-user --net --pid --fork --mount-proc --kill-child tools/interop/run.sh
# so it is the first process of a PID namespace of its own: every process the run starts (the LAN's holders, the four apps,
# the driver) ends when it does, a crashed driver's included, and as that namespace's first process it reaps every orphan.
# Its network namespace has no way to the machine's network or to another session's sockets, and its user is that user
# namespace's root, the invoking user outside it. It asks lan.py whether this kernel gives a namespace a bridge and a veth,
# makes the run's folder afresh ($SW_INTEROP_OUT/run, this user's alone) with its secrets folder a tmpfs of the run's own
# mount namespace (--mount-proc makes one), so no secret the run makes or learns is written to a disk and that folder, seen
# from outside the run, during it or after it (a killed run's included), is empty; then it runs the driver (tests/interop),
# which builds the LAN and starts the apps in it from what tools/interop/build.sh built.
# Exit codes: 0 every row passed; 1 a row failed; 2 not started as above; 3 not run (no bridge or veth), said on stderr.
set -uo pipefail
cd "$(dirname "$0")/../.."
if [ "$$" != 1 ]; then
  echo "interop: run.sh is the first process of a PID namespace of its own (tools/test.sh interop starts it so)" >&2
  exit 2
fi
# No proxy reaches into these namespaces, and an app or the driver that read one would send the LAN's own requests to it.
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy ALL_PROXY all_proxy NO_PROXY no_proxy
probe=$(python3 -I tools/interop/lan.py probe)
case $? in
  0) ;;
  3) echo "interop: not run ($(printf '%s' "$probe" | python3 -I -c 'import json,sys; print(json.load(sys.stdin)["error"])'))" >&2; exit 3 ;;
  *) echo "interop: lan.py could not probe this kernel: $probe" >&2; exit 2 ;;
esac
run="${SW_INTEROP_OUT:-artifacts/interop}/run"
mkdir -p "$(dirname "$run")" && rm -rf "$run" && mkdir -m 0700 "$run" "$run/secrets" ||
  { echo "interop: the run's folder $run could not be made afresh" >&2; exit 2; }
mount -n -t tmpfs -o mode=0700,size=4m,nosuid,nodev,noexec interop-secrets "$run/secrets" ||
  { echo "interop: the run's secrets folder could not be a tmpfs of its own, and no secret of the run goes to a disk" >&2; exit 2; }
node --test --test-concurrency=1 tests/interop/lan.interop.cjs
