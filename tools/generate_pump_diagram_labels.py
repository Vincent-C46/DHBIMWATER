"""Measure the parameter labels on the pumping-station diagram PNGs and write the highlight table.

Run: python tools/generate_pump_diagram_labels.py [preview_dir]
Writes src/DHBIMWATER.UI/ViewModels/Modeling/PumpingStationDiagramLabels.g.cs.

The pumping-station diagrams are CAD exports, not generated drawings, so the PNGs stay as they are.
This script finds the black label glyphs, groups them into words, and maps each word to a
parameter through SPEC below. Labels that touch a dimension line are not found automatically;
SPEC gives an approximate rectangle for those and the script trims it to the ink inside.
Only parameters whose input box sits on the same tab are listed (D, T6 on the profile and
T4 on the plan live on other tabs, so they are left out).
Pass a folder to also write preview PNGs with the boxes drawn on top.
"""
import sys
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
IMAGES = ROOT / "src/DHBIMWATER.UI/Resources/PumpStationImages"
TABLE = ROOT / "src/DHBIMWATER.UI/ViewModels/Modeling/PumpingStationDiagramLabels.g.cs"
PAD = 5

# Sheet name -> (PNG file, entries). An entry is either
#   ("w", word index, [names in reading order])  - an automatically found word, split at its gaps
#   ("r", (x0, y0, x1, y1), [names], "h"|"v")    - an approximate rectangle trimmed to its ink
# A name of None skips that piece. Vertical labels read bottom to top.
SPEC = {
    "종단-Type1": ("TYPE-1_종단제원.png", [
        ("w", 0, ["L1"]), ("w", 1, ["L2"]), ("w", 2, ["B1"]), ("w", 3, ["B2"]), ("w", 5, ["B3"]),
        ("w", 7, ["L3"]), ("w", 8, ["GB1"]), ("w", 9, ["OB1"]), ("w", 13, ["GH1"]), ("w", 14, ["OH1"]),
        ("w", 15, ["L4"]), ("w", 16, ["B4"]), ("w", 17, ["B5"]), ("w", 18, ["B6", "T3"]), ("w", 19, ["B7"]),
        ("w", 20, ["T4"]), ("w", 22, ["H7"]), ("w", 23, ["H3"]), ("w", 24, ["T3"]), ("w", 25, ["H2"]),
        ("w", 26, ["H5"]), ("w", 27, ["HS"]), ("w", 28, ["H4"]), ("w", 29, ["NS"]), ("w", 30, ["T2"]),
        ("r", (570, 905, 612, 958), ["H1"], "v"), ("r", (2180, 600, 2222, 655), ["H6"], "v"),
        ("r", (2328, 445, 2370, 492), ["T1"], "v"), ("r", (880, 1072, 918, 1100), ["θ"], "h"),
    ]),
    "종단-Type2": ("TYPE-2_종단제원.png", [
        ("w", 0, ["L1"]), ("w", 1, ["L2"]), ("w", 2, ["B1"]), ("w", 3, ["B2"]), ("w", 6, ["L3"]),
        ("w", 7, ["B3"]), ("w", 8, ["GB1"]), ("w", 9, ["OB1"]), ("w", 12, ["GH1"]), ("w", 13, ["OH1"]),
        ("w", 14, ["L4"]), ("w", 15, ["B5"]), ("w", 16, ["B6", "T3"]), ("w", 17, ["T4"]), ("w", 18, ["HB1"]),
        ("w", 20, ["B7"]), ("w", 21, ["HH1"]), ("w", 22, ["T3"]), ("w", 23, ["H7"]), ("w", 24, ["H3"]),
        ("w", 26, ["H2"]), ("w", 27, ["H5"]), ("w", 28, ["HS"]), ("w", 29, ["H4"]), ("w", 30, ["NS"]),
        ("w", 31, ["T2"]),
        ("r", (570, 905, 612, 958), ["H1"], "v"), ("r", (2180, 600, 2222, 655), ["H6"], "v"),
        ("r", (2328, 445, 2370, 492), ["T1"], "v"), ("r", (880, 1072, 918, 1100), ["θ"], "h"),
    ]),
    "종단-Type3": ("TYPE-3_종단제원.png", [
        ("w", 0, ["L1"]), ("w", 1, ["L2"]), ("w", 2, ["B1"]), ("w", 3, ["B2"]), ("w", 6, ["L3"]),
        ("w", 7, ["B3"]), ("w", 8, ["GB1"]), ("w", 10, ["OB1"]), ("w", 11, ["GH1"]), ("w", 13, ["L4"]),
        ("w", 14, ["OH1"]), ("w", 15, ["B5"]), ("w", 16, ["B6", "T3"]), ("w", 17, ["T4"]), ("w", 19, ["B7"]),
        ("w", 21, ["T4"]), ("w", 22, ["H7"]), ("w", 23, ["H3"]), ("w", 24, ["T3"]), ("w", 25, ["H2"]),
        ("w", 26, ["H5"]), ("w", 27, ["H1"]), ("w", 28, ["H4"]), ("w", 29, ["NS"]), ("w", 30, ["T2"]),
        ("r", (735, 1000, 778, 1055), ["HS"], "v"), ("r", (2170, 600, 2210, 655), ["H6"], "v"),
        ("r", (2258, 445, 2300, 492), ["T1"], "v"), ("r", (1048, 1066, 1086, 1095), ["θ"], "h"),
    ]),
    "평면-좌안부": ("TYPE-1_평면제원-좌안진입.png", [
        ("w", 0, ["B10", None]), ("w", 1, ["L5"]), ("w", 2, [None, "B10"]), ("w", 3, [None, "B10"]),
        ("w", 4, ["B9"]), ("w", 5, ["B10"]), ("w", 7, ["B8"]), ("w", 8, ["T5"]), ("w", 9, ["B8"]),
        ("w", 10, ["T5"]), ("w", 11, ["B8"]), ("w", 12, ["B10", None]),
        ("r", (2340, 930, 2395, 992), ["T5"], "v"),
    ]),
    "평면-우안부": ("TYPE-1_평면제원-우안진입.png", [
        ("w", 0, ["B10", None]), ("w", 1, ["L5"]), ("w", 3, ["B10"]),
        ("r", (2080, 1905, 2165, 1955), ["B10"], "h"),   # "T4 B10" 자동 분할이 틀려 사각형으로
        ("w", 5, ["B8"]), ("w", 6, ["T5"]), ("w", 7, ["B8"]), ("w", 8, ["T5"]), ("w", 9, ["B8"]),
        ("w", 10, ["B10", None]), ("w", 11, ["B9"]), ("w", 12, ["B10", None]),
        ("r", (2340, 1140, 2395, 1205), ["T5"], "v"),
    ]),
    "평면-측면부": ("TYPE-1_평면제원-측면진입.png", [
        ("w", 0, ["B10"]), ("w", 2, ["B8"]), ("w", 3, ["T5"]), ("w", 4, ["B8"]), ("w", 5, ["T5"]),
        ("w", 6, ["B8"]), ("w", 8, ["B10"]),
    ]),
}


