# 更多角色台词 (MoreCharacterLines)

> **这是一个 vibecoding 兴趣项目**（作者：Dsh）—— 设计文档、代码和台词大部分是跟 AI 边聊边写出来的，
> 所以你会看到很长的 DESIGN.md 和一堆"为什么这么定"的记录。欢迎看、改、提 issue。
> 许可：**GPL-3.0**（见 [`LICENSE`](LICENSE)）。

按「角色 + 场景」给游戏里的固定文案配随机台词。目前接好了**两个场景**：

**火堆（休息处）顶部那句提示语**：

- 铁甲战士 → “稍作调整……继续杀戮……”“稍息……前进……”
- 静默猎手 → “……稍作调整……”“……”
- 故障机器人 / 储君 / 亡灵契约师 → 各自一套
- 没写专属台词的角色（包括 mod 角色）→ 只用该场景的 `DEFAULT` 通用池
  （**ping 例外**：那边没有通用池，见 §3.1）
- 血量 **≤ 25%** 时优先说「伤得不轻」这类话 —— 复用游戏自己的低血判定
  （`CharacterModel.IsLowHealth`），和**低血边框动画是同一个临界点**
- 目前共 **14 个条件**（血量 / 南瓜蜡烛 / 壶铃 / 10 种火堆遗物），遗物拿得越多，
  越容易说出遗物相关的台词（占比 25% → 80%）

**多人催促（ping）头顶气泡**（结束回合后按 Ping 按钮时说的那句）：

- 台词按**等待时长**分三档：刚开始等 → 原版口吻；≥50% 的人已结束回合、其余人还在打，
  等过 **1 分钟** → 更急；等过 **5 分钟** → 更凶（阈值可改，见 §3.1）
- **两台装了 mod 的机器看到的是同一句**；**没装 mod 的玩家不受影响**（照旧显示游戏原话，联机正常）
- 角色死亡时不接管，保持原版的「……」

> 📐 **设计方针、概率规则、决策记录与待办计划**都在 [`DESIGN.md`](DESIGN.md)，
> 改代码或加场景前先看一眼那份。

> 火堆是回血 / 锻造的**安静**场景，不是战场。台词整体偏短、轻、内心独白 ——
> 类似一代休息处的“嗯……”“有不少选择啊……”。

---

## 1. 台词文件在哪

| 顺序 | 文件 | 谁改的 | 什么时候用 |
|---|---|---|---|
| 1 | `游戏目录\mods\MoreCharacterLines\lines.json` | 想临时定制这个 mod 的人 | **默认就是它**（打包时一起放进去的） |
| 2 | `%AppData%\SlayTheSpire2\MoreCharacterLines\lines.json` | 自己机器上的个人配置 | 上一条不存在时（例如 mod 文件夹不可写） |
| 3 | `MoreCharacterLines.pck` 里自带的默认台词 | mod 作者（你） | 前两条都不存在时 |
| 4 | 游戏原版文案 | 官方 | 全都没有时 |

> 具体这次用的是哪一份，游戏日志里会写 `[MoreCharacterLines] 台词来源：...`。
> 改完 `lines.json`，**重进一次火堆**就生效（不用重新编译、不用重启游戏）。

---

## 2. 台词文件怎么写

```jsonc
{
  // 全局设置
  "_defaultChance": 0.1,                            // 有专属台词的角色，抽到 DEFAULT 通用池的概率
  "_relicFlavor": { "perRelic": 0.25, "max": 0.8 }, // 遗物台词占比：每个火堆遗物 25%，上限 80%
  "_ping": { "urgentAfterSeconds": 60, "angryAfterSeconds": 300 }, // 催促语气升级的等待秒数

  // ── 场景：火堆 ────────────────────────────────────────
  "rest_site": {
    "_conditionChance": { "low_hp": 0.8 },          // 状态类条件的概率（遗物那些看 _relicFlavor）
    "IRONCLAD": {
      "normal": ["稍作调整……继续杀戮……", "让我缓一缓。"],
      "low_hp": ["……伤得不轻。", "血还没止住……"],
      "relic_shovel": ["（让我看看火堆下面有什么……）"]
    },
    "DEFAULT": {
      "normal": ["我该做什么呢……", "嗯……", "有不少选择啊……"],
      "low_hp": ["……先喘口气。", "得歇一会儿了。"]
    }
  },

  // ── 场景：多人催促（ping）────────────────────────────
  "ping": {
    "_conditionChance": { "wait_urgent": 1, "wait_angry": 1 },  // 满足条件就一定说
    "IRONCLAD": {
      "normal": ["快点。", "……在等什么。"],          // 只写数组 = 只有 normal 池
      "wait_urgent": ["还没好吗。"],                  // ≥50% 人结束回合 + 等满 1 分钟
      "wait_angry": ["你在磨蹭什么！"]                 // 等满 5 分钟
    },
    "DEFAULT": {
      "normal": ["……在吗？"],
      "wait_urgent": ["快一点吧。"]                    // 没写 wait_angry 就退回这一档
    }
  }
}
```

