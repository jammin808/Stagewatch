#!/usr/bin/env bash
# The interop lane's builds: each sibling (Patterns' bench host, PlayMap's node host from the Mapper repository, AGMessage's
# server) at the commit tools/interop/siblings.json pins, in a clone the lane owns ($SW_INTEROP_OUT/trees/<app>: git clone
# --shared --no-checkout from the sibling's own repository, checked out detached), never in a sibling's working tree, and
# built one tree at a time; then $SW_INTEROP_OUT/builds.json says what was built and where, and the driver reads only that.
# Pins name pushed commits only: a pin the sibling's repository holds must be on one of its remote branches (read from its own
# refs, never fetched into it), and one it does not hold is fetched into the clone from the sibling's origin, never into the
# sibling, on every run, so that fetch is what says the origin holds it. A shallow sibling is copied over git's own transport
# instead of shared, which reads it and writes nothing to it either.
# AGMessage runs on a Node its package's engines field accepts: the machine's node when it does, else the Node this script
# pins (NODE_FOR_AGMESSAGE, its SHA-256 checked) fetched once into $SW_INTEROP_OUT/node, never installed on the machine.
# Stagewatch's own rig is the build lane's (tools/test.sh build), named here by its commit.
#   SW_PATTERNS_DIR, SW_MAPPER_DIR, SW_AGMESSAGE_DIR   the siblings' repositories (defaults ../Patterns, ../Mapper, ../AGMessage)
#   SW_INTEROP_OUT     the lane's folder: clones, builds.json, and each topology's run and matrix (default artifacts/interop, which
#                      git ignores)
#   SW_INTEROP_AGMESSAGE_NODE   the node AGMessage runs on, as given (its version is still held to the engines field)
# Exit codes: 0 built; 1 a clone, a checkout or a build failed; 2 refused: siblings.json, a pin that is not pushed, or a
# SW_INTEROP_AGMESSAGE_NODE its engines field does not accept; 3 not run (a sibling's repository absent, a pin it cannot fetch,
# or no Node for AGMessage's engines field that this machine has or can fetch), said on standard error and in builds.json.
set -uo pipefail
cd "$(dirname "$0")/../.."
root=$(pwd)
out=${SW_INTEROP_OUT:-$root/artifacts/interop}
mkdir -p "$out/trees"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

say() { printf '\033[36minterop build:\033[0m %s\n' "$*"; }
notrun() {
  echo "interop: not run ($1)" >&2
  printf '{"v":1,"notRun":%s}\n' "$(python3 -I -c 'import json,sys; print(json.dumps(sys.argv[1]))' "$1")" > "$out/builds.json"
  exit 3
}

# siblings.json: exactly v, patterns, mapper and agmessage; each with repo owner/name, a full 40-hex commit (never a branch
# name) and the path it builds. Printed as "<app> <repo> <ref> <build>" lines.
pins=$(python3 -I - "$root/tools/interop/siblings.json" <<'PY'
import json, re, sys
doc = json.load(open(sys.argv[1], encoding='utf-8'))
apps = ('patterns', 'mapper', 'agmessage')
if not isinstance(doc, dict) or set(doc) != {'v', *apps} or doc['v'] != 1:
    sys.exit('siblings.json takes exactly "v":1, "patterns", "mapper" and "agmessage"')
for app in apps:
    pin = doc[app]
    if not isinstance(pin, dict) or set(pin) != {'repo', 'ref', 'build'}:
        sys.exit(f'siblings.json: {app} takes exactly "repo", "ref" and "build"')
    if not re.fullmatch(r'[A-Za-z0-9-]+/[A-Za-z0-9._-]+', pin['repo']):
        sys.exit(f'siblings.json: {app}.repo is owner/name')
    if not re.fullmatch(r'[0-9a-f]{40}', pin['ref']):
        sys.exit(f'siblings.json: {app}.ref is a full commit (40 hex), never a branch or tag name')
    if not re.fullmatch(r'[A-Za-z0-9._/-]+', pin['build']) or '..' in pin['build'] or pin['build'].startswith('/'):
        sys.exit(f'siblings.json: {app}.build is a path inside the repository')
    print(app, pin['repo'], pin['ref'], pin['build'])
PY
) || { echo "interop build: siblings.json refused" >&2; exit 2; }

