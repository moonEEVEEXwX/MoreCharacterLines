#!/usr/bin/env python3
"""Validate a MoreCharacterLines lines.json file.

Mirrors the mod's own tolerant parser:
  - UTF-8 with or without BOM
  - full-line // comments
  - trailing commas inside arrays/objects

Prints the offending line and a caret on failure, so a typo is easy to spot.

Besides syntax it also enforces two content rules that are easy to get wrong:

  1. ping（多人催促）气泡 = 角色**说出口的话** —— 里面不许出现旁白括号（）；
     火堆那套「（）= 旁白」只适用于内心独白（rest_site）。
  2. ping 是**对着队伍**说的：多人局是 2~4 人，所以别用单指称呼「你」
     （「你们」可以）。写成警告而不是错误，因为这条比较像口味。
  3. BBCode 标签只在 ping 气泡里有效（气泡是 MegaRichTextLabel）；
     火堆顶栏是 MegaLabel，写了 BBCode 会**原样显示**。

Usage:  python check_lines.py <path-to-lines.json>
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

#: 旁白括号：ping 里出现就是错的（火堆里是合法写法）
NARRATION_PARENS = "（）"

#: 单指称呼「你」：多人局是 2~4 人（还有去掉人数上限的 mod），催促使不该只对着一个人说。
#: 「你们」是复数、没问题；「你好」是打招呼，也放过。
SINGULAR_YOU_RE = re.compile(r"你(?!们|好)")

#: 允许出现在 ping 气泡里的 BBCode 标签。
#: 来源：游戏自己用过的（sine / jitter / shake / rainbow）+ Godot 4 RichTextLabel 内置效果
#: （wave / pulse / tornado / fade）+ 气泡自身用的排版标签。
KNOWN_BBCODE = {
    # 文字动效
    "wave", "shake", "pulse", "tornado", "fade", "rainbow", "sine", "jitter", "fly_in", "char",
    # 排版 / 样式
    "center", "left", "right", "fill", "indent", "i", "b", "u", "s", "code", "p", "br", "hr",
    "font", "font_size", "color", "fgcolor", "bgcolor", "outline_size", "outline_color",
    "table", "cell", "list", "ol", "ul", "img", "url", "hint", "tooltip", "lang", "kbd",
    # 具名颜色简写：游戏文本里到处都是（[red] / [gold] / [blue] / [purple] …），
    # 气泡是同一个 RichTextLabel 家族，所以照样能用
    "black", "white", "red", "green", "blue", "yellow", "cyan", "magenta", "gray", "grey",
    "orange", "purple", "pink", "lime", "brown", "olive", "navy", "teal", "maroon", "aqua",
    "fuchsia", "silver", "gold", "darkred", "darkgreen", "darkblue", "darkgray", "darkgrey",
    "lightblue", "lightgreen", "lightgray", "lightgrey", "transparent",
}

TAG_RE = re.compile(r"\[/?(?P<name>[a-zA-Z_][a-zA-Z0-9_]*)(?:[ =][^\]]*)?\]")


def strip_line_comments(text: str) -> str:
    """Replace full-line // comments with empty lines (keeps line numbers)."""
    out = []
    for line in text.split("\n"):
        out.append("" if line.lstrip().startswith("//") else line)
    return "\n".join(out)


def strip_trailing_commas(text: str) -> str:
    """Remove commas directly before ] or } (the mod parser accepts them)."""
    return re.sub(r",(\s*[\]}])", r"\1", text)


def iter_lines(value):
    """Yield every line string of a character entry (list / str / {pool: ...})."""
    if isinstance(value, str):
        yield "(normal)", value
        return
    if isinstance(value, list):
        for item in value:
            if isinstance(item, str):
                yield "(normal)", item
        return
    if isinstance(value, dict):
        for pool_name, pool in value.items():
            if pool_name.startswith("_"):
                continue
            if isinstance(pool, str):
                yield pool_name, pool
            elif isinstance(pool, list):
                for item in pool:
                    if isinstance(item, str):
                        yield pool_name, item


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
    problems: list[str] = []
    warnings: list[str] = []

    for scene_name, scene in scenes.items():
        characters = [k for k, v in scene.items() if not k.startswith("_")]
        pools = 0
        lines_total = 0
        for character, value in scene.items():
            if not character.startswith("_"):
                for pool_name, text in iter_lines(value):
                    # 1) ping 气泡是「说出口的话」：旁白括号是错的
                    if scene_name == "ping":
                        hit = next((p for p in NARRATION_PARENS if p in text), None)
                        if hit:
                            problems.append(
                                f"ping/{character}/{pool_name}: 出现旁白括号「{hit}」——"
                                f"气泡是角色说出口的话，旁白括号只用于火堆内心独白：{text}"
                            )

                        # 1.5) 多人局是 2~4 人（还有去上限的 mod），别用单指称呼「你」
                        if SINGULAR_YOU_RE.search(text):
                            warnings.append(
                                f"ping/{character}/{pool_name}: 出现单指称呼「你」——"
                                f"多人局是 2~4 人，改成不点名的说法（或「你们」）：{text}"
                            )

                    # 2) BBCode 只在 ping 气泡里生效
                    for tag in {m.group("name").lower() for m in TAG_RE.finditer(text)}:
                        if scene_name != "ping":
                            warnings.append(
                                f"{scene_name}/{character}/{pool_name}: 含 BBCode [{tag}]，"
                                f"但这条标签不吃 BBCode（会原样显示）：{text}"
                            )
                        elif tag not in KNOWN_BBCODE:
                            warnings.append(
                                f"ping/{character}/{pool_name}: 未知 BBCode 标签 [{tag}]，"
                                f"写错会在气泡里原样显示：{text}"
                            )

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

    for message in warnings:
        print(f"[warn] {message}")
    for message in problems:
        print(f"[ERROR] {message}")

    if problems:
        print(f"\n[FAIL] {path.name}: {len(problems)} 处内容问题（见上）")
        return 1

    print(f"[ok] {path.name} valid  ->  " + " | ".join(summary))
    if warnings:
        print(f"     （另有 {len(warnings)} 条提示，不影响构建）")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
