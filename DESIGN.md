# CharacterLines 设计方针与计划

> 这份文档是这个 mod 的「单一事实来源」：定位、架构、台词规范、概率与优先级方针、
> 工程约定、测试方式、待办计划、以及每条决定的理由。
> 改代码/改台词前先看这里；改完如果动了方针，记得回来更新本文件。

---

## 0. 一句话

给游戏里**固定的文案**配上「按 **角色** / 按 **遗物** / 按 **状态**」随机抽取的台词。
目前接入的场景：**火堆（休息处）顶部那句提示语**。

---

## 1. 目标与边界

### 目标

- 让原本千篇一律的文案带上角色个性与情境感（玩家捡了什么遗物、血量如何，说话不一样）
- 让玩家更容易注意到自己拿了"火堆组件"这类遗物
- 台词**可被玩家随时本地化改写**，不用重新编译

### 硬性边界（不要突破）

1. **纯外观**：不改玩法数值、不改判定；清单里 `affects_gameplay: false`，
   因此多人游戏里各客户端文案不同步是无害的。
2. **优雅降级**：任何异常（反射失败、JSON 写错、文件读不到）都必须**保持游戏原版文案**，
   绝不能崩游戏、不能刷屏报错。
3. **数据驱动**：能写进 `lines.json` 的，不写进 C#。
   代码里只放"什么时候该说什么"（条件判定），不放"说什么"（台词文本）。
4. **可测**：能在不开游戏的情况下验证的逻辑，就抽成纯函数（见 §7）。

---

## 2. 架构

### 文件分工

| 文件 | 职责 |
|---|---|
| `CharacterLinesBootstrap.cs` | 入口：`[ModInitializer]` → `PatchAll` + 生成可编辑台词文件 |
| `SceneRestSite.cs` | 火堆场景补丁：挂在 `NRestSiteRoom._Ready` 后面改写 `Header` |
| `Conditions.cs` | 条件名常量 + 火堆条件优先级 + 遗物判定（**纯函数** `FlavorConditions`） |
| `PlayerContext.cs` | 游戏侧上下文：当前玩家、角色 ID、低血判定、按类型/ID 找遗物 |
| `LineBank.cs` | 台词库：读三处 `lines.json`、解析、按场景/角色/条件抽取 |
| `OneShot.cs` | "只播一次"记录（`%AppData%\SlayTheSpire2\CharacterLines\state.json`） |
| `assets/CharacterLines/lines.json` | 出厂台词（打包进 PCK） |
| `build.ps1` | 编译 + 打包 PCK + 安装到 `mods\CharacterLines\` |
| `fix-encoding.ps1` | 修复 `.ps1` 的 UTF-8 BOM（Windows PowerShell 5.1 必需） |

### 数据流

```
游戏事件（进入火堆 NRestSiteRoom._Ready）
   └─ 补丁 Postfix
        ├─ PlayerContext：取玩家 / 角色 ID / 低血 / 遗物列表
        ├─ RestSiteConditions.For(...)：算出「当前满足哪些条件」+ 优先级顺序
        ├─ LineBank.Pick(scene, characterId, out usedCondition, conditions)
        │     └─ 按优先级与概率抽一句（见 §4）
        ├─ 写回 Header 文本（`MegaLabel.SetTextAutoSize`）
        └─ 若命中的是一次性条件（girya_maxed）→ OneShot.MarkDone(...)
```

### 台词文件来源（优先级从高到低）

1. `<mod 文件夹>\lines.json` —— 随包附带，**下载到 mod 的人可以直接就地改**
2. `%AppData%\SlayTheSpire2\CharacterLines\lines.json` —— 个人配置（mod 文件夹不可写时）
3. `res://CharacterLines/lines.json` —— PCK 里自带的出厂台词
4. 都没有 → 返回 null，保持原版文案

> 缺哪一份就自动生成哪份；`build.ps1` **不会覆盖已存在的 `lines.json`**（只会在缺 BOM 时无损补 BOM）。

---

## 3. 台词文件规范（lines.json）

### 结构

```jsonc
{
  "_defaultChance": 0.1,                            // 角色专属 ↔ DEFAULT 通用池
  "_relicFlavor": { "perRelic": 0.25, "max": 0.8 }, // 遗物台词占比

  "场景ID": {                                        // rest_site / ping / ...
    "_conditionChance": { "low_hp": 0.8 },           // 状态类条件的概率（场景级可覆盖全局）

    "角色ID": {                                      // IRONCLAD / SILENT / ... / DEFAULT
      "normal": ["平时说的"],
      "low_hp": ["血量低时说的"],
      "relic_shovel": ["拥有铲子时说的"]
    },

    "SILENT": ["只写数组也行 —— 等于只有 normal 池"]
  }
}
```

