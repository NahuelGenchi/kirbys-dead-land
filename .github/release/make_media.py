#!/usr/bin/env python3
"""Make the pictures for a release page from the launcher's own screenshots.

    cargo build --manifest-path launcher/Cargo.toml
    python3 .github/release/make_media.py

Runs the launcher's --screenshot mode (never with --art-from, so the pictures use
stand-in art and hold nothing from the game) and writes banner.png, download.png
and one framed screenshot per step to .github/release/v<launcher version>/, the
pictures that notes.py links. Needs Pillow; uses the launcher's fonts.
"""
import argparse
import random
import subprocess
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

PROJECT = Path(__file__).resolve().parents[2]
FONTS = PROJECT / 'launcher/ui/fonts'
# The launcher's colors (launcher/ui/theme.slint), sampled from its screenshots.
SKY_TOP, SKY_BOTTOM, NIGHT = (8, 10, 36), (32, 28, 93), (11, 10, 41)
PINK, ORANGE, GOLD, INK, INK_DIM = (255, 114, 176), (255, 152, 110), (255, 201, 74), (255, 255, 255), (201, 198, 232)
# The steps the release page shows, from the launcher's scenes.
STEPS = {'step-1-checks': '01-checks-first-run', 'step-2-fix': '02-checks-problems',
         'step-3-setup': '04-setup-running', 'step-4-play': '07-home'}


def font(name: str, size: int, weight: int) -> ImageFont.FreeTypeFont:
    face = ImageFont.truetype(str(FONTS / f'{name}.ttf'), size)
    axes = face.get_variation_axes()
    face.set_variation_by_axes([weight] + [axis['default'] for axis in axes[1:]])
    return face


def gradient(size: tuple[int, int], top: tuple, bottom: tuple, horizontal: bool = False) -> Image.Image:
    w, h = size
    steps = w if horizontal else h
    line = Image.new('RGB', (steps, 1))
    line.putdata([tuple(round(a + (b - a) * i / max(steps - 1, 1)) for a, b in zip(top, bottom)) for i in range(steps)])
    return line.resize((w, h)) if horizontal else line.rotate(-90, expand=True).resize((w, h))


