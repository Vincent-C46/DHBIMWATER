"""Generate every discharge-basin diagram as SVG and PNG (docs/320).

Run: python tools/generate_discharge_plan_diagrams.py
Writes src/DHBIMWATER.UI/Resources/PumpStationImages/토출조_*.{svg,png} and the label table
src/DHBIMWATER.UI/ViewModels/Modeling/DischargeBasinDiagramLabels.g.cs.

The window shows every sheet at half scale (1800 units = 900 px), so FONT_SIZE 26 reads as the
13 px input labels. PNG backgrounds are transparent: the WPF highlight sits behind the ink.
"""
import re
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "src/DHBIMWATER.UI/Resources/PumpStationImages"
TABLE = ROOT / "src/DHBIMWATER.UI/ViewModels/Modeling/DischargeBasinDiagramLabels.g.cs"
FONT = "C:/Windows/Fonts/arial.ttf"
W = 1800
SCALE = 2      # PNG pixels per unit
SUPER = 2      # extra supersampling before downscale
INK = "#000000"
THIN, MID, HATCH = 3.0, 5.0, 2.0
DOT = 5
FONT_SIZE = 26
CAP = 0.72 * FONT_SIZE
PAD_ALONG, PAD_ACROSS = 10, 8    # highlight margin around a label
MEASURE = ImageFont.truetype(FONT, FONT_SIZE * 8)
ALIASES = {"D": "RefD", "θ": "Theta"}
PIPE_DASH = (16, 8)


def text_length(s):
    return MEASURE.getlength(s) / 8


class Sheet:
    def __init__(self, height, mirror=False, top=0):
        self.height, self.mirror, self.top = height, mirror, top
        self.items = []
        self.boxes = {}

    def x(self, v):
        return W - v if self.mirror else v

    def y(self, v):
        return v + self.top

    def line(self, x1, y1, x2, y2, w=THIN, dash=None):
        self.items.append(("line", self.x(x1), self.y(y1), self.x(x2), self.y(y2), w, dash))

    def poly(self, pts, w=MID):
        self.items.append(("poly", [(self.x(a), self.y(b)) for a, b in pts], w))

    def path(self, pts, w=THIN):
        for (a, b), (c, d) in zip(pts, pts[1:]):
            self.line(a, b, c, d, w)

    def dot(self, x, y):
        self.items.append(("dot", self.x(x), self.y(y)))

    def text(self, x, y, s, vertical=False, tokens=None, knockout=False):
        """tokens: parameter names inside s; defaults to the whole label."""
        cx, cy = self.x(x), self.y(y)
        self.items.append(("text", cx, cy, s, vertical, knockout))
        for token in tokens or [s]:
            start = s.index(token)
            offset = text_length(s[:start]) + text_length(token) / 2 - text_length(s) / 2
            half = text_length(token) / 2 + PAD_ALONG
            across = CAP / 2 + PAD_ACROSS
            if vertical:    # reads upward, so the string start is at the bottom
                box = (cx - across, cy - offset - half, cx + across, cy - offset + half)
            else:
                box = (cx + offset - half, cy - across, cx + offset + half, cy + across)
            self.boxes.setdefault(ALIASES.get(token, token), []).append(box)


def hdim(s, y, xs):
    s.line(min(xs) - 14, y, max(xs) + 14, y)
    for v in xs:
        s.dot(v, y)


def vdim(s, x, ys, ext_from):
    s.line(x, min(ys) - 2, x, max(ys) + 2)
    for v in ys:
        s.line(ext_from, v, x, v)
        s.dot(x, v)


