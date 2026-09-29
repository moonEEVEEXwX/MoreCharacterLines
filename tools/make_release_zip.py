#!/usr/bin/env python3
"""把编译出的 dll/pck + 清单 + 台词 + 文档组装成"解压即装"的发布 zip。

产物：dist/<id>_v<version>.zip，解压后是 `mods/<id>/`：
    <id>/<id>.json      ← 游戏认的清单（来自 mod_manifest.json）
    <id>/<id>.dll       ← 编译产物
    <id>/<id>.pck       ← 台词包（Godot PCK）
    <id>/lines.json     ← 补了 UTF-8 BOM，玩家可以随手改
    <id>/README.md、DESIGN.md、THIRD_PARTY.md、LICENSE

用法：
    python tools/make_release_zip.py                       # 用默认构建产物（bin/ 下的）
    python tools/make_release_zip.py --dll <路径> --pck <路径>
    python tools/make_release_zip.py --expect-version v0.1.1   # tag 与清单版本不一致就失败（CI 发版用）
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
    parser = argparse.ArgumentParser(description="组装发布 zip")
    parser.add_argument("--dll", default=None, help="编译出的 dll（默认 bin/Release/net9.0/<id>.dll）")
    parser.add_argument("--pck", default=None, help="打包好的 pck（默认 bin/<id>.pck）")
    parser.add_argument("--out", default=None, help="输出 zip（默认 dist/<id>_v<version>.zip）")
    parser.add_argument("--expect-version", default=None,
                        help="校验清单里的 version（可带 v 前缀），不一致就失败 —— CI 里传 tag 名")
    args = parser.parse_args()

    manifest_path = REPO / "mod_manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    mod_id = manifest["id"]
    version = manifest["version"]

    if args.expect_version and args.expect_version.lstrip("v") != version:
        print(f"::error::tag {args.expect_version} 与清单里的 version {version} 不一致")
        return 1

    dll = pathlib.Path(args.dll) if args.dll else REPO / "bin" / "Release" / "net9.0" / f"{mod_id}.dll"
    pck = pathlib.Path(args.pck) if args.pck else REPO / "bin" / f"{mod_id}.pck"
    assets_lines = REPO / "assets" / mod_id / "lines.json"

    for path in (dll, pck, assets_lines, manifest_path):
        if not path.is_file():
            print(f"::error::缺少文件 {path}")
            print("提示：先编译（dotnet build / build.ps1）并打包 PCK，再跑这个脚本。")
            return 1

    out = pathlib.Path(args.out) if args.out else REPO / "dist" / f"{mod_id}_v{version}.zip"
    out.parent.mkdir(parents=True, exist_ok=True)

    lines_text = assets_lines.read_text(encoding="utf-8")

    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.write(manifest_path, f"{mod_id}/{mod_id}.json")
        zf.write(dll, f"{mod_id}/{mod_id}.dll")
        zf.write(pck, f"{mod_id}/{mod_id}.pck")
        # 台词文件补 BOM：Windows 记事本 / PowerShell 打开中文才不乱码
        zf.writestr(f"{mod_id}/lines.json", "\ufeff" + lines_text)
        for doc in DOCS:
            zf.write(REPO / doc, f"{mod_id}/{doc}")

    names = zipfile.ZipFile(out).namelist()
    print(f"[ok] {out.relative_to(REPO) if out.is_relative_to(REPO) else out}  "
          f"({out.stat().st_size:,} bytes, {len(names)} 个条目)")
    for name in names:
        print(f"       {name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
