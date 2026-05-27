# RimMind - Actions

> **⚠️ H2 阶段空壳化通知 (2026-05)**
> 本模块自 H2 阶段起已空壳化。所有 IActionRule 实现已迁移到 `RimMind-Core/Source/Infrastructure/Mechanisms/`。
> 原计划的 M 阶段（Actions 项目整体删除）已取消。Actions 模块保留为 `ICompositeToolCall` 编排多个原子 ToolCall 的实现载体。

AI 意图到游戏动作的执行库，将 AI 的决策（如分配工作、强制休息）映射为具体的 RimWorld 游戏操作。

## RimMind 是什么

RimMind 是一套 AI 驱动的 RimWorld 模组套件，通过接入大语言模型（LLM），让殖民者拥有人格、记忆、对话和自主决策能力。

## 子模组列表与依赖关系

| 模组 | 职责 | 依赖 | GitHub |
|------|------|------|--------|
| RimMind-Core | API 客户端、请求调度、上下文打包 | Harmony | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Core) |
| **RimMind-Actions** | **AI 控制小人的动作执行库** | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Actions) |
| RimMind-Advisor | AI 扮演小人做出工作决策 | Core, Actions | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Advisor) |
| RimMind-Dialogue | AI 驱动的对话系统 | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Dialogue) |
| RimMind-Memory | 记忆采集与上下文注入 | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Memory) |
| RimMind-Personality | AI 生成人格与想法 | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Personality) |
| RimMind-Storyteller | AI 叙事者，智能选择事件 | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Storyteller) |

```
Core ── Actions ── Advisor
  ├── Dialogue
  ├── Memory
  ├── Personality
  └── Storyteller
```

## H2 空壳化状态

### 迁移概要

H2 阶段将所有 24 个 IActionRule 实现迁移至 Core 中的 17 个 Mechanism 类。Actions 模块不再包含任何业务逻辑，仅保留以下文件：

| 保留文件 | 说明 |
|----------|------|
| `RimMindActionsAPI.cs` | [Obsolete] 转发器，转发到 `RimMindAPI.Tools` |
| `RimMindActionsMod.cs` | 仅保留 Settings 初始化 |
| `ActionsBridge.cs` | 空壳，不再订阅任何事件 |
| `RimMindActionsSettings.cs` | 玩家审批阈值设置 |

### 模块保留原因

原计划在 M 阶段整体删除 Actions 项目。现取消该计划，Actions 模块保留为 `ICompositeToolCall` 编排多个原子 ToolCall 的实现载体。未来复合动作（如"去 A 点拿物品再送到 B 点"）的编排逻辑将在此模块实现。

### 迁移对照

24 个 IActionRule → 17 个 Mechanism 类（位于 `RimMind-Core/Source/Infrastructure/Mechanisms/`），具体对照关系详见 Core 模块文档。

## 安装步骤

### 从源码安装

**Linux/macOS:**
```bash
git clone git@github.com:RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Actions.git
cd RimWorld-RimMind-Mod-Actions
./script/deploy-single.sh <your RimWorld path>
```

**Windows:**
```powershell
git clone git@github.com:RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Actions.git
cd RimWorld-RimMind-Mod-Actions
./script/deploy-single.ps1 <your RimWorld path>
```

### 从 Steam 安装

1. 安装 [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) 前置模组
2. 安装 RimMind-Core
3. 安装 RimMind-Actions
4. 在模组管理器中确保加载顺序：Harmony → Core → Actions

## 快速开始

### 填写 API Key

1. 启动游戏，进入主菜单
2. 点击 **选项 → 模组设置 → RimMind-Core**
3. 填写你的 **API Key** 和 **API 端点**
4. 填写 **模型名称**（如 `gpt-4o-mini`）
5. 点击 **测试连接**，确认显示"连接成功"

### 启用 Actions

Actions 本身无需额外配置，安装后自动生效。配合 RimMind-Advisor 使用时，Advisor 会自动调用 Actions 执行 AI 决策。

在模组设置中可按风险级别禁用特定动作。

## 核心功能

> **注意**：以下功能描述反映的是 H2 迁移前的状态。实际业务逻辑已迁移至 Core 的 Mechanism 类，Actions 模块仅保留转发和设置功能。

### 意图到动作的映射

AI 返回的意图 ID（如 `assign_work`、`force_rest`）自动转换为 RimWorld 游戏操作。24 个内置动作覆盖工作、社交、战斗、心情等场景。

### 精确到坐标的工作分配

AI 可以指定具体坐标的工作目标，比如"去 (45,32) 挖掘那块花岗岩"，而非盲目接受默认选择。`GetWorkTargets` API 枚举地图上所有可用工作目标，按距离排序供 AI 选择。

### 批量执行

支持多步骤任务序列，自动处理队列逻辑，首个动作打断当前任务，后续追加。同一小人的 Job 序列自动处理 `requestQueueing` 逻辑。

