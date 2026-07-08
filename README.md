# RimMind - Actions

> **H2 阶段迁移通知 (2026-05)**
> 24 个内置 IActionRule 已迁移至 Core Mechanism 系统。本模组现为组合工具（Composite ToolCall）编排层。

组合工具（Composite ToolCall）编排层，将 Core 提供的原子 ToolCall 组合为高级动作。

## RimMind 是什么

RimMind 是一套 AI 驱动的 RimWorld 模组套件，通过接入大语言模型（LLM），让殖民者拥有人格、记忆、对话和自主决策能力。

## 子模组列表与依赖关系

| 模组 | 职责 | 依赖 | GitHub |
|------|------|------|--------|
| RimMind-Core | API 客户端、请求调度、上下文打包 | Harmony | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Core) |
| **RimMind-Actions** | **组合工具编排层** | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Actions) |
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

H2 阶段将所有 24 个 IActionRule 实现迁移至 Core 中的 17 个 Mechanism 类。Actions 模块现在承载组合工具编排，包含以下文件：

| 保留文件 | 说明 |
|----------|------|
| `CompositeToolCallBase.cs` | 抽象基类：ExecuteAtomicAsync + 共享 JSON 辅助方法 |
| `CompositeToolRegistrar.cs` | 反射发现并注册所有 CompositeToolCallBase 子类 |
| `StabilizeRestCompositeTool.cs` | 组合工具：undraft → force_rest |
| `RimMindActionsMod.cs` | Mod 入口，反射注册组合工具 |

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

## 当前功能

H2 阶段后，Actions 模组仅承载组合工具（Composite ToolCall）编排：

- **CompositeToolCallBase** 抽象基类提供原子工具调用编排 + JSON 辅助方法
- **CompositeToolRegistrar** 通过反射自动发现并注册所有组合工具
- 当前实现：`actions.stabilize_rest`（解除征召 → 强制休息）

新增组合工具只需继承 `CompositeToolCallBase`，无需修改注册代码。

## 常见问题

**Q: Actions 可以单独使用吗？**
A: Actions 本身不直接调用 AI，需要配合 Advisor 或其他模块使用。它提供组合工具执行能力，AI 决策由其他模块负责。

**Q: H2 空壳化后 Actions 还需要安装吗？**
A: 需要。Actions 承载 ICompositeToolCall 编排逻辑，Advisor 等模块仍依赖 Actions 项目。

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

> **H2 Phase Migration Notice (2026-05)**
> 24 built-in IActionRules have been migrated to Core's Mechanism system. This module is now the Composite ToolCall orchestration layer.

Composite ToolCall orchestration layer. Combines atomic ToolCalls from Core into high-level actions.

## What is RimMind

RimMind is an AI-driven RimWorld mod suite that connects to Large Language Models (LLMs), giving colonists personality, memory, dialogue, and autonomous decision-making.

## Sub-Modules & Dependencies

| Module | Role | Depends On | GitHub |
|--------|------|------------|--------|
| RimMind-Core | API client, request dispatch, context packaging | Harmony | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Core) |
| **RimMind-Actions** | **Composite ToolCall orchestration layer** | Core | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Actions) |
| RimMind-Advisor | AI role-plays colonists for work decisions | Core, Actions | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Advisor) |
| RimMind-Dialogue | AI-driven dialogue system | Core | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Dialogue) |
| RimMind-Memory | Memory collection & context injection | Core | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Memory) |
| RimMind-Personality | AI-generated personality & thoughts | Core | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Personality) |
| RimMind-Storyteller | AI storyteller, smart event selection | Core | [Link](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Storyteller) |

## H2 Hollowing-out Status

### Migration Summary

All 24 IActionRule implementations have been migrated to 17 Mechanism classes in Core. The Actions module now carries composite tool orchestration, containing the following files:

| Retained File | Description |
|---------------|-------------|
| `CompositeToolCallBase.cs` | Abstract base: ExecuteAtomicAsync + shared JSON helpers |
| `CompositeToolRegistrar.cs` | Reflective discovery and registration of CompositeToolCallBase subclasses |
| `StabilizeRestCompositeTool.cs` | Composite tool: undraft → force_rest |
| `RimMindActionsMod.cs` | Mod entry, reflective composite tool registration |

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

## Current Features

After the H2 phase, the Actions module only carries composite ToolCall orchestration:

- **CompositeToolCallBase** abstract base provides atomic tool call orchestration + JSON helpers
- **CompositeToolRegistrar** auto-discovers and registers all composite tools via reflection
- Current implementation: `actions.stabilize_rest` (undraft → force_rest)

Adding a new composite tool only requires inheriting `CompositeToolCallBase` — no registration code changes needed.

## FAQ

**Q: Can Actions be used alone?**
A: Actions doesn't call AI directly. It needs Advisor or other modules to provide AI decisions. It provides composite tool execution capability.

**Q: Do I still need to install Actions after H2 hollowing-out?**
A: Yes. Actions carries the ICompositeToolCall orchestration logic. Modules like Advisor still depend on the Actions project.

## Acknowledgments

This project references the following excellent RimWorld mods:

- [RimTalk](https://github.com/jlibrary/RimTalk.git) - Dialogue system reference
- [RimTalk-ExpandActions](https://github.com/sanguodxj-byte/RimTalk-ExpandActions.git) - Action expansion reference
- [NewRatkin](https://github.com/solaris0115/NewRatkin.git) - Race mod architecture reference
- [VanillaExpandedFramework](https://github.com/Vanilla-Expanded/VanillaExpandedFramework.git) - Framework design reference

## Contributing

Issues and Pull Requests are welcome! If you have any suggestions or find bugs, please feedback via GitHub Issues.
