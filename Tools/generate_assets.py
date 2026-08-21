#!/usr/bin/env python3
"""Prepare the curated texture atlas and deterministic sound effects used by the game.

The script intentionally depends only on the Python standard library so the
same audio assets can be produced locally and on a clean GitHub Actions runner.
The checked-in 2048px art-directed atlas is preserved; a procedural fallback
is generated only when that source asset is absent.
"""

from __future__ import annotations

import math
import random
import hashlib
import struct
import wave
import zlib
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
RESOURCE_ROOT = ROOT / "Assets/BallisticSniper/Resources/BallisticSniper"
ATLAS_PATH = RESOURCE_ROOT / "Textures/range_material_atlas.png"
ATLAS_PARTS_DIR = ROOT / "Tools/atlas_parts"
CURATED_ATLAS_SHA256 = "4998f7dd58ddd64f648d60cded2b3e3e3b48f638dc563ca6df13e2f97490a96e"
AUDIO_DIR = RESOURCE_ROOT / "Audio"
ATLAS_SIZE = 2048
CELL_SIZE = ATLAS_SIZE // 4
SAMPLE_RATE = 22_050


def clamp(value: float, low: int = 0, high: int = 255) -> int:
    return max(low, min(high, int(value)))


def hash_noise(x: int, y: int, seed: int) -> float:
    """Fast stable pseudo-noise in the -1..1 interval."""
    value = (x * 374_761_393 + y * 668_265_263 + seed * 2_147_483_647) & 0xFFFFFFFF
    value = (value ^ (value >> 13)) * 1_274_126_177 & 0xFFFFFFFF
    value ^= value >> 16
    return (value / 2_147_483_647.5) - 1.0


