# 火堆台词 · 角色版 (RestSitePrompts)

把火堆（休息处）顶部那句固定的 **“我该做什么呢？”** 换成 **按当前角色 + 随机** 抽取的台词。

- 铁甲战士 → “稍作调整……继续杀戮……”“稍息……前进……”
- 静默猎手 → “……稍作调整……”“……”
- 故障机器人 / 储君 / 亡灵契约师 → 各自一套
- 没写专属台词的角色（包括你以后加的 mod 角色）→ 只用 `DEFAULT` 里的通用台词
- 原版角色也有 25% 概率随机到通用台词

---

## 1. 怎么加台词

### 优先级：从下往上看，先命中的先用

| 顺序 | 文件 | 谁改的 | 什么时候用 |
|---|---|---|---|
| 1 | `游戏目录\mods\RestSitePrompts\lines.json` | 想临时定制这个 mod 的人 | **默认就是它**（打包时一起放进去的） |
| 2 | `%AppData%\SlayTheSpire2\RestSitePrompts\lines.json` | 自己机器上的个人配置 | 上一条不存在时（例如 mod 文件夹不可写） |
| 3 | `RestSitePrompts.pck` 里自带的默认台词 | mod 作者（你） | 前两条都不存在时 |
| 4 | 游戏原版“我该做什么呢？” | 官方 | 全都没有时 |

> 想要哪一份生效，让更靠前的那份**不存在**即可（删掉或改名）。
> 具体这次用的是哪一份，游戏日志里会写 `[RestSitePrompts] 台词来源：...`。

### 场景 A：别人下载了你打包好的 mod，想马上改几句

直接打开 mod 文件夹里的 `lines.json`（和 `RestSitePrompts.dll` 放一起），改完**退出火堆再进去**就生效，
不用重新编译、不用重启游戏：

```
mods\RestSitePrompts\
  RestSitePrompts.dll
  RestSitePrompts.pck
  RestSitePrompts.json     ← 游戏读的清单
  lines.json               ← 改这个
  README.md                ← 就是本文档，打包时一起放进去的
```

### 场景 B：不想动 mod 文件夹（或 mod 装在创意工坊只读目录）

改这个文件（进过一次游戏后会自动生成）：

```
%AppData%\SlayTheSpire2\RestSitePrompts\lines.json
```

### 场景 C：改 mod 自带的默认台词（要重新打包，分享给别人时用）

编辑源码里的 `assets/RestSitePrompts/rest_site_prompts.json`，然后跑一次 `build.ps1`。

> `build.ps1` **不会覆盖已存在的 `lines.json`**，所以你（或别人）就地改的台词不会被下一次构建吞掉。
> 想恢复出厂设置：删掉 `lines.json`，再跑一次 `build.ps1`。

### 写台词时的注意事项

- 结构就是「角色 ID → 台词数组」：

  ```json
  {
    "IRONCLAD": [
      "稍作调整……继续杀戮……",
      "这是我新加的一句。"
    ],
    "DEFAULT": [
      "我该做什么呢……"
    ]
  }
  ```

- 每条用英文双引号包住，多条之间用英文逗号隔开（最后一条加不加逗号都行）。
- 允许写 `//` 注释；以 `_` 开头的键会被当成注释字段忽略。
- 一个角色只写一句也行：`"IRONCLAD": "只有一句台词"`。
- **不要**在台词里写 `{` `}`（那是游戏本地化的占位符语法，会解析报错）。
- 换行写 `\n`。
- 写错了不会让游戏崩：日志会提示 `JSON 格式有误`，然后自动退回下一份能用的台词。

---

## 2. 角色 ID 对照

| 角色 ID | 中文名 |
|---|---|
| `IRONCLAD` | 铁甲战士 |
| `SILENT` | 静默猎手 |
| `DEFECT` | 故障机器人 |
| `REGENT` | 储君 |
| `NECROBINDER` | 亡灵契约师 |
| `DEFAULT` | 通用（不是角色，兜底用） |

**给 mod 角色写台词**：在 JSON 里加一个以它的角色 ID 为名的数组即可，例如 `"WATCHER": ["……"]`。
ID 就是游戏存档/本地化里用的那个大写 ID（可参考 `_ref\loc_zhs` 里各角色 `xxx.title` 的前缀）。
没写的角色 → 自动只用 `DEFAULT`。

**通用台词出现概率**：默认 25%（专属 75%）。想让它永远只说自己的台词，把 `PromptLineStore.cs`
里的 `DefaultLineChance` 改成 `0`，再重新 `build.ps1`。

---

## 3. 编译 & 安装

```powershell
cd D:\sts2modtest\RestSitePrompts
powershell -ExecutionPolicy Bypass -File build.ps1
```

脚本做四件事：