规则：

- **结构**：`场景 ID → 角色 ID → { 池名: [台词...] }`
  - `normal` = 平时说的；`low_hp` = 血量 ≤ 25% 时说的；以后加条件就再加一个池名。
  - 只想写平时的台词，可以偷懒直接写数组：`"IRONCLAD": ["...", "..."]`。
- 角色没写某个池（比如只有 `normal` 没写 `low_hp`）→ 低血时自动用它的 `normal`。
- 角色完全没写 → 用同场景的 `DEFAULT`（`DEFAULT` 也可以有自己的 `low_hp`）。
- 一个角色只写一句也行：`"IRONCLAD": "只有一句"`。
- 允许 `//` 注释、结尾多余逗号；`_` 开头的键当设置/注释，不会被当成角色名。
- **不要**在台词里写 `{` `}`（游戏本地化占位符语法，会解析报错）。
- 换行写 `\n`。
- 文件是 **UTF-8（带 BOM）**，记事本 / VS Code 直接编辑即可；别存成 ANSI / GBK。
- 写错了不会崩：日志提示 `JSON 格式有误`，自动退回下一份能用的台词。
- 旧写法（场景里单独一大栏 `"_conditions": { "low_hp": { ... } }`）**仍然兼容**，
  但推荐用上面这种挂在角色下面的写法。

### 概率速查

| 设置 | 位置 | 含义 |
|---|---|---|
| `_defaultChance` | 文件顶层 | 有专属台词的角色抽到通用池的概率（默认 `0.1`，`0` = 永远只说自己的） |
| `_conditionChance` | 文件顶层（或场景内） | 状态类条件（`low_hp` / `candle_low` / `girya_*`）的概率（默认 `1`；示例里低血时 `0.8`，即 20% 还是说 normal） |
| `_relicFlavor` | 文件顶层 | **遗物台词占比**：`perRelic` × 拥有的火堆遗物个数（上限 `max`）。默认 `0.25` / `0.8` → 1 个遗物 25%、2 个 50%、3 个 75%、4 个及以上 80%；`perRelic: 0` 等于关掉 |
| `_ping` | 文件顶层 | **ping 语气升级的等待秒数**：`urgentAfterSeconds`（默认 60）/ `angryAfterSeconds`（默认 300）。测试时改成 10 / 20 就不用真等 5 分钟（见 §3.1） |

---

## 3. 角色 ID 与条件

| 角色 ID | 中文名 |
|---|---|
| `IRONCLAD` | 铁甲战士 |
| `SILENT` | 静默猎手 |
| `DEFECT` | 故障机器人 |
| `REGENT` | 储君 |
| `NECROBINDER` | 亡灵契约师 |
| `DEFAULT` | 通用（不是角色，兜底用） |

mod 角色：直接用它的角色 ID 当键即可（例如 `"WATCHER": ["……"]`），**不用改代码**。
ID 就是游戏本地化里 `xxx.title` 的前缀（可参考 `_ref\loc_zhs`）。

