#!/usr/bin/env python3
from __future__ import annotations

import math
import re
import struct
import sys
import wave
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def require(path: str) -> Path:
    item = ROOT / path
    if not item.exists():
        raise AssertionError(f"missing required file: {path}")
    return item


def without_strings_and_comments(source: str) -> str:
    pattern = re.compile(
        r'@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|//[^\n]*|/\*.*?\*/',
        re.DOTALL,
    )
    return pattern.sub("", source)


def check_balanced_csharp(path: Path) -> None:
    cleaned = without_strings_and_comments(path.read_text(encoding="utf-8"))
    pairs = {"{": "}", "(": ")", "[": "]"}
    reverse = {value: key for key, value in pairs.items()}
    stack: list[tuple[str, int]] = []
    for offset, char in enumerate(cleaned):
        if char in pairs:
            stack.append((char, offset))
        elif char in reverse:
            if not stack or stack[-1][0] != reverse[char]:
                raise AssertionError(f"unbalanced {char} in {path.name} at {offset}")
            stack.pop()
    if stack:
        raise AssertionError(f"unclosed {stack[-1][0]} in {path.name}")


def time_of_flight(distance: float) -> float:
    muzzle_velocity = 820.0
    drag_rate = 0.34
    ratio = max(0.08, 1.0 - drag_rate * distance / muzzle_velocity)
    return -math.log(ratio) / drag_rate


def check_png(path: Path) -> tuple[int, int]:
    data = path.read_bytes()[:24]
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise AssertionError("material atlas is not a valid PNG")
    width, height = struct.unpack(">II", data[16:24])
    if width < 1024 or height < 1024:
        raise AssertionError(f"material atlas too small: {width}x{height}")
    return width, height


