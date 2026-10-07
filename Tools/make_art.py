"""Zoo Zoo Club 그림 생성기.

단순한 도형만으로 동물, 건물, 작물, 장식 그림을 그려 PNG로 저장한다.
4배 크기로 그린 뒤 줄여서 가장자리를 부드럽게 만든다.

사용법: python3 make_art.py <출력 폴더>
"""
import math
import os
import sys

from PIL import Image, ImageDraw

SS = 4  # supersampling
LINE = (74, 54, 44, 255)


class Canvas:
    def __init__(self, width, height):
        self.w, self.h = width, height
        self.img = Image.new("RGBA", (width * SS, height * SS), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)
        self.lw = max(2, int(min(width, height) / 52)) * SS

    def _box(self, cx, cy, rx, ry):
        return [(cx - rx) * SS, (cy - ry) * SS, (cx + rx) * SS, (cy + ry) * SS]

    def ellipse(self, cx, cy, rx, ry, fill, outline=True):
        self.d.ellipse(self._box(cx, cy, rx, ry), fill=fill,
                       outline=LINE if outline else None, width=self.lw if outline else 0)

    def rect(self, x0, y0, x1, y1, fill, radius=0, outline=True):
        self.d.rounded_rectangle([x0 * SS, y0 * SS, x1 * SS, y1 * SS], radius=radius * SS, fill=fill,
                                 outline=LINE if outline else None, width=self.lw if outline else 0)

    def poly(self, points, fill, outline=True):
        pts = [(x * SS, y * SS) for x, y in points]
        self.d.polygon(pts, fill=fill)
        if outline:
            self.d.line(pts + [pts[0]], fill=LINE, width=self.lw, joint="curve")

    def line(self, points, fill=LINE, width=None):
        pts = [(x * SS, y * SS) for x, y in points]
        self.d.line(pts, fill=fill, width=width * SS if width else self.lw, joint="curve")

    def dot(self, cx, cy, r, fill=LINE):
        self.d.ellipse(self._box(cx, cy, r, r), fill=fill)

    def shadow(self, cx, cy, rx, ry):
        self.d.ellipse(self._box(cx, cy, rx, ry), fill=(40, 60, 40, 60))

    def save(self, path):
        out = self.img.resize((self.w, self.h), Image.LANCZOS)
        out.save(path)


def shade(color, factor):
    r, g, b = color[:3]
    return (max(0, min(255, int(r * factor))), max(0, min(255, int(g * factor))),
            max(0, min(255, int(b * factor))), 255)


# ---------------------------------------------------------------- 동물

def smile(c, cx, top):
    """코 아래로 내려와 양쪽으로 살짝 올라가는 입."""
    c.line([(cx, top), (cx, top + 5)], width=2)
    c.line([(cx - 12, top + 4), (cx - 6, top + 10), (cx, top + 5), (cx + 6, top + 10), (cx + 12, top + 4)], width=2)

