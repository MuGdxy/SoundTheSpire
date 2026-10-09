"""Generate every missing OGG referenced by a voice manifest with CosyVoice 3.

CosyVoice itself and its model are intentionally not bundled with the mod. See docs/VOICE-ANNOUNCER.md.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
from pathlib import Path


DEFAULT_INSTRUCTION = (
    "A mature female narrator with a calm, mysterious, slightly dangerous voice. "
    "She belongs in a dark-fantasy roguelike card game: restrained, atmospheric, confident, and never cheerful. "
    "Speak only Standard Mainland Mandarin Chinese (Putonghua), with a native neutral Mandarin accent. "
    "Never use Korean, Japanese, Cantonese, or a foreign accent. "
    "Use a deliberate unhurried pace. Pronounce every Mandarin syllable distinctly and leave natural separation "
    "between words. Use subtle dramatic emphasis and clean endings without swallowing the final syllable. "
    "Avoid esports hype, shouting, exaggerated heroism, and promotional delivery. "
    "Do not imitate any existing actor or character.<|endofprompt|>"
)


def voice_lines(manifest: dict, include_matrix: bool = False) -> list[dict]:
    lines: list[dict] = []
    lines.extend(value for value in manifest.get("common", {}).values() if value)
    for section in ("encounters", "rosters", "monsters", "cards", "counts"):
        lines.extend(manifest.get(section, {}).values())
    status = manifest.get("status", {})
    lines.extend(status.get("parts", {}).values())
    lines.extend(status.get("powers", {}).values())
    if include_matrix:
        lines.extend(matrix_lines(manifest))
    return lines


def matrix_lines(manifest: dict) -> list[dict]:
    status = manifest.get("status", {})
    powers = status.get("powers", {})
    buff_powers = set(status.get("buffPowers", []))
    directory = status.get("matrixDirectory", "status/matrix").rstrip("/\\")
    targets = {
        "player": (
            status.get("matrixPlayerStartText", "你现在"),
            status.get("matrixPlayerEndText", "你的"),
        )
    }
    targets.update(
        {
            monster_id: (
                line.get("matrixText", line["text"]),
                line.get("matrixEndText", line.get("matrixText", line["text"]) + "的"),
            )
            for monster_id, line in manifest.get("monsters", {}).items()
        }
    )

    lines: list[dict] = []
    for target_id, (start_target, end_target) in targets.items():
        for power_id, power in powers.items():
            power_text = power.get("matrixText", power["text"])
            verb = "获得" if power_id in buff_powers else "受到"
            base = f"{directory}/{target_id.lower()}/{power_id.lower()}"
            lines.append({"file": f"{base}_started.ogg", "text": f"{start_target}{verb}{power_text}"})
            lines.append({"file": f"{base}_ended.ogg", "text": f"{end_target}{power_text}结束"})
    return lines


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--cosyvoice-root", type=Path, help="Clone of FunAudioLLM/CosyVoice")
    parser.add_argument("--prompt-wav", type=Path, help="Optional legally licensed reference voice recording")
    parser.add_argument("--model", default="iic/CosyVoice-300M-Instruct")
    parser.add_argument("--speaker", default="中文女", help="Built-in speaker used when --prompt-wav is omitted")
    parser.add_argument("--speed", type=float, default=0.82, help="Synthesis speed; values below 1 are slower")
    parser.add_argument("--language-token", default="<|zh|>", help="CosyVoice language token; empty disables it")
    parser.add_argument("--instruction", default=DEFAULT_INSTRUCTION)
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--matrix", action="store_true", help="Generate full target × status sentences")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    manifest_path = args.manifest.resolve()
    data = json.loads(manifest_path.read_text(encoding="utf-8"))
    pending = [
        (line["text"], manifest_path.parent / line["file"])
        for line in voice_lines(data, include_matrix=args.matrix)
        if args.force or not (manifest_path.parent / line["file"]).exists()
    ]

    for text, output in pending:
        print(f"{text} -> {output}")
    if args.dry_run or not pending:
        return 0
    if args.cosyvoice_root is None:
        raise SystemExit("--cosyvoice-root is required when generating audio")

    root = args.cosyvoice_root.resolve()
    sys.path.insert(0, str(root))
    sys.path.insert(0, str(root / "third_party" / "Matcha-TTS"))

    import torch
    import torchaudio
    from cosyvoice.cli.cosyvoice import AutoModel

    model = AutoModel(model_dir=args.model)
    prompt = str(args.prompt_wav.resolve()) if args.prompt_wav else None
    with tempfile.TemporaryDirectory(prefix="sound-the-spire-voice-") as temp:
        temp_dir = Path(temp)
        for index, (text, output) in enumerate(pending):
            synthesis_text = text if text.endswith(("。", "！", "？", ".", "!", "?")) else text + "。"
            if args.language_token:
                synthesis_text = args.language_token + synthesis_text
            if prompt:
                results = model.inference_instruct2(
                    synthesis_text, args.instruction, prompt, stream=False, speed=args.speed
                )
            else:
                results = model.inference_instruct(
                    synthesis_text, args.speaker, args.instruction, stream=False, speed=args.speed
                )
            chunks = [result["tts_speech"] for result in results]
            if not chunks:
                raise RuntimeError(f"CosyVoice produced no audio for {text!r}")

            wav = temp_dir / f"{index}.wav"
            torchaudio.save(str(wav), torch.cat(chunks, dim=1), model.sample_rate)
            output.parent.mkdir(parents=True, exist_ok=True)
            subprocess.run(
                [
                    "ffmpeg",
                    "-y",
                    "-i",
                    str(wav),
                    "-af",
                    (
                        "silenceremove=start_periods=1:start_duration=0.03:start_threshold=-45dB,"
                        "loudnorm=I=-16:TP=-1.5:LRA=7,"
                        "apad=pad_dur=0.15"
                    ),
                    "-ar",
                    "48000",
                    "-ac",
                    "1",
                    "-c:a",
                    "libvorbis",
                    "-q:a",
                    "5",
                    str(output),
                ],
                check=True,
            )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