dir_of() {
  case "$1" in
    patterns) echo "${SW_PATTERNS_DIR:-$root/../Patterns}" ;;
    mapper) echo "${SW_MAPPER_DIR:-$root/../Mapper}" ;;
    agmessage) echo "${SW_AGMESSAGE_DIR:-$root/../AGMessage}" ;;
  esac
}

# One sibling's clone, checked out detached at its pin: tree[app] is where it is, pushed[app] where the pin was seen pushed.
declare -A tree pushed
place() {
  local app=$1 repo=$2 ref=$3 src clone url on=""
  src=$(dir_of "$app")
  [ -d "$src/.git" ] || [ -f "$src/.git" ] || notrun "$app: no repository at $src (set SW_${app^^}_DIR)"
  src=$(cd "$src" && pwd)
  clone="$out/trees/$app"
  url=$(git -C "$src" remote get-url origin 2>/dev/null) || url="https://github.com/$repo.git"
  if [ ! -d "$clone/.git" ]; then
    git clone -q --shared --no-checkout "$src" "$clone" || { echo "interop build: $app: $src could not be cloned into $clone" >&2; exit 1; }
  fi
  if git -C "$src" cat-file -e "$ref^{commit}" 2>/dev/null; then
    on=$(git -C "$src" for-each-ref --contains "$ref" refs/remotes --format='%(refname:short)' 2>/dev/null | head -n 1)
    [ -n "$on" ] || { echo "interop build: $app: the pin $ref is on none of $src's remote branches, and a pin names a pushed commit only (if it was pushed since, git fetch in the sibling's .git)" >&2; exit 2; }
    git -C "$clone" cat-file -e "$ref^{commit}" 2>/dev/null || git -C "$clone" fetch -q origin 2>/dev/null
  fi
  if [ -z "$on" ] || ! git -C "$clone" cat-file -e "$ref^{commit}" 2>/dev/null; then
    say "$app: fetching $ref into the clone from the sibling's origin (it is not on a branch the clone takes from $src)"
    git -C "$clone" fetch -q "$url" "$ref" 2>/dev/null || notrun "$app: the pin $ref cannot be fetched from $src or its origin"
    [ -n "$on" ] || on="fetched from its origin"
  fi
  git -C "$clone" -c advice.detachedHead=false checkout -q --force --detach "$ref" || { echo "interop build: $app: checkout of $ref failed" >&2; exit 1; }
  [ "$(git -C "$clone" rev-parse HEAD)" = "$ref" ] || { echo "interop build: $app's clone is not at $ref after its checkout" >&2; exit 1; }
  tree[$app]=$clone
  pushed[$app]=$on
}

declare -A build_of
while read -r app repo ref build; do
  build_of[$app]=$build
  place "$app" "$repo" "$ref"
done <<< "$pins"

# One build at a time, each in its own tree, with no build server left behind to meet another tree's build.
for app in patterns mapper; do
  say "$app: dotnet build ${build_of[$app]} at $(git -C "${tree[$app]}" rev-parse --short=12 HEAD)"
  (cd "${tree[$app]}" && dotnet build "${build_of[$app]}" -c Release -v q -nologo --disable-build-servers) ||
    { echo "interop build: $app did not build" >&2; exit 1; }
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
  accepts "$agm_node" || { echo "interop build: agmessage: SW_INTEROP_AGMESSAGE_NODE ($("$agm_node" --version 2>&1)) is not a Node its engines field ($engines) accepts" >&2; exit 2; }
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
    curl -fsSL --retry 3 -o "$out/node/$name.tar.xz" "https://nodejs.org/dist/$NODE_FOR_AGMESSAGE/$name.tar.xz" ||
      notrun "agmessage: $name could not be fetched for its engines field ($engines)"
    echo "${NODE_SHA256[$arch]}  $out/node/$name.tar.xz" | sha256sum -c --status ||
      { rm -f "$out/node/$name.tar.xz"; echo "interop build: $name's SHA-256 is not the one pinned here" >&2; exit 1; }
    tar -xJf "$out/node/$name.tar.xz" -C "$out/node" && rm -f "$out/node/$name.tar.xz"
    accepts "$agm_node" || { echo "interop build: $name does not run here" >&2; exit 1; }
  fi
