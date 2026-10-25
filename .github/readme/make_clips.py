#!/usr/bin/env python3
"""Make the README's gameplay clips and banner from lab runs (#274).

    python3 scripts/launch_runtime.py --data-root ROOT --start-game --outfit KirbySuperStarUltra \
        --input-plan combat --capture-fps 15 --seconds 80
    python3 .github/readme/make_clips.py --runs ROOT/lab/smoke-runs

Each clip is a stretch of one lab run's captured frames (`--capture-fps`),
cropped around Kirby and kept at the run's own timing. The runs were recorded
with `CameraZoom` 2 in the staged config.json, so Kirby fills more of the view.
Writes animated WebP files next to this script; docs/RUNTIME_LAUNCH.md records
the runs. Like the release pictures, they stay out of the source archive.
Needs Pillow with WebP; uses the launcher's fonts.
"""
import argparse
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw

PROJECT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(PROJECT / '.github/release'))
from make_media import GOLD, INK, INK_DIM, PINK, font, rounded, shadowed, sky, sparkle, tracked  # noqa: E402

OUT = Path(__file__).resolve().parent
DEFAULT_RUNS = Path.home() / '.local/share/kirby-dead-cells/repros/readme-274/lab/smoke-runs'
# name: (run, first ms, last ms, crop box (x, y, width, height) in the 1024x768 lab frame)
CLIPS = {
    'inhale-copy': ('20261025-114411', 36400, 39000, (110, 100, 800, 450)),
    'parry': ('20261025-114411', 37700, 40400, (190, 100, 800, 450)),
    'copy-attack': ('20261025-114411', 39000, 42300, (110, 100, 800, 450)),
    'spit-star': ('20261025-114800', 35300, 37900, (48, 30, 960, 540)),
    'ability-star': ('20261025-114800', 41000, 45000, (0, 20, 960, 540)),
    'ability-item': ('20261025-175554', 30700, 32400, (200, 90, 760, 428)),
    'f-slot': ('20261025-175838', 31400, 37900, (64, 10, 896, 504)),
    'projectiles': ('20261025-193443', 29900, 32900, (200, 100, 800, 450)),
    'boss': ('20261025-194833', 25500, 30700, (244, 160, 780, 439)),
}
# The gallery: one ability-damage lab run per copy ability; the ms is its first hit.
ABILITIES = {
    'fire': ('20261025-193814', 29734), 'sword': ('20261025-193923', 30492),
    'hammer': ('20261025-194032', 29471), 'ice': ('20261025-194141', 30227),
    'wheel': ('20261025-194250', 30162), 'beam': ('20261025-194359', 31239),
    'ninja': ('20261025-194508', 30884), 'jet': ('20261025-194616', 31307),
    'cutter': ('20261025-194725', 29891),
}
ABILITY_CROP = (110, 100, 800, 450)
# name: (run, frame file, crop box); still pictures.
STILLS = {
    'pause': ('20261025-195022', 'frame-012760.png', (0, 84, 1024, 300)),
}
# The banner plays this clip in its window.
BANNER_CLIP = ('20261025-114411', 36400, 42300, (110, 100, 800, 450))


def frames(runs: Path, spec: tuple, width: int) -> tuple[list[Image.Image], list[int]]:
    """The run's frames in the stretch, cropped and scaled, with each one's time on screen in ms."""
    run, start, end, (x, y, w, h) = spec
    caps = [json.loads(line) for line in (runs / run / 'captures.jsonl').read_text().splitlines()]
    caps = [c for c in caps if c.get('gameplay') and start <= c['elapsedMs'] <= end]
    if not caps:
        raise SystemExit(f'{run} has no gameplay frames between {start} and {end} ms.')
    images = [Image.open(runs / run / c['file']).convert('RGB').crop((x, y, x + w, y + h))
              .resize((width, round(h * width / w)), Image.LANCZOS) for c in caps]
    times = [c['elapsedMs'] for c in caps]
    # The last frame holds a moment before the clip loops.
    return images, [b - a for a, b in zip(times, times[1:])] + [700]


def save(images: list[Image.Image], durations: list[int], out: Path) -> None:
    images[0].save(out, save_all=True, append_images=images[1:], duration=durations, loop=0,
                   quality=72, method=6, minimize_size=True)
    print(f'{out.relative_to(PROJECT)}: {len(images)} frames, {out.stat().st_size / 1e6:.2f} MB')


def clip(runs: Path, name: str, spec: tuple, width: int = 640, folder: Path = OUT) -> None:
    images, durations = frames(runs, spec, width)
    save([rounded(image, 18 * width // 640) for image in images], durations, folder / f'{name}.webp')


def banner(runs: Path) -> None:
    w, h = 2000, 860
    back = sky((w, h), seed=11, clear=(0, 120, 860, 760))
    draw = ImageDraw.Draw(back)
    x = 110
    tracked(draw, (x, 214), 'A KIRBY MOD FOR DEAD CELLS', font('Nunito', 32, 900), PINK, 7)
    title = font('Fredoka', 150, 700)
    draw.text((x - 6, 262), "Kirby's", font=title, fill=INK)
    draw.text((x - 6, 420), 'Dead Land', font=title, fill=INK)
    body = font('Nunito', 42, 600)
    draw.text((x, 640), 'Play as Kirby in Dead Cells.', font=body, fill=INK_DIM)
    draw.text((x, 698), 'Inhale enemies. Copy their powers.', font=body, fill=INK_DIM)
    sparkle(draw, x + 640, 330, 36, GOLD)
    sparkle(draw, x + 708, 398, 15, PINK)
    images, durations = frames(runs, BANNER_CLIP, 1040)
    margin = 60
    shots = []
    for image in images:
        canvas = back.copy()
        window = shadowed(rounded(image, 26), margin, 34, 200, 24)
        canvas.alpha_composite(window, (w - window.width - 10, (h - window.height) // 2 + 8))
        shots.append(canvas.convert('RGB'))
    save(shots, durations, OUT / 'banner.webp')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    parser.add_argument('--runs', type=Path, default=DEFAULT_RUNS, help='The lab smoke-runs folder holding the runs in CLIPS.')
    parser.add_argument('names', nargs='*', help='Only these clips (or "banner"); all by default.')
    args = parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    for name, spec in CLIPS.items():
        if not args.names or name in args.names:
            clip(args.runs, name, spec)
    for name, (run, file, (x, y, w, h)) in STILLS.items():
        if not args.names or name in args.names:
            image = Image.open(args.runs / run / file).convert('RGB').crop((x, y, x + w, y + h))
            save([rounded(image.resize((800, round(h * 800 / w)), Image.LANCZOS), 18)], [0], OUT / f'{name}.webp')
    (OUT / 'abilities').mkdir(exist_ok=True)
    for name, (run, hit) in ABILITIES.items():
        if not args.names or name in args.names:
            clip(args.runs, name, (run, hit - 1700, hit + 1300, ABILITY_CROP), 480, OUT / 'abilities')
    if not args.names or 'banner' in args.names:
        banner(args.runs)


if __name__ == '__main__':
    main()