def animal(kind):
    c = Canvas(256, 256)
    palette = {
        "dog": (233, 196, 148), "rabbit": (250, 244, 236), "bear": (176, 124, 84),
        "elephant": (168, 190, 212), "fox": (242, 146, 62),
    }
    base = palette[kind] + (255,)
    dark = shade(base, 0.72)
    light = shade(base, 1.12) if kind != "rabbit" else (255, 255, 255, 255)
    pink = (246, 170, 176, 255)
    cream = (252, 242, 226, 255)

    c.shadow(128, 236, 62, 12)

    # 귀 (머리 뒤)
    if kind == "dog":
        c.ellipse(62, 112, 24, 44, dark)
        c.ellipse(194, 112, 24, 44, dark)
    elif kind == "rabbit":
        for x in (98, 158):
            c.ellipse(x, 52, 19, 50, base)
            c.ellipse(x, 56, 9, 36, pink, outline=False)
    elif kind == "bear":
        for x in (72, 184):
            c.ellipse(x, 54, 25, 25, base)
            c.ellipse(x, 56, 12, 12, shade(base, 0.8), outline=False)
    elif kind == "elephant":
        for x in (50, 206):
            c.ellipse(x, 104, 40, 50, base)
            c.ellipse(x, 106, 24, 34, pink, outline=False)
    elif kind == "fox":
        c.poly([(54, 96), (62, 22), (112, 62)], base)
        c.poly([(202, 96), (194, 22), (144, 62)], base)
        c.poly([(68, 78), (70, 44), (94, 64)], (70, 50, 44, 255), outline=False)
        c.poly([(188, 78), (186, 44), (162, 64)], (70, 50, 44, 255), outline=False)

    # 몸
    c.ellipse(128, 200, 48, 40, base)
    c.ellipse(128, 206, 28, 26, cream if kind != "elephant" else light, outline=False)
    c.ellipse(100, 234, 18, 10, dark if kind != "rabbit" else base)
    c.ellipse(156, 234, 18, 10, dark if kind != "rabbit" else base)
    if kind == "fox":
        c.ellipse(190, 206, 22, 14, base)
        c.ellipse(204, 200, 10, 8, cream, outline=False)

    # 머리
    c.ellipse(128, 112, 74, 66, base)

    # 얼굴
    if kind == "fox":
        c.poly([(70, 124), (128, 172), (186, 124), (160, 150), (128, 160), (96, 150)], cream, outline=False)
        c.ellipse(128, 146, 26, 20, cream, outline=False)
    elif kind in ("dog", "bear"):
        c.ellipse(128, 138, 30, 23, cream, outline=False)
    elif kind == "elephant":
        c.rect(112, 124, 144, 200, base, radius=16)
        c.line([(116, 150), (140, 150)], fill=dark, width=2)
        c.line([(116, 168), (140, 168)], fill=dark, width=2)

    # 눈
    eye_y = 108 if kind != "elephant" else 104
    for x in (98, 158):
        c.dot(x, eye_y, 8)
        c.dot(x + 2.5, eye_y - 3, 2.6, (255, 255, 255, 255))

    # 볼
    cheek_under = cream if kind == "fox" else base
    cheek = tuple(int(cheek_under[i] * 0.5 + (250, 140, 150)[i] * 0.5) for i in range(3)) + (255,)
    for x in (78, 178):
        c.d.ellipse(c._box(x, 132, 12, 8), fill=cheek)

    # 코와 입
    if kind == "rabbit":
        c.dot(128, 128, 5, (236, 130, 140, 255))
        smile(c, 128, 134)
    elif kind != "elephant":
        c.ellipse(128, 130, 9, 6.5, (60, 44, 40, 255), outline=False)
        smile(c, 128, 137)
    else:
        c.line([(150, 150), (158, 156), (168, 152)], width=2)
    return c


# ---------------------------------------------------------------- 건물

def house(c, wall, roof, cx=192, wall_w=240, wall_h=150, base_y=350, roof_h=120):
    x0, x1 = cx - wall_w / 2, cx + wall_w / 2
    y0 = base_y - wall_h
    c.shadow(cx, base_y + 2, wall_w * 0.56, 18)
    c.rect(x0, y0, x1, base_y, wall, radius=10)
    c.poly([(x0 - 26, y0 + 12), (cx - wall_w * 0.3, y0 - roof_h), (cx + wall_w * 0.3, y0 - roof_h), (x1 + 26, y0 + 12)], roof)
    c.line([(x0 - 8, y0 - 28), (x1 + 8, y0 - 28)], fill=shade(roof, 0.82), width=3)
    c.line([(x0 + 14, y0 - 66), (x1 - 14, y0 - 66)], fill=shade(roof, 0.82), width=3)
    return x0, y0, x1


def door_and_window(c, x0, y0, x1, base_y, door_color, window_side="right"):
    cx = (x0 + x1) / 2
    c.rect(cx - 30, base_y - 92, cx + 30, base_y, door_color, radius=8)
    c.dot(cx + 16, base_y - 44, 4, (250, 220, 120, 255))
    wx = x1 - 62 if window_side == "right" else x0 + 62
    c.rect(wx - 30, y0 + 34, wx + 30, y0 + 90, (190, 228, 246, 255), radius=6)
    c.line([(wx, y0 + 34), (wx, y0 + 90)], width=2)
    c.line([(wx - 30, y0 + 62), (wx + 30, y0 + 62)], width=2)


def sign(c, cx, cy, r=34):
    c.ellipse(cx, cy, r, r, (255, 250, 236, 255))


