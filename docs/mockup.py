"""
KC Tools - ana pencere tasarim onizlemesi (mockup).
XAML'deki renk, olcu ve tipografi degerleriyle birebir cizilir.
Cikti: docs/preview.png (1440x960, 2x supersampling)
"""

import os
from PIL import Image, ImageDraw, ImageFont

S = 2
W, H = 1440, 960
FONTS = "C:/Windows/Fonts/"

# ---------- palet ----------
BG_BASE = (9, 12, 17)
BG_TITLE = (11, 15, 21)
BG_SIDE = (13, 18, 26)
BG_CARD = (17, 25, 37)
BG_CARDA = (14, 22, 33)
BG_FIELD = (10, 17, 25)
BG_ELEV = (22, 32, 45)
BG_CONSOLE = (5, 8, 12)
BG_LOGHEAD = (11, 18, 27)
BORDER_SOFT = (25, 34, 46)
BORDER = (35, 48, 64)
BORDER_STRONG = (46, 61, 78)
TXT = (233, 240, 248)
TXT2 = (167, 183, 201)
MUTED = (110, 128, 148)
DIM = (75, 90, 106)
ACCENT = (46, 217, 195)
ACCENT_DARK = (24, 181, 162)
ACCENT_SOFT = (16, 48, 46)
ACCENT_INK = (4, 25, 26)
SUCCESS = (53, 208, 127)
WARNING = (242, 179, 61)
DANGER = (242, 109, 109)
INFO = (99, 168, 255)
SCROLL = (39, 51, 63)

img = Image.new("RGB", (W * S, H * S), BG_BASE)
d = ImageDraw.Draw(img)


def sc(v):
    return int(round(v * S))


def font(name, size, fallback="segoeui.ttf"):
    path = os.path.join(FONTS, name)
    if not os.path.exists(path):
        path = os.path.join(FONTS, fallback)
    return ImageFont.truetype(path, sc(size))


UI = font("segoeui.ttf", 12.5)
UI12 = font("segoeui.ttf", 12)
UI115 = font("segoeui.ttf", 11.5)
UI11 = font("segoeui.ttf", 11)
UI105 = font("segoeui.ttf", 10.5)
UI10 = font("segoeui.ttf", 10)
UI95 = font("segoeui.ttf", 9.5)
UI85 = font("segoeui.ttf", 8.5)
SB = font("seguisb.ttf", 13, "segoeuib.ttf")
SB12 = font("seguisb.ttf", 12.5, "segoeuib.ttf")
SB11 = font("seguisb.ttf", 11, "segoeuib.ttf")
SB10 = font("seguisb.ttf", 10.5, "segoeuib.ttf")
SB95 = font("seguisb.ttf", 9.5, "segoeuib.ttf")
SB85 = font("seguisb.ttf", 8.5, "segoeuib.ttf")
B13 = font("segoeuib.ttf", 13)
B14 = font("segoeuib.ttf", 14)
TITLE = font("segoeuisb.ttf", 19, "segoeuib.ttf")
MONO = font("consola.ttf", 11, "segoeui.ttf")
MONO10 = font("consola.ttf", 10.5, "segoeui.ttf")
MONO12 = font("consola.ttf", 12, "segoeui.ttf")


def rect(box, r=0, fill=None, outline=None, w=1):
    if fill is None and outline is None:
        return
    kwargs = {"radius": sc(r), "fill": fill}
    if outline is not None:
        kwargs["outline"] = outline
        kwargs["width"] = max(1, sc(w))
    d.rounded_rectangle(
        [sc(box[0]), sc(box[1]), sc(box[2]), sc(box[3])], **kwargs
    )


def text(xy, s, f, fill, anchor="la", spacing=0):
    d.text((sc(xy[0]), sc(xy[1])), s, font=f, fill=fill, anchor=anchor, spacing=sc(spacing))


def tw(s, f):
    return f.getlength(s) / S


