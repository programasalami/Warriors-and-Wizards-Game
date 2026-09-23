"""Draws the ADMIN tab icon (a gold crown, 32x32, hard pixels) into WaW-Client/WaWClient/Content/Ui/Tabs/AdminTab.png."""
import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'WaW-Client', 'WaWClient', 'Content', 'Ui', 'Tabs', 'AdminTab.png')
OUTLINE = (58, 36, 16, 255)
GOLD = (242, 182, 50, 255)
LIGHT = (255, 226, 122, 255)
SHADE = (181, 118, 26, 255)
RED = (208, 49, 45, 255)
RED_LIGHT = (255, 120, 110, 255)
BLUE = (60, 120, 220, 255)

im = Image.new('RGBA', (32, 32), (0, 0, 0, 0))
d = ImageDraw.Draw(im)

crown = [(4, 25), (3, 10), (10, 16), (16, 5), (22, 16), (29, 10), (28, 25)]
d.polygon(crown, fill=GOLD, outline=OUTLINE)
# lighter left face of each point, darker right side and base band
for pts in ([(5, 22), (5, 13), (9, 17)], [(14, 17), (16, 9), (16, 20)]):
    d.polygon(pts, fill=LIGHT)
d.rectangle([4, 21, 27, 25], fill=SHADE, outline=OUTLINE)
d.line([(5, 21), (26, 21)], fill=LIGHT)
# the three jewels
for x, c, hi in ((9, RED, RED_LIGHT), (16, BLUE, (150, 200, 255, 255)), (23, RED, RED_LIGHT)):
    d.rectangle([x - 1, 22, x + 1, 24], fill=c, outline=OUTLINE)
    d.point((x - 1, 22), fill=hi)
# balls on the tips
for x, y in ((3, 9), (16, 4), (29, 9)):
    d.rectangle([x - 1, y - 1, x + 1, y + 1], fill=LIGHT, outline=OUTLINE)
# re-outline the base bottom edge so it reads at small sizes
d.line([(4, 25), (28, 25)], fill=OUTLINE)

im.save(OUT)
print('wrote', os.path.normpath(OUT))