def bakery():
    c = Canvas(384, 384)
    x0, y0, x1 = house(c, (250, 232, 200, 255), (176, 106, 72, 255))
    door_and_window(c, x0, y0, x1, 350, (150, 98, 66, 255))
    # 줄무늬 차양
    for i in range(6):
        col = (236, 110, 100, 255) if i % 2 == 0 else (255, 250, 240, 255)
        c.rect(x0 + 8 + i * 37.5, y0 + 2, x0 + 8 + (i + 1) * 37.5, y0 + 26, col, radius=0)
    sign(c, 192, y0 - 62)
    c.ellipse(192, y0 - 60, 24, 15, (222, 160, 96, 255))
    for dx in (-10, 0, 10):
        c.line([(192 + dx - 3, y0 - 68), (192 + dx + 3, y0 - 56)], fill=(160, 104, 56, 255), width=2)
    # 굴뚝
    c.rect(x1 - 70, y0 - 150, x1 - 36, y0 - 92, (200, 130, 96, 255), radius=4)
    return c


def juice_bar():
    c = Canvas(384, 384)
    x0, y0, x1 = house(c, (255, 226, 170, 255), (110, 178, 110, 255))
    door_and_window(c, x0, y0, x1, 350, (236, 140, 80, 255), "left")
    sign(c, 192, y0 - 62)
    c.poly([(176, y0 - 82), (208, y0 - 82), (202, y0 - 40), (182, y0 - 40)], (255, 176, 80, 255))
    c.line([(196, y0 - 96), (190, y0 - 60)], fill=(236, 90, 100, 255), width=3)
    c.ellipse(210, y0 - 84, 8, 8, (250, 220, 90, 255))
    return c


def jam_workshop():
    c = Canvas(384, 384)
    x0, y0, x1 = house(c, (250, 214, 224, 255), (176, 70, 96, 255))
    door_and_window(c, x0, y0, x1, 350, (150, 80, 100, 255))
    sign(c, 192, y0 - 62)
    c.rect(176, y0 - 78, 208, y0 - 40, (220, 60, 90, 255), radius=7)
    c.rect(172, y0 - 88, 212, y0 - 76, (250, 236, 200, 255), radius=3)
    c.rect(182, y0 - 66, 202, y0 - 52, (255, 246, 236, 255), radius=2, outline=False)
    # 선반의 잼 병
    for i, col in enumerate([(220, 60, 90, 255), (240, 150, 60, 255), (150, 90, 170, 255)]):
        bx = x0 + 34 + i * 26
        c.rect(bx - 9, 350 - 30, bx + 9, 350 - 4, col, radius=4)
    return c


def popcorn_cart():
    c = Canvas(288, 384)
    cx = 144
    c.shadow(cx, 356, 96, 16)
    # 바퀴
    for x in (cx - 58, cx + 58):
        c.ellipse(x, 328, 28, 28, (120, 90, 70, 255))
        c.ellipse(x, 328, 9, 9, (240, 210, 150, 255))
    # 수레 몸통
    c.rect(cx - 96, 206, cx + 96, 312, (236, 84, 80, 255), radius=12)
    for i in range(4):
        c.rect(cx - 96 + 24 + i * 48, 208, cx - 96 + 48 + i * 48, 310, (255, 248, 236, 255), radius=0, outline=False)
    c.rect(cx - 96, 206, cx + 96, 312, None, radius=12)
    # 유리 상자와 팝콘
    c.rect(cx - 76, 110, cx + 76, 210, (214, 238, 250, 255), radius=8)
    for (px, py, r) in [(-44, 182, 18), (-14, 176, 20), (18, 180, 19), (46, 184, 17), (-28, 156, 16), (4, 150, 18), (34, 158, 15)]:
        c.ellipse(cx + px, py, r, r * 0.9, (255, 238, 150, 255))
    # 지붕
    c.poly([(cx - 104, 114), (cx - 70, 58), (cx + 70, 58), (cx + 104, 114)], (236, 84, 80, 255))
    c.ellipse(cx, 52, 12, 12, (255, 220, 90, 255))
    return c


# ---------------------------------------------------------------- 작물

def leaves(c, cx, cy, n=3, length=40, color=(104, 176, 96, 255)):
    for i in range(n):
        angle = math.radians(-90 + (i - (n - 1) / 2) * 34)
        ex, ey = cx + math.cos(angle) * length, cy + math.sin(angle) * length
        mx, my = (cx + ex) / 2, (cy + ey) / 2
        nx, ny = -(ey - cy) * 0.2, (ex - cx) * 0.2
        c.poly([(cx, cy), (mx + nx, my + ny), (ex, ey), (mx - nx, my - ny)], color)