def hgrad(box, c1, c2, r=0):
    x0, y0, x1, y1 = box
    w = max(1, sc(x1 - x0))
    h = max(1, sc(y1 - y0))
    strip = Image.new("RGB", (w, 1))
    px = strip.load()
    for i in range(w):
        t = i / max(1, w - 1)
        px[i, 0] = tuple(int(c1[k] + (c2[k] - c1[k]) * t) for k in range(3))
    grad = strip.resize((w, h))
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, w - 1, h - 1], radius=sc(r), fill=255)
    img.paste(grad, (sc(x0), sc(y0)), mask)


def pill(x, y, label, f, fg, bg, padx=8, h=18, dot=None, dot_color=None):
    w = tw(label, f)
    box = (x, y, x + w + padx * 2 + (10 if dot else 0), y + h)
    rect(box, h / 2, fill=bg)
    tx = x + padx
    if dot:
        cx = sc(tx + 2)
        cy = sc(y + h / 2)
        d.ellipse([cx - sc(2.5), cy - sc(2.5), cx + sc(2.5), cy + sc(2.5)], fill=dot_color)
        tx += 10
    text((tx, y + h / 2), label, f, fg, anchor="lm")
    return box


def chevron(cx, cy, size, color, w=1.6, up=False):
    s = size / 2
    y0, y1 = (cy - s * 0.35, cy + s * 0.55) if not up else (cy + s * 0.35, cy - s * 0.55)
    d.line([sc(cx - s), sc(y0), sc(cx), sc(y1)], fill=color, width=sc(w))
    d.line([sc(cx), sc(y1), sc(cx + s), sc(y0)], fill=color, width=sc(w))


