# AGENTS.md — RimMind-Actions

动作执行层，将AI意图映射为RimWorld游戏操作。

## 项目定位

意图ID→游戏操作的分发执行库，含24个内置动作、4级风险分级(Low/Medium/High/Critical)、批量执行(ExecuteBatch)、延迟队列(DelayedActionQueue)。依赖Core，被Advisor调用。

## 构建

| 项 | 值 |
|----|-----|
| Target | net48, C#9.0, Nullable enable |
| Output | `../1.6/Assemblies/` |
| Assembly | RimMindActions, RootNS: RimMind.Actions |
| 依赖 | Krafs.Rimworld.Ref, Lib.Harmony.Ref, Newtonsoft.Json, RimMindCore.dll |

## 源码结构

```
Source/
├── RimMindActionsMod.cs / RimMindActionsAPI.cs    Mod入口 + 静态API
├── Actions/
│   ├── IActionRule.cs / RiskLevel.cs / ActionResult.cs   接口+风险+结果
│   ├── ActionsBridge.cs                          IAgentActionBridge实现
│   ├── PawnActions.cs(12) / SocialActions.cs(4) / MoodActions.cs(5) / RelationActions.cs(2) / EventActions.cs(1)
├── Settings/RimMindActionsSettings.cs            设置+孤儿意图检测
├── Queue/DelayedActionQueue.cs                   GameComponent延迟队列
└── Debug/ActionsDebugActions.cs
```

## 关键 API

```csharp
// 单条/批量执行
RimMindActionsAPI.Execute(intentId, actor, target?, param?)
RimMindActionsAPI.ExecuteBatch(IReadOnlyList<BatchActionIntent> intents)

// 查询
RimMindActionsAPI.GetSupportedIntents() / GetActionDescriptions() / GetRiskLevel(id)
RimMindActionsAPI.GetWorkTargets(pawn, workTypeDef, max)
RimMindActionsAPI.GetStructuredTools() / IsAllowed(id)
RimMindActionsAPI.GetActionHintData(pawn, intentId)  // 获取意图提示数据(如可食用食物列表)

// 延迟队列
DelayedActionQueue.Instance.Enqueue(intentId, actor, target?, delaySeconds)
DelayedActionQueue.Instance.CancelForPawn(pawn)
```

## ExecuteBatch 队列逻辑

同Pawn内第一个Job类动作 `requestQueueing=false`(打断当前)，后续 `requestQueueing=true`(EnqueueLast追加)。非Job类动作互不影响。用 `ReferenceEqualityComparer` 按引用区分Pawn。

## 风险等级

| 等级 | 示例 |
|------|------|
| Low | move_to, cancel_job, assign_work, undraft |
| Medium | force_rest, draft, tend_pawn, rescue_pawn, eat_food, social_relax, add_thought, give_item, romance_attempt, set_work_priority |
| High | arrest_pawn, drop_weapon, romance_breakup, recruit_agree, adjust_faction, inspire_* |
| Critical | trigger_mental_state, trigger_incident |

## 动作清单 (24个)

| # | intentId | 风险 | Job | 说明 |
|---|----------|------|-----|------|
| 1 | force_rest | M | Y | 强制休息(param可@x,z指定床位) |
| 2 | assign_work | L | Y | 指定工作(param=WorkTypeDef/@x,z) |
| 3 | move_to | L | Y | 移动到坐标(x,z) |
| 4 | eat_food | M | Y | 吃食物(param=关键词,可选) |
| 5 | draft | M | - | 征召 |
| 6 | undraft | L | - | 解除征召 |
| 7 | tend_pawn | M | Y | 救治(target必填) |
| 8 | rescue_pawn | M | Y | 救援(target需Downed) |
| 9 | arrest_pawn | H | Y | 逮捕(target必填) |
| 10 | cancel_job | L | - | 中止当前任务 |
| 11 | set_work_priority | M | - | 调整工作优先级(WorkType,0-4) |
| 12 | drop_weapon | H | - | 丢弃武器 |
| 13 | social_relax | M | - | 社交休闲(TryInteractWith+timetable临时Joy+90s后恢复) |
| 14 | give_item | M | - | 赠送物品(param=关键词,转移1个) |
| 15 | romance_attempt | M | Y | 发起恋爱 |
| 16 | romance_breakup | H | Y | 分手 |
| 17 | inspire_work | H | - | 触发工作灵感(Frenzy_Work) |
| 18 | inspire_shoot | H | - | 触发射击灵感(Frenzy_Shoot) |
| 19 | inspire_trade | H | - | 触发交易灵感(Inspired_Trade) |
| 20 | add_thought | M | - | 添加Thought(param=defName) |
| 21 | trigger_mental_state | C | - | 触发精神崩溃(仅玩家殖民者) |
| 22 | recruit_agree | H | - | 同意招募(actor=被招募NPC) |
| 23 | adjust_faction | H | - | 修改派系关系(param=FactionDef,delta) |
| 24 | trigger_incident | C | - | 触发事件(param=defName) |

## 代码约定

- 新动作实现 `IActionRule` → `RimMindActionsAPI.RegisterAction`
- Job类动作用 `TryTakeOrderedJob`，禁止仅用 `StartJob`
- `DisplayName` 用 `"...".Translate()` 绑定翻译键
- Def查找用 `GetNamedSilentFail` 避免不存在报错
- 翻译键前缀: `RimMind.Actions.*`
- Harmony ID: `mcocdaa.RimMindActions`

## 操作边界

### ✅ 必须做
- 新动作标注正确的 `RiskLevel` 和 `IsJobBased`
- 修改动作行为后更新本文件清单表
- 所有游戏API调用在主线程，后台用 `DelayedActionQueue`
- Job类动作入口检查 `actor.Map == null`（防商队小人NRE）

### ⚠️ 先询问
- 修改现有 `RiskLevel` 等级(影响Advisor审批)
- 修改 `ExecuteBatch` 队列逻辑
- 新增跨模组硬耦合

### 🚫 绝对禁止
- 后台线程调用 `RimMindActionsAPI.Execute`
- 仅用 `StartJob` 打断任务
- 修改 `RiskLevel` 枚举成员名

## 已知问题摘要

详见 `docs/06-problem/RimMind-Actions.md`，当前高优先级：
- `_pendingRestores` 不跨存档持久化（social_relax timetable 恢复失效）
- 5个 Job 类动作缺少 Map null 检查（tend_pawn/rescue_pawn/arrest_pawn/romance_attempt/romance_breakup）
