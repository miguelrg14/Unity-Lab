"""Generates the procedural texture and sounds used by the Racing demo.

Everything here is procedural, so the repository owns all of it:
  - T_Ground.png      tiling grass/sand ground texture
  - Engine_Loop.wav   seamless engine hum (pitch is driven at runtime)
  - Tyre_Skid.wav     tyre screech loop
The low-poly car, wheel and waypoint meshes are built by the Unity editor
script ProceduralMeshes.cs instead.
"""
import math
import random
import struct
import sys
import wave
from pathlib import Path

from PIL import Image, ImageFilter

root = Path(sys.argv[1])
racing = root / "UnityProject/Assets/_Project/Racing"
rng = random.Random(14)


# ---------------------------------------------------------------- texture
def value_noise(size, cell, seed):
    r = random.Random(seed)
    n = size // cell + 1
    grid = [[r.random() for _ in range(n + 1)] for _ in range(n + 1)]

    def smooth(t):
        return t * t * (3 - 2 * t)

    out = [[0.0] * size for _ in range(size)]
    for y in range(size):
        gy, fy = divmod(y / cell, 1)
        gy = int(gy) % (n - 1)
        for x in range(size):
            gx, fx = divmod(x / cell, 1)
            gx = int(gx) % (n - 1)
            a = grid[gy][gx] + (grid[gy][gx + 1] - grid[gy][gx]) * smooth(fx)
            b = grid[gy + 1][gx] + (grid[gy + 1][gx + 1] - grid[gy + 1][gx]) * smooth(fx)
            out[y][x] = a + (b - a) * smooth(fy)
    return out


def ground_texture(path, size=512):
    # Cells divide the size exactly so the texture tiles seamlessly.
    layers = [(value_noise(size, c, s), w) for c, s, w in ((128, 1, 0.55), (32, 2, 0.3), (8, 3, 0.15))]
    img = Image.new("RGB", (size, size))
    px = img.load()
    grass, dry = (86, 140, 62), (170, 160, 96)
    for y in range(size):
        for x in range(size):
            v = sum(layer[y][x] * w for layer, w in layers)
            t = min(1.0, max(0.0, (v - 0.35) * 2.2))
            speck = rng.uniform(-8, 8)
            px[x, y] = tuple(int(g + (d - g) * t + speck) for g, d in zip(grass, dry))
    img.filter(ImageFilter.SMOOTH).save(path)


# ---------------------------------------------------------------- audio
RATE = 22050


def write_wav(path, samples):
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, s)) * 30000)) for s in samples))


def engine_loop(path, seconds=1.0):
    n = int(RATE * seconds)
    base = 60.0  # integer number of cycles per loop keeps it seamless
    out = []
    for i in range(n):
        t = i / RATE
        s = 0.0
        for h, amp in ((1, 0.6), (2, 0.35), (3, 0.2), (4, 0.12), (6, 0.06)):
            s += amp * math.sin(2 * math.pi * base * h * t)
        s *= 0.75 + 0.25 * math.sin(2 * math.pi * 30 * t)  # firing pulse
        out.append(0.55 * s)
    write_wav(path, out)


def skid_loop(path, seconds=1.5):
    n = int(RATE * seconds)
    out, lp = [], 0.0
    for i in range(n):
        t = i / RATE
        lp += 0.35 * (rng.uniform(-1, 1) - lp)
        squeal = sum(math.sin(2 * math.pi * f * t) * a for f, a in ((1200, 0.25), (1810, 0.12), (2420, 0.06)))
        env = 1.0 - 0.15 * math.sin(2 * math.pi * 4 * t)
        out.append(0.6 * env * (0.5 * lp + squeal))
    # Short crossfade so the loop point does not click.
    fade = int(RATE * 0.05)
    for i in range(fade):
        k = i / fade
        out[i] = out[i] * k + out[n - fade + i] * (1 - k)
    write_wav(path, out[: n - fade])


if __name__ == "__main__":
    ground_texture(racing / "Art/Textures/T_Ground.png")
    engine_loop(racing / "Audio/SFX/Engine_Loop.wav")
    skid_loop(racing / "Audio/SFX/Tyre_Skid.wav")
    print("done")