# ---------- ikonlar (Icons.xaml koordinatlariyla) ----------
def ico(frame, name, color):
    """frame: (x, y, size) kare kutu; ikon 24x24 birimden olceklenir."""
    x, y, size = frame
    k = size / 24.0

    def P(px, py):
        return (sc(x + px * k), sc(y + py * k))

    w = max(1, sc(1.8 * k))
    if name == "chip":
        d.rectangle([P(7, 7), P(9.4, 17)], fill=color)
        d.rectangle([P(14.6, 7), P(17, 17)], fill=color)
        d.rectangle([P(7, 7), P(17, 9.4)], fill=color)
        d.rectangle([P(7, 14.6), P(17, 17)], fill=color)
        for (a, b, c_, e) in [(10.8, 3.2, 13.2, 6.6), (10.8, 17.4, 13.2, 20.8),
                              (3.2, 10.8, 6.6, 13.2), (17.4, 10.8, 20.8, 13.2),
                              (7.4, 3.6, 8.9, 6.6), (15.1, 3.6, 16.6, 6.6),
                              (7.4, 17.4, 8.9, 20.4), (15.1, 17.4, 16.6, 20.4),
                              (3.6, 7.4, 6.6, 8.9), (3.6, 15.1, 6.6, 16.6),
                              (17.4, 7.4, 20.4, 8.9), (17.4, 15.1, 20.4, 16.6)]:
            d.rectangle([P(a, b), P(c_, e)], fill=color)
    elif name == "layers":
        d.polygon([P(12, 2.5), P(21.2, 7.1), P(12, 11.7), P(2.8, 7.1)], fill=color)
        for dy in (0, 3.8):
            d.polygon([P(12, 13.2 + dy), P(19.9, 9.3 + dy), P(21.2, 9.9 + dy),
                       P(12, 14.5 + dy), P(2.8, 9.9 + dy), P(4.1, 9.3 + dy)], fill=color)
    elif name == "bolt":
        d.polygon([P(13.4, 2), P(5, 13.6), P(10.2, 13.6), P(9.2, 22),
                   P(19, 9.6), P(13.4, 9.6)], fill=color)
    elif name == "terminal":
        d.rectangle([P(2.5, 4), P(21.5, 20)], outline=color, width=w)
        d.line([P(7.6, 8.6), P(9.1, 7.2), P(13.1, 11.4), P(9.1, 15.6), P(7.6, 14.2), P(10.1, 11.4)],
               fill=color, width=w, joint="curve")
        d.rectangle([P(13.8, 14.2), P(17.6, 15.8)], fill=color)
    elif name == "sliders":
        for py in (6, 11.15, 16.3):
            d.rectangle([P(3, py), P(21, py + 1.7)], fill=color)
        for (a, b, c_, e) in [(8.2, 4.2, 11, 9.5), (14.4, 9.35, 17.2, 14.65), (6.2, 14.5, 9, 19.8)]:
            d.rectangle([P(a, b), P(c_, e)], fill=color)
    elif name == "shield":
        d.polygon([P(12, 2.5), P(20, 5.6), P(20, 11), P(18.6, 14.4), P(16, 17.6),
                   P(12, 21.5), P(8, 17.6), P(5.4, 14.4), P(4, 11), P(4, 5.6)], outline=color, width=w)
        d.line([P(9.6, 11.6), P(8.2, 13), P(11, 15.8), P(16, 10.8), P(14.6, 9.4), P(11, 13)],
               fill=color, width=w, joint="curve")
    elif name == "plug":
        d.rectangle([P(9, 3), P(11.5, 6)], fill=color)
        d.rectangle([P(12.5, 3), P(15, 6)], fill=color)
        d.rounded_rectangle([P(7, 6), P(17, 15.5)], radius=sc(3 * k), fill=color)
        d.rectangle([P(11, 15.5), P(13, 21)], fill=color)
    elif name == "download":
        d.polygon([P(11, 3), P(13, 3), P(13, 12.4), P(16.9, 12.4), P(12, 18.2),
                   P(7.1, 12.4), P(11, 12.4)], fill=color)
        d.rectangle([P(4, 19.6), P(20, 21.4)], fill=color)
    elif name == "folder":
        d.polygon([P(3, 6.4), P(10.2, 6.4), P(11.9, 8.4), P(21, 8.4), P(21, 18.6), P(3, 18.6)], fill=color)
    elif name == "warning":
        d.polygon([P(12, 3), P(22, 20.6), P(2, 20.6)], fill=color)
        d.rectangle([P(11.2, 9), P(12.8, 15)], fill=BG_CARD)
        d.rectangle([P(11.2, 16.5), P(12.8, 18.2)], fill=BG_CARD)


# ============================ PENCERE ============================
rect((0, 0, W, H), 0, fill=BG_BASE)
rect((0, 0, W - 1, H - 1), 0, outline=BORDER, w=1)

# ---------- baslik cubugu ----------
rect((1, 1, W - 1, 54), 0, fill=BG_TITLE)
hgrad((1, 1, W - 1, 3), ACCENT, (91, 141, 255), 0)
rect((1, 54, W - 1, 54), 0, fill=BORDER_SOFT)

hgrad((16, 12, 46, 42), (59, 232, 207), (91, 141, 255), 9)
text((31, 27), "KC", SB11, (6, 18, 26), anchor="mm")

x = 56
text((x, 27), "KC TOOLS", B13, TXT, anchor="lm")
x += tw("KC TOOLS", B13) + 14
d.line([sc(x), sc(18), sc(x), sc(36)], fill=BORDER, width=sc(1))
x += 14
text((x, 27), "Android Servis İstasyonu", UI115, MUTED, anchor="lm")

# sag ust: onizleme rozeti + pencere dugmeleri
bx = W - 3 * 46 - 12
pill(bx - 116, 18, "UI ÖNİZLEME", SB95, TXT2, BG_FIELD, padx=9, h=18,
     dot=True, dot_color=WARNING)

min_cx, max_cx, cls_cx = W - 115, W - 69, W - 23

