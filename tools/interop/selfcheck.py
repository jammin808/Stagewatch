#!/usr/bin/env python3
"""The interop lane's own scripts, checked on fixtures of their own before the lane runs (tools/test.sh interop): pins.py's
reading and its refusals; build.sh's ends (built, failed, refused, not run) at pins and at heads, with what each writes to
builds.json (where a commit was seen pushed, how far a head is from its pin, the siblings placed before an end); summary.py's
words for each; and publish.py's rule for what of a run leaves the machine.

It is hermetic and deterministic. Every repository it reads is one it makes, in a folder of its own that it removes at its
end; git reads no configuration of the machine's or the user's; every origin is a local bare repository, so nothing reaches the
network. build.sh runs as a byte-for-byte copy (with pins.py) in a scratch Stagewatch of its own, whose rig is an empty file,
with stub dotnet, node and npm first on its PATH: dotnet makes the file a build would (or fails, for the tree SELFCHECK_FAIL
names), node runs the machine's, npm installs nothing.

  selfcheck.py     exit 0 every check passed; 1 a check failed (each said on standard error)
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
APPS = ('patterns', 'mapper', 'agmessage')
BUILD = {'patterns': 'tools/Patterns.Bench/Patterns.Bench.csproj', 'mapper': 'tools/PlayMap.Node/PlayMap.Node.csproj',
         'agmessage': 'app'}
# A commit no fixture holds: a pin nobody can fetch.
NOWHERE = '0123456789abcdef' * 2 + '01234567'

failures = []
passed = 0


def check(cond, words):
    global passed
    if cond:
        passed += 1
    else:
        failures.append(words)


def environment():
    env = {k: v for k, v in os.environ.items() if not k.startswith(('GIT_', 'SW_INTEROP_', 'SW_PATTERNS', 'SW_MAPPER',
                                                                   'SW_AGMESSAGE', 'SELFCHECK_'))}
    env.update(GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM='1', GIT_TERMINAL_PROMPT='0',
               GIT_AUTHOR_NAME='selfcheck', GIT_AUTHOR_EMAIL='selfcheck@example.invalid',
               GIT_COMMITTER_NAME='selfcheck', GIT_COMMITTER_EMAIL='selfcheck@example.invalid')
    return env


ENV = environment()


def git(cwd, *args):
    r = subprocess.run(['git', '-C', str(cwd), *args], capture_output=True, text=True, env=ENV, timeout=60)
    if r.returncode:
        raise RuntimeError(f'git {" ".join(args)} in {cwd}: {r.stderr.strip()}')
    return r.stdout.strip()


def write(path, text, mode=None):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding='utf-8')
    if mode is not None:
        path.chmod(mode)
    return path


def commit(repo, words):
    write(repo / 'notes.txt', words + '\n')
    git(repo, 'add', '-A')
    git(repo, 'commit', '-q', '-m', words)
    return git(repo, 'rev-parse', 'HEAD')


# ---------------------------------------------------------------------------------------------------------------- pins.py

def pins_checks(base):
    pins = HERE / 'pins.py'

    def run(path):
        return subprocess.run([sys.executable, '-I', str(pins), str(path)], capture_output=True, text=True, timeout=30)

    r = run(HERE / 'siblings.json')
    lines = r.stdout.splitlines()
    check(r.returncode == 0 and [line.split()[0] for line in lines] == list(APPS) and all(len(line.split()) == 4 for line in lines),
          f'pins.py: the real siblings.json is read as three lines in order (exit {r.returncode}: {r.stdout!r} {r.stderr!r})')

    good = {'repo': 'owner/name', 'ref': 'a' * 40, 'build': 'app'}

    def doc(**over):
        d = {'v': 1, **{app: dict(good) for app in APPS}}
        d.update(over)
        return json.dumps(d)

    refused = {
        'v true': doc(v=True),
        'v 1.0': doc(v=1.0),
        'v 2': doc(v=2),
        'a key twice at the top': doc()[:-1] + ', "agmessage": ' + json.dumps(good) + '}',
        'a key twice in a pin': doc().replace('"build": "app"}', '"build": "app", "ref": "' + 'b' * 40 + '"}', 1),
        'a branch for a ref': doc(patterns={**good, 'ref': 'main'}),
        'upper-case hex': doc(patterns={**good, 'ref': 'A' * 40}),
        'a short commit': doc(patterns={**good, 'ref': 'a' * 12}),
        'a ref not a string': doc(patterns={**good, 'ref': 7}),
        'an extra key': doc(extra=1),
        'an extra key in a pin': doc(mapper={**good, 'tag': 'x'}),
        'an app missing': json.dumps({'v': 1, 'patterns': good, 'mapper': good}),
        'a build with ..': doc(agmessage={**good, 'build': '../app'}),
        'an absolute build': doc(agmessage={**good, 'build': '/app'}),
        'a repo not owner/name': doc(mapper={**good, 'repo': 'name'}),
        'not JSON': '{"v":1,',
        'a list': '[]',
    }
    for name, text in refused.items():
        r = run(write(base / 'pins' / 'siblings.json', text))
        check(r.returncode == 2 and r.stdout == '' and r.stderr.startswith('siblings.json refused'),
              f'pins.py refuses {name} (exit {r.returncode}: {r.stdout!r} {r.stderr!r})')
    r = run(base / 'pins' / 'absent.json')
    check(r.returncode == 2 and r.stdout == '', f'pins.py refuses a file that is not there (exit {r.returncode})')
    # The control: the same fixture, well formed, is taken.
    r = run(write(base / 'pins' / 'siblings.json', doc()))
    check(r.returncode == 0 and len(r.stdout.splitlines()) == 3, f'pins.py takes a well-formed fixture (exit {r.returncode}: {r.stderr!r})')


# --------------------------------------------------------------------------------------------------------------- build.sh

class Fixture:
    """Three sibling repositories, each with a bare origin: main A-B-C, side A-D and zz-a at A pushed (each tracking its
    origin branch, origin/HEAD naming main), and local A-B-C-E never pushed; and a scratch Stagewatch with build.sh."""

    def __init__(self, base):
        self.base = base
        self.c = {}
        self.src = {}
        for app in APPS:
            bare = base / 'origins' / f'{app}.git'
            bare.mkdir(parents=True)
            git(bare, 'init', '-q', '--bare', '-b', 'main')
            src = base / 'siblings' / app
            src.mkdir(parents=True)
            git(src, 'init', '-q', '-b', 'main')
            git(src, 'remote', 'add', 'origin', str(bare))
            if app == 'agmessage':
                write(src / 'app' / 'package.json', json.dumps({'name': 'agmessage', 'version': '0.0.1', 'engines': {'node': '>=1.0.0'}}))
                write(src / 'app' / 'server.js', '// the fixture\'s server\n')
            else:
                write(src / BUILD[app], '<Project />\n')
            c = {'A': commit(src, f'{app} A')}
            c['B'] = commit(src, f'{app} B')
            c['C'] = commit(src, f'{app} C')
            git(src, 'push', '-q', '-u', 'origin', 'main')
            git(src, 'checkout', '-q', '-b', 'side', c['A'])
            c['D'] = commit(src, f'{app} D')
            git(src, 'push', '-q', '-u', 'origin', 'side')
            git(src, 'checkout', '-q', '-b', 'zz-a', c['A'])
            git(src, 'push', '-q', '-u', 'origin', 'zz-a')
            git(src, 'checkout', '-q', '-b', 'local', c['C'])
            c['E'] = commit(src, f'{app} E')
            git(src, 'remote', 'set-head', 'origin', 'main')
            self.c[app] = c
            self.src[app] = src
        root = base / 'stagewatch'
        (root / 'tools' / 'interop').mkdir(parents=True)
        for name in ('build.sh', 'pins.py'):
            shutil.copyfile(HERE / name, root / 'tools' / 'interop' / name)
        (root / 'tools' / 'interop' / 'build.sh').chmod(0o755)
        write(root / 'src' / 'Timer.App' / 'Timer.App.csproj', '<Project><PropertyGroup><Version>9.9.9</Version></PropertyGroup></Project>\n')
        write(root / 'tools' / 'Stagewatch.Rig' / 'bin' / 'Debug' / 'net10.0-windows' / 'Stagewatch.Rig.dll', '')
        git(root, 'init', '-q', '-b', 'main')
        git(root, 'add', '-A')
        git(root, 'commit', '-q', '-m', 'the scratch Stagewatch')
        self.root = root
        node = shutil.which('node')
        if not node:
            raise RuntimeError('no node on PATH: the lane runs on one')
        stubs = base / 'stubs'
        write(stubs / 'dotnet', '#!/bin/sh\n'
              '# The self-check\'s dotnet: makes the file a build of the project would, or fails for the tree SELFCHECK_FAIL names.\n'
              '[ "$1" = build ] || exit 64\n'
              '[ "$(basename "$PWD")" = "${SELFCHECK_FAIL:-}" ] && { echo "stub dotnet: this tree fails its build" >&2; exit 1; }\n'
              'dir=$(dirname "$2"); name=$(basename "$2" .csproj)\n'
              'mkdir -p "$dir/bin/Release/net10.0" && : > "$dir/bin/Release/net10.0/$name.dll"\n', 0o755)
        write(stubs / 'node', f'#!/bin/sh\nexec "{node}" "$@"\n', 0o755)
        write(stubs / 'npm', '#!/bin/sh\n# The self-check\'s npm: installs nothing.\nexit 0\n', 0o755)
        self.stubs = stubs

    def at(self, app, where):
        """Check a sibling out: a branch name (as its own checkout, following its upstream) or 'detached <commit letter>'."""
        if where.startswith('detached '):
            git(self.src[app], 'checkout', '-q', '--detach', self.c[app][where.split()[1]])
        else:
            git(self.src[app], 'checkout', '-q', where)

    def pins(self, **refs):
        """siblings.json with each app's pin: a commit letter, or a full commit as given."""
        doc = {'v': 1}
        for app in APPS:
            ref = refs.get(app, 'A')
            doc[app] = {'repo': f'owner/{app}', 'ref': self.c[app].get(ref, ref), 'build': BUILD[app]}
        write(self.root / 'tools' / 'interop' / 'siblings.json', json.dumps(doc))

    def build(self, out, heads=None, fail=None, dirs=None):
        env = dict(ENV, PATH=f'{self.stubs}{os.pathsep}{ENV.get("PATH", "")}', SW_INTEROP_OUT=str(out),
                   SW_PATTERNS_DIR=str(self.src['patterns']), SW_MAPPER_DIR=str(self.src['mapper']),
                   SW_AGMESSAGE_DIR=str(self.src['agmessage']))
        if heads is not None:
            env['SW_INTEROP_HEADS'] = heads
        if fail:
            env['SELFCHECK_FAIL'] = fail
        env.update(dirs or {})
        r = subprocess.run([str(self.root / 'tools' / 'interop' / 'build.sh')], capture_output=True, text=True, env=env, timeout=300)
        try:
            doc = json.loads((out / 'builds.json').read_text(encoding='utf-8'))
        except (OSError, json.JSONDecodeError):
            doc = None
        return r, doc