### 延迟执行

DelayedActionQueue 将动作投递到主线程执行，避免非主线程调用游戏 API。支持可配置的延迟时间和队列上限，队列跨存档持久化。

### 提示数据

`GetActionHintData` API 为 Advisor 提供动作的上下文提示数据（如当前可食用的愉悦食物列表），帮助 AI 做出更精确的决策。

### 风险分级系统

每个动作标记风险等级，玩家可在设置中控制：

| 等级 | 含义 | 示例 |
|------|------|------|
| Low | 可随时撤销 | move_to, assign_work, cancel_job, undraft |
| Medium | 生存/社交/工作类轻微副作用 | force_rest, eat_food, tend_pawn, rescue_pawn, draft, set_work_priority, social_relax, give_item, romance_attempt, add_thought |
| High | 重大行为改变 | arrest_pawn, recruit_agree, drop_weapon, romance_breakup, adjust_faction, inspire_work, inspire_shoot, inspire_trade |
| Critical | 不可逆或影响全局 | trigger_mental_state, trigger_incident |

### 内置动作清单

| 类别 | 动作 |
|------|------|
| 工作 | assign_work, set_work_priority, cancel_job |
| 生存 | force_rest, eat_food, tend_pawn, rescue_pawn |
| 社交 | social_relax, give_item, romance_attempt, romance_breakup |
| 战斗 | draft, undraft, drop_weapon |
| 移动 | move_to |
| 心情 | inspire_work, inspire_shoot, inspire_trade, add_thought, trigger_mental_state |
| 关系 | recruit_agree, adjust_faction |
| 事件 | trigger_incident |

## 设置项

在模组设置中可配置：

- **启用动作系统** — 所有 AI 动作的总开关
- **允许 AI 执行的动作** — 逐个勾选禁用，按风险级别标注
- **延迟动作队列上限** — 队列最大容量（默认 50）
- **默认延迟** — 延迟动作的默认等待时间（默认 1.5 秒）
- **快捷按钮** — 一键禁用所有高风险 / 极高风险动作，重置为默认

High/Critical 风险动作以红色背景标注。

## 常见问题

**Q: Actions 可以单独使用吗？**
A: Actions 本身不直接调用 AI，需要配合 Advisor 或其他模块使用。它提供动作执行能力，AI 决策由其他模块负责。

**Q: 高风险动作会自动执行吗？**
A: Actions 本身不区分自动执行和手动审批。配合 Advisor 使用时，被玩家禁用的动作不会执行。你可以在模组设置中取消勾选不想让 AI 使用的动作。

**Q: 可以禁用特定动作吗？**
A: 可以。在模组设置中按风险级别查看，勾选禁用不想要的动作。

**Q: 可以只让 AI 管日常，不管战斗吗？**
A: 可以。在设置中禁用 draft、drop_weapon 等战斗相关动作即可。

**Q: H2 空壳化后 Actions 还需要安装吗？**
A: 需要。Actions 仍保留 Settings 初始化和转发功能，且未来将承载 ICompositeToolCall 编排逻辑。Advisor 等模块仍依赖 Actions 项目。

## 致谢

本项目开发过程中参考了以下优秀的 RimWorld 模组：

- [RimTalk](https://github.com/jlibrary/RimTalk.git) - 对话系统参考
- [RimTalk-ExpandActions](https://github.com/sanguodxj-byte/RimTalk-ExpandActions.git) - 动作扩展参考
- [NewRatkin](https://github.com/solaris0115/NewRatkin.git) - 种族模组架构参考
- [VanillaExpandedFramework](https://github.com/Vanilla-Expanded/VanillaExpandedFramework.git) - 框架设计参考

## 贡献

欢迎提交 Issue 和 Pull Request！如果你有任何建议或发现 Bug，请通过 GitHub Issues 反馈。

---

# RimMind - Actions (English)

> **⚠️ H2 Hollowing-out Notice (2026-05)**
> This module has been hollowed out since the H2 phase. All IActionRule implementations have been migrated to `RimMind-Core/Source/Infrastructure/Mechanisms/`.
> The originally planned M phase (complete deletion of the Actions project) has been cancelled. The Actions module is retained as the implementation carrier for `ICompositeToolCall` to orchestrate multiple atomic ToolCalls.

The action execution library that maps AI intents (like assign_work, force_rest) into concrete RimWorld game operations.

## What is RimMind

RimMind is an AI-driven RimWorld mod suite that connects to Large Language Models (LLMs), giving colonists personality, memory, dialogue, and autonomous decision-making.

## Sub-Modules & Dependencies

| Module | Role | Depends On | GitHub |
|--------|------|------------|--------|
| RimMind-Core | API client, request dispatch, context packaging | Harmony | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Core) |
| **RimMind-Actions** | **AI-controlled pawn action execution** | Core | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Actions) |
| RimMind-Advisor | AI role-plays colonists for work decisions | Core, Actions | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Advisor) |
| RimMind-Dialogue | AI-driven dialogue system | Core | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Dialogue) |
| RimMind-Memory | Memory collection & context injection | Core | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Memory) |
| RimMind-Personality | AI-generated personality & thoughts | Core | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Personality) |
| RimMind-Storyteller | AI storyteller, smart event selection | Core | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Storyteller) |