d.line([sc(min_cx - 6), sc(27), sc(min_cx + 6), sc(27)], fill=TXT2, width=sc(1.2))
d.rectangle([sc(max_cx - 5), sc(22), sc(max_cx + 5), sc(32)], outline=TXT2, width=sc(1.2))
d.line([sc(cls_cx - 5), sc(22), sc(cls_cx + 5), sc(32)], fill=TXT2, width=sc(1.3))
d.line([sc(cls_cx + 5), sc(22), sc(cls_cx - 5), sc(32)], fill=TXT2, width=sc(1.3))

# ---------- sol navigasyon ----------
rect((1, 55, 244, 925), 0, fill=BG_SIDE)
d.line([sc(244), sc(55), sc(244), sc(925)], fill=BORDER_SOFT, width=sc(1))

text((20, 70), "MODÜLLER", SB10, DIM)

NAV = [
    ("Qualcomm", "EDL · Firehose", "chip", True),
    ("MediaTek", "BROM · Scatter", "layers", False),
    ("Fastboot", "Bootloader", "bolt", False),
    ("ADB", "Cihaz · Kabuk", "terminal", False),
    ("Modem", "AT · DIAG · QCN", "plug", False),
    ("Ayarlar", "Uygulama", "sliders", False),
]
ny = 88
for title, cap, ic, sel in NAV:
    rect((10, ny, 234, ny + 50), 9, fill=(19, 51, 50) if sel else None)
    if sel:
        rect((10, ny + 15, 13, ny + 35), 1.5, fill=ACCENT)
    ico((23, ny + 15, 19), ic, ACCENT if sel else TXT2)
    text((54, ny + 12), title, SB, ACCENT if sel else TXT2)
    text((54, ny + 29), cap, UI105, DIM)
    ny += 52

d.line([sc(16), sc(740), sc(238), sc(740)], fill=BORDER_SOFT, width=sc(1))
text((22, 752), "ALGILANAN CİHAZLAR", SB10, DIM)
text((238, 752), "2 cihaz", UI95, DIM, anchor="ra")

DETECTED = [
    ("chip", "COM4 · Qualcomm EDL", "Qualcomm HS-USB QDLoader 9008 (COM4)"),
    ("plug", "COM1 · Seri Port", "Standart Seri Bağlantı Noktası (COM1)"),
]
dy = 772
for ic, line1, line2 in DETECTED:
    rect((16, dy, 238, dy + 42), 7, fill=BG_FIELD, outline=BORDER_SOFT)
    ico((24, dy + 8, 13), ic, ACCENT)
    text((45, dy + 8), line1, SB11, TXT)
    text((45, dy + 25), line2, UI95, DIM)
    dy += 47

d.line([sc(16), sc(872), sc(238), sc(872)], fill=BORDER_SOFT, width=sc(1))
text((16, 884), "v1.0.0 · UI Preview", MONO10, TXT2)
text((16, 902), "© 2026 KC Tools", UI10, DIM)

# ---------- icerik basligi ----------
text((266, 68), "Qualcomm", TITLE, TXT)
text((266, 96), "EDL 9008, Firehose programlayıcı, FRP ve yedekleme iş akışları", UI12, MUTED)

# cihaz secici
combo_x, combo_y, combo_w, combo_h = 950, 74, 252, 38
rect((combo_x, combo_y, combo_x + combo_w, combo_y + combo_h), 8, fill=BG_FIELD, outline=BORDER)
ico((combo_x + 12, combo_y + 12, 14), "chip", ACCENT)
text((combo_x + 34, combo_y + 19), "COM4", MONO12, TXT, anchor="lm")
text((combo_x + 34 + tw("COM4", MONO12) + 8, combo_y + 19), "Qualcomm EDL", UI115, MUTED, anchor="lm")
chevron(combo_x + combo_w - 17, combo_y + 19, 12, MUTED)

rect((1210, combo_y, 1276, combo_y + combo_h), 8, fill=BG_FIELD, outline=BORDER)
text((1243, combo_y + 19), "Yenile", SB12, TXT2, anchor="mm")