### 容错与约定

- `//` 注释、结尾多余逗号、UTF-8 BOM、字符串/字符串数组混写 —— 都能解析
- `_` 开头的键是设置/注释（`_defaultChance` / `_conditionChance` / `_relicFlavor` / 场景内 `_conditionChance`）
- **旧写法仍兼容**：`"_conditions": { "low_hp": { "_chance": 0.8, "IRONCLAD": [...] } }`
- 池名别名：`normal` / `default` / `正常` / `平时` 都算平时池
- 角色池没写的条件 → 自动回退该条件池的 `DEFAULT` → 再不行回退该角色的 `normal`
- 台词里**不要写 `{}`**（游戏本地化占位符语法，会被 SmartFormat 解释）

---

## 4. 概率与优先级方针

### 三套概率各管一段

| 设置 | 管什么 | 当前值 |
|---|---|---|
| `_defaultChance` | 有专属台词的角色，抽到**通用池 DEFAULT** 的概率 | `0.1`（10%） |
| `_conditionChance.<条件>` | **状态类**条件（`low_hp` / `candle_low` / `girya_*`）的概率 | 见下表 |
| `_relicFlavor` | **遗物氛围**台词的整体占比 = `perRelic × 遗物个数`（上限 `max`） | `0.25` / `0.8` |

### 火堆条件一览（14 个）

| 条件 | 触发 | 概率 |
|---|---|---|
| `candle_low` | 南瓜蜡烛剩余层数 ≤ 2 | 0.8 |
| `girya_maxed` | 壶铃已练满（`TimesLifted >= maxLifts`） | 1，**只播一次** |
| `low_hp` | 血量 ≤ 25%（复用 `CharacterModel.IsLowHealth`，与低血边框动画同源） | 0.8 |
| `relic_*`（10 个） | 拥有对应火堆遗物 | 按 `_relicFlavor` 组算 |
| `girya_progress` | 壶铃还没练满 | 0.15 |

遗物条件共 10 个：`relic_shovel`(铲子) / `relic_cleaver`(切肉刀) / `relic_tent`(微型帐篷) /
`relic_dream_catcher`(捕梦网) / `relic_mailbox`(小邮箱) / `relic_pillow`(皇家枕头) /
`relic_paels_growth`(佩尔增生组织) / `relic_humidifier`(石炉加湿器) / `relic_tea_set`(古茶具套装) /
`relic_fake_tea_set`(古茶具套装？？？，**有真货时不出现**)

### 优先级（从高到低，前面命中就不看后面）

```
candle_low      ← 能立刻操作的提示（该添火了）
girya_maxed     ← 壶铃练满的一次性纪念播报
low_hp          ← 血量低
relic_* 组      ← 遗物氛围（掷一次占比，命中后组内随机挑一个）
girya_progress  ← 还没练满时的鼓励
```

排序原则：**可操作的提示 > 一次性纪念 > 情绪 > 氛围彩蛋 > 鼓励**。

### 遗物台词占比

| 拥有的火堆遗物 | 说遗物台词的概率 |
|---|---|
| 1 个 | 25% |
| 2 个 | 50% |
| 3 个 | 75% |
| 4 个及以上 | 80%（上限） |

- **为什么按数量算**：拿越多越容易听到，玩家不会觉得遗物"白拿了"；也解决了旧方案里
  "每个遗物各 15% 独立掷 → 拿 3 个仍有 6 成是普通台词"以及"永远只有列表第一个遗物有机会说话"。
- **为什么封顶 80%**：留余量给角色自己的台词 —— 角色个性是这个 mod 的底色，
  拿满遗物就 99% 全说遗物台词反而丢了味道。想更强势就把 `max` 调到 `1`。
- **壶铃的"顺延"**：只有真的把 `girya_maxed` 播出去才记进 `state.json`；
  被更高优先级顶掉时不记账，下次火堆继续尝试。

---

## 5. 文案方针

- **场景基调**：火堆是回血 / 锻造的**安静**地方，不是战场。
  台词要短、轻、偏内心独白 —— 参考一代休息处的「嗯……」「有不少选择啊……」。
