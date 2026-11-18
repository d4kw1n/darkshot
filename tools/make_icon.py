from PIL import Image, ImageDraw, ImageFilter
import os, math

S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# Rounded-square body with a subtle vertical gradient (dark graphite).
body = Image.new("RGBA", (S, S), (0, 0, 0, 0))
bd = ImageDraw.Draw(body)
radius = int(S * 0.20)
for y in range(S):
    t = y / (S - 1)
    r = int(24 + 10 * t)
    g = int(27 + 12 * t)
    b = int(34 + 16 * t)
    bd.line([(0, y), (S, y)], fill=(r, g, b, 255))
mask = Image.new("L", (S, S), 0)
md = ImageDraw.Draw(mask)
md.rounded_rectangle([0, 0, S - 1, S - 1], radius=radius, fill=255)
img.paste(body, (0, 0), mask)

# Inner border
bd2 = ImageDraw.Draw(img)
bd2.rounded_rectangle([3, 3, S - 4, S - 4], radius=radius - 2, outline=(255, 255, 255, 26), width=5)

# Selection corner brackets (like a capture marquee).
m = int(S * 0.245)
w = int(S * 0.062)
col = (245, 247, 250, 255)
r = int(S * 0.055)
def bracket(x1, y1, x2, y2, corner):
    # Two rounded strokes per corner.
    if corner in ("tl", "tr"):
        y = y1
    else:
        y = y2
    if corner in ("tl", "bl"):
        x = x1
    else:
        x = x2
    if corner in ("tl", "tr"):
        bd2.rounded_rectangle([x, y, (x + w if corner == "tl" else x + w), y + r], radius=r // 2, fill=col)
    else:
        bd2.rounded_rectangle([x, y - r, x + w, y], radius=r // 2, fill=col)
    if corner in ("tl", "bl"):
        bd2.rounded_rectangle([x, y - (0 if corner == "tl" else r), x + r, y + (r if corner == "tl" else 0)], radius=r // 2, fill=col)
    else:
        bd2.rounded_rectangle([x - r, y - (0 if corner == "tr" else r), x, y + (r if corner == "tr" else 0)], radius=r // 2, fill=col)

# Draw brackets as L shapes with thick round lines.
def L(x, y, sx, sy):
    ex = x + sx * (S * 0.16)
    ey = y + sy * (S * 0.16)
    bd2.rounded_rectangle([min(x, ex) - w // 2, y - w // 2, max(x, ex) + w // 2, y + w // 2], radius=w // 2, fill=col)
    bd2.rounded_rectangle([x - w // 2, min(y, ey) - w // 2, x + w // 2, max(y, ey) + w // 2], radius=w // 2, fill=col)

L(m, m, 1, 1)
L(S - m, m, -1, 1)
L(m, S - m, 1, -1)
L(S - m, S - m, -1, -1)

# Crosshair / target dot (red accent).
cx, cy = S // 2, S // 2
accent = (255, 59, 48, 255)
# subtle glow
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
gd.ellipse([cx - 95, cy - 95, cx + 95, cy + 95], fill=(255, 59, 48, 70))
glow = glow.filter(ImageFilter.GaussianBlur(38))
img.alpha_composite(glow)
bd3 = ImageDraw.Draw(img)
bd3.line([(cx - 138, cy), (cx - 60, cy)], fill=col, width=10)
bd3.line([(cx + 60, cy), (cx + 138, cy)], fill=col, width=10)
bd3.line([(cx, cy - 138), (cx, cy - 60)], fill=col, width=10)
bd3.line([(cx, cy + 60), (cx, cy + 138)], fill=col, width=10)
bd3.ellipse([cx - 36, cy - 36, cx + 36, cy + 36], fill=accent)
bd3.ellipse([cx - 12, cy - 12, cx + 12, cy + 12], fill=(255, 255, 255, 230))

os.makedirs("src/DarkShot/Assets", exist_ok=True)
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
img.save("src/DarkShot/Assets/darkshot.ico", format="ICO", sizes=[(s, s) for s in sizes])
img.resize((256, 256), Image.LANCZOS).save("src/DarkShot/Assets/darkshot-256.png")
print("wrote icon", os.path.getsize("src/DarkShot/Assets/darkshot.ico"), "bytes")