1. 编译 `RestSitePrompts.dll`
   - 有 **.NET 9+ SDK** → 标准 `dotnet build`；
   - 只有 .NET 8 SDK（本机当前情况）→ 自动改用 SDK 自带 Roslyn `csc`，
     引用游戏目录 `data_sts2_windows_x86_64\` 里自带的 .NET 9 运行时程序集编译。
     （所以不装 .NET 9 SDK 也能构建；想用标准方式再装。）
2. 把 `assets/` 打包成 `RestSitePrompts.pck`（脚本 `pack_godot_pck.py`，来自模板 `_tools/`）。
3. 复制到 `游戏目录\mods\RestSitePrompts\`：
   `RestSitePrompts.dll` + `.pck` + `RestSitePrompts.json` + `lines.json`（已存在则不覆盖）+ `README.md` / `THIRD_PARTY.md`。
4. 打印这次改台词该去哪个文件。

常用参数：

```powershell
# 游戏装在别处
powershell -ExecutionPolicy Bypass -File build.ps1 -GameDir "X:\...\Slay the Spire 2"
# 只构建不安装（产物在 dist\RestSitePrompts）
powershell -ExecutionPolicy Bypass -File build.ps1 -SkipInstall
# 输出到指定目录
powershell -ExecutionPolicy Bypass -File build.ps1 -OutDir "D:\myout"
```

自动探测不到游戏目录时，在本目录建一个 `game_dir.txt`，写入游戏根目录路径即可。

> **注意（重要的坑）**：Mod 清单必须叫 `<id>.json`（这里是 `RestSitePrompts.json`），
> DLL/PCK 必须叫 `<id>.dll` / `<id>.pck`，而且清单里必须有 `"id"` 字段 ——
> 游戏代码写死了这三条规则（缺 `id` 会直接报 `missing the 'id' field! This is not allowed.` 并拒绝加载）。
> 源码里的 `mod_manifest.json` 只是**源文件**，脚本复制时会改名成 `RestSitePrompts.json`。

---

## 4. git

本目录就是一个 git 仓库（已经做过一次初始提交）。常用操作：

```powershell
cd D:\sts2modtest\RestSitePrompts
git status
git add -A
git commit -m "改了几句铁甲战士的台词"
```

**第一次用之前先改成你自己的身份**（我建仓库时用的是占位身份）：

```powershell
git config user.name  "你的名字"
git config user.email "你的邮箱@example.com"
# 如果想把那次初始提交也改成你的名字：
git commit --amend --reset-author --no-edit
```

想推到 GitHub：

```powershell
git remote add origin https://github.com/你的用户名/仓库名.git
git branch -M main
git push -u origin main
```

`.gitignore` 已经排除 `bin/`、`dist/`、`game_dir.txt` 等构建产物和本地配置，所以仓库里只有源码和台词。

---

## 5. 调试

游戏日志：`%AppData%\SlayTheSpire2\logs\godot<时间>.log`，搜 `RestSitePrompts`：

- `[RestSitePrompts] loaded.` —— mod 加载成功
- `[RestSitePrompts] 已生成可编辑台词文件：...` —— 台词文件已生成（并告诉你生成在哪）
- `[RestSitePrompts] 台词来源：...` —— 这次用的是哪一份
- `[RestSitePrompts] JSON 格式有误：...` —— 台词文件写错了（会自动退回下一份继续跑）

改完没生效？检查：

1. 是不是改了「优先级更低」的那份（比如 mod 文件夹里有 `lines.json` 时，`%AppData%` 那份不生效）；
2. 是不是在火堆房间里改的 —— 台词在进入火堆时抽取，所以要出去再进来一次；
3. JSON 语法（日志里有提示）。

---

## 6. 原理（方便继续改造）

原版逻辑在 `MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom._Ready()`：

```csharp
Header.SetTextAutoSize(new LocString("rest_site_ui", "PROMPT").GetFormattedText());
// rest_site_ui.json 里 "PROMPT": "我该做什么呢？"
```

本 mod 的补丁（`RestSitePromptHeaderPatch.cs`）挂在同一个方法上：

- **Postfix**：等原版执行完，再按角色抽一句台词写进 `Header`；
- `Header` 是私有属性（Godot `[Export]`），用 `AccessTools.PropertyGetter` 反射取；
- `_runState` 是私有字段，用 `AccessTools.FieldRefAccess` 取，再 `LocalContext.GetMe(...)` 拿本地玩家的 `Character.Id.Entry`；
- 火堆里悬停选项改的是 `Description` 标签，不会覆盖 `Header`，两边互不干扰；
- `lines.json` 在每次进火堆时重新读取（所以改完重进就能看到效果），不需要重启游戏。

想继续加场景（战斗中的想法气泡 `NThoughtBubbleVfx`、事件选项等），
照 `RestSitePromptHeaderPatch.cs` 再挂一个补丁，台词库直接复用 `PromptLineStore`。

---

## 7. 已知限制

- **多人游戏**：取的是「本地玩家」的角色，所以两台机器看到的台词可能不同（纯外观，不影响联机判定）。
  想统一取 1 号位玩家，把 `CurrentCharacterId()` 里的 `LocalContext.GetMe(runState) ?? ...` 去掉。
- 只改火堆顶部提示语，不改休息/锻造等选项的说明文字。
- 台词文件不随游戏语言切换（要做多语言，可把 JSON 改成 `{"zhs": {...}, "eng": {...}}` 再用
  `LocManager.Instance.Language` 选一份）。
- 角色 ID 是运行时读的，新增角色只要 ID 不重复就自动支持，不用改代码。
- 游戏更新后若私有成员改名，补丁里的反射会失败：日志会报错，游戏表现为**保持原版文案**，不会崩。
