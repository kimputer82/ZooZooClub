"""UI에 쓰는 임시 그림(창, 버튼, 아이콘)을 만든다. Pillow가 필요하다.

    python Tools/make_ui.py            # 없는 그림만 만든다
    python Tools/make_ui.py --force    # 이미 있는 그림도 다시 만든다

그림은 Assets/ZooZooClub/Resources/Art 에 ui_*.png, item_*.png 로 저장된다.
직접 그린 그림으로 바꾸려면 같은 이름, 같은 크기의 PNG로 덮어쓰면 된다.
창·버튼처럼 늘여 쓰는 그림은 모서리 크기가 Ui.cs 의 값과 맞아야 한다.
"""
import math
import os
import sys

from PIL import Image, ImageDraw

S = 4  # 크게 그린 뒤 줄여서 가장자리를 부드럽게 한다.
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "ZooZooClub", "Resources", "Art")

INK = (92, 58, 33, 255)
CREAM = (255, 247, 226, 255)
WHITE = (255, 255, 255, 255)


class Canvas:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.im = Image.new("RGBA", (w * S, h * S), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.im)

    def layer(self):
        """반투명한 것을 겹쳐 그릴 때 쓰는 새 층."""
        return Canvas(self.w, self.h)

    def merge(self, other):
        self.im.alpha_composite(other.im)
        self.d = ImageDraw.Draw(self.im)

    def rrect(self, x0, y0, x1, y1, r, fill):
        self.d.rounded_rectangle([x0 * S, y0 * S, x1 * S - 1, y1 * S - 1], radius=r * S, fill=fill)

    def ellipse(self, x0, y0, x1, y1, fill):
        self.d.ellipse([x0 * S, y0 * S, x1 * S - 1, y1 * S - 1], fill=fill)

    def circle(self, cx, cy, r, fill):
        self.ellipse(cx - r, cy - r, cx + r, cy + r, fill)

    def poly(self, pts, fill):
        self.d.polygon([(x * S, y * S) for x, y in pts], fill=fill)

    def line(self, pts, fill, width):
        p = [(x * S, y * S) for x, y in pts]
        self.d.line(p, fill=fill, width=int(width * S), joint="curve")
        r = width * S / 2
        for x, y in p:
            self.d.ellipse([x - r, y - r, x + r, y + r], fill=fill)

    def outlined_poly(self, pts, fill, width=4, outline=INK):
        self.line(list(pts) + [pts[0]], outline, width * 2)
        self.poly(pts, fill)

    def outlined_circle(self, cx, cy, r, fill, width=4, outline=INK):
        self.circle(cx, cy, r + width, outline)
        self.circle(cx, cy, r, fill)

    def outlined_ellipse(self, x0, y0, x1, y1, fill, width=4, outline=INK):
        self.ellipse(x0 - width, y0 - width, x1 + width, y1 + width, outline)
        self.ellipse(x0, y0, x1, y1, fill)

    def outlined_rrect(self, x0, y0, x1, y1, r, fill, width=4, outline=INK):
        self.rrect(x0 - width, y0 - width, x1 + width, y1 + width, r + width, outline)
        self.rrect(x0, y0, x1, y1, r, fill)

    def save(self, name, force):
        path = os.path.join(OUT, name + ".png")
        if os.path.exists(path) and not force:
            return False
        self.im.resize((self.w, self.h), Image.LANCZOS).save(path)
        return True


def gray(v, a=255):
    return (v, v, v, a)


# ---------- 늘여 쓰는 바탕 ----------

def ui_panel():
    c = Canvas(96, 100)
    c.rrect(0, 0, 96, 100, 30, INK)            # 테두리와 아래쪽 두께
    c.rrect(4, 4, 92, 90, 26, (240, 221, 184, 255))
    c.rrect(8, 8, 88, 86, 22, CREAM)
    return c


def ui_card():
    c = Canvas(64, 68)
    c.rrect(0, 0, 64, 68, 18, (214, 188, 146, 255))
    c.rrect(2, 2, 62, 62, 16, WHITE)
    return c


