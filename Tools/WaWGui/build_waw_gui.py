"""Cuts the in-game GUI pieces out of the "Mini Medieval User Interface" pack (V3X3D; the user's WaW-InGame-GUI folder on the Desktop)
into WaW-Client/WaWClient/Content/Ui/WaW/*.png. Pixel art at 1x - the client draws them at whole-number scales (panels/slots 2x, portrait 4x, bars 3x).

Run from the repo root:  python Tools/WaWGui/build_waw_gui.py [preview.png]
Then keep the WaW entries in Content/Ui.atlas and rebuild WarriorsAndWizards.Client.sln.

Pieces (coordinates are in the pack's PLAIN 8x8 sheets, measured with a pixel grid):
  Panel          Frames.png (164,84,32x32)  orange frame with gold corner gems  + dark-brown fill (#402e2b)
  Slot           Frames.png (86,254,27x27)  the darker orange inventory-slot outline + near-black fill (#120e23)
  PortraitFrame  Portraits.png (121,1,22x22) the gem-cornered portrait frame + near-black fill
  BarFrame       Bars-Sliders-Scrollbars.png bar outline frame (long variant) + near-black fill
  Coin / Star / Close   Icons.png 8x8 gold coin, gold star, orange X
  ScrollTrack / ScrollHandle   Bars-Sliders-Scrollbars.png the larger brown vertical track with arrow caps + the orange pill handle
  Button         Inputs.png the large orange button plate;  Plus / Minus  Icons.png the cream + and - icons
"""
import json
import os
import sys
from PIL import Image

SRC = 'C:/Users/cbart/Desktop/WaW-InGame-GUI/Mini-Medieval-User-Interface-8x8/'
OUT = 'WaW-Client/WaWClient/Content/Ui/WaW/'
PANEL_FILL = (0x40, 0x2e, 0x2b, 255)
DARK_FILL = (0x12, 0x0e, 0x23, 255)

os.makedirs(OUT, exist_ok=True)
frames = Image.open(SRC + 'Frames.png').convert('RGBA')
portraits = Image.open(SRC + 'Portraits.png').convert('RGBA')
bars = Image.open(SRC + 'Bars-Sliders-Scrollbars.png').convert('RGBA')
icons = Image.open(SRC + 'Icons.png').convert('RGBA')
inputs = Image.open(SRC + 'Inputs.png').convert('RGBA')


def tight(im, box):
    c = im.crop(box)
    return c.crop(c.getbbox())


def border_thickness(im):
    """Frame line width at the middle of each side, as (left, right, top, bottom) counted from the image edge INCLUDING any
    transparent margin: i.e. the index of the first pixel inside the frame line."""
    w, h = im.size
    px = im.load()
    def inner(coords):
        seen = False
        for i, (x, y) in enumerate(coords):
            if px[x, y][3] > 0:
                seen = True
            elif seen:
                return i
        return 0
    return (inner([(x, h // 2) for x in range(w)]), inner([(w - 1 - x, h // 2) for x in range(w)]),
            inner([(w // 2, y) for y in range(h)]), inner([(w // 2, h - 1 - y) for y in range(h)]))


def fill_inside(im, colour):
    """Paints every transparent pixel that is INSIDE the frame (between its four border lines) with `colour`."""
    l, r, t, b = border_thickness(im)
    w, h = im.size
    px = im.load()
    for y in range(t, h - b):
        for x in range(l, w - r):
            if px[x, y][3] == 0:
                px[x, y] = colour
    return im, (l, r, t, b)


info = {}

panel = tight(frames, (164, 84, 196, 118))
panel, info['Panel_border'] = fill_inside(panel, PANEL_FILL)
panel.save(OUT + 'Panel.png')

slot = tight(frames, (86, 254, 113, 282))
slot, info['Slot_border'] = fill_inside(slot, DARK_FILL)
slot.save(OUT + 'Slot.png')

portrait = tight(portraits, (121, 1, 143, 23))
portrait, info['Portrait_border'] = fill_inside(portrait, DARK_FILL)
portrait.save(OUT + 'PortraitFrame.png')

# bar frame: the long orange outline at the top-left of the "bar frames" block (see the grid image: x 1..24, y 89..95)
bar = tight(bars, (0, 88, 26, 97))
bar, info['Bar_border'] = fill_inside(bar, DARK_FILL)
bar.save(OUT + 'BarFrame.png')

coin = tight(icons, (64, 39, 73, 49)); coin.save(OUT + 'Coin.png')
star = tight(icons, (32, 39, 40, 49)); star.save(OUT + 'Star.png')
close = tight(icons, (0, 127, 8, 137)); close.save(OUT + 'Close.png')
for n, im in (('Panel', panel), ('Slot', slot), ('PortraitFrame', portrait), ('BarFrame', bar), ('Coin', coin), ('Star', star), ('Close', close)):
    info[n] = im.size

# bar fill colours: the pack's coloured strips (3 rows tall) - the client draws the rows as solid rects
def strip_rows(x, y):
    px = bars.load()
    for yy in range(y, y + 14):
        if px[x, yy][3] > 0:
            return ['%02X%02X%02X' % px[x, yy + i][:3] for i in range(3)]
    return []


scroll_track = tight(bars, (16, 0, 26, 26)); scroll_track.save(OUT + 'ScrollTrack.png')
scroll_handle = tight(bars, (24, 4, 34, 20)); scroll_handle.save(OUT + 'ScrollHandle.png')
button = tight(inputs, (0, 4, 36, 22)); button.save(OUT + 'Button.png')
plus = tight(icons, (72, 128, 80, 136)); plus.save(OUT + 'Plus.png')
minus = tight(icons, (80, 128, 88, 136)); minus.save(OUT + 'Minus.png')
for n, im in (('ScrollTrack', scroll_track), ('ScrollHandle', scroll_handle), ('Button', button), ('Plus', plus), ('Minus', minus)):
    info[n] = im.size

info['fills'] = {
    'red': strip_rows(6, 118), 'teal': strip_rows(30, 118), 'olive': strip_rows(54, 118), 'pink': strip_rows(78, 118), 'orange': strip_rows(102, 118),
    'track_red': strip_rows(6, 108), 'track_teal': strip_rows(30, 108), 'track_olive': strip_rows(54, 108),
}
json.dump(info, open(OUT + 'info.json', 'w'), indent=1)
print(json.dumps(info, indent=1))

if len(sys.argv) > 1:
    prev = Image.new('RGBA', (640, 220), (30, 26, 40, 255))
    def paste(im, x, y, z):
        prev.alpha_composite(im.resize((im.width * z, im.height * z), Image.NEAREST), (x, y))
    paste(panel, 10, 10, 4); paste(slot, 150, 10, 4); paste(portrait, 280, 10, 4); paste(bar, 400, 20, 6)
    paste(coin, 400, 90, 6); paste(star, 470, 90, 6); paste(close, 540, 90, 6)
    paste(scroll_track, 10, 100, 4); paste(scroll_handle, 70, 100, 4); paste(button, 130, 120, 4); paste(plus, 300, 160, 6); paste(minus, 360, 160, 6)
    prev.save(sys.argv[1])
