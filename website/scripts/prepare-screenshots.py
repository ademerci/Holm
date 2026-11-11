"""Extract the actual captured Holm pixels; never redraw the app or its text.

Optional authoring helper: python scripts/prepare-screenshots.py (requires Pillow).
The committed results are consumed directly by the Node build; CI needs no Python.
The source renderer adds a uniform horizontal desktop colour on each row. Sample
that known backdrop only at the outside edge, preserve the solid UI verbatim, and
unmatte the few antialiased outline pixels against their adjacent UI colour.
"""

from pathlib import Path
from hashlib import sha256
import json
import argparse
from PIL import Image, ImageChops, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT.parent / "docs"
OUT = ROOT / "src/assets/screenshots"
HUB = {"chat", "settings", "activity", "achievements"}


def notch_cutout(source):
    source = source.convert("RGB")
    w, h = source.size
    result = Image.new("RGBA", source.size)
    pixels = source.load()
    target = result.load()
    for y in range(h):
        background = pixels[0, y]
        # A row has just one notch silhouette; content inside it is never keyed.
        strong = [x for x in range(w)
                  if max(abs(pixels[x, y][c] - background[c]) for c in range(3)) > 16]
        if not strong:
            continue
        left, right = strong[0], strong[-1]
        for x in range(left, right + 1):
            target[x, y] = (*pixels[x, y], 255)
        # Recover edge coverage and remove the blue matte. Only boundary pixels
        # may change RGB; text, artwork, buttons and the entire interior do not.
        for edge, direction in [(left, 1), (right, -1)]:
            reference = pixels[min(right, max(left, edge + direction * 4)), y]
            vector = tuple(reference[c] - background[c] for c in range(3))
            denominator = sum(v * v for v in vector)
            for offset in range(-3, 2):
                x = edge + direction * offset
                if not 0 <= x < w:
                    continue
                colour = pixels[x, y]
                if colour == background:
                    target[x, y] = (0, 0, 0, 0)
                    continue
                alpha = sum((colour[c] - background[c]) * vector[c]
                            for c in range(3)) / max(1, denominator)
                if alpha >= .995:
                    target[x, y] = (*colour, 255)
                elif alpha <= .01:
                    target[x, y] = (0, 0, 0, 0)
                else:
                    foreground = tuple(max(0, min(255, round(
                        (colour[c] - (1 - alpha) * background[c]) / alpha)))
                        for c in range(3))
                    target[x, y] = (*foreground, round(alpha * 255))
    # Floating indicators can leave a gap between themselves and the notch.
    # Clear only exact backdrop pixels connected to the outside; never key a
    # matching colour enclosed inside the UI (such as artwork or a blue button).
    backdrop = Image.new("RGB", (1, h))
    backdrop.putdata([pixels[0, y] for y in range(h)])
    backdrop = backdrop.resize((w, h), Image.Resampling.NEAREST)
    difference = ImageChops.difference(source, backdrop)
    red, green, blue = difference.split()
    maximum = ImageChops.lighter(ImageChops.lighter(red, green), blue)
    outside = maximum.point(lambda value: 255 if value == 0 else 0)
    for seed in [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)]:
        if outside.getpixel(seed) == 255:
            ImageDraw.floodfill(outside, seed, 128)
    result.paste((0, 0, 0, 0), mask=outside.point(lambda value: 255 if value == 128 else 0))
    return result


def window_cutout(source, rounded):
    # Native 2x hub captures have the same 16-DIP inset and 14-DIP corners.
    # The stream viewer has a rectangular 16-DIP inset. Keep its shown screen.
    source = source.convert("RGBA")
    w, h = source.size
    if rounded:
        assert (w, h) == (2024, 1520), "Review the hub outline after a layout change"
        bounds = (32, 32, 1928, 1424)
    else:
        bounds = (32, 32, w - 32, h - 32)
    scale = 4
    mask = Image.new("L", (w * scale, h * scale))
    draw = ImageDraw.Draw(mask)
    box = tuple(v * scale for v in bounds)
    if rounded:
        draw.rounded_rectangle(box, radius=28 * scale, fill=255)
    else:
        draw.rectangle((box[0], box[1], box[2] - 1, box[3] - 1), fill=255)
    mask = mask.resize((w, h), Image.Resampling.LANCZOS)
    source.putalpha(mask)
    source.paste((0, 0, 0, 0), mask=mask.point(lambda value: 255 if value == 0 else 0))
    return source


def comparison_cutout(source):
    # This is a precomposed before/after graphic, not one app window. Extract
    # each of its four captures separately, leaving the original graphic intact.
    assert source.size == (1180, 412), "Review comparison regions after a change"
    result = Image.new("RGBA", source.size)
    for box in [(20, 45, 580, 105), (600, 45, 1160, 95),
                (20, 117, 580, 388), (600, 107, 1160, 392)]:
        piece = notch_cutout(source.crop(box))
        result.paste(piece, (box[0], box[1]))
    return result


def main(only=None):
    OUT.mkdir(parents=True, exist_ok=True)
    manifest_path = OUT / "manifest.json"
    manifest = (json.loads(manifest_path.read_text(encoding="utf-8"))
                if only and manifest_path.exists()
                else {"method": "native-capture-background-extraction", "screenshots": []})
    sources = sorted(SOURCE.glob("*.png"))
    if only:
        sources = [path for path in sources if path.name == only and path.stem != "icon"]
        if not sources:
            raise ValueError(f"No product capture named {only}")
    for path in sources:
        if path.stem == "icon":
            continue  # The official logo is branding, not a desktop screenshot.
        original = Image.open(path)
        if path.stem in HUB:
            output = window_cutout(original, True)
        elif path.stem == "stream":
            output = window_cutout(original, False)
        elif path.stem == "idle-redesign":
            output = comparison_cutout(original)
        else:
            output = notch_cutout(original)
        destination = OUT / path.name
        output.save(destination, optimize=True)
        alpha = output.getchannel("A")
        histogram = alpha.histogram()
        assert histogram[0] > 0 and histogram[255] > 0, path.name
        # Full-opacity UI pixels must match the input byte for byte.
        opaque = alpha.point(lambda v: 255 if v == 255 else 0)
        difference = ImageChops.difference(original.convert("RGB"), output.convert("RGB"))
        assert ImageChops.multiply(difference, Image.merge("RGB", (opaque,) * 3)).getbbox() is None, path.name
        manifest["screenshots"] = [entry for entry in manifest["screenshots"]
                                   if entry["name"] != path.name]
        manifest["screenshots"].append({
            "name": path.name, "width": output.width, "height": output.height,
            "source_sha256": sha256(path.read_bytes()).hexdigest(),
            "output_sha256": sha256(destination.read_bytes()).hexdigest(),
            "transparent_pixels": histogram[0], "opaque_pixels": histogram[255],
            "edge_pixels": sum(histogram[1:255]),
        })
        print(f"{path.name}: transparent background; {histogram[255]:,} original UI pixels preserved")
    manifest["screenshots"].sort(key=lambda entry: entry["name"])
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Prepared {sum(path.stem != 'icon' for path in sources)} transparent captures in {OUT}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--only", help="Regenerate one named capture, for example airdrop-share.png")
    main(parser.parse_args().only)