## H2 Hollowing-out Status

### Migration Summary

All 24 IActionRule implementations have been migrated to 17 Mechanism classes in Core. The Actions module no longer contains any business logic, retaining only the following files:

| Retained File | Description |
|---------------|-------------|
| `RimMindActionsAPI.cs` | [Obsolete] Forwarder, delegates to `RimMindAPI.Tools` |
| `RimMindActionsMod.cs` | Settings initialization only |
| `ActionsBridge.cs` | Hollow shell, no longer subscribes to any events |
| `RimMindActionsSettings.cs` | Player approval threshold settings |

### Reason for Retention

The originally planned M phase (complete deletion of the Actions project) has been cancelled. The Actions module is retained as the implementation carrier for `ICompositeToolCall` to orchestrate multiple atomic ToolCalls. Future composite action orchestration logic (e.g., "go to point A to pick up item, then deliver to point B") will be implemented in this module.

### Migration Mapping

24 IActionRule → 17 Mechanism classes (located in `RimMind-Core/Source/Infrastructure/Mechanisms/`). See Core module documentation for detailed mapping.

## Installation

### Install from Source

**Linux/macOS:**
```bash
git clone git@github.com:RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Actions.git
cd RimWorld-RimMind-Mod-Actions
./script/deploy-single.sh <your RimWorld path>
```

**Windows:**
```powershell
git clone git@github.com:RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Actions.git
cd RimWorld-RimMind-Mod-Actions
./script/deploy-single.ps1 <your RimWorld path>
```

### Install from Steam

1. Install [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)
2. Install RimMind-Core
3. Install RimMind-Actions
4. Ensure load order: Harmony → Core → Actions

## Quick Start

### API Key Setup

1. Launch the game, go to main menu
2. Click **Options → Mod Settings → RimMind-Core**
3. Enter your **API Key** and **API Endpoint**
4. Enter your **Model Name** (e.g., `gpt-4o-mini`)
5. Click **Test Connection** to confirm

### Enable Actions

Actions works automatically after installation. When used with RimMind-Advisor, the Advisor calls Actions to execute AI decisions.

You can disable specific actions by risk level in mod settings.

## Key Features

> **Note**: The feature descriptions below reflect the pre-H2 migration state. Actual business logic has been migrated to Core's Mechanism classes. The Actions module only retains forwarding and settings functionality.

- **Intent-to-Action Mapping**: AI intent IDs automatically convert to RimWorld operations. 24 built-in actions cover work, social, combat, mood, and more.
- **Precise Work Assignment**: AI can specify exact map coordinates for work targets, e.g., "mine granite at (45,32)" instead of accepting default choices.
- **Risk Level System**: Each action is tagged with risk level (Low/Medium/High/Critical). Players can disable specific actions in settings.
- **Batch Execution**: Supports multi-step job sequences with automatic queue management. First action interrupts current task, subsequent actions are appended.
- **Delayed Execution**: DelayedActionQueue dispatches actions to the main thread, avoiding non-main-thread game API calls. Queue persists across saves.

## FAQ

**Q: Can Actions be used alone?**
A: Actions doesn't call AI directly. It needs Advisor or other modules to provide AI decisions. It provides the execution capability.

**Q: Will high-risk actions execute automatically?**
A: Actions itself does not distinguish between automatic and manual execution. When used with Advisor, actions disabled by the player will not execute. You can uncheck any action you don't want the AI to use in mod settings.

**Q: Can I disable specific actions?**
A: Yes. In mod settings, view actions by risk level and check to disable unwanted ones.

**Q: Can I let AI handle daily tasks but not combat?**
A: Yes. Disable combat-related actions like draft and drop_weapon in settings.

**Q: Do I still need to install Actions after H2 hollowing-out?**
A: Yes. Actions still retains Settings initialization and forwarding functionality, and will carry ICompositeToolCall orchestration logic in the future. Modules like Advisor still depend on the Actions project.

## Acknowledgments

This project references the following excellent RimWorld mods:

- [RimTalk](https://github.com/jlibrary/RimTalk.git) - Dialogue system reference
- [RimTalk-ExpandActions](https://github.com/sanguodxj-byte/RimTalk-ExpandActions.git) - Action expansion reference
- [NewRatkin](https://github.com/solaris0115/NewRatkin.git) - Race mod architecture reference
- [VanillaExpandedFramework](https://github.com/Vanilla-Expanded/VanillaExpandedFramework.git) - Framework design reference

## Contributing

Issues and Pull Requests are welcome! If you have any suggestions or find bugs, please feedback via GitHub Issues.
