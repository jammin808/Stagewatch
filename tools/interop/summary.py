#!/usr/bin/env python3
"""The interop lane's summary, as Markdown on standard output: what was built (each sibling's commit, and where it was seen
pushed; at its pin, or in a heads run how far its head is from its pin) and, per topology, how many rows passed and each failed
row with its words, read from what the lane wrote ($SW_INTEROP_OUT/builds.json and <topology>/matrix.json). CI's interop jobs
append it to the job's summary; it reads nothing else, so it says nothing the matrix's own words do not (the driver scrubs
every secret of its register out of a row's words before it writes them).

  summary.py [folder]     the lane's folder (default: $SW_INTEROP_OUT, else artifacts/interop)

builds.json is either the builds (then each topology's rows follow) or the words of the end short of them, failed, refused or
not run, with each sibling placed before that end (its commit, its pin, and where it was seen pushed or how far it is from its
pin), so a heads run whose head does not build still says how far that head is from its pin.

It reports and never judges: the lane's exit code is the verdict, and this exits 0 whatever it read (2 only on a command line
it cannot take). A topology with no matrix did not run or was stopped before its end; one whose matrix is older than this run's
builds.json is from an earlier run and is said to be.
"""
import json
import os
import sys
from pathlib import Path

TOPOLOGIES = ('lan', 'laptop')
# builds.json's ends short of a build (tools/interop/build.sh's exit codes 1, 2 and 3), and how the summary heads each.
ENDS = {'failed': 'not built', 'refused': 'refused', 'notRun': 'not run'}
APPS = (('stagewatch', 'Stagewatch (the rig)'), ('patterns', 'Patterns (bench host)'), ('mapper', 'PlayMap (node)'),
        ('agmessage', 'AGMessage'))


def cell(text, limit=400):
    """One table cell: one line, its pipes escaped, at most limit characters."""
    t = ' '.join(str(text).split())
    if len(t) > limit:
        t = t[:limit - 1] + '…'
    return t.replace('|', '\\|')


def load(path):
    try:
        return json.loads(path.read_text(encoding='utf-8')), None
    except FileNotFoundError:
        return None, 'absent'
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as e:
        return None, f'unreadable ({e.__class__.__name__})'


def placed_lines(placed):
    """The siblings placed before the builds stopped: commit, pin and note."""
    if not placed:
        return ['No sibling was placed before that.', '']
    out = ['| App | Commit | Pin | Placed from |', '|---|---|---|---|']
    for key, name in APPS:
        p = placed.get(key)
        if p:
            commit, pin = str(p.get('commit', ''))[:12], str(p.get('pin', ''))[:12]
            out.append(f"| {name} | `{cell(commit)}` | `{cell(pin)}` | {cell(p.get('note', ''))} |")
    return out + ['']


def builds_lines(builds):
    heads = builds.get('siblings') == 'heads'
    out = []
    if heads:
        out += ["Each sibling ran at its repository's head (the branch or commit the run checked out), not at its pin "
                '(`tools/interop/siblings.json`): a drift report. Nothing waits for it.', '']
    else:
        out += ['Each sibling ran at the commit `tools/interop/siblings.json` pins.', '']
    out += ['| App | Commit | Built from |', '|---|---|---|']
    for key, name in APPS:
        b = builds.get(key) or {}
        commit = str(b.get('commit', ''))[:12]
        extra = ''
        if key == 'stagewatch' and b.get('version'):
            extra = f" {b['version']}"
        if key == 'agmessage' and b.get('version'):
            extra = f" {b['version']} on node {b.get('nodeVersion', '?')}"
        out.append(f"| {name} | {cell(f'`{commit}`{extra}')} | {cell(b.get('note', ''))} |")
    return out


def topology_lines(folder, name, builds_mtime):
    path = folder / name / 'matrix.json'
    matrix, why = load(path)
    if matrix is None:
        if why == 'absent':
            return [f'### {name}: no matrix', '', 'It did not run, or was stopped before its end: the lane\'s log says why.', '']
        return [f'### {name}: matrix {why}', '']
    rows = matrix.get('rows') or []
    passed = sum(1 for r in rows if r.get('verdict') == 'pass')
    failed = [r for r in rows if r.get('verdict') != 'pass']
    stale = builds_mtime is not None and path.stat().st_mtime < builds_mtime
    head = f'### {name}: {passed} of {len(rows)} rows passed'
    if failed:
        head += f', {len(failed)} failed'
    if stale:
        head += ' (an earlier run\'s matrix, older than these builds)'
    out = [head, '']
    if not rows:
        out += ['No row ran: the topology stopped before its first row (the lane\'s log says why).', '']
        return out
    out += ['| Row | Fact | Verdict | ms | Deadline ms |', '|---|---|---|---|---|']
    for r in rows:
        deadline = r.get('deadlineMs')
        deadline = '—' if deadline is None else ' / '.join(str(d) for d in (deadline if isinstance(deadline, list) else [deadline]))
        verdict = r.get('verdict', '?')
        if verdict != 'pass' and r.get('error'):
            verdict = f"{verdict}: {r['error']}"
        out.append(f"| {cell(r.get('id', '?'))} | {cell(r.get('title', ''))} | {cell(verdict)} | {cell(r.get('ms', ''))} | {cell(deadline)} |")
    out.append('')
    return out


def main(argv):
    if len(argv) > 2:
        print('summary.py takes one argument at most: the lane\'s folder', file=sys.stderr)
        return 2
    folder = Path(argv[1] if len(argv) == 2 else os.environ.get('SW_INTEROP_OUT') or 'artifacts/interop')
    builds_path = folder / 'builds.json'
    builds, why = load(builds_path)
    lines = []
    if builds is None:
        lines += ['## Interop lane: no builds', '',
                  f'`{builds_path}` is {why}: the lane stopped before its builds were made (the step\'s log says why).', '']
    elif any(builds.get(k) for k in ENDS):
        where = "the siblings' heads" if builds.get('siblings') == 'heads' else 'the pins'
        how = next(k for k in ENDS if builds.get(k))
        lines += [f'## Interop lane at {where}: {ENDS[how]}', '', cell(builds[how], 2000), '']
        lines += placed_lines(builds.get('placed') or {})
    else:
        where = "the siblings' heads (drift report)" if builds.get('siblings') == 'heads' else 'the pins'
        lines += [f'## Interop lane at {where}', '']
        lines += builds_lines(builds)
        lines.append('')
        mtime = builds_path.stat().st_mtime
        for name in TOPOLOGIES:
            lines += topology_lines(folder, name, mtime)
    print('\n'.join(lines))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
