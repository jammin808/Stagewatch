#!/usr/bin/env bash
# The interop lane's builds: each sibling (Patterns' bench host, PlayMap's node host from the Mapper repository, AGMessage's
# server) at the commit tools/interop/siblings.json pins, in a clone the lane owns ($SW_INTEROP_OUT/trees/<app>: git clone
# --shared --no-checkout from the sibling's own repository, checked out detached), never in a sibling's working tree, and
# built one tree at a time; then $SW_INTEROP_OUT/builds.json says what was built and where, and the driver reads only that.
# Pins name pushed commits only: a pin the sibling's repository holds must be on one of its remote branches (read from its own
# refs, never fetched into it; a symbolic ref such as origin/HEAD names no branch of its own and is not read), and one it does
# not hold is fetched into the clone from the sibling's origin, never into the sibling, on every run, so that fetch is what
# says the origin holds it. A shallow sibling is copied over git's own transport instead of shared, which reads it and writes
# nothing to it either. Every wait on the network is bounded: a fetch of a pin by FETCH_SECONDS, the Node download below by
# its own connect and transfer limits.
# AGMessage runs on a Node its package's engines field accepts: the machine's node when it does, else the Node this script
# pins (NODE_FOR_AGMESSAGE, its SHA-256 checked) fetched once into $SW_INTEROP_OUT/node, never installed on the machine.
# Stagewatch's own rig is the build lane's (tools/test.sh build), named here by its commit.
# With SW_INTEROP_HEADS=1 each sibling is built at its repository's HEAD instead (CI's nightly heads run, a drift report): the
# commit that repository has checked out, never a working tree's uncommitted changes, and pushed: on one of that repository's
# remote branches (a head is always in its own repository, so it is never fetched; a head on none of them is refused);
# siblings.json still names each repository and what it builds, and builds.json says how far each head is from its pin.
#   SW_PATTERNS_DIR, SW_MAPPER_DIR, SW_AGMESSAGE_DIR   the siblings' repositories (defaults ../Patterns, ../Mapper, ../AGMessage)
#   SW_INTEROP_OUT     the lane's folder: clones, builds.json, and each topology's run and matrix (default artifacts/interop, which
#                      git ignores)
#   SW_INTEROP_AGMESSAGE_NODE   the node AGMessage runs on, as given (its version is still held to the engines field)
#   SW_INTEROP_HEADS   1: each sibling at its repository's HEAD; unset or 0: at its pin; anything else is refused
# Exit codes: 0 built; 1 a clone, a checkout or a build failed; 2 refused: siblings.json, a pin (or head) that is not pushed,
# a SW_INTEROP_HEADS it does not take, or a SW_INTEROP_AGMESSAGE_NODE its engines field does not accept; 3 not run (a sibling's
# repository absent or with no commit checked out, a pin it cannot fetch, or no Node for AGMessage's engines field that this
# machine has or can fetch). Each is said on standard error and in builds.json, which then holds the words in place of a build
# ({"v":1,"failed"|"refused"|"notRun":"<words>","siblings":"pins"|"heads","placed":{...}}, placed: each sibling's clone made so
# far, its commit, its pin and where it was seen pushed), so no earlier run's builds stay to be read as this run's.
set -uo pipefail
cd "$(dirname "$0")/../.."
root=$(pwd)
out=${SW_INTEROP_OUT:-$root/artifacts/interop}
mkdir -p "$out/trees"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
# The longest a fetch of one pin from a sibling's origin may take before the lane says it cannot be fetched.
FETCH_SECONDS=300

say() { printf '\033[36minterop build:\033[0m %s\n' "$*"; }
placed='{}'
# stop <code> <words>: the end short of a build, with its exit code (1 failed, 2 refused, 3 not run), said and in builds.json.
stop() {
  local how=notRun
  case $1 in 1) how=failed ;; 2) how=refused ;; esac
  if [ "$1" = 3 ]; then echo "interop: not run ($2)" >&2; else echo "interop build: $2" >&2; fi
  python3 -I -c 'import json,sys; print(json.dumps({"v": 1, sys.argv[1]: sys.argv[2], "siblings": "heads" if sys.argv[3] == "1" else "pins", "placed": json.loads(sys.argv[4])}))' \
    "$how" "$2" "${heads:-0}" "$placed" > "$out/builds.json"
  exit "$1"
}
notrun() { stop 3 "$1"; }