def pump(s, c):
    s.line(c - 52, 181, c - 52, 340)
    s.line(c + 52, 181, c + 52, 340)
    s.line(c - 86, 181, c - 12, 181)
    s.line(c + 12, 181, c + 86, 181)
    s.line(c - 20, 170, c + 22, 192)                    # pipe break
    s.line(c, 148, c, 300, dash=PIPE_DASH)              # center line
    s.line(c - 52, 285, c + 52, 320)                    # wall sleeve cross
    s.line(c - 52, 320, c + 52, 285)
    s.line(c - 86, 302, c - 52, 302, MID)
    s.line(c + 52, 302, c + 86, 302, MID)
    s.poly([(c - 62, 342), (c + 62, 342), (c + 62, 348), (c - 62, 348)], THIN)
    s.poly([(c - 40, 350), (c + 40, 350), (c + 40, 368), (c - 40, 368)], THIN)
    s.poly([(c - 40, 368), (c + 40, 368), (c + 57, 378), (c + 40, 394), (c - 40, 394), (c - 57, 378)], THIN)
    s.line(c - 20, 356, c - 20, 400)
    s.line(c + 20, 356, c + 20, 400)


def column(s, c, y=594):
    top, bottom = y - 17, y + 17
    s.poly([(c - 17, top), (c + 17, top), (c + 17, bottom), (c - 17, bottom)], THIN)
    for k in range(-34, 34, 10):
        x1, y1, x2, y2 = c - 17, y + k + 17, c + 17, y + k - 17
        if y1 > bottom:
            x1 += y1 - bottom
            y1 = bottom
        if y2 < top:
            x2 -= top - y2
            y2 = top
        if x1 < x2:
            s.line(x1, y1, x2, y2, HATCH)


def top_dimensions(s):
    xs = [162, 203, 237, 341, 445, 687, 791, 1033, 1137, 1380, 1484, 1588, 1622, 1664]
    hdim(s, 98, xs)
    for v in (162, 203, 1622, 1664):
        s.line(v, 98, v, 168)
    s.line(237, 29, 237, 168)
    s.line(1588, 29, 1588, 168)
    s.line(237, 29, 1588, 29)
    s.dot(237, 29)
    s.dot(1588, 29)
    s.text(912, 11, "TLT")
    for x, label in ((182, "TB3"), (290, "TL1"), (393, "D"), (566, "TL2"), (739, "D"), (912, "TL2"),
                     (1085, "D"), (1258, "TL2"), (1431, "D"), (1536, "TL1"), (1643, "TB3")):
        s.text(x, 78, label)
    for x in (220, 1605):
        s.text(x, 190, "TT1")    # below the extension lines: wider than its span


def pipe_outlet(s):
    for top, bottom in ((667, 736), (806, 875)):
        s.poly([(1588, top), (1622, top), (1622, bottom), (1588, bottom)], THIN)
        s.line(1588, top, 1622, bottom)
        s.line(1622, top, 1588, bottom)
    vdim(s, 1518, [667, 736, 806, 875], 1588)
    s.text(1498, 701, "OD", True)
    s.text(1498, 771, "O1", True)
    s.text(1498, 826, "OD", True)


def culvert_outlet(s):
    ys = [556, 577, 715, 736, 875, 896]
    vdim(s, 1518, ys, 1588)
    for y in ys:
        s.line(1622, y, 1664, y)
    s.line(1588, 577, 1588, 875)
    for top, bottom in ((577, 715), (736, 875)):
        s.line(1588, top, 1622, bottom)
        s.line(1622, top, 1588, bottom)
    s.text(1498, 562, "OT", True)
    s.text(1498, 646, "OB", True)
    s.text(1498, 725, "OT", True)
    s.text(1498, 805, "OB", True)
    s.text(1680, 885, "OT", True)    # outside the slab: span too short for the label


