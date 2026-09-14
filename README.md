# 角色台词库 (CharacterLines)

按「角色 + 场景」给游戏里的固定文案配随机台词。目前接好的是**火堆**：

- 铁甲战士 → “稍作调整……继续杀戮……”“稍息……前进……”
- 静默猎手 → “……稍作调整……”“……”
- 故障机器人 / 储君 / 亡灵契约师 → 各自一套
- 没写专属台词的角色（包括 mod 角色）→ 只用该场景的 `DEFAULT` 通用池
- 血量 **≤ 25%** 时优先说「伤得不轻」这类话 —— 复用游戏自己的低血判定
  （`CharacterModel.IsLowHealth`），和**低血边框动画是同一个临界点**
- 台词文件可以就地编辑；加新场景（多人 ping 等）只需要改 JSON + 一个小补丁

> 火堆是回血 / 锻造的**安静**场景，不是战场。台词整体偏短、轻、内心独白 ——
> 类似一代休息处的“嗯……”“有不少选择啊……”。

---

## 1. 台词文件在哪

| 顺序 | 文件 | 谁改的 | 什么时候用 |
|---|---|---|---|
| 1 | `游戏目录\mods\CharacterLines\lines.json` | 想临时定制这个 mod 的人 | **默认就是它**（打包时一起放进去的） |
| 2 | `%AppData%\SlayTheSpire2\CharacterLines\lines.json` | 自己机器上的个人配置 | 上一条不存在时（例如 mod 文件夹不可写） |
| 3 | `CharacterLines.pck` 里自带的默认台词 | mod 作者（你） | 前两条都不存在时 |
| 4 | 游戏原版文案 | 官方 | 全都没有时 |

> 具体这次用的是哪一份，游戏日志里会写 `[CharacterLines] 台词来源：...`。
> 改完 `lines.json`，**重进一次火堆**就生效（不用重新编译、不用重启游戏）。

---

## 2. 台词文件怎么写

```jsonc
{
  // 全局设置
  "_defaultChance": 0.1,                      // 有专属台词的角色，抽到 DEFAULT 通用池的概率
  "_conditionChance": { "low_hp": 0.8 },      // 满足条件时，用条件池的概率

  // ── 场景：火堆 ────────────────────────────────────────
  "rest_site": {
    "IRONCLAD": {
      "normal": ["稍作调整……继续杀戮……", "让我缓一缓。"],
      "low_hp": ["……伤得不轻。", "血还没止住……"]
    },
    "DEFAULT": {
      "normal": ["我该做什么呢……", "嗯……", "有不少选择啊……"],
      "low_hp": ["……先喘口气。", "得歇一会儿了。"]
    }
  },

  // ── 场景：多人催促（JSON 先占位，代码还没接）──────────
  "ping": {
    "IRONCLAD": ["快点。", "……在等什么？"],     // 只写数组 = 只有 normal 池
    "DEFAULT":  ["……在吗？"]
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
| `_conditionChance` | 文件顶层（或场景内） | 满足条件时用条件池的概率（默认 `1`；示例里低血时 `0.8`，即 20% 还是说 normal） |

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

---

## 4. 加新场景（比如多人 ping）

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
cd D:\sts2modtest\CharacterLines
powershell -ExecutionPolicy Bypass -File build.ps1
```

脚本会：

1. 编译 `CharacterLines.dll`
   - 有 **.NET 9+ SDK** → 标准 `dotnet build`；
   - 只有 .NET 8 SDK（本机当前情况）→ 自动改用 SDK 自带 Roslyn `csc`，
     引用游戏目录 `data_sts2_windows_x86_64\` 里自带的 .NET 9 运行时程序集编译。
2. 把 `assets/` 打包成 `CharacterLines.pck`。
3. 安装到 `游戏目录\mods\CharacterLines\`：
   `CharacterLines.dll` + `.pck` + `CharacterLines.json`（清单）+ `lines.json`（已存在则不覆盖）+ `README.md` / `THIRD_PARTY.md`。

参数：`-GameDir "X:\...\Slay the Spire 2"` / `-OutDir "D:\out"` / `-SkipInstall`。
自动探测不到游戏时，在本目录建 `game_dir.txt` 写游戏路径。

> **踩坑记录**：Mod 清单必须叫 `<id>.json`，DLL/PCK 必须叫 `<id>.dll` / `<id>.pck`，
> 且清单里必须有 `"id"` 字段 —— 游戏代码写死了这三条（缺 `id` 会报
> `missing the 'id' field! This is not allowed.` 并拒绝加载）。

---

## 6. git

```powershell
cd D:\sts2modtest\CharacterLines
git status
git add -A
git commit -m "加了低血台词"
```

第一次推 GitHub 前先改成你自己的身份：

```powershell
git config user.name  "你的名字"
git config user.email "你的邮箱@example.com"
git remote add origin https://github.com/你的用户名/仓库名.git
git branch -M main
git push -u origin main
```

---

## 7. 调试

日志：`%AppData%\SlayTheSpire2\logs\godot<时间>.log`，搜 `CharacterLines`：

- `[CharacterLines] loaded.` —— 加载成功
- `[CharacterLines] 已生成可编辑台词文件：...` —— 台词文件生成位置
- `[CharacterLines] 台词来源：...` —— 这次用的是哪一份
- `[CharacterLines] JSON 格式有误：...` —— 台词文件写错（会自动退回下一份）

改完没生效？① 改的是优先级更低的那份；② 在火堆里改的（要出去再进来）；
③ JSON 语法错误（日志有提示）。

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
        - 悬停选项改的是 Description 标签，不会覆盖 Header
```

文件分工：

| 文件 | 作用 |
|---|---|
| `LineBank.cs` | 台词库：读 JSON、按场景/角色/条件抽；三处来源优先级 |
| `PlayerContext.cs` | 取当前玩家、角色 ID、条件（低血判定） |
| `SceneRestSite.cs` | 火堆场景补丁（加场景照这个写） |
| `CharacterLinesBootstrap.cs` | 入口：`PatchAll` + 生成可编辑台词文件 |

---

## 9. 已知限制

- **多人游戏**：取「本地玩家」的角色，所以两台机器看到的台词可能不同（纯外观，不影响联机判定）。
  想统一取 1 号位玩家，把 `PlayerContext.FromRunState()` 里的 `LocalContext.GetMe(...)` 去掉。
- 只改火堆顶部提示语，不改休息 / 锻造选项的说明文字。
- 台词不随游戏语言切换（要多语言可把 JSON 改成 `{"zhs": {...}, "eng": {...}}`，
  再用 `LocManager.Instance.Language` 选一份）。
- 游戏更新后若私有成员改名，反射会失败：日志报错，表现是**保持原版文案**，不会崩。
- mod id 从 `RestSitePrompts` 改成了 `CharacterLines`，所以旧目录 `mods\RestSitePrompts\` 要删掉
  （两个都会改火堆文案，留两个会互相覆盖）。