| 条件名 | 触发时机 | 数据来源 |
|---|---|---|
| `low_hp` | 血量 ≤ 25% | 游戏自己的 `CharacterModel.IsLowHealth`（与低血边框动画同源），拿不到时退回公开的 `GetHpPercentRemaining() <= 0.25` |
| `candle_low` | 南瓜蜡烛剩余层数 ≤ 2 | `PumpkinCandle.KindleCount` |
| `girya_maxed` | 壶铃已练满（`TimesLifted >= maxLifts`） | `Girya.TimesLifted` / `Girya.maxLifts`，**只播一次**（记录在 `%AppData%\SlayTheSpire2\MoreCharacterLines\state.json`） |
| `girya_progress` | 壶铃还没练满 | 同上 |
| `relic_shovel` | 拥有铲子 | 遗物 `Shovel` |
| `relic_cleaver` | 拥有切肉刀 | 遗物 `MeatCleaver` |
| `relic_tent` | 拥有微型帐篷 | 遗物 `MiniatureTent` |
| `relic_dream_catcher` | 拥有捕梦网 | 遗物 `DreamCatcher` |
| `relic_mailbox` | 拥有小邮箱 | 遗物 `TinyMailbox` |
| `relic_pillow` | 拥有皇家枕头 | 遗物 `RegalPillow` |
| `relic_paels_growth` | 拥有佩尔的增生组织 | 遗物 `PaelsGrowth` |
| `relic_humidifier` | 拥有石炉加湿器 | 遗物 `StoneHumidifier` |
| `relic_tea_set` | 拥有古茶具套装 | 遗物 `VenerableTeaSet` |
| `relic_fake_tea_set` | 拥有古茶具套装？？？ | 遗物 `FakeVenerableTeaSet`，**同时有真货时不出现** |
| `wait_urgent` | ping：≥50% 玩家已结束回合、其余人还在打，且等超过 `_ping.urgentAfterSeconds`（默认 60 秒） | 回合号 + 游戏自己的「已结束回合」集合（见 §3.1） |
| `wait_angry` | 同上，等超过 `_ping.angryAfterSeconds`（默认 300 秒） | 同上 |

### 火堆条件的优先级

同一时刻可能有多个条件满足，按这个顺序从上往下试（前面命中就不看后面的）：

```
candle_low        ← 能立刻操作的提示（该添火了），最重要
girya_maxed       ← 壶铃练满的一次性纪念播报（被上面顶掉时不消耗，顺延到下次火堆）
low_hp            ← 血量低
relic_* 各遗物氛围 ← 合成一组：按「遗物台词占比」掷一次，命中就随机挑一个遗物说
girya_progress    ← 还没练满时的鼓励
```

**遗物台词占比**（`_relicFlavor`）：拿的火堆遗物越多，越容易听到遗物相关的台词，
而不是把它白拿了：

| 拥有的火堆遗物 | 说遗物台词的概率 |
|---|---|
| 1 个 | 25% |
| 2 个 | 50% |
| 3 个 | 75% |
| 4 个及以上 | 80%（上限） |

余下的概率才轮到角色自己的台词 / 通用台词。想调就改 `_relicFlavor.perRelic`（`0` = 关掉）。

> 「顺延」是这么实现的：只有真的把 `girya_maxed` 那句显示出来之后，才会写进
> `state.json`；被更高优先级的台词顶掉时什么都不记，下次火堆继续尝试。

---

### 3.1 催促（ping）的语气分档

结束回合后按 Ping 按钮，角色头顶会弹一句催促。**等待时间越长，语气越差**：

| 档位 | 什么时候 | 池名 | 默认阈值 |
|---|---|---|---|
| 正常 | 刚结束回合 / 还没到 50% 的人结束回合 | `normal` | — |
| 更急 | ≥50% 玩家已结束回合，其余人还在继续打 | `wait_urgent` | 60 秒 |
| 更凶 | 同上，等得更久 | `wait_angry` | 300 秒 |

```jsonc
"_ping": {
  "urgentAfterSeconds": 60,    // 想快点看到「更急」就改成 10
  "angryAfterSeconds": 300     // 想快点看到「更凶」就改成 20
}
```

- **测试提示**：把这两个数改小（改 `mods\MoreCharacterLines\lines.json`，改完即时生效），
  两个人快速结束回合后互相 ping 就能看到三档台词，不用真等 5 分钟。
- 计时从「已结束回合的人数达到 50%」开始，**下一回合自动清零**。
- 只有满足条件才升级：没人结束回合、或只有 25% 的人结束回合时，等再久也是正常语气。
- 优先级：`wait_angry` > `wait_urgent` > `normal`；某个池子没写就自动往下退。
- **气泡里那句是角色"说出口的话"**，所以 ping 台词里**不写 `（旁白）`** —— 火堆那套
  「括号 = 旁白」只属于火堆的内心独白。情绪改用 BBCode 表现：
  `[wave]…[/wave]` = 更急、`[shake]…[/shake]` = 更凶；静默猎手三档都是「……」，
  全靠这两个效果区分。（效果名写错会在气泡里原样显示，`check_lines.py` 会提示。）