def ui_button():
    # 흑백으로 그려 두고 게임에서 색을 입힌다.
    c = Canvas(64, 72)
    c.rrect(0, 0, 64, 72, 20, gray(130))
    c.rrect(3, 3, 61, 62, 17, gray(236))
    hi = c.layer()
    hi.rrect(8, 6, 56, 24, 10, gray(255, 150))
    c.merge(hi)
    return c


def ui_capsule():
    c = Canvas(56, 56)
    c.circle(28, 28, 28, WHITE)
    return c


def ui_circle():
    c = Canvas(128, 136)
    c.circle(64, 72, 64, gray(125))
    c.circle(64, 64, 64, gray(125))
    c.circle(64, 64, 59, gray(236))
    hi = c.layer()
    hi.ellipse(26, 12, 102, 58, gray(255, 120))
    c.merge(hi)
    return c


def ui_dot():
    c = Canvas(64, 64)
    c.circle(32, 32, 32, WHITE)
    return c


def ui_close():
    c = Canvas(80, 86)
    c.circle(40, 46, 40, (150, 48, 44, 255))
    c.circle(40, 40, 40, (150, 48, 44, 255))
    c.circle(40, 40, 35, (240, 98, 88, 255))
    c.line([(27, 27), (53, 53)], WHITE, 9)
    c.line([(53, 27), (27, 53)], WHITE, 9)
    return c


# ---------- 아이콘 ----------

def icon_coin():
    c = Canvas(96, 96)
    c.outlined_circle(48, 48, 38, (255, 204, 64, 255))
    c.circle(48, 48, 27, (240, 170, 40, 255))
    c.circle(48, 48, 22, (255, 216, 96, 255))
    hi = c.layer()
    hi.ellipse(24, 18, 52, 38, (255, 255, 255, 110))
    c.merge(hi)
    return c


def icon_gem():
    c = Canvas(96, 96)
    outer = [(16, 38), (32, 16), (64, 16), (80, 38), (48, 84)]
    c.outlined_poly(outer, (86, 186, 255, 255))
    c.poly([(16, 38), (32, 16), (64, 16), (80, 38)], (150, 222, 255, 255))
    c.poly([(34, 38), (42, 16), (54, 16), (62, 38)], (214, 242, 255, 255))
    c.poly([(34, 38), (62, 38), (48, 84)], (120, 204, 255, 255))
    return c


def star_points(cx, cy, r_out, r_in, n=5):
    pts = []
    for i in range(n * 2):
        r = r_out if i % 2 == 0 else r_in
        a = -math.pi / 2 + i * math.pi / n
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def icon_star():
    c = Canvas(96, 96)
    c.outlined_poly(star_points(48, 51, 40, 19), (255, 212, 70, 255), 5)
    hi = c.layer()
    hi.poly(star_points(44, 44, 16, 8), (255, 250, 200, 170))
    c.merge(hi)
    return c


def heart(c, cx, cy, s, fill):
    c.circle(cx - s * 0.5, cy - s * 0.3, s * 0.56, fill)
    c.circle(cx + s * 0.5, cy - s * 0.3, s * 0.56, fill)
    c.poly([(cx - s * 1.02, cy - s * 0.05), (cx + s * 1.02, cy - s * 0.05), (cx, cy + s * 1.05)], fill)


def icon_heart():
    c = Canvas(96, 96)
    heart(c, 48, 46, 37, INK)
    heart(c, 48, 46, 31, (255, 118, 150, 255))
    hi = c.layer()
    hi.ellipse(24, 24, 42, 38, (255, 255, 255, 130))
    c.merge(hi)
    return c


def icon_box():
    c = Canvas(96, 96)
    c.outlined_rrect(16, 30, 80, 80, 6, (214, 150, 86, 255))
    c.rrect(16, 44, 80, 50, 0, (176, 114, 60, 255))
    c.rrect(16, 62, 80, 68, 0, (176, 114, 60, 255))
    c.outlined_rrect(12, 16, 84, 32, 6, (236, 178, 110, 255))
    c.rrect(42, 22, 54, 40, 3, INK)
    c.rrect(45, 25, 51, 37, 2, (250, 222, 120, 255))
    return c