def short(c):
    return c[:12]


def said(r):
    """A build.sh run's exit and the end of its standard error, for a failed check's words."""
    return f'exit {r.returncode}; {r.stderr.strip()[-600:]!r}'


def build_checks(fx, base):
    c = fx.c
    # Built at the pins, each source detached (as CI's checkouts are), where origin/HEAD sorts first: each pin is said to
    # be on origin/main, never on the symbolic origin/HEAD.
    for app in APPS:
        fx.at(app, 'detached C')
    fx.pins(patterns='A', mapper='C', agmessage='A')
    out1 = base / 'out-pins'
    r, doc = fx.build(out1)
    ok = r.returncode == 0 and doc is not None and doc.get('siblings') == 'pins'
    check(ok, f'build.sh builds at the pins ({said(r)})')
    if ok:
        for app, pin in (('patterns', 'A'), ('mapper', 'C'), ('agmessage', 'A')):
            b = doc.get(app) or {}
            check(b.get('commit') == c[app][pin] and b.get('pin') == c[app][pin], f'builds.json: {app} at its pin ({b})')
            check(b.get('note') == 'pinned, pushed: origin/main', f'builds.json: {app} pushed on origin/main, not origin/HEAD ({b.get("note")!r})')
            check(git(out1 / 'trees' / app, 'rev-parse', 'HEAD') == c[app][pin], f'{app}\'s clone is at its pin')
        check(doc['stagewatch'].get('version') == '9.9.9' and doc['agmessage'].get('version') == '0.0.1',
              f'builds.json: the versions read ({doc["stagewatch"].get("version")}, {doc["agmessage"].get("version")})')
        check(all(Path(doc[app][key]).is_file() for app, key in (('patterns', 'bench'), ('mapper', 'node'), ('agmessage', 'server'))),
              'builds.json: each sibling\'s built file is where it says')
    for app in APPS:
        check(git(fx.src[app], 'rev-parse', 'HEAD') == c[app]['C'] and git(fx.src[app], 'status', '--porcelain') == '',
              f'build.sh leaves {app}\'s repository as it found it')

    # Heads: at its pin (on zz-a, its upstream, though origin/main sorts first and holds it too), behind its pin, apart from it.
    fx.at('patterns', 'zz-a')
    fx.at('mapper', 'detached B')
    fx.at('agmessage', 'side')
    fx.pins(patterns='A', mapper='C', agmessage='C')
    r, doc = fx.build(base / 'out-heads', heads='1')
    ok = r.returncode == 0 and doc is not None and doc.get('siblings') == 'heads'
    check(ok, f'build.sh builds at the heads ({said(r)})')
    if ok:
        want = {
            'patterns': (c['patterns']['A'], c['patterns']['A'], 'head, pushed: origin/zz-a; at its pin'),
            'mapper': (c['mapper']['B'], c['mapper']['C'],
                       f'head, pushed: origin/main; 1 commit behind its pin {short(c["mapper"]["C"])}: the pin is not in it yet'),
            'agmessage': (c['agmessage']['D'], c['agmessage']['C'],
                          f'head, pushed: origin/side; apart from its pin {short(c["agmessage"]["C"])}: 1 commit of its own, '
                          '2 commits of the pin\'s not in it'),
        }
        for app, (head, pin, note) in want.items():
            b = doc.get(app) or {}
            check((b.get('commit'), b.get('pin'), b.get('note')) == (head, pin, note), f'builds.json at the heads: {app} ({b})')

    # A heads build that fails still says where each head is, placed before the end.
    fx.at('patterns', 'main')
    fx.pins(patterns='A', mapper='C', agmessage='C')
    out3 = base / 'out-failed'
    r, doc = fx.build(out3, heads='1', fail='mapper')
    ok = r.returncode == 1 and doc is not None and set(doc) == {'v', 'failed', 'siblings', 'placed'}
    check(ok, f'build.sh: a build that fails ends with exit 1 and its words in builds.json ({said(r)}; {doc})')
    if ok:
        check('mapper did not build' in doc['failed'] and doc['siblings'] == 'heads', f'builds.json says which build failed ({doc["failed"]!r})')
        check(set(doc['placed']) == set(APPS), f'builds.json: every sibling placed before the failed build ({sorted(doc["placed"])})')
        p = doc['placed'].get('patterns') or {}
        check(p.get('note') == f'head, pushed: origin/main; 2 commits after its pin {short(c["patterns"]["A"])}',
              f'builds.json: a failed heads build still says its drift ({p})')

    # A head on no remote branch is refused, with the siblings placed before it.
    fx.at('agmessage', 'local')
    r, doc = fx.build(base / 'out-unpushed', heads='1')
    ok = r.returncode == 2 and doc is not None and 'on none of' in doc.get('refused', '')
    check(ok, f'build.sh refuses a head on no remote branch ({said(r)}; {doc})')
    if ok:
        check(set(doc['placed']) == {'patterns', 'mapper'}, f'builds.json: the siblings placed before the refusal ({sorted(doc["placed"])})')

    # A pin its repository holds on no remote branch is refused; one nobody holds is not run.
    for app in APPS:
        fx.at(app, 'detached C')
    fx.pins(patterns='E')
    r, doc = fx.build(base / 'out-pin-unpushed')
    check(r.returncode == 2 and doc is not None and 'on none of' in doc.get('refused', '') and doc.get('placed') == {},
          f'build.sh refuses a pin on no remote branch ({said(r)}; {doc})')
    fx.pins(mapper=NOWHERE)
    r, doc = fx.build(base / 'out-pin-nowhere')
    ok = r.returncode == 3 and doc is not None and 'cannot be fetched' in doc.get('notRun', '')
    check(ok, f'build.sh: a pin nobody holds is not run ({said(r)}; {doc})')
    if ok:
        check(set(doc['placed']) == {'patterns'}, f'builds.json: placed before the pin that cannot be fetched ({sorted(doc["placed"])})')

    # Refused before anything: a SW_INTEROP_HEADS it does not take, written over the earlier run's builds.json; siblings.json.
    fx.pins()
    r, doc = fx.build(out1, heads='2')
    check(r.returncode == 2 and doc is not None and 'SW_INTEROP_HEADS' in doc.get('refused', '') and doc.get('placed') == {}
          and 'stagewatch' not in doc, f'build.sh refuses SW_INTEROP_HEADS=2 over an earlier run\'s builds ({said(r)}; {doc})')
    write(fx.root / 'tools' / 'interop' / 'siblings.json', '{"v": true}')
    r, doc = fx.build(base / 'out-siblings')
    check(r.returncode == 2 and doc is not None and doc.get('refused', '').startswith('siblings.json refused'),
          f'build.sh refuses a siblings.json pins.py refuses ({said(r)}; {doc})')

    # An absent repository is not run.
    fx.pins()
    r, doc = fx.build(base / 'out-absent', dirs={'SW_AGMESSAGE_DIR': str(base / 'nowhere')})
    check(r.returncode == 3 and doc is not None and 'no repository' in doc.get('notRun', ''),
          f'build.sh: an absent repository is not run ({said(r)}; {doc})')
    return {'refused': out1, 'failed': out3, 'nowhere': base / 'out-pin-nowhere'}