fi
agm_node_version=$("$agm_node" --version)

say "agmessage: npm ci in ${build_of[agmessage]} with node $agm_node_version (no install scripts, no dev packages)"
(cd "${tree[agmessage]}/${build_of[agmessage]}" && PATH="$(dirname "$agm_node"):$PATH" npm ci --ignore-scripts --omit=dev --no-audit --no-fund >/dev/null) ||
  { echo "interop build: agmessage's packages did not install" >&2; exit 1; }

# builds.json: what the driver runs and what the matrix's header says about each build.
python3 -I - "$out/builds.json" "$root" "${tree[patterns]}" "${tree[mapper]}" "${tree[agmessage]}" "$agm_node" "$agm_node_version" \
  "${pushed[patterns]}" "${pushed[mapper]}" "${pushed[agmessage]}" <<'PY'
import json, os, re, subprocess, sys
dest, root = sys.argv[1], sys.argv[2]
trees = {'patterns': sys.argv[3], 'mapper': sys.argv[4], 'agmessage': sys.argv[5]}
agm_node, agm_node_version = sys.argv[6], sys.argv[7]
pinned = {'patterns': f'pinned, pushed: {sys.argv[8]}', 'mapper': f'pinned, pushed: {sys.argv[9]}',
          'agmessage': f'pinned, pushed: {sys.argv[10]}'}
def git(tree, *args):
    return subprocess.run(['git', '-C', tree, *args], capture_output=True, text=True, check=True).stdout.strip()
dirty = len([l for l in git(root, '--no-optional-locks', 'status', '--porcelain').splitlines() if l])
version = re.search(r'<Version>([^<]+)</Version>', open(os.path.join(root, 'src/Timer.App/Timer.App.csproj'), encoding='utf-8').read())
agm = json.load(open(os.path.join(trees['agmessage'], 'app/package.json'), encoding='utf-8'))
doc = {
    'v': 1,
    'stagewatch': {'commit': git(root, 'rev-parse', 'HEAD'), 'version': version.group(1) if version else '',
                   'note': f'{dirty} uncommitted files included' if dirty else 'committed',
                   'rig': os.path.join(root, 'tools/Stagewatch.Rig/bin/Debug/net10.0-windows/Stagewatch.Rig.dll')},
    'patterns': {'commit': git(trees['patterns'], 'rev-parse', 'HEAD'), 'note': pinned['patterns'], 'tree': trees['patterns'],
                 'bench': os.path.join(trees['patterns'], 'tools/Patterns.Bench/bin/Release/net10.0/Patterns.Bench.dll')},
    'mapper': {'commit': git(trees['mapper'], 'rev-parse', 'HEAD'), 'note': pinned['mapper'], 'tree': trees['mapper'],
               'node': os.path.join(trees['mapper'], 'tools/PlayMap.Node/bin/Release/net10.0/PlayMap.Node.dll')},
    'agmessage': {'commit': git(trees['agmessage'], 'rev-parse', 'HEAD'), 'note': pinned['agmessage'], 'tree': trees['agmessage'],
                  'version': agm.get('version', ''), 'engines': agm.get('engines', {}).get('node', ''),
                  'node': agm_node, 'nodeVersion': agm_node_version,
                  'server': os.path.join(trees['agmessage'], 'app/server.js')},
}
for app, key in (('stagewatch', 'rig'), ('patterns', 'bench'), ('mapper', 'node'), ('agmessage', 'server')):
    if not os.path.isfile(doc[app][key]):
        sys.exit(f'interop build: {app} built no {doc[app][key]}')
with open(dest, 'w', encoding='utf-8') as f:
    json.dump(doc, f, indent=1)
    f.write('\n')
PY