case "${SW_INTEROP_HEADS:-}" in
  ''|0) heads=0 ;;
  1) heads=1 ;;
  *) stop 2 "SW_INTEROP_HEADS is 1 (each sibling at its repository's HEAD) or unset (at its pin); given: \"$SW_INTEROP_HEADS\"" ;;
esac

# The pins, read by tools/interop/pins.py (the one reader of siblings.json, CI's workflows' too): "<app> <repo> <ref> <build>"
# lines, or its refusal's words.
pins=$(python3 -I "$root/tools/interop/pins.py" "$root/tools/interop/siblings.json" 2>&1) || stop 2 "$pins"

dir_of() {
  case "$1" in
    patterns) echo "${SW_PATTERNS_DIR:-$root/../Patterns}" ;;
    mapper) echo "${SW_MAPPER_DIR:-$root/../Mapper}" ;;
    agmessage) echo "${SW_AGMESSAGE_DIR:-$root/../AGMessage}" ;;
  esac
}

# One sibling's clone, checked out detached at its pin (or, as HEAD, at the commit its repository has checked out): tree[app] is
# where it is, commit[app] the commit, pushed[app] where that commit was seen pushed.
declare -A tree pushed commit
place() {
  local app=$1 repo=$2 ref=$3 src clone url on="" up what=pin
  src=$(dir_of "$app")
  [ -d "$src/.git" ] || [ -f "$src/.git" ] || notrun "$app: no repository at $src (set SW_${app^^}_DIR)"
  src=$(cd "$src" && pwd)
  if [ "$ref" = HEAD ]; then
    what='head'
    ref=$(git -C "$src" rev-parse -q --verify 'HEAD^{commit}') || notrun "$app: $src has no commit checked out"
  fi
  clone="$out/trees/$app"
  url=$(git -C "$src" remote get-url origin 2>/dev/null) || url="https://github.com/$repo.git"
  if [ ! -d "$clone/.git" ]; then
    git clone -q --shared --no-checkout "$src" "$clone" || stop 1 "$app: $src could not be cloned into $clone"
  fi
  if git -C "$src" cat-file -e "$ref^{commit}" 2>/dev/null; then
    # Where it was seen pushed: the branch the repository's checkout follows when that branch holds it (a heads run's branch),
    # else the first remote branch that does.
    on=$(git -C "$src" for-each-ref --contains "$ref" refs/remotes --format='%(if)%(symref)%(then)%(else)%(refname:short)%(end)' 2>/dev/null |
      grep -v '^$')
    up=$(git -C "$src" rev-parse -q --abbrev-ref '@{upstream}' 2>/dev/null) || up=""
    if [ -n "$up" ] && grep -qxF -- "$up" <<< "$on"; then on=$up; else on=$(head -n 1 <<< "$on"); fi
    [ -n "$on" ] || stop 2 "$app: the $what $ref is on none of $src's remote branches, and the lane runs a pushed commit only (if it was pushed since, git fetch in the sibling's .git)"
    git -C "$clone" cat-file -e "$ref^{commit}" 2>/dev/null || git -C "$clone" fetch -q origin 2>/dev/null
  fi
  if [ -z "$on" ] || ! git -C "$clone" cat-file -e "$ref^{commit}" 2>/dev/null; then
    say "$app: fetching $ref into the clone from the sibling's origin (it is not on a branch the clone takes from $src)"
    timeout "$FETCH_SECONDS" git -C "$clone" fetch -q "$url" "$ref" 2>/dev/null ||
      notrun "$app: the $what $ref cannot be fetched from $src or its origin (within ${FETCH_SECONDS} s)"
    [ -n "$on" ] || on="fetched from its origin"
  fi
  git -C "$clone" -c advice.detachedHead=false checkout -q --force --detach "$ref" || stop 1 "$app: checkout of $ref failed"
  [ "$(git -C "$clone" rev-parse HEAD)" = "$ref" ] || stop 1 "$app's clone is not at $ref after its checkout"
  tree[$app]=$clone
  pushed[$app]=$on
  commit[$app]=$ref
}