def load_dark(path):
    a = np.array(Image.open(path).convert("RGBA")).astype(int)
    return (a[:, :, 3] > 128) & (a[:, :, 0] < 100) & (a[:, :, 1] < 100) & (a[:, :, 2] < 100)


def components(dark):
    h, w = dark.shape
    seen = np.zeros_like(dark, dtype=bool)
    out = []
    for y0, x0 in zip(*np.nonzero(dark)):
        if seen[y0, x0]:
            continue
        q = deque([(y0, x0)])
        seen[y0, x0] = True
        x_lo = x_hi = x0
        y_lo = y_hi = y0
        n = 0
        while q:
            y, x = q.popleft()
            n += 1
            x_lo, x_hi, y_lo, y_hi = min(x_lo, x), max(x_hi, x), min(y_lo, y), max(y_hi, y)
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    yy, xx = y + dy, x + dx
                    if 0 <= yy < h and 0 <= xx < w and dark[yy, xx] and not seen[yy, xx]:
                        seen[yy, xx] = True
                        q.append((yy, xx))
        out.append((x_lo, y_lo, x_hi, y_hi, n))
    return out


def group(glyphs, horizontal):
    boxes = []
    for g in sorted(glyphs, key=lambda g: (g[0], g[1]) if horizontal else (g[1], g[0])):
        for b in boxes:
            if horizontal:
                overlap, gap = min(b[3], g[3]) - max(b[1], g[1]), g[0] - b[2]
            else:
                overlap, gap = min(b[2], g[2]) - max(b[0], g[0]), g[1] - b[3]
            if overlap > 10 and -5 <= gap <= 14:
                b[:] = [min(b[0], g[0]), min(b[1], g[1]), max(b[2], g[2]), max(b[3], g[3])]
                break
        else:
            boxes.append([g[0], g[1], g[2], g[3]])
    return boxes


