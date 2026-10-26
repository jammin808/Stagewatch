#!/usr/bin/env bash
# Show Bus copies (docs/SHOWBUS.md §14.5): holds each tree the master's Landed line names to this master, and names each
# stale copy. A tree's body is its docs/SHOWBUS.md from the line '## 1. Rules every node follows' to the end, CRLF read as
# LF. A copy holds the master when its body hashes to the master's, its header's Body line prints that hash, and its scripted
# peer (tools/showbus/peerlink-peer.mjs, byte for byte) hashes to §14's peerlink.peer.sha256. The master is this tree, and
# its own header and §14 are held to its body and its peer first. A tree the Landed line does not name is not compared: its
# copy is held by its own tree's test until the master's release names it.
#   tools/showbus-copies.sh                    the landed trees at $SW_PATTERNS_DIR, $SW_MAPPER_DIR and $SW_AGMESSAGE_DIR,
#                                              by default ../Patterns, ../Mapper and ../AGMessage beside this tree
#   tools/showbus-copies.sh Patterns=<dir>     a tree at a folder of any name
#   tools/showbus-copies.sh <dir> …            a tree in a folder named for it (../Patterns, /src/Mapper)
# Exit 0 when the master holds and so does every landed tree found (an absent one is "not run"); 1 when the master or a
# landed copy does not hold, or a landed tree is absent with SW_INTEROP_REQUIRED=1; 2 for a word it does not take, and then
# nothing is compared. It needs bash 4 or later and GNU sed, grep and coreutils (sha256sum): Linux, or Git Bash on Windows.
set -uo pipefail
export LC_ALL=C

names=(Stagewatch Patterns Mapper AGMessage)
copies=(Patterns Mapper AGMessage)
start='## 1. Rules every node follows'
start_re='^## 1\. Rules every node follows$'
peer=tools/showbus/peerlink-peer.mjs
caller=$(pwd)

usage() { sed -n '2,/^set /p' "$0" | sed '$d' | sed 's/^# \{0,1\}//'; }
refuse() { echo "showbus copies: $1 — nothing compared" >&2; exit 2; }
is_copy() { local n; for n in "${copies[@]}"; do [ "$n" = "$1" ] && return 0; done; return 1; }
absolute() { case "$1" in /*) printf '%s' "$1" ;; *) printf '%s/%s' "$caller" "$1" ;; esac; }
[ "${BASH_VERSINFO[0]}" -ge 4 ] || refuse "this is bash $BASH_VERSION; it needs bash 4 or later"
command -v sha256sum >/dev/null || refuse "no sha256sum (GNU coreutils) on PATH"

declare -A dir=()
for word in "$@"; do
  case "$word" in
    -h|--help) usage; exit 0 ;;
    *=*) name=${word%%=*}; at=${word#*=} ;;
    *) at=$word; name=${word%/}; name=${name##*/} ;;
  esac
  [ "$name" = Stagewatch ] && refuse "Stagewatch is the master, this tree: '$word'"
  is_copy "$name" || refuse "not a tree's word: '$word' (Patterns=<dir>, Mapper=<dir>, AGMessage=<dir>, or a folder named for one)"
  [ -n "$at" ] || refuse "no folder for $name: '$word'"
  [ -z "${dir[$name]:-}" ] || refuse "$name is named twice"
  dir[$name]=$(absolute "$at")
done
for name in "${copies[@]}"; do
  if [ -z "${dir[$name]:-}" ]; then
    var="SW_$(printf '%s' "$name" | tr '[:lower:]' '[:upper:]')_DIR"
    if [ -n "${!var:-}" ]; then dir[$name]=$(absolute "${!var}"); fi
  fi
done

cd "$(dirname "$0")/.." || exit 1
root=$(pwd)
for name in "${copies[@]}"; do [ -n "${dir[$name]:-}" ] || dir[$name]="$(dirname "$root")/$name"; done

sha() { sha256sum | cut -d' ' -f1; }
lf() { sed 's/\r$//' "$1"; }
# How many times the body's first line stands alone in a file.
starts() { lf "$1" | grep -c -x -F -e "$start"; }
body() { lf "$1" | sed -n "/$start_re/,\$p"; }
header() { lf "$1" | sed -n "1,/$start_re/p"; }
# The one value a pattern's line gives in a text on stdin, or nothing when the line is not there exactly once.
one() { local found; found=$(sed -n -E "s/$1/\\1/p"); [ -n "$found" ] && [ "$(printf '%s\n' "$found" | wc -l)" -eq 1 ] && printf '%s' "$found"; }
printed() { header "$1" | one '^\*Body SHA-256 \(§14\): ([0-9a-f]{64})\.\*$'; }
pinned() { body "$1" | one '^peerlink\.peer\.sha256 ([0-9a-f]{64})$'; }
body_form=" (§14.5: once, on a line of its own, '*Body SHA-256 (§14): <hash>.*', the hash 64 lowercase hex)"