- **不要单指称呼「你」**：多人局是 2~4 人（还有去掉人数上限的 mod），催促是对**队伍**说的 ——
  用不点名的说法（「快一点吧。」「别磨蹭。」）或复数「你们」。
- **两个人都装了 mod 时看到的是同一句**：台词由「回合号 + 催促者 + 语气档位」算出来
  （两端状态一致 ⇒ 结果一致），不需要联机同步，也不影响没装 mod 的玩家。
- **每次按 Ping 基本都会换一句**（默认）：两端各自数「第几次」，正常情况完全同步；
  万一有消息丢包，两边会错开一位，但**升档或换回合时会自动重新对齐**。
  想改成"同回合同档位永远同一句、两端 100% 一致"，把 `_ping.perPingVariety` 设成 `false`。
- **ping 没有 `DEFAULT` 通用池**（火堆有，ping 特意不要）：只有写了专属池的角色会被改写，
  其余角色 —— 包括观者、蕾忍这类 **mod 角色** —— **保持游戏/它们自己的催促语**，不会被通用句子盖掉。
- 死人不说话：死亡状态保持原版「……」。

---

## 4. 加新场景（照 `SceneRestSite.cs` / `ScenePing.cs` 写）

两步：

1. **JSON 里加一段**（现在就能写，不影响运行）：

   ```jsonc
   "ping": {
     "IRONCLAD": ["快点。", "……在等什么？"],
     "SILENT": ["……"],
     "DEFAULT": ["……在吗？", "我先走了。"]
   }
   ```

2. **写一个补丁文件**（照 `SceneRestSite.cs` 改），比如 `ScenePing.cs`：

   ```csharp
   [HarmonyPatch(typeof(某个类), "某个方法")]        // 找到 ping 文案被设置的地方
   internal static class ScenePing
   {
       private static void Postfix(...)
       {
           string? text = LineBank.Pick(
               Scenes.Ping,                                  // "ping"
               PlayerContext.CharacterId(player),
               PlayerContext.ConditionsFor(player));         // 需要的话
           // 把 text 写回对应的 Label / LocString
       }
   }
   ```

   同时在 `LineBank.cs` 的 `Scenes` 里已经有 `Ping` 常量；再跑一次 `build.ps1` 即可。
   （如果你找到了 ping 的设置入口，告诉我，我可以把它也接上。）

---

## 5. 编译 & 安装

```powershell
cd D:\sts2modtest\MoreCharacterLines
powershell -ExecutionPolicy Bypass -File build.ps1
```

脚本会：

1. 编译 `MoreCharacterLines.dll`
   - 有 **.NET 9+ SDK** → 标准 `dotnet build`；
   - 只有 .NET 8 SDK（本机当前情况）→ 自动改用 SDK 自带 Roslyn `csc`，
     引用游戏目录 `data_sts2_windows_x86_64\` 里自带的 .NET 9 运行时程序集编译。
2. 把 `assets/` 打包成 `MoreCharacterLines.pck`。
3. 安装到 `游戏目录\mods\MoreCharacterLines\`：
   `MoreCharacterLines.dll` + `.pck` + `MoreCharacterLines.json`（清单）+ `lines.json`（已存在则不覆盖）+ `README.md` / `THIRD_PARTY.md`。

参数：`-GameDir "X:\...\Slay the Spire 2"` / `-OutDir "D:\out"` / `-SkipInstall`。
自动探测不到游戏时，在本目录建 `game_dir.txt` 写游戏路径。

> **踩坑记录**：Mod 清单必须叫 `<id>.json`，DLL/PCK 必须叫 `<id>.dll` / `<id>.pck`，
> 且清单里必须有 `"id"` 字段 —— 游戏代码写死了这三条（缺 `id` 会报
> `missing the 'id' field! This is not allowed.` 并拒绝加载）。

---

> **改代码的人**：仓库自带预检工具（`tools\preflight`）—— 不用开游戏就能验补丁挂点、
> 反射目标、抽取逻辑、ping 的规则。构建 + 运行：
> `powershell -File tools\preflight\build.ps1` → `cd tools\preflight\bin` →
> `dotnet Preflight.dll "<游戏>\data_sts2_windows_x86_64" "<游戏>\mods\MoreCharacterLines\MoreCharacterLines.dll"`

---
## 6. git

```powershell
cd D:\sts2modtest\MoreCharacterLines
git status
git add -A
git commit -m "加了低血台词"
```

**已发布**：<https://github.com/moonEEVEEXwX/MoreCharacterLines>（公开，许可 GPL-3.0）。
提交署名统一是 `Dsh (vibecoding) <dsh@example.com>`；日常就是上面三步 + 一条推送：

```powershell
git push
```

> 第一次 push 会弹浏览器授权（Git Credential Manager），点一下 **Authorize** 就好，之后会记住。
> 如果代码是在 AI 会话里改的，push 也可以交给 AI —— 但沙箱默认会拦 `sh.exe`（git 凭据助手要经过它），
> 会弹一次授权确认；放行后你这台机器上已存好的凭据可以直接复用。想省事就自己 `git push`。

---

## 7. 调试

日志：`%AppData%\SlayTheSpire2\logs\godot<时间>.log`，搜 `MoreCharacterLines`。

> 有两条**看着吓人、其实正常**的日志：
> - `Caught System.Text.Json.JsonReaderException ... mods\MoreCharacterLines\lines.json` ——
>   游戏会把 mod 文件夹里**每个 `.json` 都当清单试解析**，我们这份带 `//` 注释的台词文件当然解析不了。无害。
> - `Mod MoreCharacterLines does not declare min game version` —— 旧版清单没声明最低游戏版本；
>   现在清单里已经有 `min_game_version`，这条会消失。

