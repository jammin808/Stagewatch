#!/usr/bin/env bash
# The interop lane on one topology, inside its namespaces. tools/test.sh interop starts it once per topology as
#   unshare --user --map-root-user --net --pid --fork --mount-proc --kill-child tools/interop/run.sh <topology>
# so it is the first process of a PID namespace of its own: every process the run starts (the topology's holders, the four
# apps, the driver) ends when it does, a crashed driver's included, and as that namespace's first process it reaps every
# orphan. Its network namespace is fresh, has no way to the machine's network or to another session's sockets, and is the
# topology's alone (lan.py builds in no other), and its user is that user namespace's root, the invoking user outside it.
# <topology> is lan (each app a host of its own on one switch: tests/interop/lan.interop.cjs) or laptop (the four apps on
# one host with its Wi-Fi up: tests/interop/laptop.interop.cjs). It asks lan.py whether this kernel gives a namespace a
# bridge and a veth, makes the topology's folder afresh ($SW_INTEROP_OUT/<topology>: its run folder, this user's alone, and
# its matrix) with the run's secrets folder a tmpfs of the run's own mount namespace (--mount-proc makes one), so no secret
# the run makes or learns is written to a disk and that folder, seen from outside the run, during it or after it (a killed
# run's included), is empty; then it runs the topology's driver, which builds the topology and starts the apps in it from
# what tools/interop/build.sh built.
# Exit codes: 0 every row passed; 1 a row failed; 2 not started as above; 3 not run (no bridge or veth), said on stderr.
set -uo pipefail
cd "$(dirname "$0")/../.."
if [ "$$" != 1 ]; then
  echo "interop: run.sh is the first process of a PID namespace of its own (tools/test.sh interop starts it so)" >&2
  exit 2
fi
topology="${1:-}"
case "$topology" in
  lan|laptop) ;;
  *) echo "interop: run.sh takes the topology, lan or laptop (given: \"$topology\")" >&2; exit 2 ;;
esac
# No proxy reaches into these namespaces, and an app or the driver that read one would send the LAN's own requests to it.
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy ALL_PROXY all_proxy NO_PROXY no_proxy
probe=$(python3 -I tools/interop/lan.py probe)
case $? in
  0) ;;
  3) echo "interop: not run ($(printf '%s' "$probe" | python3 -I -c 'import json,sys; print(json.load(sys.stdin)["error"])'))" >&2; exit 3 ;;
  *) echo "interop: lan.py could not probe this kernel: $probe" >&2; exit 2 ;;
esac
top="${SW_INTEROP_OUT:-artifacts/interop}/$topology"
run="$top/run"
rm -rf "$top" && mkdir -p "$top" && mkdir -m 0700 "$run" "$run/secrets" ||
  { echo "interop: the $topology run's folder $run could not be made afresh" >&2; exit 2; }
mount -n -t tmpfs -o mode=0700,size=4m,nosuid,nodev,noexec interop-secrets "$run/secrets" ||
  { echo "interop: the run's secrets folder could not be a tmpfs of its own, and no secret of the run goes to a disk" >&2; exit 2; }
SW_INTEROP_RUN_TOPOLOGY="$topology" node --test --test-concurrency=1 "tests/interop/$topology.interop.cjs"
