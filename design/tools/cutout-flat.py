"""Cut a character off a flat background into a transparent PNG.

Beki is delivered on a plain cream field, which makes this simple and exact: the alpha is a
function of how far each pixel is from the background colour, not of what it is connected to.
That matters because the pose sheet has gaps a flood fill cannot reach — the field showing
through between an arm and a body is background, but it touches no border.

Two details worth keeping:

*Soft ramp.* Alpha goes from nothing to solid across a range rather than at one threshold, so
the edge keeps its anti-aliasing instead of turning into a staircase.

*Unpremultiplying.* A half-transparent edge pixel is half character and half cream. Left alone
it carries that cream onto whatever it is composited over, and against a night sky it reads as
a pale rim around the figure. Backing the background out of the colour is the compositing maths
run in reverse, and it is what makes the cutout look drawn rather than cut.

    python cutout-flat.py <source> <destination.png> [near] [far]

Anything within `near` of the background is fully transparent; anything past `far` is fully
opaque. Widen the gap for a soft-edged subject, narrow it for a crisp one.

Requires Pillow and numpy: python -m pip install Pillow numpy
"""

import sys

import numpy as np
from PIL import Image

src = sys.argv[1]
dst = sys.argv[2]
near = float(sys.argv[3]) if len(sys.argv) > 3 else 6.0
far = float(sys.argv[4]) if len(sys.argv) > 4 else 22.0

rgb = np.asarray(Image.open(src).convert("RGB")).astype(np.float32)

# The background colour, taken from the border rather than one corner, so a little vignetting
# or a stray mark cannot decide it.
border = np.concatenate([rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1]])
bg = np.median(border, axis=0)

dist = np.sqrt(((rgb - bg) ** 2).sum(axis=2))
alpha = np.clip((dist - near) / (far - near), 0.0, 1.0)

# Undo the background the source already blended into the soft edge.
a3 = alpha[..., None]
straight = np.where(a3 > 0.004, (rgb - (1.0 - a3) * bg) / np.maximum(a3, 0.004), rgb)
straight = np.clip(straight, 0, 255)

out = np.dstack([straight, alpha * 255]).astype(np.uint8)
img = Image.fromarray(out, "RGBA")
box = img.getbbox()
if box:
    img = img.crop(box)
img.save(dst)
print(f"bg={bg.round(1)} size={img.size} opaque={(alpha > 0.5).mean():.3f}")