rect((1284, combo_y, 1418, combo_y + combo_h), 8, fill=ACCENT)
text((1351, combo_y + 19), "Bağlantıyı Kes", SB12, ACCENT_INK, anchor="mm")

d.line([sc(1), sc(130), sc(W - 1), sc(130)], fill=BORDER_SOFT, width=sc(1))

# ---------- cihaz bilgisi karti ----------
rect((266, 146, 1050, 402), 12, fill=BG_CARD, outline=BORDER_SOFT)
text((284, 164), "Cihaz Bilgisi", SB, TXT)
pill(1032 - (tw("CANLI ALGILAMA", SB85) + 14), 160, "CANLI ALGILAMA", SB85, ACCENT, ACCENT_SOFT, padx=7, h=16)

FIELDS = [
    ("Aygıt", "Qualcomm HS-USB QDLoader 9008 (COM4)"),
    ("Seri numarası", "—"),
    ("Mod", "Qualcomm EDL"),
    ("Sürücü servisi", "qcusbser"),
    ("Port", "COM4"),
    ("Durum", "OK"),
]
fy = 196
for label, value in FIELDS:
    text((284, fy), label, UI115, MUTED)
    text((410, fy), value, UI, TXT)
    fy += 25

d.line([sc(284), sc(348), sc(1032), sc(348)], fill=BORDER_SOFT, width=sc(1))
text((284, 362), "Değerler Windows PnP bilgisinden okunur; seri numarası USB tanımlayıcısından", UI11, MUTED)
text((284, 378), "çözümlenir. Oturum açmadan alanlar boş kalır.", UI11, MUTED)

# ---------- araclar karti ----------
rect((266, 418, 1050, 706), 12, fill=BG_CARD, outline=BORDER_SOFT)
text((284, 436), "Qualcomm Araçları", SB, TXT)
text((284, 459), "EDL erişimi gerektiren iş akışları", UI11, MUTED)

TILES = [
    ("EDL Moduna Al", "9008 modunda Sahara el sıkışması", "bolt"),
    ("Bootloader Kilidi", "OEM kilidini doğrula ve aç", "shield"),
    ("FRP Sıfırlama", "Fabrika korumasını kaldır", "chip"),
    ("Firehose Flash", "Ham XML + imaj ile yaz", "plug"),
    ("Tam Yedek", "Bölümleri tek dosyada topla", "download"),
    ("Bölüm Bilgisi", "GPT tablosunu ve alanları oku", "terminal"),
]
tx0, ty0 = 284, 484
tw_ = 242
th_ = 92
for i, (title, desc, ic) in enumerate(TILES):
    col, row = i % 3, i // 3
    x0 = tx0 + col * (tw_ + 10)
    y0 = ty0 + row * (th_ + 10)
    rect((x0, y0, x0 + tw_, y0 + th_), 10, fill=BG_CARDA, outline=BORDER_SOFT)
    rect((x0 + 14, y0 + 14, x0 + 50, y0 + 50), 9, fill=ACCENT_SOFT)
    ico((x0 + 23, y0 + 23, 18), ic, ACCENT)
    text((x0 + 62, y0 + 16), title, SB12, TXT)
    text((x0 + 62, y0 + 36), desc, UI105, MUTED)

# ---------- islem durumu karti ----------
rect((1066, 146, 1418, 344), 12, fill=BG_CARD, outline=BORDER_SOFT)
text((1084, 164), "İşlem Durumu", SB, TXT)
pill(1340, 160, "ÇALIŞIYOR", SB95, ACCENT, ACCENT_SOFT, padx=9, h=17)

text((1084, 196), "Firehose yükleyici (prog_firehose_ddr.elf)", UI, TXT2)
text((1084, 214), "gönderiliyor...", UI, TXT2)

