#!/bin/bash
# Patterns' App suite as N foreground parts, each short enough for one command: the suite's classes sorted by name,
# class i in part (i mod N), so the parts name every test exactly once and part k of N is parts k and k+N of 2N.
#   app-parts.sh <out-dir> <k> <n>   run part k (0-based) of n with --no-build; ends with "PART k/n exit=E total=T failed=F"
#   app-parts.sh <out-dir> sum <n>   add the parts up and hold the sum to the suite's listed count
# A part that prints exit=124 ran out of time: run it as its two halves (k and k+n of 2n) and sum those in its place.
set -uo pipefail
cd "$(dirname "$0")/.." || exit 2
out="$1"; what="$2"; n="$3"
proj=tests/Patterns.App.Tests
mkdir -p "$out"
if [ ! -s "$out/tests.txt" ]; then
  dotnet test "$proj" -c Debug --no-build --list-tests > "$out/list.log" 2>&1 || { echo "listing the tests failed: $out/list.log"; exit 2; }
  sed -n '/^The following Tests are available:/,$p' "$out/list.log" | sed 1d | sed 's/^ *//' | grep -v '^$' > "$out/tests.txt"
  # A test's class is its name before the last dot of the part ahead of any theory arguments; a nested class counts as its outer.
  sed 's/(.*$//' "$out/tests.txt" | sed 's/\.[^.]*$//' | sed 's/+.*$//' | LC_ALL=C sort -u > "$out/classes.txt"
fi
listed=$(wc -l < "$out/tests.txt")
if [ "$what" = sum ]; then
  total=0; failed=0; bad=0
  for k in $(seq 0 $((n - 1))); do
    line=$(grep -h "^PART $k/$n " "$out/part-$k-of-$n.log" 2>/dev/null | tail -n 1)
    [ -z "$line" ] && { echo "part $k/$n has no result"; bad=1; continue; }
    echo "$line"
    case "$line" in *"exit=0 "*) ;; *) bad=1 ;; esac
    t=${line##*total=}; t=${t%% *}; f=${line##*failed=}; f=${f%% *}
    total=$((total + t)); failed=$((failed + f))
  done
  echo "SUM parts=$n total=$total failed=$failed listed=$listed"
  [ "$bad" = 0 ] && [ "$failed" = 0 ] && [ "$total" = "$listed" ] && { echo "APP SUITE GREEN BY PARTS"; exit 0; }
  echo "APP SUITE NOT GREEN BY PARTS"; exit 1
fi
k="$what"
filter=$(awk -v k="$k" -v n="$n" '((NR - 1) % n) == k { printf "%sFullyQualifiedName~%s.", sep, $0; sep = "|" }' "$out/classes.txt")
[ -z "$filter" ] && { echo "PART $k/$n exit=0 total=0 failed=0" | tee "$out/part-$k-of-$n.log"; exit 0; }
log="$out/part-$k-of-$n.log"
start=$(date +%s)
timeout 570 dotnet test "$proj" -c Debug --no-build --filter "$filter" > "$log" 2>&1
e=$?
line=$(grep -E "^(Passed|Failed)! " "$log" | tail -n 1)
t=$(echo "$line" | sed -n 's/.*Total: *\([0-9]*\).*/\1/p'); f=$(echo "$line" | sed -n 's/.*Failed: *\([0-9]*\).*/\1/p')
grep -E "^\s+Failed " "$log" | head -n 20
echo "PART $k/$n exit=$e total=${t:-0} failed=${f:-0} seconds=$(( $(date +%s) - start ))" | tee -a "$log"
exit "$e"
