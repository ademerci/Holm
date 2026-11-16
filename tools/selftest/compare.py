#!/usr/bin/env python3
"""Lays the self-test pictures of the three systems over each other (docs/cross-platform/WORKFLOW.md).

    pip install pillow
    python3 compare.py <folder with selftest-Windows, selftest-Linux, selftest-macOS> <output folder>

For each picture (closed.png, open.png) it reports, per pair of systems, whether the sizes match, how many pixels
differ noticeably and by how much at most, and writes a difference image (brighter = more different). Exit code 1
when a size differs (the layout drifted apart) or a pair differs in more than MAX_SHARE of its pixels.
"""
import itertools, os, sys
from PIL import Image, ImageChops

NOTICEABLE = 24      # per channel, out of 255: below this the eye can't tell on a dark notch
MAX_SHARE = 0.01     # more than 1 % of the pixels noticeably different counts as a different look

source, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
systems = sorted(d[len("selftest-"):] for d in os.listdir(source) if d.startswith("selftest-"))
failed = False
for picture in ("closed.png", "open.png"):
    images = {}
    for system in systems:
        path = os.path.join(source, "selftest-" + system, picture)
        if os.path.exists(path):
            images[system] = Image.open(path).convert("RGBA")
        else:
            print(f"{picture}: missing on {system}")
            failed = True
    for a, b in itertools.combinations(sorted(images), 2):
        ia, ib = images[a], images[b]
        if ia.size != ib.size:
            print(f"{picture}: {a} {ia.size} vs {b} {ib.size}: sizes differ")
            failed = True
            continue
        diff = ImageChops.difference(ia, ib)
        # The largest channel difference of each pixel.
        worst = list(ImageChops.lighter(ImageChops.lighter(*diff.split()[:2]), ImageChops.lighter(*diff.split()[2:])).tobytes())
        share = sum(1 for w in worst if w >= NOTICEABLE) / len(worst)
        print(f"{picture}: {a} vs {b}: {share:.3%} of pixels noticeably different, largest difference {max(worst)}")
        if share > MAX_SHARE:
            failed = True
        scaled = diff.convert("RGB").point(lambda v: min(255, v * 4))
        scaled.save(os.path.join(out, f"{picture[:-4]}-{a}-vs-{b}.png"))
sys.exit(1 if failed else 0)
