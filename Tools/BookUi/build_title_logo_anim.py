"""Builds the animated main-menu logo from Desktop/AnimatedTitleLogo.mp4.

The video (1024x768, 24 fps, ~5.2 s, H.264 so no alpha) has a fake-transparency checkerboard baked into its background,
so the background is removed here, per frame: bright, colourless pixels connected to the frame's border are cut (the
logo's own dark outline stops the fill), and the glow effects that sit directly over the checkerboard (the staff's spell
swirl) get a soft, un-mixed alpha instead of a hard cut, so they don't keep white checkerboard patches.

Every STEP-th frame is kept (the client crossfades neighbouring frames to get back to 24 fps smoothness), cropped to the
logo, scaled down and packed into one sprite sheet: WaWClient/Content/Title/LogoSheet.png, plus the generated
WaWClient/Screens/Components/TitleLogoAnimData.g.cs describing the grid (frame size, count, columns, aspect).

Run from the repo root: python Tools/BookUi/build_title_logo_anim.py
"""
import math
import os

import cv2
import numpy as np
from PIL import Image

SRC = os.path.expanduser('~/Desktop/AnimatedTitleLogo.mp4')
SHEET_OUT = 'WaW-Client/WaWClient/Content/Title/LogoSheet.png'
DATA_OUT = 'WaW-Client/WaWClient/Screens/Components/TitleLogoAnimData.g.cs'

STEP = 2            # keep every 2nd frame (24 fps -> 12 fps stored)
FRAME_H = 440       # stored frame height; width follows the logo's aspect ratio
PAD = 6             # transparent margin (source px) around the logo inside each frame
COLS = 8
BRIGHT = 195        # min(r,g,b) above this ...
COLORLESS = 30      # ... and (max-min) below this => checkerboard background
EDGE_FILL = (12, 8, 16)   # near-black put under the transparent pixels so downscaling blends dark, never white or blue
ERODE_PX = 6        # source px cut off the silhouette: removes the video's blurry blue halo ring so only the artwork's own edge is left

cap = cv2.VideoCapture(SRC)
frames = []
while True:
    ok, bgr = cap.read()
    if not ok:
        break
    frames.append(cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB))
cap.release()
n = len(frames)
h, w = frames[0].shape[:2]
print('frames', n, (w, h))


def background_mask(rgb):
    mn = rgb.min(axis=2).astype(np.int16)
    mx = rgb.max(axis=2).astype(np.int16)
    cand = ((mn > BRIGHT) & (mx - mn < COLORLESS)).astype(np.uint8)
    count, labels = cv2.connectedComponents(cand, connectivity=4)
    border = set(np.unique(np.concatenate([labels[0], labels[-1], labels[:, 0], labels[:, -1]])).tolist())
    border.discard(0)
    return np.isin(labels, list(border))


BG_GREY = 241.0     # the checkerboard alternates ~231 / ~251, so 241 is its average
ZONE_PX = 9         # only pixels this close to the removed background are treated as possibly-translucent glow
SOLID_AT = 0.8      # a pixel this much darker than the checkerboard (in its most-different channel) is fully solid


