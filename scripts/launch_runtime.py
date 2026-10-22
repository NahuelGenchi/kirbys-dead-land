#!/usr/bin/env python3
"""Play Kirby with isolated saves, or run a bounded offscreen integration test."""
from __future__ import annotations
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import threading
import time
import _thread
from save_backup import back_up
from setup_runtime import DEFAULT_ROOT, download_checked, environment
from lab_lock import hold
from oracle_build import HOST, TARGETS
from steam_paths import steam_root

WINDOWS = os.name == 'nt'
if WINDOWS:
    from low_integrity import WindowsProcess, label_low


TURBOJPEG_URL = 'https://archive.ubuntu.com/ubuntu/pool/universe/libj/libjpeg-turbo/libturbojpeg0_2.1.5-4ubuntu4_amd64.deb'
TURBOJPEG_SHA256 = 'ea835d7bb9800202058b79118ff27528b48a27a528c46f3a9bff435bbb5f0ed8'


def turbojpeg_library(root: Path) -> Path:
    """Supply the missing official Linux loader dependency without system installation."""
    library = root / 'research/turbojpeg/usr/lib/x86_64-linux-gnu/libturbojpeg.so.0'
    if library.is_file():
        return library
    archive = root / 'research/libturbojpeg0_2.1.5-4ubuntu4_amd64.deb'
    archive.parent.mkdir(parents=True, exist_ok=True)
    download_checked(TURBOJPEG_URL, archive, 'sha256', TURBOJPEG_SHA256)
    if shutil.which('dpkg-deb'):
        subprocess.run(['dpkg-deb', '--extract', str(archive), str(root / 'research/turbojpeg')], check=True)
    else:
        extract_deb(archive, root / 'research/turbojpeg')
    if not library.is_file():
        raise SystemExit('Pinned TurboJPEG archive did not contain the expected library.')
    return library


def extract_deb(archive: Path, destination: Path) -> None:
    """Unpack a Debian package's files as dpkg-deb --extract does, on systems without dpkg."""
    data = archive.read_bytes()
    if data[:8] != b'!<arch>\n':
        raise SystemExit(f'{archive.name} is not a Debian package.')
    position = 8
    while position + 60 <= len(data):
        name = data[position:position + 16].decode().strip().rstrip('/')
        size = int(data[position + 48:position + 58])
        if name.startswith('data.tar'):
            # Recent Ubuntu packages use zstd, which tarfile reads from Python 3.14.
            with tarfile.open(fileobj=io.BytesIO(data[position + 60:position + 60 + size])) as files:
                files.extractall(destination, filter='data')
            return
        position += 60 + size + (size & 1)
    raise SystemExit(f'{archive.name} has no data archive.')


def stop_on_request() -> None:
    """Close the game when `stop` arrives on the input, as Ctrl+C does.

    The launcher asks this way on Windows (#250), where a program without a
    console window of its own cannot be sent Ctrl+C.
    """
    for line in sys.stdin:
        if line.strip() == 'stop':
            _thread.interrupt_main()
            return


