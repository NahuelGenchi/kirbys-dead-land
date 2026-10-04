#!/usr/bin/env python3
"""Run offline checks; this never launches Dead Cells or claims gameplay parity."""
import argparse
from pathlib import Path
import subprocess
import sys

from setup_runtime import DEFAULT_ROOT, environment


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data-root', type=Path, default=DEFAULT_ROOT)
    parser.add_argument('--assets', type=Path, help='Also verify a generated assets directory.')
    args = parser.parse_args()
    project = Path(__file__).resolve().parents[1]
    root = args.data_root.expanduser().resolve()
    dotnet = root / 'dotnet/dotnet'
    if not dotnet.is_file():
        parser.error('Run scripts/setup_runtime.py --build first.')
    commands = [
        [sys.executable, '-m', 'unittest', 'discover', '-s', 'tests', '-p', 'test_*.py', '-v'],
        [sys.executable, 'tools/oracle_tests.py'],
        [str(dotnet), 'run', '--project', 'tests/Kirby.Core.Tests.csproj', '-c', 'Release'],
        [str(dotnet), 'build', 'src/runtime/Kirby.Runtime.csproj', '-c', 'Release', '-v', 'minimal'],
    ]
    if args.assets:
        assets = args.assets.expanduser().resolve()
        commands.extend([
            [sys.executable, 'tools/verify_assets.py', '--decoded', str(root / 'rom/decoded'), '--build', str(assets)],
            [str(dotnet), 'run', '--project', 'tests/pak/Kirby.Pak.Verify.csproj', '-c', 'Release', '--', str(assets / 'build-manifest.json')],
        ])
    for command in commands:
        print('Checking:', ' '.join(command), flush=True)
        subprocess.run(command, cwd=project, env=environment(root), check=True)
    print('All requested offline checks passed. This command does not validate in-game gameplay fidelity.')


if __name__ == '__main__':
    main()