rect((1084, 244, 1400, 250), 3, fill=BG_FIELD, outline=BORDER_SOFT)
rect((1084, 244, 1084 + 316 * 0.62, 250), 3, fill=ACCENT)
text((1084, 260), "62%", MONO10, TXT2)
text((1400, 260), "adım günlüğü", UI105, DIM, anchor="ra")

d.line([sc(1084), sc(282), sc(1400), sc(282)], fill=BORDER_SOFT, width=sc(1))
text((1084, 296), "Her adım alt konsola zaman damgasıyla yazılır.", UI11, MUTED)
text((1084, 312), "İşlem sürerken diğer modüller kilitlenmez.", UI11, MUTED)

# ---------- gereksinimler karti ----------
rect((1066, 360, 1418, 512), 12, fill=BG_CARD, outline=BORDER_SOFT)
text((1084, 378), "Gereksinimler", SB, TXT)
req = [
    ("EDL moduna geçmek için ses kısma + USB kombinasyonu.", ACCENT),
    ("Firehose programlayıcı yonga ile eşleşmelidir.", ACCENT),
    ("FRP ve bootloader işlemleri sahiplik gerektirir.", WARNING),
]
ry = 404
for line, color in req:
    d.ellipse([sc(1084), sc(ry + 5), sc(1088), sc(ry + 9)], fill=color)
    text((1097, ry), line, UI105, MUTED)
    ry += 26

# ---------- konsol paneli ----------
LOG_Y = 712
rect((245, LOG_Y, W - 1, 925), 0, fill=BG_CONSOLE)
rect((245, LOG_Y, W - 1, LOG_Y + 42), 0, fill=BG_LOGHEAD)
d.line([sc(245), sc(LOG_Y + 42), sc(W - 1), sc(LOG_Y + 42)], fill=BORDER_SOFT, width=sc(1))
d.line([sc(245), sc(LOG_Y), sc(W - 1), sc(LOG_Y)], fill=BORDER, width=sc(1))

cy = sc(LOG_Y + 21)
d.ellipse([sc(258), cy - 3, sc(264), cy + 3], fill=SUCCESS)
text((272, LOG_Y + 21), "İŞLEM GÜNLÜĞÜ", SB10, TXT2, anchor="lm")
pill(392, LOG_Y + 13, "8", UI10, MUTED, BG_ELEV, padx=7, h=16)

# filtre segmentleri
seg_x = 960
rect((seg_x, LOG_Y + 11, seg_x + 236, LOG_Y + 31), 7, fill=BG_FIELD, outline=BORDER_SOFT)
for i, (lbl, active) in enumerate([("TÜMÜ", True), ("BİLGİ", False), ("UYARI", False), ("HATA", False)]):
    w_ = tw(lbl, SB95) + 18
    bx0 = seg_x + 2 + sum(tw(l, SB95) + 18 for l, _ in [("TÜMÜ", 0), ("BİLGİ", 0), ("UYARI", 0)][:i])
    if active:
        rect((bx0, LOG_Y + 13, bx0 + w_, LOG_Y + 29), 6, fill=ACCENT_SOFT)
    text((bx0 + w_ / 2, LOG_Y + 21), lbl, SB95, ACCENT if active else MUTED, anchor="mm")

text((1208, LOG_Y + 21), "Kopyala", UI105, TXT2, anchor="lm")
text((1272, LOG_Y + 21), "Temizle", UI105, TXT2, anchor="lm")
d.line([sc(1188), sc(LOG_Y + 13), sc(1188), sc(LOG_Y + 29)], fill=BORDER, width=sc(1))

# oto switch
sw_x = 1336
rect((sw_x, LOG_Y + 15, sw_x + 34, LOG_Y + 27), 6, fill=ACCENT)
d.ellipse([sc(sw_x + 21), sc(LOG_Y + 16.5), sc(sw_x + 31), sc(LOG_Y + 26.5)], fill=ACCENT_INK)
text((sw_x + 40, LOG_Y + 21), "Oto", UI105, MUTED, anchor="lm")

