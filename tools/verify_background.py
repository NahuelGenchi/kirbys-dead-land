#!/usr/bin/env python3
"""Check captured source BG effects against original ROM scanout in private data."""
import argparse
import json
from pathlib import Path
import subprocess
import sys

from nds_probe import Bridge, ROOT, ROM
from verify_renderer import ABILITIES

PROJECT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT / 'scripts'))
from setup_runtime import environment


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'render-tests/background')
    parser.add_argument('--all-abilities', action='store_true', help='Check all 24 original standing attack sequences.')
    args = parser.parse_args()
    output = args.output.resolve()
    if output == PROJECT or PROJECT in output.parents:
        raise SystemExit('Original ROM states/pixels must stay outside the source project.')
    output.mkdir(parents=True, exist_ok=True)
    dotnet = ROOT / 'dotnet/dotnet'
    project = PROJECT / 'tests/background/Kirby.Background.Tests.csproj'
    subprocess.run([str(dotnet), 'build', str(project), '-c', 'Release', '-v', 'minimal'],
                   env=environment(ROOT), check=True)
    dll = project.parent / 'bin/Release/net10.0/Kirby.Background.Tests.dll'
    native = ROOT / 'oracle/vendor/melon-rs/target/release/libkssu_bridge.so'
    summaries = []
    cases = list(enumerate(ABILITIES, 2)) if args.all_abilities else [(23, 'crash'), (21, 'paint'), (22, 'mike'), (24, 'cook')]
    for ability, name in cases:
        folder = output / name
        folder.mkdir(exist_ok=True)
        state = folder / 'ready.state'
        bridge = Bridge()
        try:
            bridge.step(2, 30)
            if bridge.feed(ability) != 0:
                raise RuntimeError(f'{name}: original inhale is unavailable')
            bridge.step(0, 20)
            bridge.step(128)
            bridge.step(0, 120)
            if bridge.state()['ability'] != ability:
                raise RuntimeError(f'{name}: original copy loader did not finish')
            bridge.save(state)
        finally:
            bridge.close()
        report = folder / 'report.json'
        with (folder / 'stdout.log').open('w') as log:
            result = subprocess.run([str(dotnet), str(dll), str(native), str(ROM), str(state),
                                     '300', str(report)], stdout=log, stderr=log)
        if not report.is_file():
            raise RuntimeError(f'{name}: source verification process failed; see {folder / "stdout.log"}')
        data = json.loads(report.read_text())
        summary = dict(name=name, **{k: v for k, v in data.items() if k != 'frames'})
        summaries.append(summary)
        print(json.dumps(summary), flush=True)
        if result.returncode:
            raise RuntimeError(f'{name}: source output differs, an effect is unsupported or the idle skip changes one; see {report}')
    (output / 'summary.json').write_text(json.dumps(summaries, indent=2) + '\n')


if __name__ == '__main__':
    main()
