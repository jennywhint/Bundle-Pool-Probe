# Bundle Pool Probe

A mod for Stardew Valley 1.6 that turns Community Center bundle hunting into a controlled, save-aware farming loop — with dedicated maps that spawn exactly the items your save's bundles need.

一个面向《星露谷物语》1.6 的模组：用专属地图生成当前存档捆绑包所需的物品，把"靠运气的刷材料"变成可控的收集玩法。

---

## English

### Description

This mod adds custom maps designed to generate the items required for Community Center Bundles — including Remixed Bundles and bundles added by other mods, such as those from Stardew Valley Expanded. If you enjoy bundle progression but want a more controlled, less RNG-dependent way to gather bundle items, this mod provides dedicated locations where those items can be obtained.

Everything is read from **your own save**: the mod rebuilds its item pool from the actual bundle data of the current save, so any bundle set — vanilla, remixed, or mod-added — is supported automatically.

A green Warp Crystal is placed on your farm by default, usually near the Farmhouse. Interacting with it grants access to the Hub map, which connects to all bundle-item rooms. On custom farm maps the crystal may appear in an unexpected location — if you cannot find it, use the GMCM position option described below or explore the farm edges.

### Main Features

- **Custom bundle-item maps** — A Hub plus six dedicated rooms: Crafts Room, Pantry, Fish Tank, Boiler Room, Bulletin Board, and a **General Room**. Each room spawns the items required by the bundles of the matching Community Center area.
- **Remixed & mod-added bundles** — The item pool is rebuilt from your save's real bundle data, so Remixed Bundles and added bundle rooms (e.g. SVE) are picked up automatically.
- **Missing Bundle items** — The General Room carries the items of the Abandoned Joja Mart (the Missing Bundle) and of any other bundle rooms that don't map to the five fixed areas, including rooms added by mods.
- **Farm entrance that fits any farm** — The entrance patch targets the map your save actually uses, detected at runtime: all vanilla farms (Standard, Riverland, Forest, Hilltop, Wilderness, Four Corners, Beach, Meadowlands) and custom farm types are supported without manual configuration. Only the crystal's *position* may need adjusting on unusual farm layouts (GMCM option).
- **Fishing maps** — The Fish Tank room and the General Room's pond can be fished at any time and in any weather. Every rod-fish required by your bundles should be catchable there regardless of season, weather, or time-of-day restrictions. Pool fish are always hooked first, so normal casts land bundle fish instead of junk. In the Fish Tank room, crab-pot catches (lobster, crab, snail, …) spawn on the ground while rod fish live in the water.
- **Progressive room unlocking** (on by default) — Rooms open step by step, mirroring vanilla progression:
  | Room | Unlocks when |
  |---|---|
  | Crafts Room | You've met the Wizard and drunk his potion |
  | Pantry & Fish Tank | 1 bundle completed |
  | Boiler Room | 2 bundles completed |
  | Bulletin Board | 3 bundles completed |
  | General Room | The Abandoned Joja Mart is accessible (Missing Bundle available) |

  Locked rooms can't be entered (you'll be nudged back to the Hub), and they don't spawn or clean up items. Prefer the classic all-open style? One GMCM switch turns the whole progression off.
- **GMCM support** — Everything below is configurable in-game.

### GMCM Options

| Option | Default | Description |
|---|---|---|
| Cleanup Interval (days) | 1 | How often uncollected items are cleared from the rooms. `0` = never auto-cleanup. |
| Progressive Room Unlock | On | Follow vanilla progression (table above). Turn off to keep all rooms always open. |
| Deduplicate General Pool | Off | On: an item that appears in several unmapped bundle rooms counts once. Off: duplicates increase the item's spawn weight. |
| Daily Spawn Count (per room) | 2 | How many items spawn per day in each room. Low values occasionally fail to find a free tile; higher values fill rooms more reliably — but make bundle completion trivially fast. |

The **Warp Crystal position** (X/Y) lives in the Content Patcher pack's own section of the GMCM. The default spot is near the Farmhouse on the Standard farm; if your farm layout places it somewhere unreachable, nudge the coordinates there.

