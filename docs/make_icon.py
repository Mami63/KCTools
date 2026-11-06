"""KC Tools uygulama ikonu (Assets/kctools.ico) uretir."""
import os
from PIL import Image, ImageDraw, ImageFont

SZ = 1024
img = Image.new("RGBA", (SZ, SZ), (0, 0, 0, 0))

# yuvarlatilmis kare + capraz degrade
grad = Image.new("RGB", (SZ, SZ))
px = grad.load()
for y in range(SZ):
    for x in range(SZ):
        t = (x + y) / (2 * SZ - 2)
        px[x, y] = (
            int(59 + (91 - 59) * t),
            int(232 + (141 - 232) * t),
            int(207 + (255 - 207) * t),
        )

mask = Image.new("L", (SZ, SZ), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, SZ - 1, SZ - 1], radius=int(SZ * 0.22), fill=255)
img.paste(grad, (0, 0), mask)

d = ImageDraw.Draw(img)
f = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", int(SZ * 0.38))
d.text((SZ / 2, SZ * 0.52), "KC", font=f, fill=(5, 22, 30, 255), anchor="mm")

# ince ic kenar
d.rounded_rectangle([6, 6, SZ - 7, SZ - 7], radius=int(SZ * 0.21),
                    outline=(255, 255, 255, 46), width=10)

out_dir = os.path.join("src", "KCTools", "Assets")
os.makedirs(out_dir, exist_ok=True)
out = os.path.join(out_dir, "kctools.ico")
img.save(
    out, format="ICO",
    sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
)
print("yazildi:", out, os.path.getsize(out), "bayt")
