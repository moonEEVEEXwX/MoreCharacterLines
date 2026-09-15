#!/usr/bin/env python3
"""Validate a MoreCharacterLines lines.json file.

Mirrors the mod's own tolerant parser:
  - UTF-8 with or without BOM
  - full-line // comments
  - trailing commas inside arrays/objects

Prints the offending line and a caret on failure, so a typo is easy to spot.

Usage:  python check_lines.py <path-to-lines.json>
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path


def strip_line_comments(text: str) -> str:
    """Replace full-line // comments with empty lines (keeps line numbers)."""
    out = []
    for line in text.split("\n"):
        out.append("" if line.lstrip().startswith("//") else line)
    return "\n".join(out)


def strip_trailing_commas(text: str) -> str:
    """Remove commas directly before ] or } (the mod parser accepts them)."""
    return re.sub(r",(\s*[\]}])", r"\1", text)


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: python check_lines.py <lines.json>")
        return 2

    path = Path(sys.argv[1])
    if not path.is_file():
        print(f"[ERROR] file not found: {path}")
        return 2

    raw = path.read_text(encoding="utf-8-sig")
    cleaned = strip_trailing_commas(strip_line_comments(raw))
    lines = raw.split("\n")

    try:
        data = json.loads(cleaned)
    except json.JSONDecodeError as exc:
        print(f"[ERROR] {path.name} is not valid JSON: {exc.msg}")
        print(f"        at line {exc.lineno}, column {exc.colno}")
        for i in range(max(0, exc.lineno - 3), min(len(lines), exc.lineno + 2)):
            marker = ">>" if i == exc.lineno - 1 else "  "
            print(f"  {marker} {i + 1:4}: {lines[i]}")
        return 1

    if not isinstance(data, dict):
        print("[ERROR] root should be a JSON object")
        return 1

    scenes = {k: v for k, v in data.items() if not k.startswith("_") and isinstance(v, dict)}
    if not scenes:
        print("[ERROR] no scene found (expect e.g. \"rest_site\": { ... })")
        return 1

    summary = []
    for scene_name, scene in scenes.items():
        characters = [k for k, v in scene.items() if not k.startswith("_")]
        pools = 0
        lines_total = 0
        for value in scene.values():
            # 简写形式：{"IRONCLAD": ["...", "..."]} —— 等于只有 normal 池
            if isinstance(value, (list, str)):
                pools += 1
                lines_total += len(value) if isinstance(value, list) else 1
                continue
            if not isinstance(value, dict):
                continue
            for pool_name, pool in value.items():
                if pool_name.startswith("_") or not isinstance(pool, (list, str)):
                    continue
                pools += 1
                lines_total += len(pool) if isinstance(pool, list) else 1
        summary.append(f"{scene_name}: {len(characters)} characters, {pools} pools, {lines_total} lines")

    print(f"[ok] {path.name} valid  ->  " + " | ".join(summary))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