def side_plan(mirror, conduit):
    s = Sheet(1000, mirror, top=16)
    s.poly([(162, 244), (1664, 244), (1664, 969), (162, 645)])    # base slab edge
    s.poly([(203, 285), (1622, 285), (1622, 917), (203, 613)])    # wall outer face
    s.poly([(237, 320), (1588, 320), (1588, 875), (237, 583)])    # wall inner face
    for c in (393, 739, 1085, 1431):
        pump(s, c)
    for c in (566, 912, 1258):
        column(s, c)
    s.line(237, 583, 330, 583)
    s.text(352, 598, "θ")
    (pipe_outlet if conduit == "PIPE" else culvert_outlet)(s)
    top_dimensions(s)

    vdim(s, 40, [244, 285, 320, 583, 611, 645], 103)
    for y, label in ((262, "TB3"), (451, "TB1"), (630, "TB4")):
        s.text(21, y, label, True)
    s.text(122, 303, "TT1", True)    # past the extension lines: TT1 and TT4 spans are short
    s.text(122, 597, "TT4", True)

    vdim(s, 1760, [244, 285, 320, 875, 917, 969], 1697)
    for y, label in ((262, "TB3"), (597, "TB2"), (892, "TT5"), (946, "TB4")):
        s.text(1779, y, label, True)
    s.text(1681, 303, "TT1", True)
    return s


def center_plan():
    s = Sheet(1090, top=16)
    s.poly([(162, 244), (1664, 244), (1664, 632), (1144, 825), (680, 825), (162, 632)])
    s.poly([(203, 285), (1622, 285), (1622, 603), (1132, 784), (692, 784), (203, 603)])
    s.poly([(237, 320), (1588, 320), (1588, 579), (1128, 749), (696, 749), (237, 579)])
    for c in (393, 739, 1085, 1431):
        pump(s, c)
    for c in (566, 912, 1258):
        column(s, c, 590)
    s.line(237, 579, 330, 579)
    s.line(1588, 579, 1495, 579)
    s.text(352, 592, "θ")
    s.text(1473, 592, "θ")

    for left, right in ((765, 902), (922, 1059)):    # outlets through the bottom wall
        s.line(left, 749, right, 784)
        s.line(left, 784, right, 749)
    for x in (696, 1128):
        s.line(x, 749, x, 825)
    for x in (744, 765, 902, 922, 1059, 1079):
        s.line(x, 749, x, 825)
    hdim(s, 664, [744, 765, 902, 922, 1059, 1079])
    for x in (744, 765, 902, 922, 1059, 1079):
        s.line(x, 664, x, 735)
    for x, label in ((754, "OT"), (833, "OB"), (912, "OT"), (990, "OB"), (1069, "OT")):
        s.text(x, 644, label)

    row = [162, 203, 237, 696, 765, 902, 922, 1059, 1128, 1588, 1622, 1664]
    hdim(s, 904, row)
    for x in row:
        s.line(x, 838, x, 904)
    for x, label in ((182, "TB3"), (466, "TL3"), (730, "TL4"), (833, "OB"), (912, "OT"), (990, "OB"),
                     (1093, "TL4"), (1358, "TL3"), (1643, "TB3")):
        s.text(x, 884, label, knockout=label in ("TB3", "OT"))
    for x in (220, 1605):
        s.text(x, 925, "TT1", knockout=True)
    hdim(s, 972, [162, 203, 690, 765])
    s.line(162, 904, 162, 1040)
    s.line(203, 904, 203, 972)
    s.line(765, 904, 765, 1040)
    for x, label in ((182, "TB3"), (446, "TL5"), (727, "TL6")):
        s.text(x, 952, label, knockout=label == "TB3")
    hdim(s, 1040, [162, 683, 765])
    for x, label in ((420, "TL7"), (724, "TL8")):
        s.text(x, 1020, label)
    s.line(690, 904, 690, 972)
    s.line(683, 972, 683, 1040)
    top_dimensions(s)

    vdim(s, 40, [244, 285, 320, 579, 603, 632], 103)
    for y, label in ((262, "TB3"), (451, "TB1"), (624, "TB4")):
        s.text(21, y, label, True)
    s.text(122, 303, "TT1", True)
    s.text(122, 591, "TT4", True)

    vdim(s, 1760, [244, 285, 320, 749, 784, 825], 1697)
    for y, label in ((262, "TB3"), (535, "TB2"), (806, "TB3")):
        s.text(1779, y, label, True)
    s.text(1681, 303, "TT1", True)
    s.text(1681, 767, "TT1", True)
    return s


