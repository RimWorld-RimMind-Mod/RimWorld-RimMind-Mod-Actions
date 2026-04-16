# RimMind - Actions

AI 意图到游戏动作的执行库，将 AI 的决策（如分配工作、强制休息）映射为具体的 RimWorld 游戏操作。

## RimMind 是什么

RimMind 是一套 AI 驱动的 RimWorld 模组套件，通过接入大语言模型（LLM），让殖民者拥有人格、记忆、对话和自主决策能力。

## 子模组列表与依赖关系

| 模组 | 职责 | 依赖 |
|------|------|------|
| RimMind-Core | API 客户端、请求调度、上下文打包 | Harmony |
| **RimMind-Actions** | **AI 控制小人的动作执行库** | Core |
| RimMind-Advisor | AI 扮演小人做出工作决策 | Core, Actions |
| RimMind-Dialogue | AI 驱动的对话系统 | Core |
| RimMind-Memory | 记忆采集与上下文注入 | Core |
| RimMind-Personality | AI 生成人格与想法 | Core |
| RimMind-Storyteller | AI 叙事者，智能选择事件 | Core |

```
Core ── Actions ── Advisor
  ├── Dialogue
  ├── Memory
  ├── Personality
  └── Storyteller
```

## 安装步骤

1. 安装 [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) 前置模组
2. 安装 RimMind-Core
3. 安装 RimMind-Actions
4. 在模组管理器中确保加载顺序：Harmony → Core → Actions

<!-- ![安装步骤](images/install-steps.png) -->

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

## 截图展示

<!-- ![动作设置界面](images/screenshot-actions-settings.png) -->
<!-- ![风险分级展示](images/screenshot-risk-levels.png) -->

## 核心功能

### 意图到动作的映射

AI 返回的意图 ID（如 `assign_work`、`force_rest`）自动转换为 RimWorld 游戏操作。24 个内置动作覆盖工作、社交、战斗、心情等场景。

### 风险分级系统

每个动作标记风险等级，玩家可在设置中控制：

| 等级 | 含义 | 示例 |
|------|------|------|
| Low | 可随时撤销 | move_to, assign_work, cancel_job |
| Medium | 轻微副作用 | social_relax, eat_food, add_thought |
| High | 重大行为改变 | arrest_pawn, recruit_agree, drop_weapon |
| Critical | 不可逆或影响全局 | trigger_mental_state, trigger_incident |

### 内置动作清单

| 类别 | 动作 |
|------|------|
| 工作 | assign_work, set_work_priority, cancel_job |
| 生存 | force_rest, eat_food, tend_pawn, rescue_pawn |
| 社交 | social_dining, social_relax, give_item, romance_accept, romance_breakup |
| 战斗 | draft, undraft, drop_weapon |
| 移动 | move_to |
| 心情 | inspire_work, inspire_fight, inspire_trade, add_thought, trigger_mental_state |
| 关系 | recruit_agree, adjust_faction |
| 事件 | trigger_incident |

## 设置项

在模组设置中可按风险级别查看和禁用特定动作。High/Critical 风险动作以红色背景标注。

## 常见问题

**Q: Actions 可以单独使用吗？**
A: Actions 本身不直接调用 AI，需要配合 Advisor 或其他模块使用。它提供动作执行能力，AI 决策由其他模块负责。

**Q: 高风险动作会自动执行吗？**
A: 配合 Advisor 使用时，高风险动作需要玩家审批才会执行。可在设置中调整自动拦截的风险级别。

**Q: 可以禁用特定动作吗？**
A: 可以。在模组设置中按风险级别查看，勾选禁用不想要的动作。

---

# RimMind - Actions (English)

The action execution library that maps AI intents (like assign_work, force_rest) into concrete RimWorld game operations.

## What is RimMind

RimMind is an AI-driven RimWorld mod suite that connects to Large Language Models (LLMs), giving colonists personality, memory, dialogue, and autonomous decision-making.

## Sub-Modules & Dependencies

| Module | Role | Depends On |
|--------|------|------------|
| RimMind-Core | API client, request dispatch, context packaging | Harmony |
| **RimMind-Actions** | **AI-controlled pawn action execution** | Core |
| RimMind-Advisor | AI role-plays colonists for work decisions | Core, Actions |
| RimMind-Dialogue | AI-driven dialogue system | Core |
| RimMind-Memory | Memory collection & context injection | Core |
| RimMind-Personality | AI-generated personality & thoughts | Core |
| RimMind-Storyteller | AI storyteller, smart event selection | Core |

## Installation

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

- **Intent-to-Action Mapping**: AI intent IDs automatically convert to RimWorld operations. 24 built-in actions cover work, social, combat, mood, and more.
- **Risk Level System**: Each action is tagged with risk level (Low/Medium/High/Critical). Players can disable specific actions in settings.
- **Batch Execution**: Supports multi-step job sequences with automatic queue management.

## FAQ

**Q: Can Actions be used alone?**
A: Actions doesn't call AI directly. It needs Advisor or other modules to provide AI decisions. It provides the execution capability.

**Q: Will high-risk actions execute automatically?**
A: When used with Advisor, high-risk actions require player approval. You can adjust the auto-block risk level in settings.

**Q: Can I disable specific actions?**
A: Yes. In mod settings, view actions by risk level and check to disable unwanted ones.