- **角色台词**：写"这个角色此刻会怎么想"，不要复述机制
  （反例：「我要使用锻炼选项。」正例：「是时候举举重了……」）。
- **氛围/动作描写**用中文括号包起来：`（你看着自己的肌肉，很是满意。）`；
  直接说的台词不加括号。
- **机制提示类**（该添火了 / 鼓励锻炼）可以直白，但保持语气克制。
- 默认台词写在 `DEFAULT` 池 → 全角色通用；想给某个角色单独写，就在它自己那段加同名池。
- 标点风格：`……` 表示停顿，`。` 收尾；避免感叹号堆砌。

---

## 6. 命名与工程约定

| 约定 | 说明 |
|---|---|
| 场景 ID | 小写下划线：`rest_site` / `ping` |
| 条件名 | 状态类 `low_hp` / `candle_low` / `girya_progress`；遗物类统一 `relic_<英文名>` |
| 遗物判定 | **用类型匹配**（`FindRelic<Shovel>`），不用 ID 字符串 —— 类名写错会在编译期报错，不会静默失效 |
| 反射访问 | 私有成员用 `AccessTools`，拿不到就返回 null / 走兜底分支，不抛异常 |
| mod 清单 | 必须 `<id>.json` + `<id>.dll` + `<id>.pck`，且清单里必须有 `"id"` 字段（游戏硬性要求） |
| 日志前缀 | 一律 `[CharacterLines]`，方便过滤 |
| `.ps1` 编码 | **UTF-8 with BOM**（否则 Windows PowerShell 5.1 按 GBK 解码会语法错误）；丢了就跑 `fix-encoding.ps1` |
| 一次性状态 | 存 `%AppData%\SlayTheSpire2\CharacterLines\state.json`（按 `种子 + 角色` 作 key） |
| 绝对路径文件 | 用 `System.IO`（更快更稳）；`res://` / `user://` 用 Godot `FileAccess` |

---

## 7. 测试方针

不开游戏也能验大半，靠 `_tools\preflight`（加载**真实的 sts2.dll** + 已安装的 mod DLL）：

- Harmony 补丁是否挂上 `NRestSiteRoom._Ready`
- 反射目标是否存在：私有字段 `_runState`、私有属性 `Header`、`CharacterModel.IsLowHealth`
- 就地 `lines.json` 路径解析是否正确
- 场景/角色抽取：专属池、通用池混合比例、未知角色回退 DEFAULT、另一个场景（ping）可抽
- 13 个遗物/状态条件：每个都能命中、`usedCondition` 报告正确、不污染平时随机
- 条件优先级：`girya_maxed` 排在前面时 30/30 优先
- 遗物台词占比：1/2/3/4 个遗物 ≈ 25/50/75/80%
- 遗物类型 → 条件映射：含**真假茶具互斥**的三种组合
- JSON 解析：新写法（条件挂角色下）与旧写法（`_conditions` 大栏）都认

**改动 → 必测对照表**

| 改了什么 | 必须确认 |
|---|---|
| 加/改台词文本 | 预检 JSON 解析通过；游戏内日志 `台词来源` 正确 |
| 加条件 | 预检：该条件能命中、不污染平时随机、优先级顺序符合预期 |
| 加遗物 | 预检：类型→条件映射、占比公式、真假互斥（如有） |
| 改概率 | 预检里的占比/命中率断言要同步更新 |
| 改 `.ps1` | 跑 `fix-encoding.ps1`，确认 BOM 在 |
| 改清单/文件名 | 确认 `<id>.json` / `<id>.dll` / `<id>.pck` 三件套齐全 |

---

## 8. 计划（TODO）

**P0 — 待接入**

- [ ] `ping`（多人催促）：JSON 已占位、抽取已验证可用；还差找到游戏里设置该文案的入口，
      然后照 `SceneRestSite.cs` 写 `ScenePing.cs`

**P1 — 扩展场景/条件**

- [ ] 更多场景：事件选项、宝箱、商店、战斗开始、Boss 战前
- [ ] 低血分档：再加 `critical_hp`（≤ 10%）说更虚弱的话（机制现成，加条件 + 加一行判定）
- [ ] `girya_progress` 也纳入遗物组（目前是独立的小概率）

**P2 — 内容与体验**

