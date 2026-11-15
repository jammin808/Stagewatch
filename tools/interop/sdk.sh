#!/usr/bin/env bash
# Whether this machine's dotnet has the .NET SDK a sibling's global.json leads it to. A sibling that pins its SDK so (Patterns
# does) builds only with that SDK: dotnet refuses a machine whose SDKs are all of another feature band. dotnet is asked first, in
# the folder that holds the global.json, as its build reads it, so its own rules (the version or none, rollForward,
# allowPrerelease, comments in the file) decide; the file is read here only to word a refusal with the SDK to install.
# Stagewatch has no global.json and is not asked: it builds with the newest .NET 10 SDK the machine has.
#   tools/interop/sdk.sh <app> <folder>     whether dotnet resolves an SDK in the folder
#   tools/interop/sdk.sh --named <folder>   the SDK version the folder's global.json names, or nothing (no file, or one naming
#                                           none: dotnet then takes the newest SDK); for a runner that installs it first
# Exit codes: 0 the folder holds no global.json, or dotnet resolves an SDK there (its version on standard output), or --named's
# answer; 1 dotnet resolves none there (the words on standard output: the SDK to install and what this machine has); 2 usage.
set -uo pipefail

# The SDK version a global.json names, read as dotnet reads the file (// and /* */ comments and trailing commas allowed), with
# " (rollForward X)" after it when it says one; nothing when it names none or does not read (dotnet's words then say why).
named() {
  python3 -I - "$1" <<'PY'
import json, re, sys

def plain(text):
    """The JSON with its comments and trailing commas left out, strings kept as they are."""
    out, i, n = [], 0, len(text)
    while i < n:
        ch = text[i]
        if ch == '"':
            j = i + 1
            while j < n and text[j] != '"':
                j += 2 if text[j] == '\\' else 1
            out.append(text[i:j + 1]); i = j + 1
        elif text.startswith('//', i):
            j = text.find('\n', i); i = n if j < 0 else j
        elif text.startswith('/*', i):
            j = text.find('*/', i + 2); i = n if j < 0 else j + 2
        elif ch == ',' and re.match(r'\s*(//[^\n]*\s*|/\*.*?\*/\s*)*[}\]]', text[i + 1:], re.S):
            i += 1
        else:
            out.append(ch); i += 1
    return ''.join(out)

try:
    with open(sys.argv[1], encoding='utf-8-sig') as f:
        doc = json.loads(plain(f.read()))
except (OSError, ValueError):
    sys.exit(0)
sdk = doc.get('sdk') if isinstance(doc, dict) else None
sdk = sdk if isinstance(sdk, dict) else {}
v = sdk.get('version')
if isinstance(v, str) and re.fullmatch(r'\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?', v):
    roll = sdk.get('rollForward')
    print(v + (f' (rollForward {roll})' if isinstance(roll, str) and roll else ''))
PY
}

if [ "${1:-}" = --named ]; then
  [ $# = 2 ] || { echo "usage: tools/interop/sdk.sh --named <folder>" >&2; exit 2; }
  [ -f "$2/global.json" ] || exit 0
  want=$(named "$2/global.json")
  echo "${want%% *}"
  exit 0
fi
[ $# = 2 ] || { echo "usage: tools/interop/sdk.sh <app> <folder> | --named <folder>" >&2; exit 2; }
app=$1
dir=$2
[ -f "$dir/global.json" ] || exit 0
if have=$(cd "$dir" && dotnet --version 2>/dev/null); then
  echo "$have"
  exit 0
fi
sdks=$(dotnet --list-sdks 2>/dev/null | cut -d' ' -f1 | paste -sd, - | sed 's/,/, /g')
want=$(named "$dir/global.json")
if [ -n "$want" ]; then
  echo "$app: its global.json names the .NET SDK $want, which this machine's dotnet does not have (it has: ${sdks:-none}); install that SDK beside the others (dotnet-install.sh --version ${want%% *} into the dotnet folder; docs/BUILD.md, the interop lane)"
else
  why=$(cd "$dir" && dotnet --version 2>&1 | grep -v '^[[:space:]]*$' | head -n 1)
  echo "$app: dotnet resolves no .NET SDK from its global.json, which names no version this lane reads (it has: ${sdks:-none}; dotnet: ${why:-no words})"
fi
exit 1