Player settings are stored in `config.json` inside the mod folder and survive mod updates.

### Console Commands (advanced/debug)

| Command | Effect |
|---|---|
| `bpp_debug` | Toggle verbose logging for the current session (useful when reporting issues) |
| `bpp_dump` | Print the current save's item pool |
| `bpp_rebuild` | Rebuild the item pool from the save's bundle data |
| `bpp_rawbundle` | Print raw bundle entries for a room |

### How to Install

1. Download and install [SMAPI](https://smapi.io).
2. Download and install [Content Patcher](https://www.nexusmods.com/stardewvalley/mods/1915).
3. Download and unzip this mod into your `Mods` folder.

### Requirements

- SMAPI
- Content Patcher
- [Generic Mod Config Menu (GMCM)](https://www.nexusmods.com/stardewvalley/mods/3788) — optional, for the configuration options

### Compatibility

**Tested and working**

- Stardew Valley Expanded (SVE) — the SVE bundle set is supported in my testing.
- Custom farm maps — supported via runtime farm-map detection, including custom farm types (e.g. SpaceCore-based layouts). Some small farms may place the Warp Crystal outside the walkable area with default settings; use the GMCM position option to correct this.
- Remixed Bundles and bundles modified without Unlockable Bundles — theoretically supported.

**Known incompatible**

- None encountered so far. If you run into a compatibility issue, please report it in the Posts or Bugs tab and I will look into adding support where possible.

### Uninstalling & upgrading

- **Upgrading between versions of this mod**: simply replace the mod folder and load your save as usual — chests and items you placed inside the custom rooms are kept.
- **Uninstalling**: items left inside the custom rooms belong to this mod's locations. Please collect anything you want to keep before uninstalling.
- **Never load and save the game with this mod uninstalled** — the save loader drops locations it doesn't recognize, so the custom rooms and everything inside them would be permanently removed from your save.

### Future Plans

- Unlockable Bundles framework support: bundles created through the Unlockable Bundles mod.
- Breakable containers: crates and barrels (mine-style) that can be filled with basics like Wood and Stone.
- Multiplayer hardening: spawn/cleanup currently run on every client; restricting them to the host is planned.

### Shout Outs

This mod was inspired by **Lnh's Community Center Item Output Points** by liunianhui. That mod provides a similar concept, but it only supports the game's default bundles — it does not support Remixed Bundles or added bundles such as those from SVE. I made this mod to fill that gap. Thanks to liunianhui for the original idea.

### How this mod was made

All maps in this mod were hand-drawn by the author in Tiled. The C# code was written with AI assistance.

---

## 中文

### 简介

喜欢社区中心的捆绑包收集玩法，又受够了满地图碰运气？这个模组为捆绑包材料提供了一批专属收集地图。模组读取的是**当前存档**的真实捆绑包数据——原版捆绑包、重混捆绑包（Remixed Bundles）、SVE 等模组新增的捆绑包，全部自动适配，不需要任何手动配置。

农场上会生成一个绿色传送水晶（默认在农舍附近），交互后进入枢纽地图，再前往各个收集房间。

### 主要功能

**六大收集房间**

枢纽地图连接六个房间：工艺室、食物储藏、水族箱、锅炉房、布告板、通用房。每个房间生成对应社区中心区域的捆绑包材料。

**迷失献祭也有去处**

废弃 Joja 超市（迷失献祭包）和所有无法归入五个固定区域的捆绑包房间——包括模组新增的房间——材料统一投放到**通用房**。

**农场入口全面适配**

入口补丁按存档实际使用的农场地图自动落点：原版全部八种农场（标准、河流、森林、山顶、荒野、四角、海滩、瑞奇草场），以及 SpaceCore/FTM 等自定义农场类型，都开箱即用。水晶的默认位置在农舍附近，个别农场布局下可能需要用 GMCM 微调坐标。

**钓鱼房：缺什么鱼，钓什么鱼**

水族箱房间和通用房的水池全年可钓——**不受季节、天气、时间段限制**。捆绑包需要的鱼竿鱼类理论上都能在这里钓到；正常情况下每竿必出池内鱼种，不会钓上垃圾。在水族箱房间里，蟹笼渔获（龙虾、螃蟹、蜗牛等）刷新在地面上——钓鱼和捡蟹笼物品两不误。

**逐步解锁（默认开启）**

房间按原版社区中心的推进节奏逐间开放：

| 房间 | 解锁条件 |
|---|---|
| 工艺室 | 找到法师、喝下药水 |
| 食物储藏、水族箱 | 完成任意 1 个捆绑包 |
| 锅炉房 | 完成任意 2 个捆绑包 |
| 布告板 | 完成任意 3 个捆绑包 |
| 通用房 | 废弃 Joja 超市开放（迷失献祭可用） |

未解锁的房间进不去（会被送回枢纽地图），也不会生成或清理物品。想要开局全开的传统玩法？GMCM 里一个开关就能关掉整套进度。

**GMCM 游戏内配置**

| 选项 | 默认值 | 说明 |
|---|---|---|
| 清理周期（天） | 1 | 房间里未拾取物品的清理间隔；0 = 不自动清理 |
| 房间逐步解锁 | 开启 | 按上表逐间解锁；关闭后所有房间常开 |
| 通用池去重 | 关闭 | 开启后，同一物品在多个未映射捆绑包里只计一份；关闭则重复物品出现概率更高 |
| 每日生成数量（按房间分别设置） | 2 | 每个房间每天生成的物品数量；数值太低偶尔会找不到空位导致生成失败，太高则收集毫无难度 |

传送水晶的位置（X/Y）在 GMCM 里 Content Patcher 补丁包的分区中调整。

玩家设置保存在模组目录下的 `config.json`，更新模组不会丢失。

### 安装方法

1. 安装 [SMAPI](https://smapi.io)
2. 安装 [Content Patcher](https://www.nexusmods.com/stardewvalley/mods/1915)（CP mod）
3. 下载本模组，解压到 Mods 文件夹

### 前置需求

- SMAPI
- Content Patcher
- [GMCM](https://www.nexusmods.com/stardewvalley/mods/3788)（可选，用于游戏内调整设置）

### 兼容性

**已测试可用**

- Stardew Valley Expanded（SVE）：SVE 新增的捆绑包已实测支持
- 自定义农场地图：运行时自动检测，含 SpaceCore/FTM 等自定义农场类型；个别小农场可能需要用 GMCM 微调水晶位置
- 重混捆绑包，以及未通过 Unlockable Bundles 修改的捆绑包：理论上支持

**已知不兼容**

暂无。如遇到问题，欢迎在 Posts 或 Bugs 页反馈。

### 控制台命令（调试用）

| 命令 | 作用 |
|---|---|
| `bpp_debug` | 切换本会话的详细日志（反馈问题时请开着） |
| `bpp_dump` | 打印当前存档的物品池 |
| `bpp_rebuild` | 按存档捆绑包数据重建物品池 |
| `bpp_rawbundle` | 打印指定房间的原始捆绑包条目 |

### 升级与卸载

- **升级模组版本**：直接替换 Mods 文件夹里的模组文件，然后正常读档即可——房间里放置的箱子和物品会原样保留。
- **卸载模组**：留在收集房间里的物品归属于本模组的地图，卸载前请先取走想要保留的东西。
- **绝对不要在卸载模组后读档再存档**——存档加载器会丢弃无法识别的地点，自定义房间连同里面的一切会被从存档里永久移除。

### 未来计划

- Unlockable Bundles 框架兼容：支持通过 UB 创建的捆绑包
- 可破坏容器：把木头、石头等塞进矿井风格的木箱和木桶
- 多人联机加固：目前生成/清理会在所有客户端执行，计划改为仅由主机执行

### 致谢

本模组的灵感来自 liunianhui 的 **Lnh's Community Center Item Output Points**——概念相似，但它只支持原版默认捆绑包，不支持重混捆绑包和 SVE 这类新增捆绑包。我做这个模组就是为了补上这块空缺，感谢原作者的创意。

### 关于本模组的制作

本模组的全部地图均由作者使用 Tiled 手工绘制；代码部分由 AI 编写完成。
