# RimMind - Actions

AI 意图到游戏动作的翻译引擎，让 AI 的决策真正落地为殖民者的行为。

## 核心能力

**意图映射系统** - 将 AI 的抽象意图（如 `assign_work`、`force_rest`、`social_dining`）转换为具体的游戏操作。

**智能工作分配** - AI 可以指定具体坐标的工作目标，比如"去 (45,32) 挖掘那块花岗岩"，而非盲目接受默认选择。

**社交行为支持** - 支持聚餐、放松、赠送物品、求爱、分手等社交互动，让 AI 能主动经营殖民者关系。

**风险分级控制** - 每个动作标记风险等级（Low / Medium / High / Critical），高风险操作需要额外确认。

**批量执行** - 支持多步骤任务序列，自动处理队列逻辑，首个动作打断当前任务，后续追加。

## 动作类型一览

| 类别 | 动作示例 |
|------|---------|
| 工作 | 分配工作、设置优先级、取消任务 |
| 生存 | 强制休息、进食、治疗、救援 |
| 社交 | 聚餐、赠送物品、求爱、分手 |
| 战斗 | 征召/解除征召、丢弃武器 |
| 移动 | 移动到指定坐标 |
| 心情 | 工作灵感、战斗灵感、添加 Thought |
| 关系 | 同意招募、调整派系关系 |
| 事件 | 触发随机事件 |

## 建议配图

1. 殖民者执行 AI 分配工作的场景
2. 风险分级设置界面
3. 社交互动（如聚餐）的截图

---

# RimMind - Actions (English)

The translation engine from AI intent to game actions, turning AI decisions into actual colonist behaviors.

## Key Features

**Intent Mapping System** - Converts abstract AI intents (like `assign_work`, `force_rest`, `social_dining`) into concrete game operations.

**Smart Work Assignment** - AI can specify exact coordinates for work targets, such as "go mine that granite block at (45,32)", rather than blindly accepting default selection.

**Social Behavior Support** - Supports social interactions like dining together, relaxing, gift-giving, romance, and breakups, allowing AI to actively manage colonist relationships.

**Risk Level Control** - Each action is tagged with risk level (Low / Medium / High / Critical). High-risk operations require additional confirmation.

**Batch Execution** - Supports multi-step task sequences with automatic queue management.

## Action Types Overview

| Category | Examples |
|----------|----------|
| Work | Assign work, set priority, cancel job |
| Survival | Force rest, eat, tend, rescue |
| Social | Social dining, give item, romance, breakup |
| Combat | Draft/undraft, drop weapon |
| Movement | Move to specific coordinates |
| Mood | Work inspiration, fight inspiration, add thought |
| Relations | Agree to recruit, adjust faction relations |
| Events | Trigger random incident |

## Suggested Screenshots

1. Colonist executing AI-assigned work
2. Risk level settings interface
3. Social interaction (e.g., dining together)
