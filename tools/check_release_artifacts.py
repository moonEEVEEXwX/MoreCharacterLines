#!/usr/bin/env python3
"""校验 release\\ 里的构建产物没被手工替换过，并且能组装成发布包。

CI 用（见 .github/workflows/ci.yml）：本地只有 .NET SDK、没有游戏 DLL，编译不了，
所以二进制是"本地 build.ps1 构建后提交"的 —— 这个脚本就是那道防线：
对照 build.ps1 写出的 release/ARTIFACTS.txt，核对 dll/pck 的 SHA256 与字节数。

用法：python tools/check_release_artifacts.py
"""
from __future__ import annotations

import hashlib
import json
import pathlib
import sys

REPO = pathlib.Path(__file__).resolve().parent.parent


def main() -> int:
    manifest = json.loads((REPO / "mod_manifest.json").read_text(encoding="utf-8"))
    mod_id = manifest["id"]
    errors: list[str] = []

    fingerprint = REPO / "release" / "ARTIFACTS.txt"
    if not fingerprint.is_file():
        print("::error::缺少 release/ARTIFACTS.txt（本地跑一次 build.ps1 会生成）")
        return 1

    seen: set[str] = set()
    for line in fingerprint.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        fields = line.split()
        name, attrs = fields[0], dict(p.split("=", 1) for p in fields[1:] if "=" in p)
        seen.add(name)
        path = REPO / "release" / name
        if not path.is_file():
            errors.append(f"缺少 release/{name}")
            continue
        data = path.read_bytes()
        actual_sha = hashlib.sha256(data).hexdigest()
        if actual_sha.lower() != attrs.get("sha256", "").lower():
            errors.append(f"release/{name} 的 SHA256 与 ARTIFACTS.txt 不符"
                          f"（期望 {attrs.get('sha256')}，实际 {actual_sha}）—— 二进制被手工改过？重跑 build.ps1")
        if str(len(data)) != attrs.get("bytes"):
            errors.append(f"release/{name} 字节数与 ARTIFACTS.txt 不符"
                          f"（期望 {attrs.get('bytes')}，实际 {len(data)}）")

    for expected in (f"{mod_id}.dll", f"{mod_id}.pck"):
        if expected not in seen:
            errors.append(f"ARTIFACTS.txt 里没有记录 {expected}")

    if errors:
        for e in errors:
            print(f"::error::{e}")
        return 1

    print(f"release/ 产物核对通过：{', '.join(sorted(seen))}（id={mod_id} version={manifest['version']}）")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
