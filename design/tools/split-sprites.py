"""Split a sheet of separate elements into one transparent sprite per element.

The world map arrives as one image holding six islands and the open book, each already cut
out against transparency. The app places them itself, so it needs them apart.

Connected components alone will not do it. Every island trails floating rocks, a moon, a
scattering of debris — dozens of little islands of pixels that touch nothing. Split naively,
they become their own sprites and the map loses its rubble. So the biggest components are
taken as anchors, and every smaller one is given to whichever anchor it is nearest.

    python split-sprites.py <sheet> <out-dir> <name1> <name2> ...

Names are assigned in reading order — left to right, top to bottom — which is how the sheets
are laid out. The script prints each sprite's box as a fraction of the sheet, which is the
number the layout table wants.

Requires Pillow, numpy and scipy: python -m pip install Pillow numpy scipy
"""

import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

sheet_path, out_dir = sys.argv[1], Path(sys.argv[2])
names = sys.argv[3:]
if not names:
    raise SystemExit("give a name per element, in reading order")

out_dir.mkdir(parents=True, exist_ok=True)

sheet = Image.open(sheet_path).convert("RGBA")
rgba = np.asarray(sheet)
# 40, not 1: lossy compression leaves a haze of alpha 1-20 across the empty field, and at a
# threshold of 1 the whole sheet is one component.
solid = rgba[..., 3] > 40

labels, count = ndimage.label(solid)
areas = np.array(ndimage.sum(solid, labels, range(1, count + 1)))
boxes = ndimage.find_objects(labels)

anchors = list(np.argsort(areas)[::-1][: len(names)])


def centre(i):
    ys, xs = boxes[i]
    return ((xs.start + xs.stop) / 2, (ys.start + ys.stop) / 2)


# Reading order, with a generous row band: the islands do not line up to the pixel, and
# sorting on y alone would interleave two rows that overlap by a few dozen pixels.
row_height = sheet.height / 3
anchors.sort(key=lambda i: (int(centre(i)[1] // row_height), centre(i)[0]))

# Every leftover goes to the nearest anchor, so a moon travels with its island.
groups = {i: [i] for i in anchors}
for i in range(count):
    if i in groups:
        continue
    cx, cy = centre(i)
    nearest = min(anchors, key=lambda a: (centre(a)[0] - cx) ** 2 + (centre(a)[1] - cy) ** 2)
    groups[nearest].append(i)

for name, anchor in zip(names, anchors):
    members = groups[anchor]
    mask = np.isin(labels, [m + 1 for m in members])

    ys, xs = np.where(mask)
    x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1

    cut = rgba.copy()
    cut[..., 3] = np.where(mask, cut[..., 3], 0)
    sprite = Image.fromarray(cut[y0:y1, x0:x1], "RGBA")

    path = out_dir / f"{name}.webp"
    sprite.save(path, "WEBP", quality=92, method=6)

    print(
        f"{name:12s} {sprite.size[0]:4d}x{sprite.size[1]:4d}px  "
        f"centre {(x0 + x1) / 2 / sheet.width:.4f} {(y0 + y1) / 2 / sheet.height:.4f}  "
        f"width {(x1 - x0) / sheet.width:.4f}  parts {len(members)}  {path.stat().st_size // 1024}kB"
    )