def crop(kind):
    c = Canvas(160, 160)
    c.shadow(80, 146, 40, 8)
    if kind == "sprout":
        c.line([(80, 142), (80, 104)], fill=(92, 160, 84, 255), width=5)
        c.ellipse(62, 96, 20, 12, (120, 196, 104, 255))
        c.ellipse(98, 96, 20, 12, (120, 196, 104, 255))
    elif kind == "carrot":
        leaves(c, 80, 70, 3, 46)
        c.poly([(52, 66), (108, 66), (96, 112), (80, 146), (64, 112)], (244, 140, 50, 255))
        for y in (86, 104, 122):
            c.line([(70 + (y - 86) * 0.2, y), (84, y + 3)], fill=(210, 106, 30, 255), width=2)
    elif kind == "wheat":
        for dx, lean in ((-24, -10), (0, 0), (24, 10)):
            c.line([(80 + dx * 0.4, 146), (80 + dx + lean * 0.4, 60)], fill=(200, 160, 70, 255), width=4)
            for k in range(4):
                y = 34 + k * 15
                c.ellipse(80 + dx + lean * (1 - k * 0.1) - 7, y + 4, 8, 11, (244, 204, 96, 255))
                c.ellipse(80 + dx + lean * (1 - k * 0.1) + 7, y + 4, 8, 11, (244, 204, 96, 255))
    elif kind == "strawberry":
        c.poly([(38, 70), (56, 50), (104, 50), (122, 70), (112, 108), (80, 144), (48, 108)], (232, 60, 74, 255))
        for (x, y) in [(62, 76), (82, 70), (100, 80), (70, 98), (92, 100), (80, 120)]:
            c.dot(x, y, 3, (255, 232, 150, 255))
        for a in (-60, -20, 20, 60):
            ang = math.radians(-90 + a)
            c.poly([(80, 54), (80 + math.cos(ang) * 30 - 8, 54 + math.sin(ang) * 30),
                    (80 + math.cos(ang) * 34, 54 + math.sin(ang) * 34)], (104, 176, 96, 255))
    elif kind == "corn":
        c.ellipse(80, 82, 26, 56, (250, 214, 80, 255))
        for y in range(40, 130, 14):
            c.line([(60, y), (100, y)], fill=(224, 176, 50, 255), width=2)
        for x in (70, 80, 90):
            c.line([(x, 34), (x, 132)], fill=(224, 176, 50, 255), width=2)
        c.poly([(80, 146), (40, 120), (36, 70), (62, 110)], (118, 186, 100, 255))
        c.poly([(80, 146), (120, 120), (124, 70), (98, 110)], (118, 186, 100, 255))
    elif kind == "pumpkin":
        c.rect(74, 38, 88, 66, (112, 150, 84, 255), radius=5)
        c.ellipse(80, 100, 58, 44, (240, 142, 50, 255))
        c.ellipse(80, 100, 30, 44, (246, 158, 66, 255))
        c.line([(80, 58), (80, 142)], fill=(206, 110, 34, 255), width=2)
    return c


# ---------------------------------------------------------------- 장식

def fence():
    c = Canvas(160, 160)
    c.shadow(80, 146, 56, 7)
    for x in (34, 80, 126):
        c.poly([(x - 14, 144), (x - 14, 66), (x, 48), (x + 14, 66), (x + 14, 144)], (214, 170, 118, 255))
    c.rect(16, 84, 144, 98, (190, 144, 96, 255), radius=4)
    c.rect(16, 116, 144, 130, (190, 144, 96, 255), radius=4)
    return c


def flower_bed():
    c = Canvas(160, 160)
    c.shadow(80, 146, 58, 8)
    c.ellipse(80, 126, 62, 24, (116, 180, 96, 255))
    for (x, y, col) in [(46, 96, (250, 150, 180, 255)), (82, 78, (255, 214, 90, 255)), (116, 98, (180, 150, 240, 255))]:
        c.line([(x, 124), (x, y)], fill=(84, 150, 80, 255), width=4)
        for k in range(5):
            ang = math.radians(k * 72 - 90)
            c.ellipse(x + math.cos(ang) * 12, y + math.sin(ang) * 12, 9, 9, col)
        c.ellipse(x, y, 7, 7, (255, 240, 180, 255))
    return c