def snapshot(paths: list[Path]) -> dict[str, str]:
    result = {}
    for root in paths:
        if not root.is_dir():
            continue
        for path in sorted(root.rglob('*')):
            if path.is_file():
                with path.open('rb') as f:
                    result[str(path)] = hashlib.file_digest(f, 'sha256').hexdigest()
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data-root', type=Path, default=DEFAULT_ROOT)
    parser.add_argument('--play', action='store_true', help='Open the game for normal play, with isolated saves and no time limit.')
    parser.add_argument('--prepare', action='store_true', help='Only fetch and unpack the private TurboJPEG library a launch needs, then exit.')
    parser.add_argument('--steam-root', type=Path, help='The Steam installation whose userdata holds the cloud saves to back up (default: the one holding the game).')
    parser.add_argument('--input-plan', choices=['movement', 'combat', 'ability', 'cook', 'healing', 'fslot', 'visual', 'breakable', 'boss', 'crowd', 'aim', 'shield', 'repro', 'hazard', 'stars', 'screen', 'water', 'items', 'rewards', 'forge', 'scroll', 'tentacle', 'thorny', 'arrival'], help='Run an isolated input test (repro tests normal keyboard/controller bindings).')
    parser.add_argument('--probe-ability', type=int, choices=range(2, 26), metavar='2..25', help='Prepare this original ability through inhale/feed/swallow before the ability or screen probe.')
    parser.add_argument('--probe-scrolls', metavar='B,T,S', help='Give the ability probe this many Brutality, Tactics and Survival scrolls before measuring damage.')
    parser.add_argument('--probe-mutations', metavar='ID,ID', help='Give the ability probe these mutation item IDs (for example P_CDR_Kill) and record the text the host shows for each.')
    parser.add_argument('--probe-affects', metavar='N:SEC[:VALUE],...', help='With --probe-mutations, set these host hero status effects (index:seconds[:value]), for example 57:30 for the effect Vengeance reads or 108:30:368 for Counterattack.')
    parser.add_argument('--probe-collector', nargs='?', const='filtered', choices=['filtered', 'unfiltered'], help='With the ability probe, reveal Collector entries in the private profile, open the real Collector panel and record which it lists and the unlock count; unfiltered turns the Kirby filter off for comparison.')
    parser.add_argument('--probe-menu', nargs='?', const='filtered', choices=['filtered', 'unfiltered'], help='With the ability probe, open the real mutation menu in the private profile and record which mutations it lists; unfiltered turns the Kirby filter off for comparison.')
    parser.add_argument('--probe-hud', action='store_true', help='Add a shield beside the starting sword before the ability probe and record the HUD weapon slots.')
    parser.add_argument('--probe-pause', action='store_true', help='With --probe-hud, open the pause/equipment screen while copied and record its cards.')
    parser.add_argument('--probe-legendary', action='store_true', help='With --probe-hud, add the Legendary affix to the copy\'s ability item once it is in a slot.')
    parser.add_argument('--capture-fps', type=int, choices=range(1, 31), metavar='1..30', help='Capture real gameplay frames at this rate for a private showcase recording.')
    parser.add_argument('--gdb', action='store_true', help=argparse.SUPPRESS)
    parser.add_argument('--start-game', action='store_true', help='Start a fresh run in the isolated lab save.')
    parser.add_argument('--resume-game', action='store_true', help='Resume the existing isolated save for a persistence check.')
    parser.add_argument('--crowd-size', type=int, choices=range(1, 32), metavar='1..31', help='Zombies the crowd plan spawns (default 31).')
    parser.add_argument('--save-after', type=int, help='Request a normal host save after this many source frames.')
    parser.add_argument('--outfit', help='Set this outfit ID before starting the isolated new run.')
    parser.add_argument('--seconds', type=int, help='Offscreen test duration, default 45 seconds.')
    parser.add_argument('--gpu', action='store_true', help='Draw the offscreen test on the GPU instead of in software.')
    parser.add_argument('--shader-cache', action='store_true', help='Keep the GL driver\'s shader cache in the lab\'s cache folder for this test, as later runs would.')
    parser.add_argument('--crash-dump', choices=['heap', 'small', 'off'], default='heap', help='What a crashing offscreen test writes: a dump with the heap (about the size of the game\'s memory), a small one with the threads\' stacks, or none.')
    parser.add_argument('--normal-integrity', action='store_true', help='Windows: start the game with your normal rights instead of low integrity; only the save backup and check then protect your files.')
    args = parser.parse_args()
    if WINDOWS and not (args.play or args.prepare):
        parser.error('on Windows, only --play and --prepare are available; offscreen tests run on Linux (#246)')
    if args.normal_integrity and not WINDOWS:
        parser.error('--normal-integrity applies to Windows; Linux runs the game in bubblewrap')
    if args.play and (args.input_plan or args.start_game or args.resume_game or args.save_after or args.seconds is not None or args.capture_fps):
        parser.error('--play cannot be combined with automated input, --start-game, or --seconds')
    if args.gdb and args.play:
        parser.error('--gdb is only available for bounded offscreen diagnostics')
    if args.gpu and args.play:
        parser.error('--gpu applies to offscreen tests; play always draws on the GPU')
    if args.start_game and args.resume_game:
        parser.error('--start-game and --resume-game are mutually exclusive')
    if args.probe_ability is not None and args.input_plan not in ('ability', 'screen'):
        parser.error('--probe-ability requires --input-plan ability or screen')
    if args.crowd_size is not None and args.input_plan != 'crowd':
        parser.error('--crowd-size requires --input-plan crowd')
    if args.probe_hud and args.input_plan != 'ability':
        parser.error('--probe-hud requires --input-plan ability')
    if args.probe_pause and not args.probe_hud:
        parser.error('--probe-pause requires --probe-hud')
    if args.probe_legendary and not args.probe_hud:
        parser.error('--probe-legendary requires --probe-hud')
    if args.probe_mutations is not None:
        if args.input_plan != 'ability':
            parser.error('--probe-mutations requires --input-plan ability')
        if not all(m.replace('_', '').isalnum() for m in args.probe_mutations.split(',')):
            parser.error('--probe-mutations takes item IDs such as P_CDR_Kill,P_CDR_Parry')
    if args.probe_affects is not None:
        if args.probe_mutations is None:
            parser.error('--probe-affects requires --probe-mutations')
        for entry in args.probe_affects.split(','):
            index, _, seconds = entry.partition(':')
            seconds = seconds.split(':')[0]
            if not (index.isdigit() and seconds.replace('.', '', 1).isdigit()):
                parser.error('--probe-affects takes index:seconds pairs such as 57:30')
    if args.probe_menu is not None and args.input_plan != 'ability':
        parser.error('--probe-menu requires --input-plan ability')
    if args.probe_collector is not None and args.input_plan != 'ability':
        parser.error('--probe-collector requires --input-plan ability')
    if args.probe_scrolls is not None:
        if args.input_plan != 'ability':
            parser.error('--probe-scrolls requires --input-plan ability')
        counts = args.probe_scrolls.split(',')
        if len(counts) != 3 or not all(c.isdigit() and int(c) <= 30 for c in counts):
            parser.error('--probe-scrolls needs three counts in 0..30, such as 4,0,2')
    if args.save_after is not None and args.save_after < 1:
        parser.error('--save-after must be positive')
    if args.play and not WINDOWS and not (os.environ.get('DISPLAY') or os.environ.get('WAYLAND_DISPLAY')):
        parser.error('--play needs a desktop display; run this command from your desktop terminal')
    args.seconds = 45 if args.seconds is None else args.seconds
    if not 1 <= args.seconds <= 300:
        parser.error('--seconds must be in 1..300')
    root = args.data_root.resolve()
    root.mkdir(parents=True, exist_ok=True)
    if args.prepare:
        # Only the Linux loader lacks a library; the Windows release carries its own.
        print('Nothing to prepare on Windows.' if WINDOWS else f'TurboJPEG ready: {turbojpeg_library(root)}')
        return
    runtime_lock = hold(root / 'lab.runtime.lock')
    if runtime_lock is None:
        raise SystemExit('The private lab is already running or being staged.')
    lab = root / 'lab'
    if WINDOWS:
        # The Windows lab holds hard links or copies; staging records where they came from.
        record = lab / 'game-files.json'
        if not record.is_file():
            raise SystemExit('Run setup_runtime.py --build and stage_runtime.py first.')
        game = Path(json.loads(record.read_text())['game'])
    else:
        game = (lab / 'hlboot.dat').resolve().parent
    if lab == game or game in lab.parents or lab in game.parents:
        raise SystemExit('Lab and installed game must be disjoint.')
    loader = lab / 'coremod/core/host/startup' / ('DeadCellsModding.exe' if WINDOWS else 'DeadCellsModding')
    runtime = lab / 'coremod/mods/KirbySuperStarUltra/Kirby.Runtime.dll'
    if not loader.is_file() or not runtime.is_file():
        raise SystemExit('Run setup_runtime.py --build and stage_runtime.py first.')
    if args.play:
        mod = runtime.parent
        settings = json.loads((mod / 'config.json').read_text())
        # The bridge as tools/nds_build.py names it for this system.
        if not settings.get('EnableOriginalRuntime') or not (mod / 'outfit.pak').is_file() or not (mod / TARGETS[HOST][1]).is_file():
            raise SystemExit('Stage the original runtime and generated outfit pack before using --play.')
    config_path = lab / 'coremod/config/modcore.json'
    config = json.loads(config_path.read_text())
    if config.get('EnableGoldberg') is not False:
        raise SystemExit('Lab must explicitly disable Goldberg.')
    # This flag records the popup as already acknowledged in this private lab.
    config['DCCMWarningPopup'] = True
    config_path.write_text(json.dumps(config, indent=2) + '\n')
    if not WINDOWS and not shutil.which('bwrap'):
        raise SystemExit('bubblewrap is required to protect installed files during the isolated launch.')
    # Every launch is preceded by a snapshot of local and Steam saves.
    saves = [game / 'save']
    # A game in a second Steam library keeps its cloud saves in the main Steam folder,
    # which Windows records in the registry.
    steam = args.steam_root.expanduser().resolve() if args.steam_root else steam_root() if WINDOWS else game.parents[2]
    saves += sorted((steam / 'userdata').glob('*/588650'))
    backup = back_up(saves, root)
    print(f'Saves backed up to {backup}', flush=True)
    before = snapshot(saves)
    (lab / 'save-isolation.ready').unlink(missing_ok=True)
    run = lab / ('play-runs' if args.play else 'smoke-runs') / time.strftime('%Y%m%d-%H%M%S')
    run.mkdir(parents=True, exist_ok=False)
    for path in [lab / 'save', lab / 'tmp', lab / 'cache', lab / 'config', lab / 'data']:
        if path.is_symlink():
            raise SystemExit(f'Lab write directory must not be a symlink: {path}')
        path.mkdir(exist_ok=True)
    env = environment(root)
    if args.play:
        # New diagnostic probes may use their own variables. Normal play must
        # not inherit any of them from a development shell.
        for key in list(env):
            if key.startswith('KIRBY_LAB_'):
                env.pop(key)
    for key in ['KIRBY_LAB_PLAY', 'KIRBY_LAB_INPUT_PLAN', 'KIRBY_LAB_START_GAME', 'KIRBY_LAB_OUTFIT',
                'KIRBY_LAB_RESUME_GAME', 'KIRBY_LAB_SAVE_AFTER', 'KIRBY_LAB_TEST_ABILITY', 'KIRBY_LAB_TEST_SCROLLS', 'KIRBY_LAB_TEST_MENU', 'KIRBY_LAB_TEST_COLLECTOR', 'KIRBY_LAB_TEST_MUTATIONS', 'KIRBY_LAB_TEST_AFFECTS', 'KIRBY_LAB_TEST_HUD', 'KIRBY_LAB_TEST_PAUSE', 'KIRBY_LAB_TEST_LEGENDARY',
                'KIRBY_LAB_CAPTURE_FPS', 'KIRBY_LAB_CROWD_SIZE']:
        env.pop(key, None)
    env.update({
        'DCCM_OverridePath_CORE_ROOT': str(lab / 'coremod'),
        'DCCM_OverridePath_GAME_ROOT': str(lab),
        'DCCM_OverridePath_SAVE_ROOT': str(lab / 'save'),
        'KIRBY_LAB_SAVE_ROOT': str(lab / 'save'),
        'KIRBY_LAB_RUN_ROOT': str(run),
        'DOTNET_BUNDLE_EXTRACT_BASE_DIR': str(lab / 'tmp/dotnet-bundle'),
        'SteamAppId': '588650',
        'SteamGameId': '588650',
    })
    if WINDOWS:
        env.update({'TEMP': str(lab / 'tmp'), 'TMP': str(lab / 'tmp')})
    else:
        env.update({
            'MESA_SHADER_CACHE_DISABLE': '1',
            'XDG_CACHE_HOME': str(lab / 'cache'),
            'XDG_CONFIG_HOME': str(lab / 'config'),
            'XDG_DATA_HOME': str(lab / 'data'),
            'TMPDIR': str(lab / 'tmp'),
            'LD_LIBRARY_PATH': str(lab / 'coremod/core/native/linux-x64'),
        })
    if args.play:
        env['KIRBY_LAB_PLAY'] = '1'
        env['KIRBY_LAB_OUTFIT'] = args.outfit or 'KirbySuperStarUltra'
        # A parent terminal may have inherited smoke-test values; normal play
        # must never inherit automated inputs or disabled display/audio drivers.
        for key in ['KIRBY_LAB_INPUT_PLAN', 'KIRBY_LAB_START_GAME', 'SDL_VIDEODRIVER',
                    'SDL_VIDEO_DRIVER', 'SDL_AUDIODRIVER', 'SDL_AUDIO_DRIVER',
                    'ALSOFT_DRIVERS', 'LIBGL_ALWAYS_SOFTWARE', 'KSSU_NATIVE_TRACE',
                    'DOTNET_DbgEnableMiniDump', 'DOTNET_DbgMiniDumpType', 'DOTNET_DbgMiniDumpName']:
            env.pop(key, None)
    else:
        env.update({'SDL_VIDEODRIVER': 'offscreen', 'SDL_VIDEO_DRIVER': 'offscreen',
                    'SDL_AUDIODRIVER': 'dummy', 'SDL_AUDIO_DRIVER': 'dummy',
                    'ALSOFT_DRIVERS': 'null', 'LIBGL_ALWAYS_SOFTWARE': '1',
                    'DOTNET_DbgEnableMiniDump': '1', 'DOTNET_DbgMiniDumpType': '2',
                    'DOTNET_DbgMiniDumpName': str(run / 'crash-%p.dmp')})
        # A dump with the heap is as large as the game's memory (6-7 GB), and the
        # crashed game holds that memory until it is written.
        if args.crash_dump == 'small':
            env['DOTNET_DbgMiniDumpType'] = '1'
        elif args.crash_dump == 'off':
            for key in ['DOTNET_DbgEnableMiniDump', 'DOTNET_DbgMiniDumpType', 'DOTNET_DbgMiniDumpName']:
                env.pop(key)
        if args.shader_cache:
            env.pop('MESA_SHADER_CACHE_DISABLE')
        if args.gpu:
            # SDL's offscreen driver takes the first EGL device that starts: the
            # GPU once its render node is visible and software drawing is not forced.
            env.pop('LIBGL_ALWAYS_SOFTWARE')
    if not WINDOWS:
        env['LD_PRELOAD'] = str(turbojpeg_library(root))
    if args.input_plan:
        env['KIRBY_LAB_INPUT_PLAN'] = args.input_plan
    if args.probe_ability is not None:
        env['KIRBY_LAB_TEST_ABILITY'] = str(args.probe_ability)
    if args.crowd_size is not None:
        env['KIRBY_LAB_CROWD_SIZE'] = str(args.crowd_size)
    if args.probe_scrolls is not None:
        env['KIRBY_LAB_TEST_SCROLLS'] = args.probe_scrolls
    if args.probe_mutations is not None:
        env['KIRBY_LAB_TEST_MUTATIONS'] = args.probe_mutations
    if args.probe_affects is not None:
        env['KIRBY_LAB_TEST_AFFECTS'] = args.probe_affects
    if args.probe_menu is not None:
        env['KIRBY_LAB_TEST_MENU'] = '2' if args.probe_menu == 'unfiltered' else '1'
    if args.probe_collector is not None:
        env['KIRBY_LAB_TEST_COLLECTOR'] = '2' if args.probe_collector == 'unfiltered' else '1'
    if args.probe_hud:
        env['KIRBY_LAB_TEST_HUD'] = '1'
    if args.probe_pause:
        env['KIRBY_LAB_TEST_PAUSE'] = '1'
    if args.probe_legendary:
        env['KIRBY_LAB_TEST_LEGENDARY'] = '1'
    if args.capture_fps:
        env['KIRBY_LAB_CAPTURE_FPS'] = str(args.capture_fps)
    if args.start_game:
        env['KIRBY_LAB_START_GAME'] = '1'
    if args.resume_game:
        env['KIRBY_LAB_RESUME_GAME'] = '1'
    if args.save_after:
        env['KIRBY_LAB_SAVE_AFTER'] = str(args.save_after)
    if args.outfit:
        env['KIRBY_LAB_OUTFIT'] = args.outfit
    # Offscreen tests inherit no desktop input endpoints. Interactive play uses
    # the user's display and devices. LabIsolation blocks Cloud in both modes.
    if not args.play:
        for key in ['DISPLAY', 'WAYLAND_DISPLAY', 'XAUTHORITY']:
            env.pop(key, None)
    if WINDOWS:
        command = []
        if args.normal_integrity:
            print('Starting the game with your normal rights (--normal-integrity): only the save backup '
                  'and the before/after save check protect your files.', flush=True)
        else:
            # Everything in the lab, the run folder included, may now be written at low integrity.
            try:
                label_low(lab)
            except OSError as error:
                raise SystemExit(f'Windows refused to label the lab low integrity ({error}). '
                                 'With --normal-integrity the game runs with only the save backup and check.')
    else:
        command = ['bwrap', '--die-with-parent', '--unshare-pid', '--ro-bind', '/', '/',
                   '--dev-bind' if args.play else '--dev', '/dev']
        if args.play:
            command += ['/dev']
        elif args.gpu and Path('/dev/dri').is_dir():
            command += ['--dev-bind', '/dev/dri', '/dev/dri']
        command += ['--proc', '/proc', '--tmpfs', '/tmp']
        if args.play and Path('/tmp/.X11-unix').exists():
            command += ['--ro-bind', '/tmp/.X11-unix', '/tmp/.X11-unix']
        if Path('/tmp/steam').exists():
            command += ['--ro-bind', '/tmp/steam', '/tmp/steam']
        command += ['--bind', str(lab), str(lab), '--chdir', str(lab), '--']
    if args.gdb:
        command += ['gdb', '--batch', '--return-child-result', '-ex', 'set pagination off',
                    '-ex', 'set startup-with-shell off', '-ex', 'set follow-fork-mode parent',
                    '-ex', 'handle SIGPWR SIGXCPU SIG34 nostop noprint pass',
                    '-ex', 'run', '-ex', 'thread apply all bt 20', '-ex', 'info registers', '--args']
    command += [str(loader)]
    if args.play:
        print('Opening Kirby in Dead Cells. Saves are private to this mod. Choose New Game to begin.', flush=True)
        print(f'Runtime log: {run / "stdout.log"}', flush=True)
    with (run / 'stdout.log').open('wb') as output:
        if WINDOWS:
            try:
                process = WindowsProcess(command, env, lab, output, low=not args.normal_integrity)
            except OSError as error:
                raise SystemExit(f'Windows refused to start the game ({error}).'
                                 + ('' if args.normal_integrity else ' With --normal-integrity it starts with your normal rights.'))
        else:
            process = subprocess.Popen(command, env=env, stdout=output, stderr=subprocess.STDOUT)
        deadline = time.monotonic() + args.seconds
        stopped = False
        if WINDOWS and args.play and sys.stdin is not None:
            threading.Thread(target=stop_on_request, daemon=True).start()
        try:
            while process.poll() is None and (args.play or time.monotonic() < deadline):
                time.sleep(.1)
        except KeyboardInterrupt:
            stopped = True
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
    after = snapshot(saves)
    log_text = (run / 'stdout.log').read_text(errors='replace')
    fatal = any(marker in log_text for marker in (' FTL]', 'Uncaught .NET Exception', 'Unhandled exception.', 'symbol lookup error:',
                                                 'received signal SIGSEGV', 'received signal SIGABRT'))
    frames_file = run / 'frames.json'
    frames = json.loads(frames_file.read_text()) if frames_file.exists() else {}
    if not args.play:
        from PIL import Image
        for ppm in run.glob('frame-*.ppm'):
            try:
                with Image.open(ppm) as img:
                    img.save(ppm.with_suffix('.png'))
            except OSError:
                # The bounded run can end while the last capture is being written.
                print(f'Skipped incomplete capture {ppm.name}')
            ppm.unlink()
    report = {'run': str(run), 'exit_code': process.returncode,
              'sandbox': ('none' if args.normal_integrity else 'low integrity') if WINDOWS else 'bubblewrap',
              'save_isolation_installed': (lab / 'save-isolation.ready').is_file(),
              'source_saves_unchanged': before == after,
              'offscreen': not args.play, 'gpu': args.gpu, 'shader_cache': args.shader_cache, 'duration_limit_seconds': None if args.play else args.seconds,
              'frames': frames, 'expected_timeout': not args.play and process.returncode == -15, 'fatal_log': fatal}
    (run / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))
    print((run / 'stdout.log').read_text(errors='replace')[-3000:])
    if before != after:
        raise SystemExit('Source save files changed during smoke test; inspect the recorded backups.')
    if fatal:
        raise SystemExit('The runtime reported a fatal error; inspect the smoke log.')
    # A Windows game closed on request ends with TerminateProcess's code 1.
    if process.returncode not in (0, -15) and not (WINDOWS and stopped):
        raise SystemExit('The game exited with an error; inspect the smoke log.')
    if not frames and not args.play:
        raise SystemExit('No rendered frame was captured.')
    if not report['save_isolation_installed']:
        raise SystemExit('Runtime did not reach save-isolation installation.')


if __name__ == '__main__':
    main()
