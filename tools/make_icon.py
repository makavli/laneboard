"""Draws the app icon (three lanes, each with a progress dot) into src/Laneboard/Assets/laneboard.ico.

Run: python tools/make_icon.py   (needs Pillow)
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent.parent / "src" / "Laneboard" / "Assets" / "laneboard.ico"
S = 1024  # drawn large, then downscaled for each size


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def draw():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    # Diagonal gradient tile: Windows-blue to violet.
    grad = Image.new("RGBA", (S, S))
    top, bottom = (0, 120, 212), (124, 77, 255)
    px = grad.load()
    for y in range(S):
        for x in range(S):
            px[x, y] = lerp(top, bottom, (x + y) / (2 * S)) + (255,)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((40, 40, S - 40, S - 40), radius=220, fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    lanes = [(300, 0.78), (512, 0.52), (724, 0.66)]  # y centre, progress of the dot
    left, right, h = 210, S - 210, 92
    for y, p in lanes:
        d.rounded_rectangle((left, y - h // 2, right, y + h // 2), radius=h // 2, fill=(255, 255, 255, 70))
        x = left + (right - left) * p
        d.rounded_rectangle((left, y - h // 2, x, y + h // 2), radius=h // 2, fill=(255, 255, 255, 235))
        r = 70
        d.ellipse((x - r, y - r, x + r, y + r), fill=(255, 255, 255, 255))
    return img


def main():
    img = draw()
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    OUT.parent.mkdir(parents=True, exist_ok=True)
    img.resize((256, 256), Image.LANCZOS).save(OUT, sizes=[(s, s) for s in sizes])
    img.resize((512, 512), Image.LANCZOS).save(OUT.with_suffix(".png"))
    print("wrote", OUT)


if __name__ == "__main__":
    sys.exit(main())
