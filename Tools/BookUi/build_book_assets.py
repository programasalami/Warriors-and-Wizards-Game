"""Builds the character-select book art from Desktop/GUI "Pocket Inventory Series #7 Gems of Status"
plus the user's tab icons, into WaW-Client/WaWClient/Content/Ui/BookGems, and writes the frame
layout table (BookFrameData.g.cs) the CharacterBook screen reads.

Run from the repo root:  python Tools/BookUi/build_book_assets.py     (needs Pillow + numpy)
Then add/keep the BookGems entries in Content/Ui.atlas and rebuild WarriorsAndWizards.Client.sln.

Every frame in the pack lives on one shared 896x736 canvas. The static open book is
Open & Close/Style 1/Open/6. The page-flip frames differ from it only inside a small rectangle
(and never erase static pixels), so they are stored as tiny patches drawn OVER the static book.
"""
import glob
import os
import sys

import numpy as np
from PIL import Image

HOME = os.path.expanduser('~')
PACK = f'{HOME}/Desktop/Other GUI/Pocket Inventory Series #7 Gems of Status v1.1/Sprites/Inventory Book'
ICONS = f'{HOME}/Desktop'       # the page icons sit loose on the desktop
OUT = 'WaW-Client/WaWClient/Content/Ui/BookGems'
CS_OUT = 'WaW-Client/WaWClient/Screens/Components/CharacterList/BookFrameData.g.cs'


def load(path):
    return Image.open(path).convert('RGBA')


def arr(im):
    return np.array(im).astype(int)


def bbox_of(mask):
    ys, xs = np.where(mask)
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def save_crop(im, box, rel_path):
    p = f'{OUT}/{rel_path}'
    os.makedirs(os.path.dirname(p), exist_ok=True)
    im.crop(box).save(p)


static = load(f'{PACK}/Open & Close/Style 1/Open/6.png')
S = arr(static)
SB = static.getbbox()                       # (96, 208, 768, 624)
assert SB == (96, 208, 768, 624), SB
OX, OY = SB[0], SB[1]
os.makedirs(OUT, exist_ok=True)
static.crop(SB).save(f'{OUT}/Book.png')
book_w, book_h = SB[2] - SB[0], SB[3] - SB[1]

rows = []   # C# lines


def rel(box):
    return (box[0] - OX, box[1] - OY, box[2] - box[0], box[3] - box[1])


# ---- open animation: full frames (the closed book is a different silhouette, so no patching)
open_frames = []
prev = None
for i in range(1, 6):
    im = load(f'{PACK}/Open & Close/Style 1/Open/{i}.png')
    if prev is not None and np.array_equal(arr(im), prev):
        continue                      # identical to the previous frame
    prev = arr(im)
    box = im.getbbox()
    n = len(open_frames) + 1
    save_crop(im, box, f'Open/{n}.png')
    open_frames.append((n, rel(box)))

# ---- page flips: patches drawn over the static book
def flip_patches(side):
    out = []
    for i in range(2, 9):
        im = load(f'{PACK}/Page Flip/Style 1/Page Flip {side}/{i}.png')
        F = arr(im)
        d = np.abs(F - S).sum(axis=2) > 0
        assert not ((F[:, :, 3] == 0) & (S[:, :, 3] > 0)).any(), 'flip frame erases static pixels'
        box = bbox_of(d)
        n = len(out) + 1
        save_crop(im, box, f'Flip{side[0]}/{n}.png')
        out.append((n, rel(box)))
    return out


flip_left = flip_patches('Left')     # right page turning to the left  = NEXT page
flip_right = flip_patches('Right')   # left page turning back to the right = PREVIOUS page

# ---- side tabs: diff the pack's tabbed frames against the plain static book
tab_base = arr(load(f'{PACK}/Side Tabs/Tabs/Without icons/1.png'))
d0 = (np.abs(tab_base - S).sum(axis=2) > 0)
row_has = d0.any(axis=1)
bands = []
y = 0
while y < len(row_has):
    if row_has[y]:
        y0 = y
        while y < len(row_has) and row_has[y]:
            y += 1
        bands.append((y0, y))
    y += 1
assert len(bands) == 6, bands


def tab_pixels(frame_arr, y0, y1):
    m = np.zeros(frame_arr.shape[:2], bool)
    band = (np.abs(frame_arr - S).sum(axis=2) > 0)
    m[max(0, y0 - 4):y1 + 4, :] = band[max(0, y0 - 4):y1 + 4, :]
    return m


tabs = []
for k, (y0, y1) in enumerate(bands):
    idle_im = load(f'{PACK}/Side Tabs/Tabs/Without icons/1.png')
    act_im = load(f'{PACK}/Side Tabs/Tabs/Without icons/{k + 2}.png')
    m_idle = tab_pixels(arr(idle_im), y0, y1)
    m_act = tab_pixels(arr(act_im), y0, y1)
    box = bbox_of(m_idle | m_act)           # shared box so idle/active swap in place

    def cut(im, m):
        a = np.array(im)
        a[~m] = 0
        return Image.fromarray(a).crop(box)

    p = f'{OUT}/Tab{k + 1}'
    os.makedirs(p, exist_ok=True)
    cut(idle_im, m_idle).save(f'{p}/Idle.png')
    cut(act_im, m_act).save(f'{p}/Active.png')
    tabs.append((k + 1, rel(box), (y0 - OY, y1 - OY)))