- [ ] 给每个角色写**遗物专属**台词（现在遗物台词全在 `DEFAULT`，所有角色共用一套）
- [ ] 首次获得某遗物时提高一次占比（"新玩具"加权），之后回归常态
- [ ] 多语言：把 JSON 改成 `{"zhs": {...}, "eng": {...}}`，用 `LocManager.Instance.Language` 选一份

**P3 — 工程**

- [ ] 游戏内配置界面（ModConfig）替代手改 JSON
- [ ] 装 .NET 9 SDK 后把构建切回标准 `dotnet build`（现在自动回退 Roslyn csc）
- [ ] 把 `_tools\preflight` 挪进仓库（现在在工作区，不在 git 里）

---

## 9. 决策记录（为什么这么定）

| 决定 | 理由 |
|---|---|
| 只改 `Header`，不动 `Description` | 悬停选项时游戏会改写 `Description`，动它会互相覆盖 |
| 火堆提示语自定义而非改本地化表 | 本地化表只能整表替换 / 合并，做不到"按角色 + 按状态"条件抽取 |
| 台词放自己的 `lines.json` 而非游戏本地化表 | 可就地改、可热重载（重进火堆生效）、不必重新打包 PCK |
| `_defaultChance` 从 25% 降到 10% | 火堆是安静场景，让角色更多"说自己那几句" |
| 遗物条件合并成一组 + 占比制 | 见 §4：避免"拿多个遗物仍常常落空"与"永远只说第一个遗物" |
| 遗物占比封顶 80% | 保留角色个性（见 §4） |
| `FlavorConditions` 抽成纯函数 | 真假茶具互斥这类规则必须可测；纯函数能直接喂"拥有的遗物类型"验证 |
| 壶铃练满只播一次 + 持久化 | 用户明确要求"之后不播报"；存 `state.json` 让读档/重启也不重复 |
| 一次性记录"播了才记账" | 被更高优先级顶掉时能顺延，符合"有更重要信息则推迟" |
| 遗物判定用类型而非 ID | 编译期即可发现写错；ID 字符串错了只会静默失效 |
| 低血沿用 `CharacterModel.IsLowHealth` | 与低血边框动画同一个临界点，阈值不自己拍脑袋 |
| `.ps1` 用 UTF-8 BOM | Windows PowerShell 5.1 按 ANSI/GBK 读取无 BOM 文件 → 中文注释破坏语法 |
| 放弃 `working-tree-encoding=UTF-8-BOM` | Git for Windows 不支持该转换，`git add` 直接 fatal；改用 `fix-encoding.ps1` |
| `build.ps1` 不覆盖 `lines.json` | 玩家就地改的台词不能被下次构建吞掉 |

---

## 10. 排障速查

日志：`%AppData%\SlayTheSpire2\logs\godot<时间>.log`，搜 `CharacterLines`

| 现象 | 先查 |
|---|---|
| 完全没生效 | 日志有没有 `[CharacterLines] loaded.`；mod 三件套是否齐全 |
| 改了台词没变化 | 改的是不是优先级更低的那份（mod 文件夹 > %AppData% > PCK）；是不是在火堆里改的（要出去再进来） |
| 台词退回原版 | 日志 `JSON 格式有误` / `读取失败` → 检查编码（UTF-8）与逗号引号；`台词来源` 看用的哪份 |
| 某遗物台词不出现 | 预检该条件能否命中；是否被更高优先级顶掉（`candle_low` > `girya_maxed` > `low_hp`） |
| `build.ps1` 报奇怪的语法错误 | BOM 丢了 → 跑 `fix-encoding.ps1` |
| 游戏更新后失效 | 私有成员改名 → 日志会报反射失败；mod 会保持原版文案，不会崩 |

---

## 11. 加新场景的两步流程

1. **JSON 加一段**（立刻可用，不影响运行）

   ```jsonc
   "场景ID": { "IRONCLAD": ["..."], "DEFAULT": ["..."] }
   ```

2. **写补丁**（照 `SceneRestSite.cs`）

   ```csharp
   [HarmonyPatch(typeof(某类), "某方法")]
   internal static class SceneXxx
   {
       private static void Postfix(...)
       {
           string? text = LineBank.Pick(Scenes.Xxx, characterId, out string? used, conditions);
           // 写回对应 Label；若 used 是一次性条件 → OneShot.MarkDone(...)
       }
   }
   ```

3. 在 `LineBank.Scenes` 补常量、在 `Conditions.cs` 补条件判定（能抽成纯函数就抽）
4. 预检加两条：该场景能抽到台词、条件不污染其他场景