# How far a sibling's head is from its pin, in words, read in its clone (heads runs only).
commits() { if [ "$1" = 1 ]; then echo "1 commit"; else echo "$1 commits"; fi; }
drift() {
  local pin=$2 clone=${tree[$1]} head=${commit[$1]} edge="" ahead behind
  [ "$head" = "$pin" ] && { echo "at its pin"; return; }
  [ "$(git -C "$clone" rev-parse --is-shallow-repository)" = true ] && edge=" (a shallow history: counts stop at its edge)"
  git -C "$clone" cat-file -e "$pin^{commit}" 2>/dev/null || { echo "its pin ${pin:0:12} is not in the history this run has$edge"; return; }
  ahead=$(git -C "$clone" rev-list --count "$pin..$head")
  behind=$(git -C "$clone" rev-list --count "$head..$pin")
  if [ "$behind" = 0 ]; then
    echo "$(commits "$ahead") after its pin ${pin:0:12}$edge"
  elif [ "$ahead" = 0 ]; then
    echo "$(commits "$behind") behind its pin ${pin:0:12}: the pin is not in it yet$edge"
  else
    echo "apart from its pin ${pin:0:12}: $(commits "$ahead") of its own, $(commits "$behind") of the pin's not in it$edge"
  fi
}

# record <app> <pin>: the sibling as placed, into placed (builds.json's, whether the builds are made or not), with its note: where
# its commit was seen pushed and, in a heads run, how far it is from its pin.
record() {
  local note="pinned, pushed: ${pushed[$1]}"
  [ $heads = 1 ] && note="head, pushed: ${pushed[$1]}; $(drift "$1" "$2")"
  placed=$(python3 -I -c 'import json,sys; d = json.loads(sys.argv[1]); d[sys.argv[2]] = {"commit": sys.argv[3], "pin": sys.argv[4], "note": sys.argv[5]}; print(json.dumps(d))' \
    "$placed" "$1" "${commit[$1]}" "$2" "$note")
}

declare -A build_of
while read -r app repo ref build; do
  build_of[$app]=$build
  if [ $heads = 1 ]; then place "$app" "$repo" HEAD; else place "$app" "$repo" "$ref"; fi
  record "$app" "$ref"
done <<< "$pins"

# One build at a time, each in its own tree, with no build server left behind to meet another tree's build.
for app in patterns mapper; do
  say "$app: dotnet build ${build_of[$app]} at $(git -C "${tree[$app]}" rev-parse --short=12 HEAD)"
  (cd "${tree[$app]}" && dotnet build "${build_of[$app]}" -c Release -v q -nologo --disable-build-servers) ||
    stop 1 "$app did not build (dotnet's words above)"
done
# The Node AGMessage runs on: one its engines field (">=a.b.c") accepts.
NODE_FOR_AGMESSAGE=v24.21.0
declare -A NODE_SHA256=(
  [x64]=fd8e59d5a511510f6a298afb548f18c7d2b1be404d8b4a27d94fbe49f56cb2d6
  [arm64]=6ad1325edbdb5649c379b75a237147a666c95d4f9ae8d340fef2d1575d289ad2
)
engines=$(python3 -I -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8")).get("engines", {}).get("node", ""))' \
  "${tree[agmessage]}/${build_of[agmessage]}/package.json")
accepts() {   # accepts <node binary>: its version is at least the engines field's minimum
  local v
  v=$("$1" --version 2>/dev/null) || return 1
  python3 -I -c '
import re, sys
want = re.fullmatch(r">=\s*(\d+)\.(\d+)\.(\d+)", sys.argv[1].strip())
have = re.fullmatch(r"v(\d+)\.(\d+)\.(\d+)", sys.argv[2].strip())
sys.exit(0 if want and have and tuple(map(int, have.groups())) >= tuple(map(int, want.groups())) else 1)' "$engines" "$v"
}
if [ -n "${SW_INTEROP_AGMESSAGE_NODE:-}" ]; then
  agm_node=$SW_INTEROP_AGMESSAGE_NODE
  accepts "$agm_node" || stop 2 "agmessage: SW_INTEROP_AGMESSAGE_NODE ($("$agm_node" --version 2>&1)) is not a Node its engines field ($engines) accepts"
elif accepts "$(command -v node)"; then
  agm_node=$(command -v node)
else
  case "$(uname -m)" in x86_64) arch=x64 ;; aarch64) arch=arm64 ;; *) arch="" ;; esac
  [ -n "$arch" ] || notrun "agmessage: this machine's node is not one its engines field ($engines) accepts, and no Node is pinned for $(uname -m)"
  name="node-$NODE_FOR_AGMESSAGE-linux-$arch"
  agm_node="$out/node/$name/bin/node"
  if [ ! -x "$agm_node" ] || ! accepts "$agm_node"; then
    say "agmessage: the machine's node is not one its engines field ($engines) accepts; fetching $name into $out/node"
    rm -rf "$out/node/$name" "$out/node/$name.tar.xz"
    mkdir -p "$out/node"
    curl -fsSL --retry 3 --connect-timeout 30 --max-time 600 -o "$out/node/$name.tar.xz" \
      "https://nodejs.org/dist/$NODE_FOR_AGMESSAGE/$name.tar.xz" ||
      notrun "agmessage: $name could not be fetched for its engines field ($engines)"
    echo "${NODE_SHA256[$arch]}  $out/node/$name.tar.xz" | sha256sum -c --status ||
      { rm -f "$out/node/$name.tar.xz"; stop 1 "$name's SHA-256 is not the one pinned here"; }
    tar -xJf "$out/node/$name.tar.xz" -C "$out/node" && rm -f "$out/node/$name.tar.xz"
    accepts "$agm_node" || stop 1 "$name does not run here"
  fi