def find_words(dark):
    glyphs = [c for c in components(dark)
              if 20 <= max(c[2] - c[0], c[3] - c[1]) + 1 <= 42 and min(c[2] - c[0], c[3] - c[1]) + 1 >= 3 and c[4] >= 25]
    hz = [c for c in glyphs if 26 <= c[3] - c[1] + 1 <= 42 and c[2] - c[0] + 1 <= 42]
    vt = [c for c in glyphs if 26 <= c[2] - c[0] + 1 <= 42 and c[3] - c[1] + 1 <= 42]
    words = [("h", b) for b in group(hz, True) if (b[2] - b[0]) >= (b[3] - b[1]) - 4]
    words += [("v", b) for b in group(vt, False) if (b[3] - b[1]) > (b[2] - b[0]) + 4]
    return words


def trim(dark, rect):
    """Ink box inside an approximate rectangle, ignoring dimension lines that cross it."""
    x0, y0, x1, y1 = rect
    sub = dark[y0:y1 + 1, x0:x1 + 1].copy()
    sub[:, sub.sum(axis=0) >= 0.8 * sub.shape[0]] = False
    sub[sub.sum(axis=1) >= 0.8 * sub.shape[1], :] = False
    ys, xs = np.nonzero(sub)
    return [x0 + xs.min(), y0 + ys.min(), x0 + xs.max(), y0 + ys.max()]


def split(dark, box, direction, count):
    """Split a word box into count pieces at its widest ink gaps (or evenly when there are none)."""
    if count == 1:
        return [box]
    x0, y0, x1, y1 = box
    sub = dark[y0:y1 + 1, x0:x1 + 1]
    profile = sub.sum(axis=0 if direction == "h" else 1)
    runs, start = [], None
    for i, v in enumerate(profile):
        if v == 0 and start is None:
            start = i
        elif v != 0 and start is not None:
            runs.append((i - start, start, i))
            start = None
    cuts = sorted(sorted(runs, reverse=True)[:count - 1], key=lambda r: r[1])
    if len(cuts) < count - 1 or min(r[0] for r in cuts) < 6:
        length = len(profile)
        cuts = [(0, length * k // count, length * k // count) for k in range(1, count)]
    pieces, prev = [], 0
    for _, a, b in cuts:
        pieces.append((prev, a))
        prev = b
    pieces.append((prev, len(profile)))
    out = []
    for a, b in pieces:
        out.append([x0 + a, y0, x0 + b - 1, y1] if direction == "h" else [x0, y0 + a, x1, y0 + b - 1])
    return out if direction == "h" else out[::-1]   # vertical text reads bottom to top


def main():
    preview_dir = Path(sys.argv[1]) if len(sys.argv) > 1 else None
    sheets = {}
    for sheet, (file, entries) in SPEC.items():
        path = IMAGES / file
        dark = load_dark(path)
        words = find_words(dark)
        labels = {}
        for entry in entries:
            if entry[0] == "w":
                direction, box = words[entry[1]]
                names = entry[2]
            else:
                box, names, direction = trim(dark, entry[1]), entry[2], entry[3]
            for name, piece in zip(names, split(dark, box, direction, len(names))):
                if name:
                    labels.setdefault(name, []).append(piece)
        sheets[sheet] = labels
        if preview_dir:
            img = Image.open(path).convert("RGBA")
            bg = Image.new("RGBA", img.size, "white")
            draw = ImageDraw.Draw(bg)
            for name, boxes in labels.items():
                for b in boxes:
                    draw.rectangle([b[0] - PAD, b[1] - PAD, b[2] + PAD, b[3] + PAD], fill=(198, 255, 0, 255))
            bg.alpha_composite(img)
            draw = ImageDraw.Draw(bg)
            for name, boxes in labels.items():
                for b in boxes:
                    draw.text((b[2] + PAD + 2, b[1]), name, fill=(220, 0, 160, 255))
            bg.convert("RGB").save(preview_dir / f"pump_{sheet}.png")

    lines = [
        "// tools/generate_pump_diagram_labels.py 자동 생성. 직접 수정 금지.",
        "using System.Collections.Generic;",
        "",
        "namespace DHBIMWATER.UI.ViewModels.Modeling",
        "{",
        "    internal static partial class PumpingStationDiagramLabels",
        "    {",
        "        private static readonly Dictionary<string, Dictionary<string, string>> Sheets = new()",
        "        {",
    ]
    for sheet, labels in sheets.items():
        lines.append(f'            ["{sheet}"] = new()')
        lines.append("            {")
        for name in sorted(labels):
            path = " ".join(f"M{b[0] - PAD},{b[1] - PAD} H{b[2] + PAD} V{b[3] + PAD} H{b[0] - PAD} Z" for b in labels[name])
            lines.append(f'                ["{name}"] = "{path}",')
        lines.append("            },")
    lines += ["        };", "    }", "}", ""]
    TABLE.write_text("\n".join(lines), encoding="utf-8-sig")
    for sheet, labels in sheets.items():
        print(sheet, len(labels), sorted(labels))


if __name__ == "__main__":
    main()
