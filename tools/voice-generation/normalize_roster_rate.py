"""Normalize pauses and speaking-rate spread across generated roster sentences."""

from __future__ import annotations

import argparse
import datetime
import json
import re
import shutil
import statistics
import subprocess
import tempfile
from pathlib import Path

import soundfile as sf


def units(text: str) -> int:
    return len(re.findall(r"[\u4e00-\u9fff]|[A-Za-z]+|\d+", text))


def run(*args: str) -> None:
    subprocess.run(args, check=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--section", choices=("rosters", "cards"), default="rosters")
    parser.add_argument("--backup", type=Path)
    parser.add_argument("--min-tempo", type=float, default=0.80)
    parser.add_argument("--max-tempo", type=float, default=1.30)
    parser.add_argument("--target-rate", type=float)
    parser.add_argument("--only", nargs="*", help="Optional roster file stems to process")
    parser.add_argument("--modified-after", help="Only process files modified after this ISO-8601 timestamp")
    args = parser.parse_args()

    manifest_path = args.manifest.resolve()
    root = manifest_path.parent
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    modified_after = (
        datetime.datetime.fromisoformat(args.modified_after.replace("Z", "+00:00")).timestamp()
        if args.modified_after
        else None
    )
    entries = [
        (root / line["file"], line["text"])
        for line in manifest[args.section].values()
        if (root / line["file"]).exists()
        and (not args.only or (root / line["file"]).stem in args.only)
        and (modified_after is None or (root / line["file"]).stat().st_mtime >= modified_after)
    ]
    if args.backup:
        backup = args.backup.resolve()
        backup.mkdir(parents=True, exist_ok=True)
        for path, _ in entries:
            shutil.copy2(path, backup / path.name)

    with tempfile.TemporaryDirectory(prefix="sound-the-spire-rate-") as temp:
        temp_dir = Path(temp)
        analyzed: list[tuple[Path, Path, str, float]] = []
        for index, (source, text) in enumerate(entries):
            wave = temp_dir / f"{index}.wav"
            run(
                "ffmpeg", "-y", "-loglevel", "error", "-i", str(source),
                "-af",
                "silenceremove=stop_periods=-1:stop_duration=0.25:"
                "stop_threshold=-50dB:stop_silence=0.16",
                "-ar", "48000", "-ac", "1", str(wave),
            )
            info = sf.info(wave)
            rate = units(text) / max(info.duration - 0.16, 0.1)
            analyzed.append((source, wave, text, rate))

        target = args.target_rate or statistics.median(item[3] for item in analyzed)
        before = sorted(item[3] for item in analyzed)
        tempos: list[float] = []
        for source, wave, _, rate in analyzed:
            tempo = max(args.min_tempo, min(args.max_tempo, target / rate))
            tempos.append(tempo)
            output = temp_dir / f"{source.stem}.ogg"
            run(
                "ffmpeg", "-y", "-loglevel", "error", "-i", str(wave),
                "-af",
                f"atempo={tempo:.6f},loudnorm=I=-16:TP=-1.5:LRA=7,"
                "apad=pad_dur=0.25",
                "-ar", "48000", "-ac", "1", "-c:a", "libvorbis", "-q:a", "5",
                str(output),
            )
            output.replace(source)

    after = sorted(rate * tempo for (_, _, _, rate), tempo in zip(analyzed, tempos))
    count = len(before)
    print(
        f"count={count} target={target:.2f} chars/s "
        f"before={before[0]:.2f}/{statistics.median(before):.2f}/{before[-1]:.2f} "
        f"after={after[0]:.2f}/{statistics.median(after):.2f}/{after[-1]:.2f}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
