#!/usr/bin/env python3
"""What of an interop run may leave the machine (CI's artifact): copied from the lane's folder into a folder of its own, made
afresh, with a README saying what it holds and what stayed behind and why; the same words go to standard output, as Markdown,
for the run's summary.

  publish.py <lane folder> <artifact folder>     the lane's folder is $SW_INTEROP_OUT (artifacts/interop)

What it copies, and nothing else:
- builds.json (what was built: commits, pins, notes and the clones' paths);
- each topology's matrix.json and matrix.md (the driver's own words, every secret of the run's register named, never said);
- and, only for a topology whose sweep (row S7.1) passed in this run, its logs (run/logs), its captures (run/captures) and each
  app's journals and logs from its home (run/apps/<app>/home: the files named *.log, *.jsonl or *.csv). That sweep ran once the
  apps had stopped and the files each keeps a secret in by design were removed, and read every file of the run for every
  secret the run made or learned, so each file copied was read by it and held none.

A topology whose sweep did not pass keeps all of its run on the machine: a run stopped before its end (a timeout, a crash) never
removed the apps' secret files, and a sweep that found a secret names the file it is in. So does a topology with no matrix, or
with one older than builds.json (an earlier run's). Never copied: the clones and their builds, the run's secrets folder, any
other file of an app's home (its settings, keys, recovery record, media), and anything a symbolic link points at.

It reports and never judges (the lane's exit code is the verdict): it exits 0 whatever it found, 2 on a command line it cannot
take or an artifact folder that is the lane's own or inside it.
"""
import json
import shutil
import sys
from pathlib import Path

TOPOLOGIES = ('lan', 'laptop')
# The sweep's row: every file of a topology's run read for every secret of its register (tests/interop/lib/sweep.cjs).
SWEEP_ROW = 'S7.1'
# From an app's home, only its journals and logs.
HOME_SUFFIXES = ('.log', '.jsonl', '.csv')


def regular_files(root):
    """Every regular file under root, sorted; a symbolic link (to a file or a folder) is never followed or copied."""
    out = []
    if not root.is_dir() or root.is_symlink():
        return out
    for p in sorted(root.iterdir()):
        if p.is_symlink():
            continue
        if p.is_dir():
            out += regular_files(p)
        elif p.is_file():
            out.append(p)
    return out


def copy(src, folder, dest):
    target = dest / src.relative_to(folder)
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(src, target)
    return target


def sweep_verdict(matrix):
    """The sweep's row's verdict in this matrix ('pass', 'fail'), or None when the row did not run."""
    for row in matrix.get('rows') or []:
        if isinstance(row, dict) and row.get('id') == SWEEP_ROW:
            return row.get('verdict')
    return None


def topology(folder, dest, name, builds_mtime):
    """Copy what of one topology may leave; its line for the README."""
    top = folder / name
    matrix_path = top / 'matrix.json'
    if not matrix_path.is_file() or matrix_path.is_symlink():
        if (top / 'run').exists():
            return f'- **{name}**: nothing. It has no matrix: its run was stopped before its end, so its files stay on the machine.'
        return f'- **{name}**: nothing. It did not run.'
    if builds_mtime is not None and matrix_path.stat().st_mtime < builds_mtime:
        return f"- **{name}**: nothing. Its matrix is an earlier run's (older than this run's builds.json)."
    try:
        matrix = json.loads(matrix_path.read_text(encoding='utf-8'))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as e:
        return f'- **{name}**: nothing. Its matrix cannot be read ({e.__class__.__name__}), so the sweep\'s verdict is unknown.'
    copied = [copy(matrix_path, folder, dest)]
    if (top / 'matrix.md').is_file() and not (top / 'matrix.md').is_symlink():
        copied.append(copy(top / 'matrix.md', folder, dest))
    verdict = sweep_verdict(matrix if isinstance(matrix, dict) else {})
    if verdict != 'pass':
        why = 'did not run' if verdict is None else f'says {verdict}'
        return (f'- **{name}**: its matrix only. The sweep ({SWEEP_ROW}) {why}, so its logs, captures and the apps\' files stay '
                'on the machine: a file of that run may hold a secret.')
    run = top / 'run'
    files = regular_files(run / 'logs') + regular_files(run / 'captures')
    apps = run / 'apps'
    if apps.is_dir() and not apps.is_symlink():
        for app in sorted(p for p in apps.iterdir() if p.is_dir() and not p.is_symlink()):
            files += [f for f in regular_files(app / 'home') if f.name.endswith(HOME_SUFFIXES)]
    copied += [copy(f, folder, dest) for f in files]
    return (f'- **{name}**: its matrix, logs, captures and the apps\' journals and logs ({len(copied)} files), each read by the '
            f'sweep ({SWEEP_ROW}), which passed.')


def main(argv):
    if len(argv) != 3:
        print('publish.py takes the lane\'s folder and the artifact\'s folder', file=sys.stderr)
        return 2
    folder, dest = Path(argv[1]).resolve(), Path(argv[2]).resolve()
    if dest == folder or folder in dest.parents or dest in folder.parents:
        print('publish.py: the artifact\'s folder is a folder of its own, neither the lane\'s nor inside it nor around it', file=sys.stderr)
        return 2
    if dest.exists():
        shutil.rmtree(dest)
    dest.mkdir(parents=True)
    lines = ['### What leaves the runner (the artifact)', '']
    builds = folder / 'builds.json'
    builds_mtime = None
    if builds.is_file() and not builds.is_symlink():
        copy(builds, folder, dest)
        builds_mtime = builds.stat().st_mtime
        lines.append('- **builds.json**: what was built, or the words of the end short of it.')
    else:
        lines.append('- **builds.json**: absent; the lane stopped before its builds.')
    for name in TOPOLOGIES:
        lines.append(topology(folder, dest, name, builds_mtime))
    lines += ['', 'Never: the clones or their builds, the run\'s secrets, or any other file of an app\'s home.', '']
    text = '\n'.join(lines)
    (dest / 'README.md').write_text(text, encoding='utf-8')
    print(text)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
