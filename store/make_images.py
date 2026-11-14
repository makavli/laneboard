"""The package logos and the Store screenshots, from the app icon and the demo snapshots tools/check.sh renders.

    python -I store/make_images.py <icon.png> <logo dir> <snapshot dir> <screenshot dir>

Screenshots are 1920x1080 (the Store wants at least 1366x768): the flyout where it really sits, at the lower right
above the taskbar, with one line saying what the picture shows.
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

LOGOS = {"StoreLogo.png": (50, 50), "Square44x44Logo.png": (44, 44), "Square150x150Logo.png": (150, 96)}

SHOTS = [  # snapshot, background top and bottom, text colour, headline
    ("snap-light-view.png", (226, 234, 246), (198, 212, 236), (24, 28, 36), "Every project, at a glance"),
    ("snap-dark-view.png", (28, 34, 48), (14, 17, 26), (240, 242, 247), "What needs you comes first"),
    ("snap-advanced.png", (30, 28, 46), (14, 13, 24), (240, 242, 247), "Pull requests, checks and handoff notes, side by side"),
    ("snap-settings.png", (24, 36, 40), (12, 18, 22), (240, 242, 247), "Your folders, your theme, your notifications"),
]


def logos(icon: Path, out: Path) -> None:
    src = Image.open(icon).convert("RGBA")
    for name, (size, logo) in LOGOS.items():
        tile = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        tile.alpha_composite(src.resize((logo, logo), Image.LANCZOS), ((size - logo) // 2, (size - logo) // 2))
        tile.save(out / name)


def font(px: int) -> ImageFont.ImageFont:
    for f in ("C:/Windows/Fonts/segoeuisl.ttf", "C:/Windows/Fonts/segoeui.ttf"):
        if Path(f).exists():
            return ImageFont.truetype(f, px)
    return ImageFont.load_default(px)


def screenshot(snap: Path, top, bottom, ink, text: str, out: Path) -> None:
    W, H = 1920, 1080
    bg = Image.linear_gradient("L").resize((W, H))
    img = Image.composite(Image.new("RGB", (W, H), bottom), Image.new("RGB", (W, H), top), bg).convert("RGBA")

    flyout = Image.open(snap).convert("RGBA")
    h = 980
    flyout = flyout.resize((round(flyout.width * h / flyout.height), h), Image.LANCZOS)
    x, y = W - flyout.width - 24, H - h - 60  # 60 px: where a taskbar would start
    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle((x, y + 10, x + flyout.width, y + h + 10), 16, fill=(0, 0, 0, 110))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(24)))
    img.alpha_composite(flyout, (x, y))

    d = ImageDraw.Draw(img)
    words, lines, f = text.split(), [], font(64)
    for word in words:  # wrap to the free space left of the flyout
        if lines and d.textlength(lines[-1] + " " + word, font=f) < x - 220:
            lines[-1] += " " + word
        else:
            lines.append(word)
    for i, line in enumerate(lines):
        d.text((120, 380 + i * 84), line, font=f, fill=ink)
    img.convert("RGB").save(out)


def main() -> None:
    icon, logo_dir, snaps, shots = map(Path, sys.argv[1:5])
    logos(icon, logo_dir)
    for i, (snap, top, bottom, ink, text) in enumerate(SHOTS, 1):
        screenshot(snaps / snap, top, bottom, ink, text, shots / f"screenshot-{i}.png")
    for p in [*sorted(logo_dir.glob("*.png")), *sorted(shots.glob("screenshot-*.png"))]:
        assert Image.open(p).size in [(s, s) for s, _ in LOGOS.values()] + [(1920, 1080)], p  # the sizes the Store checks
    print(f"   {len(LOGOS)} logos, {len(SHOTS)} screenshots (1920x1080) in {shots}")


if __name__ == "__main__":
    main()
