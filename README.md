# 更多角色台词 (MoreCharacterLines)

[![Release](https://img.shields.io/github/v/release/moonEEVEEXwX/MoreCharacterLines?label=release&color=green)](https://github.com/moonEEVEEXwX/MoreCharacterLines/releases/latest)
[![License: GPL-3.0](https://img.shields.io/badge/License-GPL--3.0-blue.svg)](LICENSE)
[![Game](https://img.shields.io/badge/Slay%20the%20Spire%202-v0.111.0-orange.svg)](#兼容性与边界)
[![Mod type](https://img.shields.io/badge/mod-pure%20cosmetic-lightgrey.svg)](#兼容性与边界)
[![CI](https://github.com/moonEEVEEXwX/MoreCharacterLines/actions/workflows/ci.yml/badge.svg)](https://github.com/moonEEVEEXwX/MoreCharacterLines/actions/workflows/ci.yml)

> 给《杀戮尖塔 2》里那些**固定的文案**，配上「按**角色** / 按**状态**」随机抽取的台词。
>
> 目前覆盖两个场景：**火堆（休息处）顶部提示语**、**多人催促（ping）头顶气泡** ——
> 同一个火堆，铁甲战士低血时会嘴硬、静默猎手永远只有省略号；同一次 ping，等得越久语气越难看。

<div align="center">

*Slay the Spire 2 mod — random character-flavored lines for fixed UI text.*

</div>

---

## 特性

| | |
|---|---|
| 🔥 **火堆台词** | 14 个条件：低血 / 南瓜蜡烛 / 壶铃 ×2 / 10 种火堆遗物。遗物拿得越多，越容易说出遗物相关的台词（占比 25% → 80%） |
| 📣 **多人催促（ping）** | 三档语气：正常 → **更急**（等满 1 分钟）→ **更凶**（等满 5 分钟）；每次按都会换一句，连按不重复 |
| 🤝 **两端一致，且不绑架队友** | 两个装了 mod 的人看到的是**同一句**；**没装的人完全不受影响** —— 照样联机、显示游戏原话 |
| ✍️ **台词可以就地改** | 改 `lines.json` → 重进一次场景即生效，**不用重编译、不用重启游戏** |
| 🧩 **数据驱动** | 代码只管"什么时候该说什么"，文案全在 JSON；加角色 / 加场景都不用动 C# |
| 🛡️ **优雅降级** | 反射失败、JSON 写错、文件读不到 —— 一律**保持游戏原版文案**，不崩、不刷屏 |
| 👻 **纯外观** | 清单里 `affects_gameplay: false`：不改数值、不改判定、不碰同步状态（发牌用的 RNG 一概不抽） |

## 安装

1. 到 [Releases](https://github.com/moonEEVEEXwX/MoreCharacterLines/releases) 下载 `MoreCharacterLines_v0.1.0.zip`，解压得到 `MoreCharacterLines` 文件夹；
   （也可以自己构建，见 [开发](#开发)。）
2. 整个文件夹丢进游戏目录：

   ```
   Slay the Spire 2/
   └─ mods/
      └─ MoreCharacterLines/
         ├─ MoreCharacterLines.json      # 清单（游戏靠它识别 mod）
         ├─ MoreCharacterLines.dll       # 逻辑
         ├─ MoreCharacterLines.pck       # 出厂台词
         ├─ lines.json                   # 你可以随手改的台词文件
         └─ README.md / DESIGN.md
   ```

3. 启动游戏（需要 **v0.111.0** 及以上，清单里已声明 `min_game_version`）。

卸载 = 删掉这个文件夹；想临时关掉，把 `MoreCharacterLines.json` 改个名即可。

## 上手：改台词（30 秒）

用记事本 / VS Code 打开 `mods/MoreCharacterLines/lines.json`，照着已有结构加句子：

```jsonc
"rest_site": {
  "IRONCLAD": {
    "normal": ["火。很好。", "稍作调整。继续杀戮。"],
    "low_hp": ["致命伤算什么。"]                 // 血量 ≤ 25% 时说的
  },
  "DEFAULT": { "normal": ["我该做什么呢……"] }    // 没写专属台词的角色走这里（ping 没有通用池）
}
```

- 允许 `//` 注释、结尾多余逗号、字符串或字符串数组混写；用 UTF-8 保存。
- **改完重进一次火堆（或再 ping 一次）就生效** —— 不用重新编译、不用重启游戏。
- 写错不会崩：日志里会提示 `JSON 格式有误`，并自动退回上一份能用的台词。

> 详细语法、条件表、概率规则，以及**每个角色的口吻考证**（写台词前必看）都在
> [`DESIGN.md`](DESIGN.md) 与 [`assets/MoreCharacterLines/lines.json`](assets/MoreCharacterLines/lines.json) 的头部注释里。

## 参考

### 角色 ID

| 角色 ID | 中文名 |
|---|---|
| `IRONCLAD` | 铁甲战士 |
| `SILENT` | 静默猎手 |
| `DEFECT` | 故障机器人 |
| `REGENT` | 储君 |
| `NECROBINDER` | 亡灵契约师 |
| `DEFAULT` | 通用兜底（**火堆有，ping 没有**） |

mod 角色直接用它的角色 ID 当键即可（例如 `"WATCHER"`），**不用改代码**。

### 条件一览

| 条件名 | 触发时机 | 数据来源 |
|---|---|---|
| `low_hp` | 血量 ≤ 25% | 游戏自己的 `CharacterModel.IsLowHealth`（与低血边框动画同源） |
| `candle_low` | 南瓜蜡烛剩余 ≤ 2 层 | `PumpkinCandle.KindleCount` |
| `girya_maxed` | 壶铃已练满 | `Girya.TimesLifted`，**只播一次** |
| `girya_progress` | 壶铃还没练满 | 同上 |
| `relic_*` | 拥有对应火堆遗物 | 10 个：铲子 / 切肉刀 / 微型帐篷 / 捕梦网 / 小邮箱 / 皇家枕头 / 佩尔增生组织 / 石炉加湿器 / 古茶具（真·假互斥） |
| `wait_urgent` | ping：≥50% 玩家已结束回合、其余人还在打，且等超过 60 秒 | 回合号 + 游戏自己的"已结束回合"集合 |
| `wait_angry` | 同上，等超过 300 秒 | 同上 |

### 概率速查

| 设置 | 位置 | 含义 |
|---|---|---|
| `_defaultChance` | 顶层 | 有专属台词的角色抽到通用池的概率（默认 `0.1`；**ping 无通用池，只对火堆生效**） |
| `_conditionChance` | 顶层 / 场景内 | 状态类条件的概率（默认 `1`；火堆示例里低血是 `0.8`） |
| `_relicFlavor` | 顶层 | 遗物台词占比：`perRelic` × 遗物个数（上限 `max`）。默认 `0.25` / `0.8` |
| `_ping.urgentAfterSeconds` / `angryAfterSeconds` | 顶层 | ping 语气升级的等待秒数（默认 `60` / `300`）；**测试时改成 10 / 20 秒**就能一次看完三档 |
| `_ping.perPingVariety` | 顶层 | 每次 ping 是否换一句（默认 `true`）；设 `false` 则同回合同档位永远同一句、两端 100% 一致 |

## 兼容性与边界

- **游戏版本**：`v0.111.0`（清单里声明了 `min_game_version`，低于它游戏会拒绝加载）。
- **多人游戏**：
  - ping：两端都装 mod → 显示同一句；只有一端装 → 对方显示游戏原话，**联机完全不受影响**；
  - 火堆：取「本地玩家」的角色，两台机器看到的可能不同（纯外观，不影响任何判定）。
- **不碰同步状态**：发牌用的 `PlayerRng` 是两端校验的确定性状态，本 mod **只读不抽** ——
  不给游戏状态埋雷，也不会把原版客户端挤出同步。
- **死人不说话**：ping 到死亡角色时保持原版的「……」。

## 文档与工具

| 文件 | 说明 |
|---|---|
| [`DESIGN.md`](DESIGN.md) | **单一事实来源**：定位、架构、台词规范、概率方针、决策记录、排障、交接备忘 |
| [`assets/MoreCharacterLines/lines.json`](assets/MoreCharacterLines/lines.json) | 出厂台词（打包进 PCK；头部注释就是写作指南） |
| [`tools/preflight`](tools/preflight) | 预检工具：**不开游戏**跑 84 项断言（补丁挂点、反射目标、抽取逻辑、ping 规则、JSON 解析） |
| [`check_lines.py`](check_lines.py) | 台词文件语法 + 语义闸门（少逗号、ping 里写旁白括号、未知 BBCode……） |
| [`tools/make_release_zip.py`](tools/make_release_zip.py) | 把仓库里的东西组装成可安装的发布 zip（本机 / CI / 发版共用同一份逻辑） |
| [`release/`](release) | **提交进仓库的构建产物**（`dll` + `pck` + `ARTIFACTS.txt`）：公共 CI 编译不了，只能本地构建后提交 |

## 开发

```powershell
# 构建 + 安装（改完 assets 里的台词也必须跑一次）
powershell -ExecutionPolicy Bypass -File build.ps1

# 只校验台词文件（少逗号会报行号）
python check_lines.py assets\MoreCharacterLines\lines.json

# 预检：加载真实 sts2.dll + 已安装的 mod DLL，跑 84 项断言
powershell -ExecutionPolicy Bypass -File tools\preflight\build.ps1
cd tools\preflight\bin
dotnet Preflight.dll "<游戏>\data_sts2_windows_x86_64" "<游戏>\mods\MoreCharacterLines\MoreCharacterLines.dll"

# 本地组装一个发布 zip（和 CI 发版用的是同一个脚本）
python tools\make_release_zip.py
```

### CI / 发版

`push` 时（[`ci.yml`](.github/workflows/ci.yml)）跑不需要游戏本体的检查：
**台词校验 + 所有 `.ps1` 的 BOM 闸门 + 清单字段检查 + `release/` 产物指纹核对 + 打包演练**。

打 tag 时（[`release.yml`](.github/workflows/release.yml)）自动发版：
组装 `MoreCharacterLines_v<版本>.zip` → 建 GitHub Release（附自动生成的更新说明）。
tag 名和 `mod_manifest.json` 里的 `version` 不一致会**直接失败**，避免发错版本号。

**编译与预检跑不了**（要游戏目录里的 `sts2.dll` / `GodotSharp.dll` / `0Harmony.dll`，版权原因不能进公开仓库），
所以流程是「**本地构建 → 提交 `release/` 里的 dll+pck → 打 tag 自动发版**」；
`release/ARTIFACTS.txt` 是 `build.ps1` 写的指纹，CI 靠它确认提交的二进制没被手工换过。

工程约定（都是踩过坑的）：

- `.ps1` 必须存成 **UTF-8 with BOM** —— 否则 Windows PowerShell 5.1 按 GBK 读会直接语法错误；
  丢了就跑 `fix-encoding.ps1`（递归扫全仓库）。**用编辑器改完 `.ps1` 请顺手跑一次**（有些编辑器会吃掉 BOM）。
- `build.ps1` **不覆盖**已存在的 `lines.json`，但会提示"和仓库不一致"；加 `-SyncLines` 用仓库版本覆盖。
- 只有 .NET 8 SDK 时，脚本会自动回退到 Roslyn(csc) 编译 + 游戏自带的 .NET 9 程序集，不用额外装 SDK。

## 已知限制

- 只改火堆顶部提示语和 ping 气泡；**不改**休息 / 锻造选项说明、事件、商店等文案（后续计划）。
- ping **故意没有通用池**：静默猎手 / 故障机器人被抽中会突然"开口说人话"，
  而观者、蕾忍这类 mod 角色本来就有自己的催促语 —— 没写专属池的角色一律**保持游戏原句**。
- 台词目前只有中文（英文客户端也会显示中文）；多语言（`zhs` / `eng` 双份）在计划中。
- 火堆那句标签是 `MegaLabel`（不吃 BBCode）；ping 气泡是富文本，可以用 `[wave]` / `[shake]` / `[red]` 等效果。

## 许可与致谢

- **许可**：[GPL-3.0](LICENSE)。
- **作者 / 维护**：[@moonEEVEEXwX](https://github.com/moonEEVEEXwX)。
- **代码与文档是怎么来的**：这是一个 **vibecoding 兴趣项目** —— 代码、文档与台词由
  **DeepSeek Harness（DSH，本会话使用的 agent 工具）** 与作者协作产出；
  仓库里的提交者署名 `Dsh (vibecoding)` 指的就是这个工具，不是真人。
- **第三方**：Godot / .NET / Harmony 等组件说明见 [`THIRD_PARTY.md`](THIRD_PARTY.md)；
  台词口吻参考了游戏内本地化文本（仅作风格参考，不复制原文）。