def glow(canvas: Image.Image, center: tuple[int, int], radius: int, color: tuple, alpha: int) -> None:
    layer = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    x, y = center
    ImageDraw.Draw(layer).ellipse((x - radius, y - radius, x + radius, y + radius), fill=(*color, alpha))
    canvas.alpha_composite(layer.filter(ImageFilter.GaussianBlur(radius // 2)))


def sparkle(draw: ImageDraw.ImageDraw, x: float, y: float, r: float, color: tuple) -> None:
    """The launcher's four-point star."""
    k = r * 0.22
    draw.polygon([(x, y - r), (x + k, y - k), (x + r, y), (x + k, y + k), (x, y + r), (x - k, y + k), (x - r, y), (x - k, y - k)], fill=color)


def sky(size: tuple[int, int], seed: int, clear: tuple = (0, 0, 0, 0)) -> Image.Image:
    """The night sky; `clear` is a box (left, top, right, bottom) kept free of stars for text."""
    w, h = size
    canvas = gradient(size, SKY_TOP, SKY_BOTTOM).convert('RGBA')
    glow(canvas, (int(w * 0.78), int(h * 0.42)), int(h * 0.55), PINK, 46)
    glow(canvas, (int(w * 0.12), int(h * 0.85)), int(h * 0.5), (90, 70, 200), 60)
    rng = random.Random(seed)
    stars = Image.new('RGBA', size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(stars)
    for _ in range(w * h // 9000):
        x, y, r = rng.uniform(0, w), rng.uniform(0, h * 0.8), rng.choice((1.2, 1.6, 2.2, 3))
        if clear[0] <= x <= clear[2] and clear[1] <= y <= clear[3] and rng.random() < 0.85:
            continue
        draw.ellipse((x - r, y - r, x + r, y + r), fill=(255, 255, 255, rng.randint(60, 220)))
    for _ in range(9):
        x, y = rng.uniform(0, w), rng.uniform(0, h * 0.7)
        if not (clear[0] <= x <= clear[2] and clear[1] <= y <= clear[3]):
            sparkle(draw, x, y, rng.uniform(8, 18), (*rng.choice((GOLD, PINK, INK)), 220))
    canvas.alpha_composite(stars)
    return canvas


def castle(canvas: Image.Image, seed: int) -> None:
    """Hills and a castle skyline along the bottom, like the launcher's."""
    w, h = canvas.size
    rng = random.Random(seed)
    layer = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    draw.ellipse((-w * 0.2, h * 0.86, w * 0.75, h * 1.4), fill=(22, 18, 64, 255))
    base = h * 0.9
    x = w * 0.62
    while x < w:
        tw, th = rng.uniform(60, 130), rng.uniform(90, 260)
        draw.rectangle((x, base - th, x + tw, h), fill=(*NIGHT, 255))
        draw.polygon([(x - 8, base - th), (x + tw / 2, base - th - tw * 0.8), (x + tw + 8, base - th)], fill=(*NIGHT, 255))
        for _ in range(rng.randint(0, 2)):
            wx, wy = x + rng.uniform(tw * 0.25, tw * 0.65), base - th + rng.uniform(30, th - 20)
            draw.rounded_rectangle((wx, wy, wx + 10, wy + 20), radius=5, fill=(*GOLD, 230))
        wall = rng.uniform(80, 200)
        draw.rectangle((x + tw, base - 40, x + tw + wall, h), fill=(*NIGHT, 255))
        for cx in range(int(x + tw), int(x + tw + wall), 24):
            draw.rectangle((cx, base - 56, cx + 12, base - 40), fill=(*NIGHT, 255))
        x += tw + wall
    draw.rectangle((0, h * 0.97, w, h), fill=(*NIGHT, 255))
    canvas.alpha_composite(layer)


def rounded(image: Image.Image, radius: int) -> Image.Image:
    mask = Image.new('L', image.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, image.width - 1, image.height - 1), radius=radius, fill=255)
    out = image.convert('RGBA')
    out.putalpha(mask)
    border = Image.new('RGBA', image.size, (0, 0, 0, 0))
    ImageDraw.Draw(border).rounded_rectangle((0, 0, image.width - 1, image.height - 1), radius=radius,
                                            outline=(255, 255, 255, 48), width=max(2, radius // 12))
    out.alpha_composite(border)
    return out


def shadowed(image: Image.Image, margin: int, blur: int, alpha: int, offset: int) -> Image.Image:
    """The image on a transparent canvas with a soft shadow below it."""
    canvas = Image.new('RGBA', (image.width + 2 * margin, image.height + 2 * margin), (0, 0, 0, 0))
    shadow = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    shadow.paste((10, 8, 30, alpha), (margin, margin + offset), image.split()[3])
    canvas.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(blur)))
    canvas.alpha_composite(image, (margin, margin))
    return canvas


def window(shot: Path, width: int) -> Image.Image:
    image = Image.open(shot).convert('RGB')
    image = image.resize((width, round(image.height * width / image.width)), Image.LANCZOS)
    return rounded(image, width // 40)


def tracked(draw: ImageDraw.ImageDraw, xy: tuple[int, int], text: str, face: ImageFont.FreeTypeFont, fill: tuple, spacing: int) -> None:
    """Text with extra space between letters, like the launcher's small labels."""
    x, y = xy
    for char in text:
        draw.text((x, y), char, font=face, fill=fill)
        x += face.getlength(char) + spacing


def banner(shots: Path, out: Path, version: str) -> None:
    w, h = 2400, 1100
    canvas = sky((w, h), seed=7, clear=(0, 200, 1120, 900))
    castle(canvas, seed=3)
    back = shadowed(window(shots / '04-setup-running.png', 880), 60, 32, 170, 22)
    front = shadowed(window(shots / '01-checks-first-run.png', 1000), 70, 40, 210, 28)
    canvas.alpha_composite(back, (w - back.width + 20, 30))
    canvas.alpha_composite(front, (1130, h - front.height + 10))
    draw = ImageDraw.Draw(canvas)
    x = 130
    tracked(draw, (x, 270), f'NEW · VERSION {version}', font('Nunito', 40, 900), PINK, 9)
    title = font('Fredoka', 160, 700)
    draw.text((x - 6, 330), "Kirby's", font=title, fill=INK)
    draw.text((x - 6, 500), 'Dead Land', font=title, fill=INK)
    body = font('Nunito', 46, 600)
    draw.text((x, 730), 'Play as Kirby in Dead Cells on Linux.', font=body, fill=INK_DIM)
    draw.text((x, 795), 'Now with a launcher: no terminal needed.', font=body, fill=INK_DIM)
    sparkle(draw, x + 690, 395, 40, GOLD)
    sparkle(draw, x + 765, 470, 17, PINK)
    canvas.convert('RGB').save(out / 'banner.png', optimize=True)


def download_button(out: Path) -> None:
    w, h, margin = 900, 176, 50
    pill = gradient((w, h), PINK, ORANGE, horizontal=True).convert('RGBA')
    mask = Image.new('L', (w, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, w - 1, h - 1), radius=h // 2, fill=255)
    pill.putalpha(mask)
    canvas = Image.new('RGBA', (w + 2 * margin, h + 2 * margin), (0, 0, 0, 0))
    halo = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    halo.paste((*PINK, 120), (margin, margin + 14), mask)
    canvas.alpha_composite(halo.filter(ImageFilter.GaussianBlur(22)))
    canvas.alpha_composite(pill, (margin, margin))
    draw = ImageDraw.Draw(canvas)
    # A download arrow over a tray, drawn like the launcher's line icons.
    ix, iy, s = margin + 110, margin + h // 2, 34
    line = dict(fill=INK, width=10)
    draw.line((ix, iy - s, ix, iy + s * 0.45), **line)
    draw.line((ix - s * 0.6, iy - s * 0.1, ix, iy + s * 0.5), **line)
    draw.line((ix + s * 0.6, iy - s * 0.1, ix, iy + s * 0.5), **line)
    draw.line((ix - s, iy + s * 0.55, ix - s, iy + s), **line)
    draw.line((ix - s, iy + s, ix + s, iy + s), **line)
    draw.line((ix + s, iy + s * 0.55, ix + s, iy + s), **line)
    text = font('Fredoka', 70, 600)
    draw.text((margin + 180, margin + h // 2), 'Download for Linux', font=text, fill=INK, anchor='lm')
    canvas.save(out / 'download.png', optimize=True)


def steps(shots: Path, out: Path) -> None:
    for name, scene in STEPS.items():
        image = Image.open(shots / f'{scene}.png').convert('RGB')
        if scene == '07-home':
            # Leave out the row of ability icons, which are blank stand-ins here.
            image = image.crop((0, 0, image.width, round(image.height * 0.84)))
        image = image.resize((1600, round(image.height * 1600 / image.width)), Image.LANCZOS)
        shadowed(rounded(image, 36), 48, 26, 140, 16).save(out / f'{name}.png', optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--launcher', type=Path, default=PROJECT / 'launcher/target/debug/kirbys-dead-land',
                        help='The built launcher (default: the debug build).')
    args = parser.parse_args()
    version = subprocess.run([args.launcher, '--version'], check=True, capture_output=True, text=True).stdout.split()[-1]
    out = Path(__file__).resolve().parent / f'v{version}'
    out.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory() as shots:
        subprocess.run([args.launcher, '--screenshot', shots, '--scale', '2'], check=True, stdout=subprocess.DEVNULL)
        banner(Path(shots), out, version)
        download_button(out)
        steps(Path(shots), out)
    for path in sorted(out.glob('*.png')):
        print(f'{path.relative_to(PROJECT)}: {path.stat().st_size // 1024} KB')


if __name__ == '__main__':
    main()