def opening_plan():
    s = Sheet(560, top=16)
    s.poly([(149, 147), (1786, 147), (1786, 527), (149, 527)], THIN)
    for a, b, c, d in ((471, 188, 1746, 188), (189, 317, 189, 487), (189, 487, 1746, 487), (1746, 188, 1746, 487)):
        s.line(a, b, c, d, dash=(44, 26) if a != c else (44, 26))
    openings = ((189, 471, "TOL2"), (673, 875, "TOL1"), (1077, 1278, "TOL1"), (1480, 1682, "TOL1"))
    for left, right, length in openings:
        s.poly([(left, 188), (right, 188), (right, 317), (left, 317)], THIN)
        s.line(left, 188, right, 317)
        s.line(left, 317, right, 188)
        hdim(s, 34, [left, right])
        s.line(left, 34, left, 115)
        s.line(right, 34, right, 115)
        c = (left + right) / 2
        s.text(c, 12, length)
        s.text(c, 234, "OPEN", knockout=True, tokens=[])
        s.text(c, 271, f"TOB1x{length}", tokens=["TOB1", length], knockout=True)
    vdim(s, 56, [188, 317], 118)
    s.text(36, 252, "TOB1", True)
    return s


def level(s, x, y, label, tokens):
    """Level triangle standing on y with the label to its right."""
    s.poly([(x - 20, y - 36), (x + 20, y - 36), (x, y)], THIN)
    s.text(x + 34 + text_length(label) / 2, y - 22, label, tokens=tokens)


def water(s, x1, x2, y):
    s.line(x1, y, x2, y)
    c = x1 + 86
    s.poly([(c - 16, y - 26), (c + 16, y - 26), (c, y)], THIN)
    for k, half in ((12, 70), (24, 44), (36, 20)):
        s.line(c - half, y + k, c + half, y + k)
    s.text(c + 40 + text_length('H.W.L.TA') / 2, y - 22, 'H.W.L.TA', tokens=["TA"])


def soil(s, x1, x2, y):
    for x in range(x1, x2, 24):
        s.line(x, y + 22, x + 22, y, HATCH)


def leader(s, tip, knee, end, upper, lower, tokens):
    s.path([tip, knee, end])
    s.poly([tip, (tip[0] + 18, tip[1] + 4), (tip[0] + 4, tip[1] + 18)], THIN)
    c = (knee[0] + end[0]) / 2
    s.text(c, knee[1] - 22, upper)
    s.text(c, knee[1] + 24, lower, tokens=tokens)


def hatch_lid(s, left, right, top, bottom):
    s.poly([(left - 10, top - 8), (right + 10, top - 8), (right + 10, top), (left - 10, top)], THIN)
    s.poly([(left, top), (right, top), (right, bottom), (left, bottom)], THIN)
    s.line(left, top, right, bottom)
    s.line(left, bottom, right, top)


def inlet(s, wall_out, wall_in, stub, top, bottom):
    """Pipe through the wall, flange, flap gate and broken stub."""
    s.line(stub, top, wall_out, top)
    s.line(stub, bottom, wall_out, bottom)
    s.line(stub, top - 40, stub, bottom - 72)
    s.path([(stub, bottom - 72), (stub + 22, bottom - 88), (stub - 22, bottom - 44), (stub, bottom - 58)])
    s.line(stub, bottom - 58, stub, bottom + 40)
    s.poly([(wall_out, top), (wall_in, top), (wall_in, bottom), (wall_out, bottom)], THIN)
    s.line(wall_out, top, wall_in, bottom)
    s.line(wall_out, bottom, wall_in, top)
    mid = (wall_out + wall_in) / 2
    s.line(mid, top - 44, mid, top)
    s.line(mid, bottom, mid, bottom + 44)
    f = wall_in + 44
    s.line(wall_in, top, f + 76, top)
    s.line(wall_in, bottom, f + 110, bottom)
    s.poly([(f, top - 16), (f + 14, top - 16), (f + 14, bottom + 16), (f, bottom + 16)], THIN)
    s.poly([(f + 72, top - 18), (f + 94, top - 18), (f + 132, bottom + 12), (f + 114, bottom + 12)], THIN)