LOGS = [
    ("19:20:01", "BİLGİ", "── KC TOOLS 1.0.0 · CİHAZ SERVİSİ ─────────────────", TXT2),
    ("19:20:01", "BİLGİ", "Cihaz izleyici başlatıldı (WMI olayları + 15 sn güvenlik taraması).", TXT2),
    ("19:20:02", "TAMAM", "COM1 Connected (Seri Port)", SUCCESS),
    ("19:20:09", "TAMAM", "COM4 Connected (Qualcomm EDL)", SUCCESS),
    ("19:20:09", "BİLGİ", "── COM4 · QUALCOMM EDL · OTURUM AÇILIYOR ─────", TXT2),
    ("19:20:10", "ADIM", "Aygıt: Qualcomm HS-USB QDLoader 9008 (COM4)", INFO),
    ("19:20:11", "ADIM", "Sahara protokolü el sıkışması yapılıyor...", INFO),
    ("19:20:11", "ADIM", "Firehose yükleyici (prog_firehose_ddr.elf) gönderiliyor...", INFO),
]
ly = LOG_Y + 54
for t, tag, msg, color in LOGS:
    text((258, ly), t, MONO, (78, 98, 116))
    tag_color = {"ADIM": INFO, "TAMAM": SUCCESS, "UYARI": WARNING, "BİLGİ": MUTED}[tag]
    text((316, ly), tag, MONO, tag_color)
    text((366, ly), msg, MONO, (195, 211, 227) if tag == "BİLGİ" else color)
    ly += 17

d.line([sc(245), sc(900), sc(W - 1), sc(900)], fill=BORDER_SOFT, width=sc(1))
text((258, 913), ">", MONO, ACCENT, anchor="lm")
text((272, 913), "Günlük hazır — komutlar modül sayfalarından çalıştırılır.", MONO10, DIM, anchor="lm")

# ---------- durum cubugu ----------
rect((1, 926, W - 1, H - 1), 0, fill=BG_TITLE)
d.line([sc(1), sc(926), sc(W - 1), sc(926)], fill=BORDER_SOFT, width=sc(1))

sy = 943
d.ellipse([sc(16), sc(sy - 3), sc(22), sc(sy + 3)], fill=SUCCESS)
text((30, sy), "PORT", SB95, DIM, anchor="lm")
text((63, sy), "COM4 · Qualcomm EDL", UI11, TXT2, anchor="lm")
d.line([sc(198), sc(936), sc(198), sc(950)], fill=BORDER, width=sc(1))
text((212, sy), "MOD", SB95, DIM, anchor="lm")
text((248, sy), "EDL", SB95, ACCENT, anchor="lm")
d.line([sc(282), sc(936), sc(282), sc(950)], fill=BORDER, width=sc(1))
text((296, sy), "Bağlı", UI11, MUTED, anchor="lm")

vx = 1418
text((vx, sy), "W10/11", MONO10, DIM, anchor="rm")
vx -= tw("W10/11", MONO10) + 9
text((vx, sy), "·", UI11, DIM, anchor="rm")
vx -= tw("·", UI11) + 9
text((vx, sy), "v1.0.0 · UI Preview", MONO10, MUTED, anchor="rm")
vx -= tw("v1.0.0 · UI Preview", MONO10) + 14
d.line([sc(vx), sc(936), sc(vx), sc(950)], fill=BORDER, width=sc(1))
vx -= 14
lic_w = tw("Deneme · 30 gün", SB95) + 16
pill(vx - lic_w, 934, "Deneme · 30 gün", SB95, ACCENT, ACCENT_SOFT, padx=8, h=17)
vx -= lic_w + 8
text((vx, sy), "LİSANS", SB95, DIM, anchor="rm")

out = os.path.join("docs", "preview.png")
os.makedirs("docs", exist_ok=True)
img.resize((W, H), Image.LANCZOS).save(out, "PNG")
print("yazildi:", out)