def icon_build():
    c = Canvas(96, 96)
    c.line([(26, 74), (58, 42)], INK, 18)
    c.line([(26, 74), (58, 42)], (200, 140, 80, 255), 10)
    head = [(44, 14), (84, 46), (70, 60), (30, 28)]
    c.outlined_poly(head, (176, 184, 196, 255))
    c.poly([(44, 14), (84, 46), (79, 51), (39, 19)], (214, 220, 230, 255))
    return c


def icon_orders():
    c = Canvas(96, 96)
    c.outlined_rrect(18, 18, 78, 84, 8, (226, 170, 100, 255))
    c.rrect(25, 28, 71, 77, 4, WHITE)
    for y in (42, 54, 66):
        c.line([(34, y), (62, y)], (196, 176, 150, 255), 4)
    c.outlined_rrect(34, 10, 62, 26, 6, (176, 184, 196, 255))
    return c


def icon_residents():
    c = Canvas(96, 96)
    fill = (255, 240, 214, 255)
    toes = [(22, 40, 10), (39, 24, 11), (59, 24, 11), (76, 40, 10)]
    for x, y, r in toes:
        c.circle(x, y, r + 4, INK)
    c.ellipse(22 - 4, 44 - 4, 74 + 4, 84 + 4, INK)
    for x, y, r in toes:
        c.circle(x, y, r, fill)
    c.ellipse(22, 44, 74, 84, fill)
    return c


def icon_expand():
    c = Canvas(96, 96)
    c.outlined_poly([(48, 22), (88, 44), (48, 66), (8, 44)], (140, 204, 96, 255))
    c.poly([(48, 22), (88, 44), (48, 44)], (166, 220, 120, 255))
    c.outlined_circle(66, 66, 17, WHITE)
    c.line([(57, 66), (75, 66)], (90, 170, 70, 255), 6)
    c.line([(66, 57), (66, 75)], (90, 170, 70, 255), 6)
    return c


def icon_clock():
    c = Canvas(96, 96)
    c.outlined_circle(48, 48, 36, WHITE)
    c.circle(48, 48, 30, (236, 244, 250, 255))
    c.line([(48, 48), (48, 28)], INK, 6)
    c.line([(48, 48), (62, 56)], INK, 6)
    return c


def icon_lock():
    c = Canvas(96, 96)
    c.ellipse(26, 12, 70, 62, INK)
    c.ellipse(36, 22, 60, 60, (0, 0, 0, 0))
    c.outlined_rrect(20, 42, 76, 84, 8, (250, 204, 86, 255))
    c.circle(48, 60, 7, INK)
    c.line([(48, 60), (48, 72)], INK, 6)
    return c


def icon_move():
    c = Canvas(96, 96)
    for dx, dy in ((0, -1), (1, 0), (0, 1), (-1, 0)):
        tip = (48 + dx * 38, 48 + dy * 38)
        base = (48 + dx * 20, 48 + dy * 20)
        side = (-dy * 14, dx * 14)
        tri = [tip, (base[0] + side[0], base[1] + side[1]), (base[0] - side[0], base[1] - side[1])]
        c.outlined_poly(tri, WHITE, 4)
    c.outlined_circle(48, 48, 9, WHITE)
    return c


# ---------- 물건 ----------

def item_bread():
    c = Canvas(96, 96)
    c.outlined_rrect(12, 34, 84, 76, 20, (222, 156, 84, 255))
    c.rrect(16, 36, 80, 56, 16, (240, 184, 110, 255))
    for x in (30, 46, 62):
        c.line([(x, 42), (x + 8, 58)], (190, 120, 60, 255), 5)
    return c


def item_carrot_cake():
    c = Canvas(96, 96)
    c.outlined_rrect(16, 40, 80, 80, 8, (232, 164, 92, 255))
    c.rrect(16, 56, 80, 62, 0, (255, 240, 214, 255))
    c.outlined_rrect(12, 28, 84, 46, 9, (255, 244, 224, 255))
    c.outlined_poly([(40, 14), (56, 14), (48, 30)], (255, 150, 50, 255), 3)
    c.line([(48, 14), (48, 8)], (110, 180, 80, 255), 5)
    return c