def profile_flat():
    s = Sheet(1030, top=16)
    hdim(s, 42, [396, 466, 999])
    for x in (396, 466, 999):
        s.line(x, 42, x, 124)
    s.text(431, 20, "TT1")
    s.text(732, 20, "TB1")

    s.path([(396, 880), (396, 170), (1531, 170), (1531, 930)], MID)
    s.path([(396, 760), (396, 930)], MID)
    s.path([(466, 205), (466, 930)], MID)
    s.path([(466, 205), (930, 205), (930, 930)], MID)
    s.path([(999, 930), (999, 205), (1459, 205), (1459, 930)], MID)
    s.line(466, 235, 1459, 235)
    s.line(466, 930, 1459, 930, MID)
    s.path([(396, 930), (312, 930), (312, 980), (1617, 980), (1617, 930), (1531, 930)], MID)
    s.poly([(298, 980), (1631, 980), (1631, 992), (298, 992)], THIN)
    hatch_lid(s, 466, 690, 170, 205)
    inlet(s, 396, 466, 256, 760, 880)

    vdim(s, 60, [170, 205, 295, 760, 880, 930, 980], 200)
    for y, label in ((186, "TT3"), (250, "TH1"), (527, "TH2"), (820, "D"), (905, "TH3"), (962, "TT2")):
        s.text(38, y, label, True)

    level(s, 800, 170, 'EL.TB', ["TB"])
    water(s, 520, 900, 295)
    level(s, 702, 880, 'BOP.EL.TE', ["TE"])
    level(s, 702, 930, 'EL.TF', ["TF"])
    s.line(1531, 560, 1796, 560)
    level(s, 1582, 560, 'EL.TC', ["TC"])
    soil(s, 1560, 1770, 560)
    leader(s, (1110, 238), (1210, 310), (1440, 310), "GIRDER", "TGB1xTGH1", ["TGB1", "TGH1"])
    leader(s, (999, 600), (1100, 680), (1320, 680), "COLUMN", "TCB1xTCH1", ["TCB1", "TCH1"])
    return s


def profile_step():
    s = Sheet(1030, top=16)
    hdim(s, 42, [311, 365, 791, 844])
    for x in (311, 365, 791, 844):
        s.line(x, 42, x, 124)
    s.text(338, 20, "TT1")
    s.text(578, 20, "TB6")
    s.text(818, 20, "TT1")

    s.path([(311, 760), (311, 170), (844, 170), (844, 550)], MID)
    s.path([(311, 880), (311, 930)], MID)
    s.path([(365, 205), (365, 930)], MID)
    s.path([(365, 205), (791, 205), (791, 930)], MID)
    s.path([(844, 550), (1504, 550), (1504, 680)], MID)
    s.path([(791, 585), (1450, 585), (1450, 930)], MID)
    s.path([(844, 612), (844, 930)], MID)
    s.line(844, 612, 1450, 612)
    s.line(844, 530, 1504, 530)
    s.line(1504, 680, 1664, 680)
    s.line(1450, 680, 1504, 680)
    s.line(1450, 680, 1504, 930)
    s.line(1504, 680, 1450, 930)
    s.line(1504, 680, 1504, 930)
    s.line(1664, 680, 1664, 760, dash=PIPE_DASH)
    s.path([(1664, 760), (1686, 744), (1642, 790), (1664, 776)])
    s.line(1664, 776, 1664, 930, dash=PIPE_DASH)
    s.line(365, 930, 1664, 930, MID)
    s.path([(311, 930), (237, 930), (237, 980), (1578, 980), (1578, 930)], MID)
    s.poly([(226, 980), (1590, 980), (1590, 992), (226, 992)], THIN)
    hatch_lid(s, 365, 535, 170, 205)
    inlet(s, 311, 365, 205, 760, 880)

    vdim(s, 60, [170, 205, 300, 760, 880, 930, 980], 150)
    for y, label in ((186, "TT3"), (252, "TH1"), (530, "TH2"), (820, "D"), (905, "TH3"), (962, "TT2")):
        s.text(38, y, label, True)
    vdim(s, 1760, [530, 550, 585, 930, 980], 1690)
    s.text(1779, 496, "TH5", True)
    s.text(1779, 566, "TT3", True, knockout=True)
    s.text(1779, 757, "TH4", True)
    s.text(1779, 956, "TT2", True)

    level(s, 618, 170, 'EL.TB', ["TB"])
    water(s, 405, 770, 300)
    level(s, 562, 880, 'BOP.EL.TE', ["TE"])
    level(s, 577, 930, 'EL.TF', ["TF"])
    level(s, 1110, 530, 'EL.TC', ["TC"])
    soil(s, 1200, 1440, 530)
    leader(s, (1130, 615), (1205, 690), (1420, 690), "GIRDER", "TGB1xTGH1", ["TGB1", "TGH1"])
    leader(s, (844, 730), (920, 800), (1130, 800), "COLUMN", "TCB1xTCH1", ["TCB1", "TCH1"])
    return s


