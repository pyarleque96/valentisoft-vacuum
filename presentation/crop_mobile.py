# -*- coding: utf-8 -*-
"""Recorta cada captura MÓVIL (mobile/) a su contenido real, quitando el
fondo gris sobrante (sobre todo el espacio vacío inferior de páginas cortas)."""
import os
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
MOB = os.path.join(HERE, "mobile")

# Fondo gris de las páginas (#eef1f5). Detectamos lo que NO es fondo.
BG = (0xEE, 0xF1, 0xF5)

def content_bbox(im, tol=8):
    im = im.convert("RGB")
    px = im.load(); W, H = im.size
    minx, miny, maxx, maxy = W, H, 0, 0
    for y in range(0, H, 2):
        for x in range(0, W, 2):
            r, g, b = px[x, y]
            if abs(r-BG[0]) > tol or abs(g-BG[1]) > tol or abs(b-BG[2]) > tol:
                if x < minx: minx = x
                if x > maxx: maxx = x
                if y < miny: miny = y
                if y > maxy: maxy = y
    if minx > maxx or miny > maxy:
        return (0, 0, W, H)
    return (minx, miny, maxx, maxy)

def crop_to_content(name, pad=22):
    src = os.path.join(MOB, name)
    im = Image.open(src).convert("RGB")
    W, H = im.size
    x0, y0, x1, y1 = content_bbox(im)
    # margen uniforme; mantiene ancho completo del teléfono para que no se vea recortado a los lados
    y0 = max(0, y0 - pad); y1 = min(H, y1 + pad)
    x0 = max(0, x0 - pad); x1 = min(W, x1 + pad)
    im.crop((x0, y0, x1, y1)).save(src, quality=92)
    print(name, "->", (x1-x0, y1-y0))

for f in sorted(os.listdir(MOB)):
    if f.lower().endswith(".jpg"):
        crop_to_content(f)
print("listo")
