# release/ —— 构建产物（给 CI 打包用）

这个目录只放**编译好的两个文件**，源码在仓库根目录：

| 文件 | 是什么 | 怎么来的 |
|---|---|---|
| `MoreCharacterLines.dll` | mod 逻辑（C# 编译产物） | 本地 `build.ps1`（用游戏目录的 DLL 作引用编译） |
| `MoreCharacterLines.pck` | 出厂台词包（Godot PCK） | 同一个 `build.ps1`（把 `assets/MoreCharacterLines/` 打包） |
| `ARTIFACTS.txt` | 上面两个文件的 SHA256 / 字节数 | `build.ps1` 顺带写出来的，**别手改** |

**为什么要提交二进制**：编译需要游戏本体里的 `sts2.dll` / `GodotSharp.dll` / `0Harmony.dll`，
它们不能进公开仓库（版权），所以 CI 无法现场编译。把产物提交进来后，
**GitHub Actions 就能在打 tag 时自动组装发布包并创建 Release**（`.github/workflows/release.yml`）。

- 安装：把 `MoreCharacterLines.dll` + `.pck` + 仓库根的 `mod_manifest.json`（重命名为 `MoreCharacterLines.json`）
  和 `assets/MoreCharacterLines/lines.json` 放进 `mods/MoreCharacterLines/`；或者直接下 Release 里打好的 zip。
- `build.ps1` 每次构建都会自动刷新这两个文件；改了代码记得连它们一起提交。
- `ARTIFACTS.txt` 是防呆用的：CI 拿它核对 `release/` 里的二进制确实出自 `build.ps1`
  （`python tools/check_release_artifacts.py`），防止二进制被手工替换却没更新指纹。
  所以重跑 `build.ps1` 之后，`ARTIFACTS.txt` 的变化也要一起提交。