def knock_box(item):
    _, x, y, t, vertical, _ = item
    along, across = text_length(t) / 2 + 5, CAP / 2 + 5
    return (x - across, y - along, x + across, y + along) if vertical else (x - along, y - across, x + along, y + across)


def svg(s):
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {s.height}" width="{W}" height="{s.height}">',
           f'<rect width="{W}" height="{s.height}" fill="#FFFFFF"/>',
           f'<g fill="none" stroke="{INK}" stroke-linecap="round" stroke-linejoin="round">']
    for item in s.items:
        if item[0] == "line":
            _, x1, y1, x2, y2, w, dash = item
            d = f' stroke-dasharray="{dash[0]} {dash[1]}"' if dash else ""
            out.append(f'<line x1="{x1:g}" y1="{y1:g}" x2="{x2:g}" y2="{y2:g}" stroke-width="{w:g}"{d}/>')
        elif item[0] == "poly":
            pts = " ".join(f"{a:g},{b:g}" for a, b in item[1])
            out.append(f'<polygon points="{pts}" stroke-width="{item[2]:g}"/>')
        elif item[0] == "dot":
            out.append(f'<circle cx="{item[1]:g}" cy="{item[2]:g}" r="{DOT}" fill="{INK}" stroke="none"/>')
    out.append("</g>")
    out.append(f'<g fill="{INK}" font-family="Arial" font-size="{FONT_SIZE}" text-anchor="middle" dominant-baseline="central">')
    for item in s.items:
        if item[0] == "text":
            _, x, y, t, vertical, knockout = item
            if knockout:
                a, b, c, d = knock_box(item)
                out.append(f'<rect x="{a:g}" y="{b:g}" width="{c - a:g}" height="{d - b:g}" fill="#FFFFFF"/>')
            rot = f' transform="rotate(-90 {x:g} {y:g})"' if vertical else ""
            safe = t.replace('"', "&quot;")
            out.append(f'<text x="{x:g}" y="{y:g}"{rot}>{safe}</text>')
    out += ["</g>", "</svg>", ""]
    return "\n".join(out)


