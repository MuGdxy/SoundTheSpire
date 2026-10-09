"""Merge an sts_roster_catalog export into a locale voice manifest."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("catalog", type=Path)
    parser.add_argument("manifest", type=Path)
    args = parser.parse_args()

    catalog = json.loads(args.catalog.read_text(encoding="utf-8"))
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    rosters = manifest.setdefault("rosters", {})
    roster_overrides = manifest.get("rosterOverrides", {})
    invalidated = 0
    replacements = [
        (monster["text"], monster["matrixText"])
        for monster in manifest.get("monsters", {}).values()
        if monster.get("matrixText")
    ]
    replacements.sort(key=lambda pair: len(pair[0]), reverse=True)

    for key, entry in catalog.items():
        text = entry.get("text", entry.get("Text"))
        for display_name, spoken_name in replacements:
            text = text.replace(display_name, spoken_name)
        roster_ids = key.split("+")
        if roster_ids and all(monster_id.endswith("_RUBY_RAIDER") for monster_id in roster_ids):
            members = text.removeprefix("敌人来袭。").removesuffix("。").replace("劫掠者", "")
            text = f"敌人来袭。劫掠者队伍：{members}。"
        if roster_ids and all(
            monster_id.startswith(("LEAF_SLIME_", "TWIG_SLIME_"))
            for monster_id in roster_ids
        ):
            members = text.removeprefix("敌人来袭。").removesuffix("。").replace("史莱姆", "")
            text = f"敌人来袭。史莱姆队伍：{members}。"
        text = roster_overrides.get(key, text)
        old = rosters.get(key)
        if old and old.get("text") != text:
            old_audio = args.manifest.parent / old["file"]
            if old_audio.exists():
                old_audio.unlink()
                invalidated += 1
        roster_line = {
            "file": entry.get("file", entry.get("File")),
            "text": text,
        }
        if "咔咔" in text:
            roster_line["instruction"] = (
                "成熟冷静的女性旁白，标准中国大陆普通话。"
                "咔咔两个字，第一个咔读第一声 kā，第二个咔读第四声 kà。"
                "逐字清晰，语速从容，完整读出句末。"
            )
        if "蛆蛆" in text:
            roster_line["instruction"] = (
                "成熟冷静的女性旁白，标准中国大陆普通话。"
                "蛆蛆两个字都读第一声 jū，每个字的元音都稍微拉长，"
                "两个字都要清楚完整，第二个字不要读成轻声。"
            )
        if "四漫" in text:
            roster_line["instruction"] = (
                "成熟冷静的女性旁白，标准中国大陆普通话。"
                "四漫两个字必须连续读出，中间不要停顿，"
                "吐字清晰，从容但不要拆开成两个词。"
            )
        if "史莱姆队伍" in text:
            roster_line["instruction"] = (
                "成熟冷静的女性旁白，标准中国大陆普通话。"
                "清楚、完整地读出整句，尤其必须完整读出最后一个词的最后一个字，"
                "句末不得吞音、弱化或提前收尾。"
            )
        rosters[key] = roster_line

    manifest["rosters"] = dict(sorted(rosters.items()))
    args.manifest.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"catalog={len(catalog)} total={len(rosters)} invalidated={invalidated}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
