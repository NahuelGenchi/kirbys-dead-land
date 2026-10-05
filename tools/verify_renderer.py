#!/usr/bin/env python3
"""Compare the actual C# OBJ renderer with original ROM scanout, in private data.

Every ability seed is reached through inhale, supplied absorbed content, and the
original DOWN swallow routine. No frame selection or ability graphics are faked.
"""
import argparse
import json
from pathlib import Path
import subprocess
import sys

from nds_probe import Bridge, ROOT, ROM

PROJECT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT / 'scripts'))
from setup_runtime import environment

ABILITIES = ['cutter', 'mirror', 'beam', 'fighter', 'suplex', 'yoyo', 'wheel', 'bomb',
             'ice', 'fire', 'plasma', 'ninja', 'stone', 'wing', 'jet', 'copy', 'sword',
             'hammer', 'parasol', 'paint', 'mike', 'crash', 'cook', 'sleep']


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'render-tests')
    args = parser.parse_args()
    output = args.output.resolve()
    if output == PROJECT or PROJECT in output.parents:
        raise SystemExit('Original ROM states/pixels must stay outside the source project')
    output.mkdir(parents=True, exist_ok=True)
    dotnet = ROOT / 'dotnet/dotnet'
    subprocess.run([str(dotnet), 'build', str(PROJECT / 'tests/render/Kirby.Render.Tests.csproj'),
                    '-c', 'Release', '-v', 'minimal'], env=environment(ROOT), check=True)
    dll = PROJECT / 'tests/render/bin/Release/net10.0/Kirby.Render.Tests.dll'
    native = ROOT / 'oracle/vendor/melon-rs/target/release/libkssu_bridge.so'
    cases = [('idle', ROOT / 'oracle/ready2/000.state', 0)]
    for ability, name in enumerate(ABILITIES, 2):
        path = output / name
        path.mkdir(parents=True, exist_ok=True)
        state = path / 'ready.state'
        bridge = Bridge()
        try:
            bridge.step(2, 30)
            if bridge.feed(ability) != 0:
                raise RuntimeError(f'{name}: original inhale is unavailable')
            bridge.step(0, 20)
            bridge.step(128, 1)
            bridge.step(0, 120)
            if bridge.state()['ability'] != ability:
                raise RuntimeError(f'{name}: original copy loader did not finish')
            bridge.save(state)
        finally:
            bridge.close()
        cases.append((name, state, 2))
    for name in ['sword', 'cutter', 'beam', 'wing']:
        cases.append((name + '-left', output / name / 'ready.state', 34))
    summaries, failed = [], False
    for name, state, buttons in cases:
        folder = output / name
        folder.mkdir(parents=True, exist_ok=True)
        with (folder / 'stdout.log').open('w') as log:
            result = subprocess.run([str(dotnet), str(dll), str(native), str(ROM), str(state),
                                     '120', str(buttons), str(folder)], stdout=log, stderr=log)
        if result.returncode not in (0, 1) or not (folder / 'report.json').is_file():
            raise RuntimeError(f'{name}: renderer process failed; see {folder / "stdout.log"}')
        report = json.loads((folder / 'report.json').read_text())
        summaries.append(dict(name=name, **{k: v for k, v in report.items() if k != 'frames'}))
        failed |= result.returncode != 0
        print(f'{name}: {report["compared"]} pixels, {report["mismatched"]} mismatches, {report["unsupported"]} unsupported objects', flush=True)
    (output / 'summary.json').write_text(json.dumps(summaries, indent=2))
    if failed:
        raise SystemExit('Some source graphics differ; see private reports.')


if __name__ == '__main__':
    main()