def item_pumpkin_pie():
    c = Canvas(96, 96)
    c.outlined_circle(48, 50, 36, (228, 172, 102, 255))
    c.circle(48, 50, 27, (238, 142, 52, 255))
    for a in range(0, 180, 60):
        dx, dy = math.cos(math.radians(a)) * 27, math.sin(math.radians(a)) * 27
        c.line([(48 - dx, 50 - dy), (48 + dx, 50 + dy)], (206, 112, 40, 255), 3)
    c.circle(48, 50, 6, (255, 246, 226, 255))
    return c


def juice(liquid):
    c = Canvas(96, 96)
    c.line([(58, 30), (70, 8)], INK, 12)
    c.line([(58, 30), (70, 8)], (255, 255, 255, 255), 5)
    glass = [(24, 26), (72, 26), (66, 84), (30, 84)]
    c.outlined_poly(glass, (236, 246, 250, 255))
    c.poly([(26, 40), (70, 40), (66, 84), (30, 84)], liquid)
    hi = c.layer()
    hi.poly([(31, 44), (38, 44), (40, 78), (35, 78)], (255, 255, 255, 120))
    c.merge(hi)
    return c


def item_carrot_juice():
    return juice((255, 152, 52, 255))


def item_strawberry_juice():
    return juice((255, 110, 134, 255))


def item_strawberry_jam():
    c = Canvas(96, 96)
    c.outlined_rrect(20, 32, 76, 84, 12, (222, 64, 86, 255))
    c.rrect(28, 46, 68, 70, 5, (255, 246, 230, 255))
    heart(c, 48, 57, 9, (232, 80, 100, 255))
    c.outlined_rrect(18, 16, 78, 32, 6, (250, 226, 150, 255))
    return c


def item_popcorn():
    c = Canvas(96, 96)
    for x, y, r in ((30, 30, 12), (48, 22, 14), (66, 30, 12), (40, 36, 11), (57, 36, 11)):
        c.circle(x, y, r + 4, INK)
    for x, y, r in ((30, 30, 12), (48, 22, 14), (66, 30, 12), (40, 36, 11), (57, 36, 11)):
        c.circle(x, y, r, (255, 240, 170, 255))
    box = [(20, 40), (76, 40), (68, 86), (28, 86)]
    c.outlined_poly(box, WHITE)
    for i in (0, 2, 4):
        x0, x1 = 20 + i * 11.2, 20 + (i + 1) * 11.2
        b0, b1 = 28 + i * 8, 28 + (i + 1) * 8
        c.poly([(x0, 40), (x1, 40), (b1, 86), (b0, 86)], (236, 80, 74, 255))
    return c


SPRITES = {
    "ui_panel": ui_panel,
    "ui_card": ui_card,
    "ui_button": ui_button,
    "ui_capsule": ui_capsule,
    "ui_circle": ui_circle,
    "ui_dot": ui_dot,
    "ui_close": ui_close,
    "ui_icon_coin": icon_coin,
    "ui_icon_gem": icon_gem,
    "ui_icon_star": icon_star,
    "ui_icon_heart": icon_heart,
    "ui_icon_box": icon_box,
    "ui_icon_build": icon_build,
    "ui_icon_orders": icon_orders,
    "ui_icon_residents": icon_residents,
    "ui_icon_expand": icon_expand,
    "ui_icon_clock": icon_clock,
    "ui_icon_lock": icon_lock,
    "ui_icon_move": icon_move,
    "item_bread": item_bread,
    "item_carrot_cake": item_carrot_cake,
    "item_pumpkin_pie": item_pumpkin_pie,
    "item_carrot_juice": item_carrot_juice,
    "item_strawberry_juice": item_strawberry_juice,
    "item_strawberry_jam": item_strawberry_jam,
    "item_popcorn": item_popcorn,
}


def main():
    force = "--force" in sys.argv
    os.makedirs(OUT, exist_ok=True)
    made = 0
    for name, draw in SPRITES.items():
        if draw().save(name, force):
            made += 1
    print("made %d of %d" % (made, len(SPRITES)))


if __name__ == "__main__":
    main()
