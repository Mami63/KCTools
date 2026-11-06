from PIL import Image, ImageDraw, ImageFilter
import math

S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

CYAN = (34, 211, 238, 255)
CYAN2 = (56, 189, 248, 255)
DARK = (8, 15, 28, 255)
BODY = (15, 23, 42, 255)
SCREEN = (6, 18, 32, 255)


def rounded(draw, box, radius, fill=None, outline=None, width=1):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def wing(draw, cx, cy, direction, scale=1.0):
    """direction=+1 sağ, -1 sol."""
    base_x = cx + direction * 150 * scale
    for i, (length, thickness, dy) in enumerate(((300, 34, -95), (250, 30, -30), (195, 26, 35))):
        x0 = base_x
        y0 = cy + dy * scale
        x1 = base_x + direction * length * scale
        y1 = cy + (dy - 95) * scale
        pts = [
            (x0, y0),
            (x1, y1 - thickness * 0.4),
            (x1 + direction * 22 * scale, y1 + thickness * 0.5),
            (x0 + direction * 40 * scale, y0 + thickness),
        ]
        draw.polygon(pts, fill=CYAN if i == 0 else CYAN2)

    # kanat ucu parlaklığı
    draw.ellipse(
        (base_x - 12, cy - 12, base_x + 12, cy + 12),
        fill=(34, 211, 238, 255),
    )


# hareket çizgileri (arkada)
for i, (off, ln) in enumerate(((0, 210), (52, 150), (-52, 120))):
    x = 250 + i * 18
    d.rounded_rectangle((x, 620 + off, x + ln, 636 + off), radius=8, fill=(34, 211, 238, 70))

# kanatlar
wing(d, 512, 470, +1)
wing(d, 512, 470, -1)

# gövde (telefon)
body = (352, 232, 672, 800)
# dış parlama
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
rounded(gd, (body[0] - 10, body[1] - 10, body[2] + 10, body[3] + 10), 78, fill=(34, 211, 238, 90))
glow = glow.filter(ImageFilter.GaussianBlur(26))
img.alpha_composite(glow)

rounded(d, body, 70, fill=BODY, outline=CYAN, width=9)

# ekran
screen = (378, 300, 646, 732)
rounded(d, screen, 46, fill=SCREEN)

# ekran içi: sinyal çubukları + "KC" benzeri minimal
for i, h in enumerate((28, 46, 64, 84)):
    x = 420 + i * 34
    d.rounded_rectangle((x, 560 - h, x + 20, 560), radius=6, fill=CYAN)

# uçak/ok simgesi ekranda
d.polygon([(512, 380), (562, 470), (512, 448), (462, 470)], fill=(230, 253, 255, 235))

# üst çentik
rounded(d, (478, 250, 546, 272), 11, fill=(2, 8, 16, 255))

# kanat bağlantı halkaları
for side in (-1, 1):
    x = 512 + side * 150
    d.ellipse((x - 16, 454, x + 16, 486), fill=(230, 253, 255, 255))
    d.ellipse((x - 9, 461, x + 9, 479), fill=CYAN)

img.save("src/KCTools/Assets/logo.png")

sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256), (512, 512)]
img.save("src/KCTools/Assets/kctools.ico", sizes=sizes)
print("logo.png + kctools.ico yazildi")