def octave_noise(x: int, y: int, seed: int) -> float:
    return (
        hash_noise(x, y, seed) * 0.48
        + hash_noise(x // 3, y // 3, seed + 11) * 0.30
        + hash_noise(x // 11, y // 11, seed + 29) * 0.22
    )


def shade(base: tuple[int, int, int], amount: float) -> tuple[int, int, int]:
    return tuple(clamp(channel + amount) for channel in base)


def atlas_pixel(cell: int, x: int, y: int) -> tuple[int, int, int]:
    noise = octave_noise(x, y, cell + 1)

    if cell == 0:  # Dirt
        color = shade((105, 76, 45), noise * 28)
        if hash_noise(x // 3, y // 3, 61) > 0.84:
            color = shade((137, 111, 74), noise * 12)
        return color

    if cell == 1:  # Grass
        blade = 18 if (x + (y // 7) * 3) % 17 in (0, 1) else 0
        return shade((54, 104 + blade, 42), noise * 31 + math.sin(y / 17) * 6)

    if cell == 2:  # Sandstone
        strata = math.sin(y / 10 + math.sin(x / 31) * 1.8) * 18
        return shade((183, 143, 88), strata + noise * 17)

    if cell == 3:  # Granite
        speck = hash_noise(x // 2, y // 2, 73)
        amount = noise * 30
        if speck > 0.82:
            amount += 58
        elif speck < -0.86:
            amount -= 53
        return shade((105, 107, 106), amount)

    if cell == 4:  # Planks
        seam_distance = min(y % 64, 63 - y % 64)
        if seam_distance < 2:
            return (45, 28, 17)
        grain = math.sin(x / 12 + math.sin(y / 8)) * 11
        color = shade((133, 87, 46), grain + noise * 18)
        nail_x = x % 96
        nail_y = y % 64
        if (nail_x - 10) ** 2 + (nail_y - 12) ** 2 < 9:
            return (38, 37, 34)
        return color

    if cell == 5:  # Splintered wood
        splinter = 26 if (x * 3 + y) % 47 < 3 else 0
        return shade((169, 125, 74), noise * 25 + splinter)

    if cell == 6:  # Rusted red steel
        rust = hash_noise(x // 13, y // 13, 97)
        base = (122, 42, 30) if rust < 0.25 else (164, 73, 31)
        return shade(base, noise * 25)

    if cell == 7:  # Scratched black steel
        scratch = 0
        if (x + 2 * y) % 89 < 2 or (3 * x - y) % 127 < 2:
            scratch = 75
        return shade((39, 43, 45), noise * 16 + scratch)

    if cell == 8:  # Corrugated steel
        ridge = math.sin(x / 10.5) * 37
        rust = 24 if hash_noise(x // 15, y // 15, 113) > 0.63 else 0
        return shade((119 + rust, 124 - rust // 2, 122 - rust), ridge + noise * 13)

    if cell == 9:  # Clay
        ring = math.sin(math.hypot(x - 128, y - 128) / 8) * 5
        return shade((174, 82, 47), noise * 19 + ring)

    if cell == 10:  # Watermelon skin
        stripe = (math.sin(x / 15 + math.sin(y / 34)) + 1) * 0.5
        return shade((35, 91 + int(stripe * 53), 38), noise * 15)

    if cell == 11:  # Watermelon flesh
        seed_x = x % 57 - 28
        seed_y = y % 61 - 30
        if (seed_x / 4.0) ** 2 + (seed_y / 10.0) ** 2 < 1:
            return (38, 27, 23)
        return shade((221, 70, 78), noise * 12)

    if cell == 12:  # Cracked glass
        crack = (
            abs((x + y * 2) % 83 - 41) < 1
            or abs((x * 3 - y) % 137 - 68) < 1
            or abs((x - 128) * 5 - (y - 128) * 2) < 5
        )
        return (224, 244, 249) if crack else shade((130, 172, 181), noise * 13)

    if cell == 13:  # Paper target
        cx, cy = x - 128, y - 128
        radius = math.hypot(cx, cy)
        if radius < 16:
            return (188, 37, 34)
        if int(radius / 18) % 2 == 0 and radius < 112:
            return shade((38, 42, 42), noise * 8)
        return shade((231, 222, 193), noise * 8)

    if cell == 14:  # Snow
        sparkle = 32 if hash_noise(x, y, 149) > 0.94 else 0
        return shade((211, 226, 232), noise * 15 + sparkle)

    # Concrete
    amount = noise * 23
    if hash_noise(x // 2, y // 2, 163) > 0.90:
        amount += 36
    crack = abs((x * 2 + y * 5) % 181 - 90) < 1
    return shade((125, 126, 121), amount - (45 if crack else 0))


def png_chunk(kind: bytes, data: bytes) -> bytes:
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))


def generate_atlas() -> None:
    ATLAS_PATH.parent.mkdir(parents=True, exist_ok=True)
    raw = bytearray()
    for image_y in range(ATLAS_SIZE):
        raw.append(0)  # PNG filter: none
        cell_row = image_y // CELL_SIZE
        local_y = image_y % CELL_SIZE
        for image_x in range(ATLAS_SIZE):
            cell = cell_row * 4 + image_x // CELL_SIZE
            raw.extend(atlas_pixel(cell, image_x % CELL_SIZE, local_y))
    header = struct.pack(">IIBBBBB", ATLAS_SIZE, ATLAS_SIZE, 8, 2, 0, 0, 0)
    png = b"\x89PNG\r\n\x1a\n" + png_chunk(b"IHDR", header)
    png += png_chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + png_chunk(b"IEND", b"")
    ATLAS_PATH.write_bytes(png)


def restore_curated_atlas() -> bool:
    parts = sorted(ATLAS_PARTS_DIR.glob("range_material_atlas.png.part-*"))
    if not parts:
        return False
    data = b"".join(part.read_bytes() for part in parts)
    digest = hashlib.sha256(data).hexdigest()
    if digest != CURATED_ATLAS_SHA256:
        raise RuntimeError(f"Curated atlas checksum mismatch: {digest}")
    if not ATLAS_PATH.exists() or ATLAS_PATH.read_bytes() != data:
        ATLAS_PATH.parent.mkdir(parents=True, exist_ok=True)
        ATLAS_PATH.write_bytes(data)
    return True


def synth_sample(name: str, t: float, rng: random.Random, state: list[float]) -> float:
    white = rng.uniform(-1.0, 1.0)
    state[0] = state[0] * 0.88 + white * 0.12
    state[1] = state[1] * 0.97 + white * 0.03

    if name == "shot":
        env = math.exp(-7.0 * t)
        crack = rng.uniform(-1, 1) * math.exp(-65 * t)
        return env * (0.70 * white + 0.48 * math.sin(2 * math.pi * 92 * t)) + crack
    if name == "hit":
        env = math.exp(-12 * t)
        return env * (0.75 * state[0] + 0.70 * math.sin(2 * math.pi * 115 * t))
    if name == "glass_break":
        env = math.exp(-4.6 * t)
        tones = sum(math.sin(2 * math.pi * f * t) for f in (1240, 1810, 2470, 3190)) / 4
        return env * (0.54 * tones + 0.48 * white)
    if name == "clay_break":
        env = math.exp(-6.1 * t)
        pulse = 1.0 if (t * 28) % 1.0 < 0.24 else 0.38
        return env * pulse * (0.82 * state[0] + 0.25 * math.sin(2 * math.pi * 310 * t))
    if name == "cans_crash":
        env = math.exp(-3.7 * t)
        ring = sum(math.sin(2 * math.pi * f * t) for f in (520, 690, 910, 1280)) / 4
        return env * (0.76 * ring + 0.25 * white)
    if name == "wood_break":
        env = math.exp(-6.7 * t)
        snap = rng.uniform(-1, 1) * math.exp(-38 * abs(t - 0.045))
        return env * (0.65 * state[0] + 0.36 * math.sin(2 * math.pi * 165 * t)) + 0.38 * snap
    if name == "melon_splat":
        env = math.exp(-7.3 * t)
        return env * (0.88 * state[1] + 0.34 * math.sin(2 * math.pi * (82 - 24 * t) * t))
    if name == "explosion":
        env = math.exp(-2.8 * t)
        thump = math.sin(2 * math.pi * (58 - 17 * t) * t)
        return env * (0.70 * state[1] + 0.52 * thump + 0.20 * white)
    if name == "bullseye":
        env = math.exp(-5.4 * t)
        return env * (0.78 * math.sin(2 * math.pi * 1046.5 * t) + 0.32 * math.sin(2 * math.pi * 1568 * t))
    raise ValueError(name)


def generate_wav(name: str, duration: float, seed: int) -> None:
    path = AUDIO_DIR / f"{name}.wav"
    count = int(duration * SAMPLE_RATE)
    rng = random.Random(seed)
    state = [0.0, 0.0]
    frames = bytearray()
    for index in range(count):
        sample = synth_sample(name, index / SAMPLE_RATE, rng, state)
        sample = math.tanh(sample * 1.25) * 0.87
        frames.extend(struct.pack("<h", int(sample * 32767)))
    with wave.open(str(path), "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(SAMPLE_RATE)
        output.writeframes(frames)


def generate_audio() -> None:
    AUDIO_DIR.mkdir(parents=True, exist_ok=True)
    sounds = {
        "shot": (0.72, 101),
        "hit": (0.48, 102),
        "glass_break": (0.96, 103),
        "clay_break": (0.78, 104),
        "cans_crash": (1.05, 105),
        "wood_break": (0.84, 106),
        "melon_splat": (0.76, 107),
        "explosion": (1.45, 108),
        "bullseye": (0.62, 109),
    }
    for name, (duration, seed) in sounds.items():
        generate_wav(name, duration, seed)


def main() -> None:
    restored = restore_curated_atlas()
    if not restored and not ATLAS_PATH.exists():
        generate_atlas()
    dimensions = ATLAS_PATH.read_bytes()[:24]
    if dimensions[:8] != b"\x89PNG\r\n\x1a\n":
        raise RuntimeError(f"Invalid PNG atlas: {ATLAS_PATH}")
    width, height = struct.unpack(">II", dimensions[16:24])
    if (width, height) != (ATLAS_SIZE, ATLAS_SIZE):
        raise RuntimeError(f"Atlas must be {ATLAS_SIZE}x{ATLAS_SIZE}, got {width}x{height}")
    generate_audio()
    print(f"Using {ATLAS_PATH.relative_to(ROOT)} ({width}x{height})")
    print(f"Generated 9 WAV effects at {SAMPLE_RATE} Hz in {AUDIO_DIR.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
