"""Merge an sts_card_voice_catalog export into a locale voice manifest."""

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
    cards = manifest.setdefault("cards", {})

    added = 0
    for card_id, localized in catalog.items():
        old = cards.get(card_id, {})
        cards[card_id] = {
            "file": localized["file"],
            "text": old.get("spokenText", localized["text"]),
            "displayText": localized["text"],
        }
        if not old:
            added += 1

    manifest["cards"] = dict(sorted(cards.items()))
    args.manifest.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"catalog={len(catalog)} added={added} total={len(cards)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