# ------------------------------------------------------------------------------------------------------------- summary.py

def summary(folder):
    r = subprocess.run([sys.executable, '-I', str(HERE / 'summary.py'), str(folder)], capture_output=True, text=True, timeout=30)
    return r.returncode, r.stdout


def summary_checks(fx, outs, base):
    c = fx.c
    # A built run with a lan matrix of one pass and one fail, and no laptop matrix.
    fx.pins(patterns='A', mapper='C', agmessage='A')
    for app in APPS:
        fx.at(app, 'detached C')
    out = base / 'out-summary'
    r, doc = fx.build(out)
    check(r.returncode == 0, f'build.sh builds the summary\'s fixture (exit {r.returncode})')
    write(out / 'lan' / 'matrix.json', json.dumps({'rows': [
        {'id': 'S0.1', 'title': 'ready', 'verdict': 'pass', 'ms': 10, 'deadlineMs': 60000},
        {'id': 'S7.1', 'title': 'no secret', 'verdict': 'fail', 'ms': 5, 'deadlineMs': None, 'error': 'held | here'}]}))
    rc, text = summary(out)
    check(rc == 0 and '## Interop lane at the pins' in text and f'| Stagewatch (the rig) | `{short(git(fx.root, "rev-parse", "HEAD"))}` 9.9.9 |' in text,
          f'summary.py: a built run\'s heading and builds ({text[:400]!r})')
    check('### lan: 1 of 2 rows passed, 1 failed' in text and 'fail: held \\| here' in text and '### laptop: no matrix' in text,
          f'summary.py: each topology\'s rows, a failed row\'s words with its pipe escaped, an absent matrix ({text!r})')
    old = (out / 'builds.json').stat().st_mtime - 100
    os.utime(out / 'lan' / 'matrix.json', (old, old))
    rc, text = summary(out)
    check('older than these builds' in text, 'summary.py: a matrix older than the builds is said to be an earlier run\'s')

    rc, text = summary(outs['failed'])
    pat = f'| Patterns (bench host) | `{short(c["patterns"]["C"])}` | `{short(c["patterns"]["A"])}` | head, pushed: origin/main; ' \
          f'2 commits after its pin {short(c["patterns"]["A"])} |'
    check(rc == 0 and "## Interop lane at the siblings' heads: not built" in text and 'mapper did not build' in text and pat in text,
          f'summary.py: a failed heads build, with each sibling placed ({text!r})')
    rc, text = summary(outs['nowhere'])
    check(rc == 0 and '## Interop lane at the pins: not run' in text and 'cannot be fetched' in text, f'summary.py: not run ({text!r})')
    rc, text = summary(outs['refused'])
    check(rc == 0 and '## Interop lane at the pins: refused' in text and 'No sibling was placed before that.' in text,
          f'summary.py: a refusal with nothing placed ({text!r})')
    (base / 'empty').mkdir()
    rc, text = summary(base / 'empty')
    check(rc == 0 and '## Interop lane: no builds' in text, f'summary.py: no builds.json ({text!r})')