failed=()
master=$root/docs/SHOWBUS.md
[ -f "$master" ] || { echo "showbus copies: no docs/SHOWBUS.md in $root" >&2; exit 1; }
[ "$(starts "$master")" = 1 ] || { echo "showbus copies: the master has no body: its line '$start' is not there once" >&2; exit 1; }
want_body=$(body "$master" | sha)
want_peer=$(pinned "$master")
echo "showbus copies: the master, $master"
echo "  body $want_body"
echo "  peer ${want_peer:-(none: §14 pins no peerlink.peer.sha256)}"
said=$(printed "$master")
if [ "$said" != "$want_body" ]; then
  failed+=(Stagewatch)
  echo "  Stagewatch  the master does not hold: its header's Body line prints ${said:-nothing$body_form}, its body hashes to $want_body"
fi
if [ ! -f "$root/$peer" ]; then
  failed+=(Stagewatch)
  echo "  Stagewatch  the master does not hold: no scripted peer at $peer"
elif [ -z "$want_peer" ] || [ "$(sha < "$root/$peer")" != "$want_peer" ]; then
  failed+=(Stagewatch)
  echo "  Stagewatch  the master does not hold: its scripted peer hashes to $(sha < "$root/$peer"), §14 pins ${want_peer:-nothing}"
fi

landed_line=$(header "$master" | one '^\*Landed \(§14\): (.+)\.\*$')
landed=()
if [ -z "$landed_line" ]; then
  failed+=(Stagewatch)
  echo "  Stagewatch  the master does not hold: its header has no one line '*Landed (§14): <trees>.*'"
else
  IFS=$'\n' read -r -d '' -a landed < <(printf '%s' "$landed_line" | sed 's/, /\n/g'; printf '\0')
  seen=" "
  for name in "${landed[@]}"; do
    known=0; for n in "${names[@]}"; do [ "$n" = "$name" ] && known=1; done
    if [ $known = 0 ] || [[ "$seen" == *" $name "* ]]; then
      failed+=(Stagewatch)
      echo "  Stagewatch  the master does not hold: its Landed line names '$name' (each of ${names[*]} at most once)"
    fi
    seen+="$name "
  done
  if [ "${landed[0]:-}" != Stagewatch ]; then
    failed+=(Stagewatch)
    echo "  Stagewatch  the master does not hold: its Landed line names Stagewatch first ($landed_line)"
  fi
fi
[[ " ${failed[*]} " == *" Stagewatch "* ]] || echo "  Stagewatch  the master: its header's Body line prints its body's hash, and §14 pins its peer's"

is_landed() { local n; for n in "${landed[@]}"; do [ "$n" = "$1" ] && return 0; done; return 1; }
for name in "${copies[@]}"; do
  if ! is_landed "$name"; then
    printf '  %-11s not landed: not compared; its copy is held by its own tree'"'"'s test until the Landed line names it\n' "$name"
    continue
  fi
  at=${dir[$name]}
  if [ ! -d "$at" ]; then
    if [ "${SW_INTEROP_REQUIRED:-}" = 1 ]; then
      failed+=("$name")
      printf '  %-11s not run: no tree at %s, and SW_INTEROP_REQUIRED=1 asks for every landed tree\n' "$name" "$at"
    else
      printf '  %-11s not run: no tree at %s\n' "$name" "$at"
    fi
    continue
  fi
  why=()
  copy=$at/docs/SHOWBUS.md
  if [ ! -f "$copy" ]; then
    why+=("no docs/SHOWBUS.md")
  elif [ "$(starts "$copy")" != 1 ]; then
    why+=("no body: its line '$start' is not there once")
  else
    got=$(body "$copy" | sha)
    [ "$got" = "$want_body" ] || why+=("its body hashes to $got")
    said=$(printed "$copy")
    [ "$said" = "$want_body" ] || why+=("its header's Body line prints ${said:-nothing$body_form}")
  fi
  if [ ! -f "$at/$peer" ]; then
    why+=("no scripted peer at $peer")
  else
    got=$(sha < "$at/$peer")
    [ "$got" = "$want_peer" ] || why+=("its scripted peer hashes to $got")
  fi
  if [ ${#why[@]} -eq 0 ]; then
    printf '  %-11s holds the master (%s)\n' "$name" "$at"
  else
    failed+=("$name")
    joined=$(printf '%s; ' "${why[@]}")
    printf '  %-11s stale (%s): %s\n' "$name" "$at" "${joined%; }"
  fi
done

if [ ${#failed[@]} -gt 0 ]; then
  echo "showbus copies: does not hold: $(printf '%s\n' "${failed[@]}" | awk '!seen[$0]++' | paste -sd' ')"
  exit 1
fi
echo "showbus copies: every landed tree found holds the master (landed: ${landed_line})"