- `[MoreCharacterLines] loaded.` —— 加载成功
- `[MoreCharacterLines] 已生成可编辑台词文件：...` —— 台词文件生成位置
- `[MoreCharacterLines] 台词来源：...` —— 这次用的是哪一份
- `[MoreCharacterLines] 催促台词（档位 N）：...` —— 触发了 ping 台词，N = 0 正常 / 1 更急 / 2 更凶
- `[MoreCharacterLines] JSON 格式有误：...` —— 台词文件写错（会自动退回下一份）

ping 不生效？按顺序查：① **`mods\MoreCharacterLines\lines.json` 是不是旧的**
（`build.ps1` 默认不覆盖已存在的台词文件，但会**警告内容不一致**；加 `-SyncLines` 就用仓库版本覆盖）；
② 是不是死人在催（死人保持原版）；③ 日志里有没有 `催促台词` 那行 —— 没有就是抽取没命中，
ping 那边**故意没有** `DEFAULT` 通用池，所以"这个角色没写专属池"= 保持游戏原句（这是预期的，不是 bug）。

### 想立刻看台词长什么样？（单人也能看 —— ⚠️ 施工中，当前版本还没接上，按了没反应）

多人环境（浏览器客户端 / 局域网 / 假联机）各有各的坑，但**调台词不该依赖联机**。
在 `lines.json` 里打开调试预览键：

```jsonc
"_debug": { "pingPreviewKey": "F8" }
```

进战斗后按这个键：**本地角色头顶会弹出催促使气泡**，连按循环
**正常 → 更急 → 更凶**，日志里同步打同一句。这样 `[wave]` / `[shake]` / `[red]`
这些效果好不好看，一个人就能当场对齐。

> 它是**纯本地外观**：走的是和原版 ping 一样的气泡创建路径，但不发网络消息、
> 别人看不见、也不改任何判定。正式游玩请把值改回 `""`（默认就是关的）。

改完没生效？① 改的是优先级更低的那份；② 在火堆里改的（要出去再进来）；
③ JSON 语法错误（日志有提示）。

**如果 `build.ps1` 报奇怪的语法错误**（类似 `Unexpected token ...`）：多半是脚本的
UTF-8 BOM 丢了 —— Windows PowerShell 5.1 会把没有 BOM 的 `.ps1` 按本地编码（GBK）读，
中文注释就被解成乱码把语法搞坏。修法：

```powershell
powershell -ExecutionPolicy Bypass -File fix-encoding.ps1
```

（它会检查本目录所有 `.ps1`，缺 BOM 的补上；不是合法 UTF-8 的会跳过不动。
用 VS Code 编辑时右下角编码选 “UTF-8 with BOM” 就不会再丢。）

---

## 8. 原理