def tree():
    c = Canvas(192, 256)
    c.shadow(96, 240, 50, 10)
    c.rect(82, 150, 110, 240, (150, 104, 70, 255), radius=6)
    c.ellipse(96, 96, 76, 78, (86, 164, 96, 255))
    c.ellipse(72, 76, 34, 30, (116, 190, 112, 255), outline=False)
    for (x, y) in [(126, 112), (88, 134), (116, 66)]:
        c.dot(x, y, 7, (240, 90, 80, 255))
    return c


def bench():
    c = Canvas(256, 160)
    c.shadow(128, 146, 104, 8)
    for x in (48, 208):
        c.rect(x - 8, 92, x + 8, 146, (120, 86, 60, 255), radius=3)
    c.rect(22, 42, 234, 62, (206, 156, 100, 255), radius=6)
    c.rect(22, 68, 234, 88, (206, 156, 100, 255), radius=6)
    c.rect(14, 96, 242, 118, (222, 172, 112, 255), radius=7)
    return c


def street_lamp():
    c = Canvas(128, 256)
    c.shadow(64, 244, 30, 7)
    c.d.ellipse(c._box(64, 60, 54, 54), fill=(255, 240, 150, 70))
    c.rect(57, 86, 71, 240, (86, 94, 110, 255), radius=4)
    c.rect(44, 232, 84, 244, (86, 94, 110, 255), radius=4)
    c.poly([(40, 80), (46, 36), (82, 36), (88, 80)], (255, 236, 150, 255))
    c.poly([(34, 38), (64, 14), (94, 38)], (86, 94, 110, 255))
    return c


def fountain():
    c = Canvas(384, 320)
    c.shadow(192, 292, 150, 20)
    c.ellipse(192, 244, 160, 52, (196, 200, 210, 255))
    c.ellipse(192, 236, 134, 38, (120, 196, 240, 255))
    c.rect(174, 130, 210, 240, (206, 210, 220, 255), radius=8)
    c.ellipse(192, 136, 62, 18, (196, 200, 210, 255))
    c.ellipse(192, 130, 46, 11, (120, 196, 240, 255))
    c.rect(184, 70, 200, 132, (206, 210, 220, 255), radius=6)
    for dx in (-1, 1):
        pts = []
        for t in range(0, 11):
            u = t / 10
            pts.append((192 + dx * u * 78, 70 - math.sin(u * math.pi) * 44 + u * 150))
        c.line(pts, fill=(170, 224, 250, 255), width=7)
    c.ellipse(192, 62, 14, 14, (170, 224, 250, 255))
    return c


def plot():
    c = Canvas(256, 128)
    soil = (150, 104, 68, 255)
    c.poly([(128, 4), (252, 64), (128, 124), (4, 64)], soil)
    for k in (0.28, 0.5, 0.72):
        # 한쪽 변과 나란한 고랑
        ax, ay = 4 + (128 - 4) * k, 64 - (64 - 4) * k
        bx, by = 128 + (252 - 128) * k, 124 - (124 - 64) * k
        c.line([(ax + 10, ay + 5), (bx - 10, by - 5)], fill=(120, 80, 50, 255), width=5)
    return c


def main(out_dir):
    os.makedirs(out_dir, exist_ok=True)
    jobs = {}
    for kind in ("dog", "rabbit", "bear", "elephant", "fox"):
        jobs["resident_" + kind] = animal(kind)
    jobs["bakery"] = bakery()
    jobs["juice_bar"] = juice_bar()
    jobs["jam_workshop"] = jam_workshop()
    jobs["popcorn_cart"] = popcorn_cart()
    for kind in ("sprout", "carrot", "wheat", "strawberry", "corn", "pumpkin"):
        jobs["crop_" + kind] = crop(kind)
    jobs["fence"] = fence()
    jobs["flower_bed"] = flower_bed()
    jobs["tree"] = tree()
    jobs["bench"] = bench()
    jobs["street_lamp"] = street_lamp()
    jobs["fountain"] = fountain()
    jobs["plot"] = plot()
    for name, canvas in jobs.items():
        canvas.save(os.path.join(out_dir, name + ".png"))
    print(len(jobs), "sprites written to", out_dir)


if __name__ == "__main__":
    main(sys.argv[1])
