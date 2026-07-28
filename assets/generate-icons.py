#!/usr/bin/env python3
"""Generate every raster icon used by the web app and the Revit add-in.

Source of truth for the shape is assets/icon.svg; this script draws the same
geometry with Pillow so the repo does not depend on an SVG rasteriser.

    pip install pillow
    python assets/generate-icons.py

Outputs (all regenerated, never hand-edited):
    web/public/icon-{192,512}.png   PWA / manifest
    web/public/apple-icon.png       180px, Apple touch icon
    web/public/favicon.ico          16/32/48
    web/public/og.png               1200x630 social card
    addin/src/CircuitSync.Ui/Resources/ribbon-{16,32}.png
    assets/icon-1024.png            master render
"""

from __future__ import annotations

import os
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

TOP = (58, 160, 255)
BOTTOM = (0, 87, 216)
WHITE = (255, 255, 255)

# Geometry in a 0..100 design space, matching assets/icon.svg.
BOLT = [(60, 5), (28, 55), (47, 55), (40, 95), (74, 43), (53, 43)]
TRACES = [((14, 26), (30, 26)), ((14, 50), (24, 50)), ((70, 74), (86, 74)), ((78, 50), (86, 50))]
NODES = [(14, 26), (14, 50), (86, 74), (86, 50)]
CORNER_RATIO = 0.23
SS = 8  # supersampling factor


def _gradient(size: int) -> Image.Image:
    grad = Image.new("RGB", (1, size))
    px = grad.load()
    for y in range(size):
        t = y / max(1, size - 1)
        px[0, y] = tuple(round(TOP[i] + (BOTTOM[i] - TOP[i]) * t) for i in range(3))
    return grad.resize((size, size), Image.NEAREST)


def render(size: int, *, rounded: bool = True, detail: bool = True) -> Image.Image:
    """Render the mark at `size` px. `detail` draws the circuit nodes."""
    n = size * SS
    scale = n / 100.0

    base = _gradient(n).convert("RGBA")

    mask = Image.new("L", (n, n), 0)
    md = ImageDraw.Draw(mask)
    if rounded:
        md.rounded_rectangle([0, 0, n - 1, n - 1], radius=round(n * CORNER_RATIO), fill=255)
    else:
        md.rectangle([0, 0, n - 1, n - 1], fill=255)
    base.putalpha(mask)

    fg = ImageDraw.Draw(base)
    if detail:
        faint = WHITE + (140,)
        for (x1, y1), (x2, y2) in TRACES:
            fg.line([x1 * scale, y1 * scale, x2 * scale, y2 * scale],
                    fill=faint, width=round(3.2 * scale), joint="curve")
        for cx, cy in NODES:
            r = 4 * scale
            fg.ellipse([cx * scale - r, cy * scale - r, cx * scale + r, cy * scale + r], fill=faint)
    fg.polygon([(x * scale, y * scale) for x, y in BOLT], fill=WHITE + (255,))

    return base.resize((size, size), Image.LANCZOS)


def save(img: Image.Image, *parts: str) -> None:
    path = os.path.join(ROOT, *parts)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)
    print("wrote", os.path.relpath(path, ROOT))


def main() -> None:
    save(render(1024), "assets", "icon-1024.png")

    for size in (192, 512):
        save(render(size), "web", "public", f"icon-{size}.png")
    save(render(180), "web", "public", "apple-icon.png")

    ico = render(256)
    path = os.path.join(ROOT, "web", "public", "favicon.ico")
    ico.save(path, sizes=[(16, 16), (32, 32), (48, 48)])
    print("wrote", os.path.relpath(path, ROOT))

    # Social card: the mark on a near-white field, same hue family.
    card = Image.new("RGBA", (1200, 630), (247, 248, 250, 255))
    mark = render(232)
    card.alpha_composite(mark, (484, 150))
    d = ImageDraw.Draw(card)
    d.rectangle([0, 626, 1200, 630], fill=(0, 87, 216, 255))
    save(card.convert("RGB"), "web", "public", "og.png")

    # Revit ribbon: 32px large image, 16px small. No detail at 16px — it muddies.
    save(render(32), "addin", "src", "CircuitSync.Ui", "Resources", "ribbon-32.png")
    save(render(16, detail=False), "addin", "src", "CircuitSync.Ui", "Resources", "ribbon-16.png")


if __name__ == "__main__":
    main()
