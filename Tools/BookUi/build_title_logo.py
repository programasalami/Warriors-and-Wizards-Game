"""Builds the main-menu logo (Content/Ui/Title/Logo.png) from Desktop/TitleLogo.png.

The source is an RGB picture with a fake-transparency checkerboard (white / light grey) baked into its background, so
the background is removed here: every bright, colourless pixel connected to the picture's border becomes transparent
(flood fill - the logo's own dark outline stops it), then the edge is cleaned up. The result is cropped to the logo and
scaled down to the size the title screen draws it at (2x the design size, drawn smaller with a linear filter).
Run from the repo root: python Tools/BookUi/build_title_logo.py"""
import os
from collections import deque

from PIL import Image, ImageFilter

SRC = os.path.expanduser('~/Desktop/TitleLogo.png')
OUT = 'WaW-Client/WaWClient/Content/Ui/Title/Logo.png'
OUT_H = 560   # stored height; width follows the cropped aspect ratio

BRIGHT = 195      # min(r,g,b) above this ...
COLORLESS = 30    # ... and (max-min) below this counts as checkerboard background

im = Image.open(SRC).convert('RGB')
w, h = im.size
px = im.load()


def is_bg(x, y):
    r, g, b = px[x, y]
    return min(r, g, b) > BRIGHT and max(r, g, b) - min(r, g, b) < COLORLESS


bg = bytearray(w * h)
q = deque()
for x in range(w):
    for y in (0, h - 1):
        if is_bg(x, y) and not bg[y * w + x]:
            bg[y * w + x] = 1
            q.append((x, y))
for y in range(h):
    for x in (0, w - 1):
        if is_bg(x, y) and not bg[y * w + x]:
            bg[y * w + x] = 1
            q.append((x, y))
while q:
    x, y = q.popleft()
    for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
        if 0 <= nx < w and 0 <= ny < h and not bg[ny * w + nx] and is_bg(nx, ny):
            bg[ny * w + nx] = 1
            q.append((nx, ny))

alpha = Image.new('L', (w, h), 255)
alpha.putdata([0 if v else 255 for v in bg])
# shave a pixel of any light fringe left on the edge, then soften the cut a touch
alpha = alpha.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(0.6))

rgba = im.convert('RGBA')
rgba.putalpha(alpha)
rgba = rgba.crop(rgba.getbbox())
print('cropped', rgba.size)
ratio = OUT_H / rgba.size[1]
rgba = rgba.resize((round(rgba.size[0] * ratio), OUT_H), Image.LANCZOS)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
rgba.save(OUT)
print('saved', OUT, rgba.size)