```
火堆：NRestSiteRoom._Ready()
        Header.SetTextAutoSize(new LocString("rest_site_ui", "PROMPT").GetFormattedText())
        ↑ 原版把“我该做什么呢？”写死

本 mod：Postfix 挂在同一个方法后面，用 LineBank 抽一句覆盖 Header
        - Header 是私有属性（Godot [Export]）→ AccessTools.PropertyGetter 反射
        - _runState 是私有字段 → AccessTools.FieldRefAccess，再 LocalContext.GetMe 取角色
        - 低血条件 → CharacterModel.IsLowHealth（protected，反射取），退回 25% 规则
        - 遗物条件 → Player.Relics 按 ID / 类型找，南瓜蜡烛读 KindleCount、壶铃读 TimesLifted
        - 悬停选项改的是 Description 标签，不会覆盖 Header
```

```
ping：NPingButton.OnRelease → FlavorSynchronizer.SendEndTurnPing()
        → CreateEndTurnPingDialogueIfNecessary(player)   ← 自己这边 / 对端收到消息都走这里
              LocString("characters", "<角色>.banter.<alive|dead>.endTurnPing")

本 mod：Postfix 挂在后面，把气泡里那句话换掉
        - 死人（Creature.IsDead）直接撒手 → 保持原版「……」
        - 语气档位 = PingClock（挂在 SetReadyToEndTurn 后面计时，下一回合清零）
        - 台词种子 = 回合号 + 催促者 NetId + 档位 → **两端抽出同一句**，不需要发网络消息
        - 气泡标签是富文本，只换中间那句，保留游戏自己的 [center][fly_in …] 包装
```

文件分工：

| 文件 | 作用 |
|---|---|
| `LineBank.cs` | 台词库：读 JSON、按场景/角色/条件抽（含 ping 的确定性抽取）；三处来源优先级 |
| `PlayerContext.cs` | 取当前玩家、角色 ID、低血判定、按 ID/类型找遗物 |
| `Conditions.cs` | 条件名 + 火堆条件优先级（含各遗物判定）+ 催促语气分档纯函数 |
| `OneShot.cs` | “只播一次”的记录，存 `%AppData%\SlayTheSpire2\MoreCharacterLines\state.json` |
| `SceneRestSite.cs` | 火堆场景补丁（加新场景照这个写） |
| `ScenePing.cs` | 多人催促补丁：换气泡文案 |
| `PingClock.cs` | 催促计时：≥50% 玩家结束回合后开始等，1 分钟 / 5 分钟分档 |
| `MoreCharacterLinesBootstrap.cs` | 入口：`PatchAll` + 生成可编辑台词文件 |

---

## 9. 已知限制

- **多人游戏里的台词同步**：
  - **ping**：两台都装了 mod 时显示**同一句**（种子来自同步状态）；
    只有一台装也不影响联机，没装的那台显示游戏原话。
  - **火堆**：取「本地玩家」的角色，两台机器看到的可能不同（纯外观，不影响联机判定）。
    想统一取 1 号位玩家，把 `PlayerContext.FromRunState()` 里的 `LocalContext.GetMe(...)` 去掉。
- 只改火堆顶部提示语，不改休息 / 锻造选项的说明文字。
- **ping 每次按基本都会换一句**（默认 `_ping.perPingVariety: true`）：两端各自数"第几次"，
  正常完全同步；万一丢包会错开一位，但**升档 / 换回合时自动重新对齐**。
  想改成"同回合同档位永远同一句"就把 `perPingVariety` 设成 `false`（详见 `DESIGN.md` §2）。
- ping **没有 `DEFAULT` 通用池**：没写专属池的角色（含观者、蕾忍这类 mod 角色）保持游戏原句，不被改写。
- 台词不随游戏语言切换（要多语言可把 JSON 改成 `{"zhs": {...}, "eng": {...}}`，
  再用 `LocManager.Instance.Language` 选一份）。
- 游戏更新后若私有成员改名，反射会失败：日志报错，表现是**保持原版文案**，不会崩。
- mod 改过两次名：`RestSitePrompts` → `CharacterLines` → **`MoreCharacterLines`**（现在这个名字）。
  旧目录 `mods\RestSitePrompts\`、`mods\CharacterLines\` 都要删掉 —— 它们和本 mod 改的是同一处文案，
  留两个会互相覆盖，而且旧的 DLL 还会多挂一次补丁。
