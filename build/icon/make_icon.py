#!/usr/bin/env python3
"""Draws the softreset cloud mark and writes softreset.ico + softreset.png into the App's Assets."""
import math
import os
from PIL import Image, ImageDraw, ImageFilter

S = 1024
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "src", "SoftReset.App", "Assets")


def cloud_mask(size):
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    k = size / 1024
    def circle(cx, cy, r):
        d.ellipse([(cx - r) * k, (cy - r) * k, (cx + r) * k, (cy + r) * k], fill=255)
    circle(330, 600, 210)
    circle(540, 430, 270)
    circle(740, 610, 200)
    d.rounded_rectangle([140 * k, 560 * k, 900 * k, 810 * k], radius=125 * k, fill=255)
    return m


def gradient(size, top, bottom):
    g = Image.new("RGBA", (size, size))
    px = g.load()
    for y in range(size):
        t = y / (size - 1)
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,)
        for x in range(size):
            px[x, y] = c
    return g


def draw_reset_arrow(img):
    d = ImageDraw.Draw(img)
    cx, cy, r, w = 520, 610, 150, 46
    d.arc([cx - r, cy - r, cx + r, cy + r], start=-50, end=245, fill=(255, 255, 255, 255), width=w)
    # arrow head at the end of the arc (angle -50 degrees)
    a = math.radians(-50)
    ex, ey = cx + r * math.cos(a), cy + r * math.sin(a)
    tangent = a + math.pi / 2
    size = 78
    p1 = (ex + size * math.cos(tangent + 0.0) * 0.0 + size * 0.75 * math.cos(a), ey + size * 0.75 * math.sin(a))
    p2 = (ex - size * 0.75 * math.cos(a), ey - size * 0.75 * math.sin(a))
    p3 = (ex + size * math.cos(tangent - math.pi), ey + size * math.sin(tangent - math.pi))
    d.polygon([p1, p2, p3], fill=(255, 255, 255, 255))


def make(size=S):
    base = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    mask = cloud_mask(size)
    # soft shadow
    shadow = Image.new("RGBA", (size, size), (11, 37, 69, 0))
    sh = mask.filter(ImageFilter.GaussianBlur(size / 64))
    shadow.putalpha(sh.point(lambda v: int(v * 0.28)))
    base.alpha_composite(shadow, (0, int(size * 0.02)))
    body = gradient(size, (160, 214, 255), (47, 128, 237))
    body.putalpha(mask)
    base.alpha_composite(body)
    draw_reset_arrow(base)
    return base


os.makedirs(OUT, exist_ok=True)
img = make()
img.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "softreset.png"))
img.resize((512, 512), Image.LANCZOS).save(os.path.join(OUT, "softreset-512.png"))
img.save(os.path.join(OUT, "softreset.ico"), sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
print("icons written to", os.path.normpath(OUT))
