"""Erzeugt die App- und Tray-Icons fuer wStreamAudio.

Motiv: ein abstraktes S-foermiges Audio-Stream-Zeichen ohne Hintergrund.
Keine Lautsprecher, keine Funkwellen, keine alte Farbwelt.

Schreibt nach src/wStreamAudio/Assets/:
- App.ico
- TrayIdle.ico
- TrayActive.ico
- App*.png
- Tray*.png
- App.svg

Ausfuehren:
    python tools/generate-icons.py

Voraussetzung: Pillow (>=10) installiert.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageFilter
except ImportError:
    sys.exit("Pillow fehlt - bitte 'python -m pip install pillow' ausfuehren.")

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "src" / "wStreamAudio" / "Assets"

APP_ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]
APP_PNG_SIZES = [16, 24, 32, 44, 48, 64, 96, 128, 150, 256, 310, 512]
TRAY_SIZES = [16, 20, 24, 32, 40, 48]

TRANSPARENT = (0, 0, 0, 0)
GRAPHITE = (31, 35, 38, 255)
GRAPHITE_SOFT = (68, 75, 79, 255)
SIGNAL = (0, 184, 169, 255)
SIGNAL_DARK = (0, 116, 116, 255)
LIVE = (55, 205, 105, 255)
LIVE_DARK = (20, 116, 58, 255)


def cubic(
    p0: tuple[float, float],
    p1: tuple[float, float],
    p2: tuple[float, float],
    p3: tuple[float, float],
    steps: int,
) -> list[tuple[float, float]]:
    pts = []
    for i in range(steps):
        t = i / (steps - 1)
        u = 1 - t
        x = u**3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t**3 * p3[0]
        y = u**3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t**3 * p3[1]
        pts.append((x, y))
    return pts


def s_path(s: int, *, small: bool) -> list[tuple[float, float]]:
    if small:
        return cubic((s * 0.86, s * 0.14), (s * 0.02, s * 0.02), (s * 0.04, s * 0.43), (s * 0.50, s * 0.50), 30) + cubic(
            (s * 0.50, s * 0.50), (s * 0.96, s * 0.57), (s * 0.98, s * 0.98), (s * 0.14, s * 0.86), 30
        )[1:]
    return cubic((s * 0.90, s * 0.10), (s * 0.00, s * 0.00), (s * 0.02, s * 0.40), (s * 0.50, s * 0.50), 56) + cubic(
        (s * 0.50, s * 0.50), (s * 0.98, s * 0.60), (s * 1.00, s * 1.00), (s * 0.10, s * 0.90), 56
    )[1:]


def draw_audio_ticks(d: ImageDraw.ImageDraw, s: int, *, small: bool) -> None:
    if small:
        ticks = [(0.30, 0.41, 0.59), (0.43, 0.31, 0.69), (0.56, 0.40, 0.60)]
        width = max(2, s // 10)
    else:
        ticks = [(0.28, 0.41, 0.59), (0.39, 0.26, 0.74), (0.50, 0.36, 0.64), (0.61, 0.31, 0.69)]
        width = max(3, s // 22)
    for x, y0, y1 in ticks:
        d.rounded_rectangle(
            [s * x - width / 2, s * y0, s * x + width / 2, s * y1],
            radius=width / 2,
            fill=SIGNAL,
        )


def draw_symbol(d: ImageDraw.ImageDraw, s: int, *, active: bool, small: bool) -> None:
    path = s_path(s, small=small)
    body_width = max(4, s // (6 if small else 8))
    inner_width = max(2, s // (13 if small else 16))

    d.line(path, fill=GRAPHITE, width=body_width, joint="curve")
    d.line(path, fill=SIGNAL, width=inner_width, joint="curve")

    r = max(3, s // (7 if small else 10))
    for x, y, fill in (
        (path[0][0], path[0][1], SIGNAL),
        (path[len(path) // 2][0], path[len(path) // 2][1], SIGNAL),
        (path[-1][0], path[-1][1], SIGNAL),
    ):
        d.ellipse([x - r, y - r, x + r, y + r], fill=fill)
        d.ellipse([x - r * 0.42, y - r * 0.42, x + r * 0.42, y + r * 0.42], fill=TRANSPARENT)

    draw_audio_ticks(d, s, small=small)

    if not small:
        for phase, alpha in ((0, 165), (math.pi, 110)):
            pts = []
            for i in range(44):
                t = i / 43
                x = s * (0.18 + 0.64 * t)
                y = s * (0.50 + math.sin(t * math.tau * 2 + phase) * 0.040)
                pts.append((x, y))
            d.line(pts, fill=(0, 116, 116, alpha), width=max(2, s // 70), joint="curve")

    if active:
        live_r = max(3, round(s * (0.105 if small else 0.080)))
        live_x = s * 0.83
        live_y = s * 0.17
        d.ellipse(
            [live_x - live_r, live_y - live_r, live_x + live_r, live_y + live_r],
            fill=LIVE,
            outline=LIVE_DARK,
            width=max(1, s // 64),
        )


def render(size: int, *, active: bool, tray: bool) -> Image.Image:
    scale = 5 if size <= 32 else 3
    canvas = size * scale
    img = Image.new("RGBA", (canvas, canvas), TRANSPARENT)
    draw_symbol(ImageDraw.Draw(img), canvas, active=active, small=tray or size <= 32)
    return img.resize((size, size), Image.Resampling.LANCZOS)


def save_ico(path: Path, sizes: list[int], renderer) -> None:
    sizes_sorted = sorted(set(sizes), reverse=True)
    images = [renderer(s) for s in sizes_sorted]
    images[0].save(
        path,
        format="ICO",
        sizes=[(s, s) for s in sizes_sorted],
        append_images=images[1:],
    )
    print(f"  {path.name} ({', '.join(f'{s}x{s}' for s in sizes_sorted)})")


def save_png_set(prefix: str, sizes: list[int], renderer) -> None:
    for size in sizes:
        renderer(size).save(OUT / f"{prefix}{size}.png", "PNG")
    print(f"  {prefix}*.png ({', '.join(str(s) for s in sizes)} px)")


def save_svg() -> None:
    svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" role="img" aria-label="wStreamAudio icon">
  <path d="M230 26C0 0 5 102 128 128c123 26 128 256 26 230" fill="none" stroke="#1f2326" stroke-width="32" stroke-linecap="round" stroke-linejoin="round"/>
  <path d="M230 26C0 0 5 102 128 128c123 26 128 256 26 230" fill="none" stroke="#00b8a9" stroke-width="16" stroke-linecap="round" stroke-linejoin="round"/>
  <circle cx="230" cy="26" r="26" fill="#00b8a9"/>
  <circle cx="128" cy="128" r="25" fill="#00b8a9"/>
  <circle cx="26" cy="230" r="26" fill="#00b8a9"/>
  <circle cx="230" cy="26" r="11" fill="none"/>
  <circle cx="128" cy="128" r="11" fill="none"/>
  <circle cx="26" cy="230" r="11" fill="none"/>
  <rect x="72" y="105" width="14" height="46" rx="7" fill="#00b8a9"/>
  <rect x="100" y="67" width="14" height="122" rx="7" fill="#00b8a9"/>
  <rect x="128" y="92" width="14" height="72" rx="7" fill="#00b8a9"/>
  <rect x="156" y="80" width="14" height="96" rx="7" fill="#00b8a9"/>
</svg>
"""
    (OUT / "App.svg").write_text(svg, encoding="utf-8")
    print("  App.svg")


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    print(f"Erzeuge Icons in {OUT} ...")

    save_ico(OUT / "App.ico", APP_ICO_SIZES, lambda s: render(s, active=False, tray=False))
    save_ico(OUT / "TrayIdle.ico", TRAY_SIZES, lambda s: render(s, active=False, tray=True))
    save_ico(OUT / "TrayActive.ico", TRAY_SIZES, lambda s: render(s, active=True, tray=True))
    save_png_set("App", APP_PNG_SIZES, lambda s: render(s, active=False, tray=False))
    save_png_set("TrayIdle", TRAY_SIZES, lambda s: render(s, active=False, tray=True))
    save_png_set("TrayActive", TRAY_SIZES, lambda s: render(s, active=True, tray=True))
    save_svg()

    print("Fertig.")


if __name__ == "__main__":
    main()
