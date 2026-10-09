"""Merge an sts_voice_catalog export into a locale voice manifest."""

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
    monsters = manifest.setdefault("monsters", {})

    added = 0
    for monster_id, localized in catalog.items():
        if monster_id not in monsters:
            monsters[monster_id] = localized
            added += 1
            continue
        # The game is authoritative for file identity and spoken localized name.
        # Keep optional authoring overrides such as matrixText.
        monsters[monster_id]["file"] = localized["file"]
        monsters[monster_id]["text"] = localized["text"]

    manifest["monsters"] = dict(sorted(monsters.items()))
    args.manifest.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"catalog={len(catalog)} added={added} total={len(monsters)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