# ---- tab icons: the user's own full-colour 16x16 pixel-art page icons, used exactly as drawn - not recoloured, cropped or
# resampled, so every art pixel stays crisp. CharacterBook draws them at an exact 2x on the tabs and 7x on the
# "coming soon" pages (the *Big copies are the same picture under their own atlas name).
PAGE_ICONS = {
    'Profile': 'ProfileIconNew',
    'Characters': 'CharacterListIcon',
    'Graveyard': 'GraveyardIcon',
    'Inbox': 'InboxIcon',
    'DailySpin': 'DailySpinIcon',
    'DailyGift': 'DailyGiftIcon',
}


def make_icon(src, name):
    im = load(src)
    assert im.size == (16, 16), f'{src} is {im.size}, expected 16x16'
    p = f'{OUT}/Icon/{name}.png'
    os.makedirs(os.path.dirname(p), exist_ok=True)
    im.save(p)
    return im.size


icon_sizes = {}
for name, file in PAGE_ICONS.items():
    icon_sizes[name] = make_icon(f'{ICONS}/{file}.png', name)
for name in ('Graveyard', 'Inbox', 'DailySpin', 'DailyGift'):
    icon_sizes[name + 'Big'] = make_icon(f'{ICONS}/{PAGE_ICONS[name]}.png', name + 'Big')

# tab face centre (sprite-local native px): the flat blue area the icon should be centred on
_a = np.array(Image.open(f'{OUT}/Tab1/Idle.png').convert('RGBA'))
_face = (_a[:, :, 0] == 50) & (_a[:, :, 1] == 95) & (_a[:, :, 2] == 126) & (_a[:, :, 3] > 0)
_ys, _xs = np.where(_face)
face_cx, face_cy = (_xs.min() + _xs.max() + 1) / 2.0, (_ys.min() + _ys.max() + 1) / 2.0
tab_icon_order = ['Profile', 'Characters', 'Graveyard', 'Inbox', 'DailySpin', 'DailyGift']

# ---- generated C# table
def fmt(t):
    return f'new({t[0]}, {t[1]}, {t[2]}, {t[3]})'


cs = ['// <auto-generated> by Tools/BookUi/build_book_assets.py - do not edit by hand.',
      'namespace WaWClient.Screens.Components.CharacterList;', '',
      '// Coordinates are in the book\'s native pixels, relative to the static open book\'s top-left corner.',
      'internal static class BookFrameData {',
      '    internal readonly record struct Frame(int X, int Y, int W, int H);', '',
      f'    internal const int BookWidth = {book_w};',
      f'    internal const int BookHeight = {book_h};', '',
      '    // Open animation frames Open/1..N (the last state is the static book itself).',
      '    internal static readonly Frame[] Open = [']
cs += [f'        {fmt(f)},' for _, f in open_frames]
cs += ['    ];', '',
       '    // Page-flip patches FlipL/1..N (turning to the NEXT page) and FlipR/1..N (PREVIOUS page), drawn over the static book.',
       '    internal static readonly Frame[] FlipLeft = [']
cs += [f'        {fmt(f)},' for _, f in flip_left]
cs += ['    ];', '    internal static readonly Frame[] FlipRight = [']
cs += [f'        {fmt(f)},' for _, f in flip_right]
cs += ['    ];', '',
       '    // Side tabs Tab1..Tab6, top to bottom: the shared box of the Idle/Active sprites.',
       '    internal static readonly Frame[] Tabs = [']
cs += [f'        {fmt(t[1])},' for t in tabs]
cs += ['    ];', '',
       '    // Vertical extent (native px) of each tab\'s idle body.',
       '    internal static readonly (int Top, int Bottom)[] TabBands = [']
cs += [f'        ({t[2][0]}, {t[2][1]}),' for t in tabs]
cs += ['    ];', '',
       '    // Tab face centre (sprite-local native px) and the tab icons true native sizes, in tab order.',
       f'    internal const float TabFaceCenterX = {face_cx}f;',
       f'    internal const float TabFaceCenterY = {face_cy}f;',
       '    internal static readonly (int W, int H)[] TabIconSizes = [']
cs += [f'        ({icon_sizes[n][0]}, {icon_sizes[n][1]}),' for n in tab_icon_order]
cs += ['    ];', '}', '']
open(CS_OUT, 'w', encoding='utf-8', newline='\n').write('\n'.join(cs))

print('book', book_w, book_h)
print('open', [(n, f) for n, f in open_frames])
print('flipL', len(flip_left), 'flipR', len(flip_right))
print('tabs', tabs)
print('icons', icon_sizes)