def frame_alpha_and_color(rgb):
    """Per-frame alpha + un-mixed colour. Hard part: the staff's spell swirl has no dark outline, so it is a glow drawn
    over the checkerboard. Near the removed background, a pixel is treated as glow-over-checker: the least alpha that
    could explain it (its most-different channel vs the checker grey) is its alpha, and the checker is subtracted back
    out of its colour. Dark outline pixels come out solid; pale white-ish haze comes out nearly transparent."""
    strict = background_mask(rgb)
    strict_u8 = strict.astype(np.uint8)
    near = cv2.dilate(strict_u8, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (ZONE_PX * 2 + 1, ZONE_PX * 2 + 1))) > 0
    zone = near & ~strict

    c = rgb.astype(np.float32)
    a_min = np.clip((BG_GREY - c) / BG_GREY, 0, 1).max(axis=2)
    alpha = np.ones(rgb.shape[:2], np.float32)
    alpha[strict] = 0.0
    alpha[zone] = np.clip(a_min[zone] / SOLID_AT, 0, 1)

    color = c.copy()
    soft = zone & (alpha < 0.999)
    a3 = np.maximum(alpha[soft], 0.06)[:, None]
    color[soft] = np.clip((c[soft] - (1.0 - alpha[soft])[:, None] * BG_GREY) / a3, 0, 255)

    # drop stray specks: keep only the biggest connected blob of visible pixels
    vis = (alpha > 0.04).astype(np.uint8)
    cnt, labels, stats, _ = cv2.connectedComponentsWithStats(vis, connectivity=8)
    biggest = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
    alpha[labels != biggest] = 0.0
    # cut the silhouette in past the blue halo ring the video has around the whole logo (see ERODE_PX)
    alpha = cv2.erode(alpha, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (ERODE_PX * 2 + 1, ERODE_PX * 2 + 1)))
    alpha = cv2.GaussianBlur(alpha, (0, 0), 0.6)
    return alpha, color


results = [frame_alpha_and_color(f) for f in frames]
union = np.zeros((h, w), bool)
for al, _ in results:
    union |= al > 0.04
ys, xs = np.where(union)
x0, x1 = max(xs.min() - PAD, 0), min(xs.max() + PAD + 1, w)
y0, y1 = max(ys.min() - PAD, 0), min(ys.max() + PAD + 1, h)
cw, ch = x1 - x0, y1 - y0
fw = round(FRAME_H * cw / ch)
print('crop', (cw, ch), 'frame', (fw, FRAME_H))

keep = list(range(0, n, STEP))
count = len(keep)
rows = math.ceil(count / COLS)
sheet = Image.new('RGBA', (COLS * fw, rows * FRAME_H), (0, 0, 0, 0))
for i, k in enumerate(keep):
    al, col = results[k]
    col = col.copy()
    col[al < 0.02] = EDGE_FILL
    rgba = np.dstack([np.clip(col, 0, 255), al * 255.0]).astype(np.uint8)[y0:y1, x0:x1]
    img = Image.fromarray(rgba, 'RGBA').resize((fw, FRAME_H), Image.LANCZOS)
    sheet.paste(img, ((i % COLS) * fw, (i // COLS) * FRAME_H))

os.makedirs(os.path.dirname(SHEET_OUT), exist_ok=True)
sheet.save(SHEET_OUT, optimize=True)
print('sheet', sheet.size, 'frames kept', count, 'size KB', os.path.getsize(SHEET_OUT) // 1024)

with open(DATA_OUT, 'w', encoding='utf-8', newline='\n') as f:
    f.write(f'''// <auto-generated> by Tools/BookUi/build_title_logo_anim.py - do not edit by hand. </auto-generated>
namespace WaWClient.Screens.Components;

public static class TitleLogoAnimData {{
    public const string SheetPath = "Title/LogoSheet.png";
    public const int SheetWidth = {sheet.size[0]};
    public const int SheetHeight = {sheet.size[1]};
    public const int FrameWidth = {fw};
    public const int FrameHeight = {FRAME_H};
    public const int Columns = {COLS};
    public const int FrameCount = {count};

    // Frame rate of the stored frames (the source clip is 24 fps, every {STEP}nd frame is kept).
    public const float SourceFps = {24 / STEP:.1f}f;
}}
''')
print('wrote', DATA_OUT)

# loop-quality report: how different is the last kept frame from the first, next to a normal frame-to-frame step?
def diff(a, b):
    return float(np.abs(frames[a].astype(np.int16) - frames[b].astype(np.int16)).mean())


print('loop seam diff', round(diff(keep[-1], keep[0]), 2), 'typical step diff', round(np.mean([diff(keep[i], keep[i + 1]) for i in range(count - 1)]), 2))