def png(s, path):
    k = SCALE * SUPER
    h = s.height
    img = Image.new("L", (W * k, h * k), 255)
    draw = ImageDraw.Draw(img)
    font = ImageFont.truetype(FONT, FONT_SIZE * k)
    for item in s.items:
        if item[0] == "line":
            _, x1, y1, x2, y2, w, dash = item
            if dash:
                on, off = dash
                length = ((x2 - x1) ** 2 + (y2 - y1) ** 2) ** 0.5
                ux, uy = (x2 - x1) / length, (y2 - y1) / length
                t = 0
                while t < length:
                    e = min(t + on, length)
                    draw.line([(x1 + ux * t) * k, (y1 + uy * t) * k, (x1 + ux * e) * k, (y1 + uy * e) * k],
                              fill=0, width=round(w * k))
                    t += on + off
            else:
                draw.line([x1 * k, y1 * k, x2 * k, y2 * k], fill=0, width=round(w * k))
                r = w * k / 2    # round caps
                for px, py in ((x1, y1), (x2, y2)):
                    draw.ellipse([px * k - r, py * k - r, px * k + r, py * k + r], fill=0)
        elif item[0] == "poly":
            pts = [(a * k, b * k) for a, b in item[1]]
            draw.line(pts + [pts[0], pts[1]], fill=0, width=round(item[2] * k), joint="curve")
        elif item[0] == "dot":
            r = DOT * k
            draw.ellipse([item[1] * k - r, item[2] * k - r, item[1] * k + r, item[2] * k + r], fill=0)
    for item in s.items:
        if item[0] == "text":
            _, x, y, t, vertical, knockout = item
            if knockout:
                draw.rectangle([v * k for v in knock_box(item)], fill=255)
            box = draw.textbbox((0, 0), t, font=font)
            tile = Image.new("L", (box[2] - box[0] + 8, box[3] - box[1] + 8), 0)
            ImageDraw.Draw(tile).text((4 - box[0], 4 - box[1]), t, font=font, fill=255)
            if vertical:
                tile = tile.rotate(90, expand=True)
            px, py = round(x * k - tile.width / 2), round(y * k - tile.height / 2)
            img.paste(0, (px, py, px + tile.width, py + tile.height), tile)
    small = img.resize((W * SCALE, h * SCALE), Image.LANCZOS)
    out = Image.new("LA", small.size, 0)
    out.putalpha(small.point(lambda v: 255 - v))    # black ink on transparency
    out.save(path, optimize=True)


def table(sheets):
    rows = []
    for name, sheet in sheets.items():
        rows.append(f'            ["{name}"] = new()\n            {{')
        for param, boxes in sorted(sheet.boxes.items()):
            data = " ".join(f"M{a:.0f},{b:.0f} H{c:.0f} V{d:.0f} H{a:.0f} Z" for a, b, c, d in boxes)
            rows.append(f'                ["{param}"] = "{data}",')
        rows.append("            },")
    body = "\n".join(rows)
    return f"""// tools/generate_discharge_plan_diagrams.py 자동 생성. 직접 수정 금지.
using System.Collections.Generic;

namespace DHBIMWATER.UI.ViewModels.Modeling
{{
    /// <summary>도해별 매개변수 글자 영역. 도해 좌표 경로(docs/320).</summary>
    internal static class DischargeBasinDiagramLabels
    {{
        private static readonly Dictionary<string, Dictionary<string, string>> Sheets = new()
        {{
{body}
        }};

        /// <summary>도해에 해당 글자가 없으면 null.</summary>
        public static string? Find(string sheet, string parameter)
            => Sheets.TryGetValue(sheet, out var labels) && labels.TryGetValue(parameter, out var path) ? path : null;
    }}
}}
"""


def main():
    sheets = {}
    for side, mirror in (("좌측", True), ("우측", False)):
        for name, conduit in (("암거", "CULVERT"), ("PIPE", "PIPE")):
            sheets[f"평면-{side}-{name}"] = side_plan(mirror, conduit)
    sheets["평면-중앙부"] = center_plan()
    sheets["개구부-평면"] = opening_plan()
    sheets["종단-단차미적용"] = profile_flat()
    sheets["종단-단차적용"] = profile_step()
    for name, sheet in sheets.items():
        base = OUT / f"토출조_{name}"
        base.with_suffix(".svg").write_text(svg(sheet), encoding="utf-8")
        png(sheet, base.with_suffix(".png"))
    TABLE.write_text(table(sheets), encoding="utf-8")
    assert re.search("TOB1", TABLE.read_text(encoding="utf-8"))


if __name__ == "__main__":
    main()
