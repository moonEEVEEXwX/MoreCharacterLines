#!/usr/bin/env python3
"""把仓库里的东西组装成"可直接安装"的发布 zip —— 本机和 GitHub Actions 共用。

产物：dist/<id>_v<version>.zip
内容：
    MoreCharacterLines/MoreCharacterLines.json   ← mod_manifest.json（游戏按 <id>.json 找清单）
    MoreCharacterLines/MoreCharacterLines.dll    ← release/ 里的构建产物
    MoreCharacterLines/MoreCharacterLines.pck    ← release/ 里的构建产物
    MoreCharacterLines/lines.json                ← assets/ 里那份（补 UTF-8 BOM，方便记事本改）
    MoreCharacterLines/README.md / DESIGN.md / THIRD_PARTY.md / LICENSE

用法：
    python tools/make_release_zip.py
    python tools/make_release_zip.py --expect-version v0.1.0   # tag 与清单版本不一致就报错（CI 用）
"""
from __future__ import annotations

import argparse
import json
import pathlib
import sys
import zipfile

REPO = pathlib.Path(__file__).resolve().parent.parent
DOCS = ("README.md", "DESIGN.md", "THIRD_PARTY.md", "LICENSE")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--expect-version", default=None,
                        help="校验清单版本（可带 v 前缀），不一致就失败 —— CI 里传 tag 名")
    args = parser.parse_args()

    manifest_path = REPO / "mod_manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    mod_id = manifest["id"]
    version = manifest["version"]

    if args.expect_version and args.expect_version.lstrip("v") != version:
        print(f"::error::tag {args.expect_version} 与清单里的 version {version} 不一致")
        return 1

    required = [
        REPO / "release" / f"{mod_id}.dll",
        REPO / "release" / f"{mod_id}.pck",
        REPO / "assets" / mod_id / "lines.json",
        manifest_path,
    ] + [REPO / d for d in DOCS]

    missing = [str(p.relative_to(REPO)) for p in required if not p.is_file()]
    if missing:
        for m in missing:
            print(f"::error::缺少文件 {m}")
        print("提示：release/ 里的 dll/pck 是构建产物，本地跑一次 build.ps1 就会生成/刷新。")
        return 1

    lines_text = (REPO / "assets" / mod_id / "lines.json").read_text(encoding="utf-8")

    out = REPO / "dist" / f"{mod_id}_v{version}.zip"
    out.parent.mkdir(parents=True, exist_ok=True)

    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.write(manifest_path, f"{mod_id}/{mod_id}.json")
        zf.write(REPO / "release" / f"{mod_id}.dll", f"{mod_id}/{mod_id}.dll")
        zf.write(REPO / "release" / f"{mod_id}.pck", f"{mod_id}/{mod_id}.pck")
        # 台词文件补 BOM：Windows 记事本 / PowerShell 打开中文才不乱码
        zf.writestr(f"{mod_id}/lines.json", "\ufeff" + lines_text)
        for doc in DOCS:
            zf.write(REPO / doc, f"{mod_id}/{doc}")

    names = zipfile.ZipFile(out).namelist()
    print(f"[ok] {out.relative_to(REPO)}  ({out.stat().st_size:,} bytes, {len(names)} 个条目)")
    for name in names:
        print(f"       {name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
