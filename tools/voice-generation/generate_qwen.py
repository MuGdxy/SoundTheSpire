"""Generate manifest voice assets with Qwen3-TTS CustomVoice."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

from generate import voice_lines


DEFAULT_INSTRUCTION = (
    "成熟冷静的女性旁白，标准中国大陆普通话。声音神秘、克制、略带危险感，"
    "符合黑暗奇幻牌组构筑游戏；逐字清晰，语速从容，完整读出句末，"
    "不要电竞腔、广告腔、日韩口音或夸张喊叫。"
)

CARD_INSTRUCTION = (
    "标准中国大陆普通话女性系统语音，像屏幕阅读器逐项读取名称。"
    "完全平淡、没有情感、没有表演，音高稳定、起伏极小、没有重音，"
    "不要拉长或加入戏剧停顿。语速较快，清晰读出卡牌名称。"
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--model", default="Qwen/Qwen3-TTS-12Hz-1.7B-CustomVoice")
    parser.add_argument("--speaker", default="Serena")
    parser.add_argument("--instruction")
    parser.add_argument("--matrix", action="store_true")
    parser.add_argument("--rosters-only", action="store_true")
    parser.add_argument("--cards-only", action="store_true")
    parser.add_argument("--file", help="Only generate the manifest entry with this relative file path")
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--limit", type=int, help="Only process the first N pending lines")
    parser.add_argument("--sampling", action="store_true", help="Use seeded sampling instead of deterministic decoding")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    manifest_path = args.manifest.resolve()
    data = json.loads(manifest_path.read_text(encoding="utf-8"))
    default_instruction = args.instruction or (CARD_INSTRUCTION if args.cards_only else DEFAULT_INSTRUCTION)
    if args.rosters_only:
        lines = data.get("rosters", {}).values()
    elif args.cards_only:
        lines = data.get("cards", {}).values()
    else:
        lines = voice_lines(data, include_matrix=args.matrix)
    if args.file:
        lines = (line for line in lines if line.get("file") == args.file)
    pending = [
        (line["text"], manifest_path.parent / line["file"], line.get("instruction", default_instruction))
        for line in lines
        if args.force or not (manifest_path.parent / line["file"]).exists()
    ]
    if args.limit is not None:
        pending = pending[:max(args.limit, 0)]
    for text, output, _ in pending:
        print(f"{text} -> {output}")
    if args.dry_run or not pending:
        return 0

    import soundfile as sf
    import torch
    from qwen_tts import Qwen3TTSModel

    model = Qwen3TTSModel.from_pretrained(
        args.model,
        device_map="cuda:0",
        dtype=torch.bfloat16,
    )
    with tempfile.TemporaryDirectory(prefix="sound-the-spire-qwen-") as temp:
        temp_dir = Path(temp)
        for index, (text, output, instruction) in enumerate(pending):
            synthesis_text = text if text.endswith(("。", "！", "？", ".", "!", "?")) else text + "。"
            text_units = len(re.findall(r"[\u4e00-\u9fff]|[A-Za-z]+|\d+", text))
            max_tokens = max(64, min(512, text_units * 12))
            base_seed = int(hashlib.sha256(str(output).encode()).hexdigest()[:8], 16)

            def synthesize(sampling: bool, seed: int):
                torch.manual_seed(seed)
                torch.cuda.manual_seed_all(seed)
                return model.generate_custom_voice(
                    text=synthesis_text,
                    language="Chinese",
                    speaker=args.speaker,
                    instruct=instruction,
                    max_new_tokens=max_tokens,
                    do_sample=sampling,
                    subtalker_dosample=sampling,
                )

            wavs, sample_rate = synthesize(args.sampling, base_seed)
            expected_max_seconds = max(5.0, text_units * 0.7 + 2.0)
            if not args.sampling and len(wavs[0]) / sample_rate > expected_max_seconds:
                print(f"deterministic runaway; retrying with seeded sampling: {text}", flush=True)
                wavs, sample_rate = synthesize(True, base_seed)
            wav = temp_dir / f"{index}.wav"
            sf.write(wav, wavs[0], sample_rate)
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
