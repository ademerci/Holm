#!/usr/bin/env python3
"""Builds Holm.App/Assets/HolmIcons.ttf: the handful of icons Holm uses, taken from Fluent System Icons (MIT)
and filed under the code points of Segoe Fluent Icons, so the app's code is the same on every system.

    pip install fonttools
    npm pack @fluentui/react-icons && tar xzf fluentui-react-icons-*.tgz package/lib/utils/fonts
    python3 build.py package/lib/utils/fonts

Each glyph is centred in a square em with no line gap, which is how Segoe's icon fonts are laid out: an icon
at FontSize 14 is a 14 by 14 box.
"""
import json, os, sys
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.recordingPen import DecomposingRecordingPen
from fontTools.pens.transformPen import TransformPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont

here = os.path.dirname(os.path.abspath(__file__))
source_dir = sys.argv[1]
source = TTFont(os.path.join(source_dir, "FluentSystemIcons-Resizable.ttf"))
names = json.load(open(os.path.join(source_dir, "FluentSystemIcons-Resizable.json")))
cmap, glyph_set, em = source.getBestCmap(), source.getGlyphSet(), source["head"].unitsPerEm

wanted = []
for line in open(os.path.join(here, "icons.txt"), encoding="utf-8"):
    line = line.split("#")[0].split()
    if len(line) == 2: wanted.append((int(line[0], 16), line[1]))

glyphs, metrics, mapping, order = {}, {}, {}, [".notdef"]
glyphs[".notdef"] = TTGlyphPen(None).glyph()
metrics[".notdef"] = (em, 0)
for code, name in wanted:
    if name not in names: sys.exit(f"No icon named {name} in Fluent System Icons")
    glyph_name = f"u{code:04X}"
    recording = DecomposingRecordingPen(glyph_set)
    glyph_set[cmap[names[name]]].draw(recording)
    bounds = source["glyf"][cmap[names[name]]]
    dx = (em - (bounds.xMax + bounds.xMin)) / 2
    dy = (em - (bounds.yMax + bounds.yMin)) / 2
    pen = TTGlyphPen(None)
    recording.replay(TransformPen(pen, (1, 0, 0, 1, round(dx), round(dy))))
    glyphs[glyph_name] = pen.glyph()
    metrics[glyph_name] = (em, round(dx) + bounds.xMin)
    mapping[code] = glyph_name
    order.append(glyph_name)

builder = FontBuilder(em, isTTF=True)
builder.setupGlyphOrder(order)
builder.setupCharacterMap(mapping)
builder.setupGlyf(glyphs)
builder.setupHorizontalMetrics(metrics)
builder.setupHorizontalHeader(ascent=em, descent=0, lineGap=0)
builder.setupOS2(sTypoAscender=em, sTypoDescender=0, sTypoLineGap=0, usWinAscent=em, usWinDescent=0, fsSelection=0x40)
builder.setupNameTable({
    "familyName": "Holm Icons", "styleName": "Regular",
    "copyright": "Icons from Fluent System Icons, Copyright (c) 2020 Microsoft Corporation, MIT License",
    "licenseDescription": "MIT License",
})
builder.setupPost()
out = os.path.join(here, "..", "..", "Holm.App", "Assets", "HolmIcons.ttf")
builder.save(out)
print(f"{len(wanted)} icons, {os.path.getsize(out)} bytes -> {os.path.normpath(out)}")