# ------------------------------------------------------------------------------------------------------------- publish.py

def publish(folder, dest):
    r = subprocess.run([sys.executable, '-I', str(HERE / 'publish.py'), str(folder), str(dest)], capture_output=True, text=True, timeout=30)
    return r.returncode, r.stdout


def files_under(folder):
    return sorted(str(p.relative_to(folder)) for p in folder.rglob('*') if p.is_file() or p.is_symlink())


def publish_checks(base):
    lane = base / 'lane'
    write(lane / 'builds.json', '{"v": 1}')

    def sweep(verdict):
        return json.dumps({'rows': [{'id': 'S0.1', 'verdict': 'pass'}, {'id': 'S7.1', 'verdict': verdict}]})

    lan = lane / 'lan'
    write(lan / 'matrix.json', sweep('pass'))
    write(lan / 'matrix.md', '# lan\n')
    write(lan / 'run' / 'logs' / 'rig.out', 'ready\n')
    write(lan / 'run' / 'captures' / 'context.json', '{}\n')
    write(lan / 'run' / 'apps' / 'rig' / 'home' / 'journal.jsonl', '{}\n')
    write(lan / 'run' / 'apps' / 'rig' / 'home' / 'engine.log', 'up\n')
    write(lan / 'run' / 'apps' / 'rig' / 'home' / 'settings.json', '{"token": "held"}\n')
    write(lan / 'run' / 'apps' / 'rig' / 'home' / 'keys' / 'node.pem', 'held\n')
    write(lan / 'run' / 'apps' / 'rig' / 'user' / 'cache.log', 'held\n')
    write(lan / 'run' / 'secrets' / 'pin', 'held\n')
    (lan / 'run' / 'logs' / 'link.out').symlink_to(lan / 'run' / 'secrets' / 'pin')
    laptop = lane / 'laptop'
    write(laptop / 'matrix.json', sweep('fail'))
    write(laptop / 'run' / 'logs' / 'rig.out', 'held\n')
    dest = base / 'artifact'
    write(dest / 'old.txt', 'an earlier artifact\n')
    rc, text = publish(lane, dest)
    got = files_under(dest)
    want = ['README.md', 'builds.json', 'lan/matrix.json', 'lan/matrix.md', 'lan/run/apps/rig/home/engine.log',
            'lan/run/apps/rig/home/journal.jsonl', 'lan/run/captures/context.json', 'lan/run/logs/rig.out', 'laptop/matrix.json']
    check(rc == 0 and got == want, f'publish.py copies only what may leave (got {got})')
    check('**laptop**: its matrix only. The sweep (S7.1) says fail' in text
          and (dest / 'README.md').read_text(encoding='utf-8').rstrip('\n') == text.rstrip('\n'),
          f'publish.py says why a topology\'s run stayed behind, on standard output and in the README ({text!r})')

    # A run stopped before its end (no matrix, the apps' secret files still there) and an earlier run's matrix: nothing of either.
    (lan / 'matrix.json').unlink()
    (lan / 'matrix.md').unlink()
    old = (lane / 'builds.json').stat().st_mtime - 100
    os.utime(laptop / 'matrix.json', (old, old))
    rc, text = publish(lane, dest)
    check(rc == 0 and files_under(dest) == ['README.md', 'builds.json'], f'publish.py: nothing of a stopped run or an earlier one ({files_under(dest)})')
    check('stopped before its end' in text and "an earlier run's" in text, f'publish.py says why ({text!r})')

    rc, _ = publish(lane, lane / 'lan' / 'artifact')
    check(rc == 2, 'publish.py refuses an artifact folder inside the lane\'s')
    rc, _ = publish(lane, base)
    check(rc == 2, 'publish.py refuses an artifact folder around the lane\'s')


def main():
    base = Path(tempfile.mkdtemp(prefix='sw-interop-selfcheck-'))
    try:
        pins_checks(base)
        fx = Fixture(base)
        outs = build_checks(fx, base)
        summary_checks(fx, outs, base)
        publish_checks(base)
    except (RuntimeError, OSError, subprocess.SubprocessError) as e:
        failures.append(f'the self-check could not run: {e}')
    finally:
        shutil.rmtree(base)
    if failures:
        for f in failures:
            print(f'interop self-check: {f}', file=sys.stderr)
        print(f'interop self-check: {len(failures)} failed, {passed} passed', file=sys.stderr)
        return 1
    print(f'interop self-check: {passed} checks passed (pins.py, build.sh, summary.py, publish.py)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