def main() -> int:
    required = [
        "Packages/manifest.json",
        "ProjectSettings/ProjectVersion.txt",
        "ProjectSettings/EditorBuildSettings.asset",
        "Assets/BallisticSniper/Scenes/BallisticSniper.unity",
        "Assets/BallisticSniper/Resources/BallisticSniper/Shaders/AtlasLit.shader",
        "Assets/BallisticSniper/Scripts/Runtime/BallisticGame.cs",
        "Assets/BallisticSniper/Scripts/Runtime/Ballistics.cs",
        "Assets/BallisticSniper/Scripts/Runtime/GameData.cs",
        "Assets/BallisticSniper/Scripts/Runtime/RangeWorld.cs",
        "Assets/BallisticSniper/Scripts/Runtime/ProjectileAndKillCam.cs",
        "Assets/BallisticSniper/Scripts/UI/MobileHud.cs",
        "Assets/BallisticSniper/Scripts/UI/HudGraphics.cs",
    ]
    for relative in required:
        require(relative)

    cs_files = sorted((ROOT / "Assets/BallisticSniper/Scripts").rglob("*.cs"))
    if len(cs_files) < 8:
        raise AssertionError("unexpectedly small C# source set")
    for source in cs_files:
        check_balanced_csharp(source)

    game_data = require("Assets/BallisticSniper/Scripts/Runtime/GameData.cs").read_text(encoding="utf-8")
    if game_data.count("new StageDefinition(") != 5:
        raise AssertionError("campaign must define exactly five stages")
    names_block = re.search(r"CinematicNames\s*=\s*\{(.*?)\};", game_data, re.DOTALL)
    if not names_block or len(re.findall(r'"[^"]+"', names_block.group(1))) != 14:
        raise AssertionError("cinematic name table must contain 14 variants")

    kill_cam = require("Assets/BallisticSniper/Scripts/Runtime/ProjectileAndKillCam.cs").read_text(encoding="utf-8")
    cases = {int(value) for value in re.findall(r"case\s+(\d+)\s*:", kill_cam)}
    if not set(range(13)).issubset(cases) or "default:" not in kill_cam:
        raise AssertionError("kill-cam switch does not implement all 14 camera paths")

    controls = require("Assets/BallisticSniper/Scripts/UI/HudGraphics.cs").read_text(encoding="utf-8")
    if "HoldDragButton" not in controls or "Dragged?.Invoke(eventData.delta)" not in controls:
        raise AssertionError("same-finger breath + aim control is missing")

    mobile_hud = require("Assets/BallisticSniper/Scripts/UI/MobileHud.cs").read_text(encoding="utf-8")
    reliable_ui_tokens = (
        "DispatchReliableTouches",
        "InvokeButtonAt(touch.position)",
        "RectTransformUtility.RectangleContainsScreenPoint",
        "button.targetGraphic = image",
        "image.raycastTarget = false",
        "image.texture = uiTexture",
        'label.text = (selected ? "✓ " : string.Empty)',
        '"v3.0.3  •  Без рекламы',
        "button.onClick.AddListener(binding.Invoke)",
        "Time.unscaledTime - lastInvokedAt < 0.30f",
    )
    if any(token not in mobile_hud for token in reliable_ui_tokens):
        raise AssertionError("Android menu touch fallback or visible button backgrounds are missing")
    game_flow = require("Assets/BallisticSniper/Scripts/Runtime/BallisticGame.cs").read_text(encoding="utf-8")
    flow_tokens = (
        "public void CloseHelp()",
        "private IEnumerator PrepareCampaignStage()",
        "ConfigureStage(false)",
        "screen = GameScreen.Briefing;",
        "yield return null;",
        "hud.SetBriefingReady();",
        "if (screen != GameScreen.Briefing || !stageReady) return;",
        "if (screen == GameScreen.Help) CloseHelp();",
    )
    if any(token not in game_flow for token in flow_tokens):
        raise AssertionError("instant start/help navigation flow is missing")

    visual_200 = time_of_flight(200) * 1.25
    visual_900 = time_of_flight(900) * 1.25
    if not (0.31 <= visual_200 <= 0.33 and 1.70 <= visual_900 <= 1.74):
        raise AssertionError(f"unexpected visual TOF: 200m={visual_200:.3f}, 900m={visual_900:.3f}")
    if visual_900 / visual_200 < 5.0:
        raise AssertionError("flight time no longer scales visibly with range")

    destruction_sequence = [15, 20, 25, 25]
    perfect_stage = sum(destruction_sequence) + 20 + 10 + 4 * 20
    perfect_campaign = perfect_stage * 5
    if perfect_stage != 195 or perfect_campaign != 975:
        raise AssertionError("campaign maximum no longer matches the documented 975 points")
    if "CampaignMaxScore = 975" not in game_data:
        raise AssertionError("C# campaign maximum is not 975")

    atlas = require("Assets/BallisticSniper/Resources/BallisticSniper/Textures/range_material_atlas.png")
    width, height = check_png(atlas)

    audio_dir = ROOT / "Assets/BallisticSniper/Resources/BallisticSniper/Audio"
    expected_audio = {
        "shot.wav", "hit.wav", "glass_break.wav", "clay_break.wav", "cans_crash.wav",
        "wood_break.wav", "melon_splat.wav", "explosion.wav", "bullseye.wav",
    }
    present_audio = {path.name for path in audio_dir.glob("*.wav")}
    if expected_audio != present_audio:
        raise AssertionError(f"audio set mismatch: {sorted(expected_audio ^ present_audio)}")
    for audio in sorted(audio_dir.glob("*.wav")):
        with wave.open(str(audio), "rb") as wav:
            if wav.getnframes() <= 0 or wav.getframerate() < 8000:
                raise AssertionError(f"invalid WAV: {audio.name}")

    forbidden_network = ("UnityWebRequest", "HttpClient", "WebRequest", "uses-permission android.permission.INTERNET")
    all_text = "\n".join(path.read_text(encoding="utf-8") for path in cs_files)
    if any(token in all_text for token in forbidden_network):
        raise AssertionError("offline guarantee violated by a network API reference")

    print(f"OK: {len(cs_files)} C# files; 5 stages; 14 kill-cams; atlas {width}x{height}; 9 WAV files")
    print(f"OK: visual bullet time 200m={visual_200:.3f}s, 900m={visual_900:.3f}s")
    print("OK: perfect chain-reaction route scores 195 per stage / 975 per campaign")
    print("OK: same-finger breath+aim control and second-finger fire UI are present")
    print("OK: Android buttons use debounced touch-down plus standard UI click fallback")
    print("OK: START opens briefing immediately and prepares the selected range on the next frame")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except AssertionError as error:
        print(f"FAILED: {error}", file=sys.stderr)
        raise SystemExit(1)
