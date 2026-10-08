"""경비병, 경비탑, 침략자, 문제·방패 아이콘의 임시 그림을 만든다. Pillow가 필요하다.

    python Tools/make_defense.py            # 없는 그림만 만든다
    python Tools/make_defense.py --force    # 이미 있는 그림도 다시 만든다

그림은 Assets/ZooZooClub/Resources/Art 에 저장된다. 직접 그린 그림으로 바꾸려면
같은 이름의 PNG로 덮어쓰고, 크기가 달라지면 Resources/art_layout.json 의 값을 맞춘다.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_ui import Canvas, INK, S, WHITE  # noqa: E402


def arc(c, box, start, end, fill, width):
    x0, y0, x1, y1 = box
    c.d.arc([x0 * S, y0 * S, x1 * S, y1 * S], start, end, fill=fill, width=int(width * S))


def eyes(c, cx, y, gap, r=4, fill=INK):
    c.circle(cx - gap, y, r, fill)
    c.circle(cx + gap, y, r, fill)


def angry_brows(c, cx, y, gap, size=9, width=4):
    c.line([(cx - gap - size, y - 5), (cx - gap + size * 0.6, y + 2)], INK, width)
    c.line([(cx + gap + size, y - 5), (cx + gap - size * 0.6, y + 2)], INK, width)


# ---------- 방어 ----------

# 등급마다 옷과 장비가 달라진다: (옷, 투구, 투구 장식, 허리띠, 창날, 방패, 망토)
GUARD_LOOKS = {
    "common": ((150, 138, 120, 255), (176, 188, 204, 255), None, (120, 104, 88, 255), (214, 222, 234, 255), None, None),
    "uncommon": ((96, 170, 92, 255), (176, 188, 204, 255), (232, 92, 84, 255), (236, 196, 92, 255), (214, 222, 234, 255), None, None),
    "rare": ((80, 140, 220, 255), (150, 190, 240, 255), (255, 255, 255, 255), (236, 196, 92, 255), (190, 226, 255, 255), (96, 160, 236, 255), None),
    "legendary": ((230, 170, 50, 255), (255, 214, 90, 255), (232, 70, 70, 255), (255, 244, 200, 255), (255, 236, 150, 255), (255, 200, 70, 255), (214, 60, 60, 255)),
}


def guard(look="uncommon"):
    tunic, metal, plume, belt, blade, shield, cape = GUARD_LOOKS[look]
    c = Canvas(150, 210)
    skin = (255, 226, 190, 255)

    # 망토
    if cape:
        c.outlined_poly([(40, 108), (106, 108), (118, 186), (28, 186)], cape)

    # 창
    c.line([(122, 196), (122, 34)], INK, 11)
    c.line([(122, 196), (122, 34)], (176, 122, 72, 255), 5)
    c.outlined_poly([(122, 6), (136, 38), (108, 38)], blade, 3)

    # 다리와 몸
    c.outlined_rrect(46, 162, 66, 198, 8, (92, 74, 60, 255))
    c.outlined_rrect(80, 162, 100, 198, 8, (92, 74, 60, 255))
    c.outlined_rrect(36, 104, 110, 172, 22, tunic)
    c.rrect(36, 138, 110, 150, 0, belt)
    c.outlined_circle(118, 126, 11, skin)

    # 방패(없으면 맨손)
    if shield:
        pts = [(8, 112), (52, 112), (52, 144), (30, 168), (8, 144)]
        c.outlined_poly(pts, shield)
        c.line([(30, 122), (30, 154)], WHITE, 5)
        c.line([(18, 136), (42, 136)], WHITE, 5)
    else:
        c.outlined_circle(30, 132, 11, skin)

    # 머리와 투구
    c.outlined_circle(73, 70, 40, skin)
    c.d.pieslice([29 * S, 22 * S, 117 * S, 110 * S], 180, 360, fill=INK)
    c.d.pieslice([33 * S, 26 * S, 113 * S, 106 * S], 180, 360, fill=metal)
    c.outlined_rrect(27, 62, 119, 72, 5, (226, 232, 240, 255) if look != "legendary" else (255, 240, 170, 255), 3)
    if plume:
        c.outlined_poly([(73, 4), (86, 30), (60, 30)], plume, 3)
    if look == "legendary":
        c.outlined_poly(star(73, 46, 11, 5), WHITE, 2)

    eyes(c, 73, 86, 15)
    c.line([(65, 100), (81, 100)], INK, 4)
    return c


def star(cx, cy, r_out, r_in):
    import math
    pts = []
    for i in range(10):
        r = r_out if i % 2 == 0 else r_in
        a = -math.pi / 2 + i * math.pi / 5
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


# 등급마다 지붕과 재료가 달라진다: (기둥, 기둥 어두운 색, 벽, 지붕, 지붕 밝은 색, 깃발)
TOWER_LOOKS = {
    "common": ((196, 142, 88, 255), (156, 106, 62, 255), (232, 206, 160, 255), (150, 132, 112, 255), (176, 158, 138, 255), None),
    "uncommon": ((196, 142, 88, 255), (156, 106, 62, 255), (232, 206, 160, 255), (92, 168, 96, 255), (128, 196, 124, 255), (255, 255, 255, 255)),
    "rare": ((176, 184, 198, 255), (128, 138, 156, 255), (222, 228, 238, 255), (72, 134, 216, 255), (116, 172, 240, 255), (255, 255, 255, 255)),
    "legendary": ((226, 214, 190, 255), (176, 160, 130, 255), (255, 246, 222, 255), (236, 176, 50, 255), (255, 216, 110, 255), (220, 60, 60, 255)),
}


def watchtower(look="uncommon"):
    wood, dark, wall, roof, roof_light, flag = TOWER_LOOKS[look]
    c = Canvas(210, 340)

    # 다리와 가새
    for x0, x1 in ((48, 36), (162, 174)):
        c.line([(x0, 170), (x1, 326)], INK, 20)
        c.line([(x0, 170), (x1, 326)], wood, 12)
    c.line([(46, 200), (166, 300)], INK, 13)
    c.line([(46, 200), (166, 300)], dark, 6)
    c.line([(164, 200), (44, 300)], INK, 13)
    c.line([(164, 200), (44, 300)], dark, 6)

    # 망대
    c.outlined_rrect(26, 150, 184, 186, 8, wood)
    c.outlined_rrect(34, 96, 176, 154, 6, wall)
    for x in (62, 92, 122, 150):
        c.line([(x, 100), (x, 152)], dark, 4)
    c.rrect(86, 108, 124, 150, 6, INK)
    c.rrect(90, 112, 120, 150, 4, (88, 66, 52, 255))

    # 지붕과 깃발
    c.outlined_poly([(105, 30), (198, 100), (12, 100)], roof, 5)
    c.poly([(105, 30), (150, 64), (60, 64)], roof_light)
    if flag:
        c.line([(105, 34), (105, 4)], INK, 7)
        c.outlined_poly([(108, 4), (146, 14), (108, 26)], flag, 3)
    if look == "legendary":
        c.outlined_poly(star(105, 72, 13, 6), WHITE, 2)
    return c


# ---------- 침략자 ----------

def sack(c, x, y):
    c.outlined_circle(x, y, 19, (214, 184, 132, 255))
    c.outlined_poly([(x - 9, y - 20), (x + 9, y - 20), (x, y - 8)], (190, 158, 108, 255), 3)
    c.circle(x, y + 2, 7, (255, 208, 70, 255))


def raccoon():
    c = Canvas(170, 190)
    fur = (156, 160, 170, 255)
    dark = (78, 80, 92, 255)

    # 줄무늬 꼬리
    c.outlined_ellipse(8, 112, 60, 160, fur)
    for x in (18, 34, 48):
        c.line([(x, 118), (x - 4, 154)], dark, 6)

    c.outlined_rrect(48, 108, 122, 176, 26, fur)
    c.ellipse(64, 124, 106, 172, (222, 224, 230, 255))
    c.outlined_rrect(54, 164, 74, 186, 8, dark)
    c.outlined_rrect(96, 164, 116, 186, 8, dark)

    # 귀와 머리
    c.outlined_poly([(44, 44), (56, 8), (76, 36)], fur)
    c.outlined_poly([(126, 44), (114, 8), (94, 36)], fur)
    c.outlined_ellipse(36, 26, 134, 114, fur)
    c.ellipse(42, 54, 128, 90, dark)
    c.ellipse(60, 80, 110, 112, (236, 238, 242, 255))
    c.circle(68, 72, 9, WHITE)
    c.circle(102, 72, 9, WHITE)
    eyes(c, 85, 73, 17, 5)
    c.circle(85, 92, 6, INK)
    angry_brows(c, 85, 58, 17)

    sack(c, 140, 142)
    return c


def wolf():
    c = Canvas(170, 190)
    fur = (112, 118, 140, 255)
    light = (206, 210, 224, 255)

    c.outlined_poly([(14, 150), (48, 116), (58, 160)], fur)
    c.outlined_rrect(48, 106, 122, 176, 24, fur)
    c.ellipse(66, 122, 104, 170, light)
    c.outlined_rrect(54, 164, 74, 186, 8, fur)
    c.outlined_rrect(96, 164, 116, 186, 8, fur)

    c.outlined_poly([(40, 52), (46, 2), (80, 34)], fur)
    c.outlined_poly([(130, 52), (124, 2), (90, 34)], fur)
    c.outlined_ellipse(34, 26, 136, 112, fur)
    c.outlined_poly([(58, 76), (112, 76), (85, 118)], light, 3)
    c.circle(85, 86, 7, INK)
    c.circle(66, 64, 8, (255, 226, 110, 255))
    c.circle(104, 64, 8, (255, 226, 110, 255))
    eyes(c, 85, 65, 19, 4)
    angry_brows(c, 85, 50, 19, 10)
    c.poly([(74, 100), (79, 110), (84, 100)], WHITE)
    c.poly([(86, 100), (91, 110), (96, 100)], WHITE)
    return c


def boar():
    c = Canvas(200, 180)
    fur = (150, 104, 72, 255)
    dark = (112, 74, 50, 255)

    for x in (40, 70, 118, 148):
        c.outlined_rrect(x, 140, x + 20, 174, 7, dark)
    c.outlined_ellipse(18, 44, 176, 156, fur)
    c.d.pieslice([40 * S, 30 * S, 150 * S, 90 * S], 180, 360, fill=dark)

    # 얼굴
    c.outlined_poly([(120, 62), (138, 34), (150, 66)], dark)
    c.outlined_ellipse(108, 66, 192, 146, (170, 122, 86, 255))
    c.outlined_ellipse(150, 100, 194, 138, (226, 160, 150, 255), 3)
    c.circle(164, 118, 5, INK)
    c.circle(181, 118, 5, INK)
    c.circle(138, 92, 6, INK)
    c.line([(126, 80), (146, 88)], INK, 5)
    c.outlined_poly([(146, 136), (152, 160), (160, 138)], WHITE, 3)
    c.outlined_poly([(176, 136), (184, 158), (190, 136)], WHITE, 3)
    return c


# ---------- 아이콘 ----------

def icon_quiz():
    c = Canvas(96, 96)
    c.outlined_rrect(12, 10, 84, 86, 12, (255, 244, 214, 255))
    c.rrect(12, 10, 84, 26, 10, (255, 196, 92, 255))
    ink = (224, 110, 60, 255)
    arc(c, (32, 30, 64, 60), 180, 80, ink, 9)
    c.line([(54, 56), (48, 62), (48, 66)], ink, 9)
    c.circle(48, 77, 5.5, ink)
    return c


def icon_shield():
    c = Canvas(96, 96)
    outer = [(48, 8), (84, 20), (84, 48), (70, 74), (48, 90), (26, 74), (12, 48), (12, 20)]
    c.outlined_poly(outer, (104, 168, 232, 255))
    c.poly([(48, 8), (84, 20), (84, 48), (70, 74), (48, 90)], (72, 134, 206, 255))
    c.line([(48, 30), (48, 66)], WHITE, 8)
    c.line([(30, 46), (66, 46)], WHITE, 8)
    return c


SPRITES = {
    # 경비병과 경비탑은 직접 그린 그림으로 바꿨으므로 여기서 더는 만들지 않는다.
    # (예전 임시 그림이 필요하면 guard(look), watchtower(look)를 직접 부른다.)
    "invader_raccoon": raccoon,
    "invader_wolf": wolf,
    "invader_boar": boar,
    "ui_icon_quiz": icon_quiz,
    "ui_icon_shield": icon_shield,
}


def main():
    force = "--force" in sys.argv
    made = 0
    for name, draw in SPRITES.items():
        if draw().save(name, force):
            made += 1
    print("made %d of %d" % (made, len(SPRITES)))


if __name__ == "__main__":
    main()
