"""Crops Desktop/LoaderLogo.png to its opaque bounds and shrinks it to the size the loader draws it at
(the loader shows it ~420x336 on the 1280x720 canvas), into Content/Ui/Loader/Logo.png.
Run from the repo root: python Tools/BookUi/build_loader_logo.py"""
import os
from PIL import Image

SRC = os.path.expanduser('~/Desktop/LoaderLogo.png')
OUT = 'WaW-Client/WaWClient/Content/Ui/Loader/Logo.png'
W, H = 420, 336

im = Image.open(SRC).convert('RGBA')
im = im.crop(im.getbbox())
print('cropped', im.size)
im = im.resize((W, H), Image.LANCZOS)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
im.save(OUT)
print('saved', OUT, im.size)