fi
agm_node_version=$("$agm_node" --version)

say "agmessage: npm ci in ${build_of[agmessage]} with node $agm_node_version (no install scripts, no dev packages)"
(cd "${tree[agmessage]}/${build_of[agmessage]}" && PATH="$(dirname "$agm_node"):$PATH" npm ci --ignore-scripts --omit=dev --no-audit --no-fund >/dev/null) ||
  stop 1 "agmessage's packages did not install (npm's words above)"

# builds.json: what the driver runs and what the matrix's header says about each build: each sibling's commit, its pin and its
# note (where it was seen pushed; in a heads run, how far it is from its pin). A file the lane runs that no build made is said
# (on standard output, for stop) in place of the document.
missing=$(python3 -I - "$out/builds.json" "$root" "${tree[patterns]}" "${tree[mapper]}" "${tree[agmessage]}" "$agm_node" \
  "$agm_node_version" "$heads" "$placed" <<'PY'
import json, os, re, subprocess, sys
dest, root = sys.argv[1], sys.argv[2]
trees = {'patterns': sys.argv[3], 'mapper': sys.argv[4], 'agmessage': sys.argv[5]}
agm_node, agm_node_version = sys.argv[6], sys.argv[7]
heads = sys.argv[8] == '1'
placed = json.loads(sys.argv[9])
def git(tree, *args):
    return subprocess.run(['git', '-C', tree, *args], capture_output=True, text=True, check=True).stdout.strip()
def sibling(app, **fields):
    return {'commit': placed[app]['commit'], 'note': placed[app]['note'], 'tree': trees[app], **fields, 'pin': placed[app]['pin']}
dirty = len([l for l in git(root, '--no-optional-locks', 'status', '--porcelain').splitlines() if l])
version = re.search(r'<Version>([^<]+)</Version>', open(os.path.join(root, 'src/Timer.App/Timer.App.csproj'), encoding='utf-8').read())
agm = json.load(open(os.path.join(trees['agmessage'], 'app/package.json'), encoding='utf-8'))
doc = {
    'v': 1,
    'stagewatch': {'commit': git(root, 'rev-parse', 'HEAD'), 'version': version.group(1) if version else '',
                   'note': f'{dirty} uncommitted files included' if dirty else 'committed',
                   'rig': os.path.join(root, 'tools/Stagewatch.Rig/bin/Debug/net10.0-windows/Stagewatch.Rig.dll')},
    'patterns': sibling('patterns', bench=os.path.join(trees['patterns'], 'tools/Patterns.Bench/bin/Release/net10.0/Patterns.Bench.dll')),
    'mapper': sibling('mapper', node=os.path.join(trees['mapper'], 'tools/PlayMap.Node/bin/Release/net10.0/PlayMap.Node.dll')),
    'agmessage': sibling('agmessage', version=agm.get('version', ''), engines=agm.get('engines', {}).get('node', ''),
                         node=agm_node, nodeVersion=agm_node_version,
                         server=os.path.join(trees['agmessage'], 'app/server.js')),
    'siblings': 'heads' if heads else 'pins',
}
for app, key in (('stagewatch', 'rig'), ('patterns', 'bench'), ('mapper', 'node'), ('agmessage', 'server')):
    if not os.path.isfile(doc[app][key]):
        print(f'{app} built no {doc[app][key]}')
        sys.exit(1)
with open(dest, 'w', encoding='utf-8') as f:
    json.dump(doc, f, indent=1)
    f.write('\n')
PY
) || stop 1 "${missing:-builds.json could not be written}"
