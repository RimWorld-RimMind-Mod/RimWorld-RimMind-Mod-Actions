# AGENTS.md — RimMind-Actions

本文件供 AI 编码助手阅读，描述 RimMind-Actions 的架构、代码约定和扩展模式。

## 项目定位

RimMind-Actions 是 RimMind 套件的**动作执行层**。它将 AI 意图（如 `assign_work`、`force_rest`）映射为具体的 RimWorld 游戏操作。

**核心职责**：
1. **动作注册与分发** — 通过 `RimMindActionsAPI.RegisterAction()` 注册动作规则
2. **意图到操作的映射** — 每个 `IActionRule` 实现将 intentId 转换为游戏内 Job 或状态修改
3. **风险分级** — 四级风险系统（Low/Medium/High/Critical）控制 AI 可执行的动作范围
4. **批量执行** — 支持多步骤 Job 序列，自动处理队列追加逻辑
5. **延迟执行** — `DelayedActionQueue` 将动作投递到主线程执行，避免非主线程调用游戏 API

**依赖关系**：
- 依赖 RimMind-Core 提供的 API 和上下文
- 被 RimMind-Advisor 调用以执行 AI 决策

## 源码结构

```
Source/
├── RimMindActionsMod.cs          Mod 入口，注册 Harmony，初始化设置
├── RimMindActionsAPI.cs          公共静态 API，动作注册与执行入口
├── Actions/
│   ├── IActionRule.cs            动作规则接口定义
│   ├── RiskLevel.cs              风险等级枚举
│   ├── PawnActions.cs            小人基础动作（移动、休息、工作、征召等）
│   ├── SocialActions.cs          社交动作（聚餐、恋爱、分手等）
│   ├── MoodActions.cs            心情动作（灵感、Thought、精神崩溃）
│   ├── RelationActions.cs        派系关系动作（招募、好感度调整）
│   └── EventActions.cs           事件动作（触发 Incident）
├── Settings/
│   └── RimMindActionsSettings.cs 模组设置（禁用特定意图）
├── Queue/
│   └── DelayedActionQueue.cs     GameComponent，延迟动作队列
└── Debug/
    └── ActionsDebugActions.cs    Dev 菜单调试动作
```

## 关键类与 API

### RimMindActionsAPI

所有子模组通过此静态类执行动作：

```csharp
// 单条执行
RimMindActionsAPI.Execute("assign_work", pawn, param: "Mining");

// 批量执行（自动处理 Job 队列逻辑）
var intents = new List<BatchActionIntent>
{
    new BatchActionIntent { IntentId = "move_to", Actor = pawn, Param = "45,32" },
    new BatchActionIntent { IntentId = "assign_work", Actor = pawn, Param = "Mining" }
};
RimMindActionsAPI.ExecuteBatch(intents);

// 查询可用工作目标（供 Advisor 构建 Prompt）
var targets = RimMindActionsAPI.GetWorkTargets(pawn, "Mining", maxCount: 8);

// 查询已注册动作信息
IReadOnlyList<string> GetSupportedIntents()
IReadOnlyList<(string intentId, string displayName, RiskLevel riskLevel)> GetActionDescriptions()
RiskLevel? GetRiskLevel(string intentId)

// 检查意图是否被允许
bool IsAllowed(string intentId)
```

### IActionRule

```csharp
public interface IActionRule
{
    string IntentId { get; }           // 唯一标识，如 "force_rest"
    string DisplayName { get; }        // 显示名称
    RiskLevel RiskLevel { get; }       // 风险等级
    bool IsJobBased => false;          // 是否为 Job 类动作（影响批量执行逻辑）

    bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false);
}
```

### 风险等级

| 等级 | 含义 | 示例 |
|------|------|------|
| Low | 无副作用，可随时撤销 | move_to, cancel_job, assign_work, undraft |
| Medium | 轻微副作用 | force_rest, draft, tend_pawn, rescue_pawn, eat_food, set_work_priority, social_dining, social_relax, give_item, romance_accept, add_thought |
| High | 重大行为改变 | arrest_pawn, drop_weapon, romance_breakup, recruit_agree, adjust_faction, inspire_work, inspire_fight, inspire_trade |
| Critical | 不可逆或影响全局 | trigger_mental_state, trigger_incident |

### 内置动作清单（24 个）

#### PawnActions
| intentId | 风险 | 说明 | param |
|----------|------|------|-------|
| force_rest | Medium | 强制休息 | @x,z（可选，指定床位坐标） |
| assign_work | Low | 指定工作 | WorkTypeDefName 或 WorkType@x,z |
| move_to | Low | 移动到坐标 | x,z |
| eat_food | Medium | 吃指定食物 | 食物 defName 关键词（可选） |
| draft | Medium | 征召 | - |
| undraft | Low | 解除征召 | - |
| tend_pawn | Medium | 救治目标 | target 必填 |
| rescue_pawn | Medium | 救援倒地目标 | target 必填 |
| arrest_pawn | High | 逮捕目标 | target 必填 |
| cancel_job | Low | 中止当前任务 | - |
| set_work_priority | Medium | 调整工作优先级 | WorkType,priority（0-4） |
| drop_weapon | High | 丢弃武器 | - |

#### SocialActions
| intentId | 风险 | 说明 | target |
|----------|------|------|--------|
| social_dining | Medium | 社交聚餐 | 目标小人 |
| social_relax | Medium | 社交休闲 | - |
| give_item | Medium | 赠送物品 | 受赠小人 |
| romance_accept | Medium | 发起恋爱 | 目标小人 |
| romance_breakup | High | 分手 | 目标小人 |

#### MoodActions
| intentId | 风险 | 说明 | param |
|----------|------|------|-------|
| inspire_work | High | 触发工作灵感 | - |
| inspire_fight | High | 触发战斗灵感 | - |
| inspire_trade | High | 触发交易灵感 | - |
| add_thought | Medium | 添加 Thought | ThoughtDef 名称 |
| trigger_mental_state | Critical | 触发精神崩溃 | MentalStateDef 名称 |

#### RelationActions
| intentId | 风险 | 说明 | param |
|----------|------|------|-------|
| recruit_agree | High | 同意招募 | - |
| adjust_faction | High | 修改派系关系 | FactionDef,delta |

#### EventActions
| intentId | 风险 | 说明 | param |
|----------|------|------|-------|
| trigger_incident | Critical | 触发事件 | IncidentDef 名称 |

## 动作执行流程

```
Advisor 或其他调用方
    │
    ├── 构建 BatchActionIntent 列表
    │       ▼
    ├── RimMindActionsAPI.ExecuteBatch(intents)
    │       ▼
    ├── 检查设置：该意图是否被禁用？
    │       ▼
    ├── 查找 IActionRule
    │       ▼
    ├── 判断 IsJobBased
    │   ├── true → 第一个 requestQueueing=false，后续=true
    │   └── false → requestQueueing 无效，直接执行
    │       ▼
    └── rule.Execute(actor, target, param, requestQueueing)
            ▼
        生成 Job 或直接修改状态
```

## 延迟执行队列

`DelayedActionQueue` 是 GameComponent，用于将动作从后台线程（AI 回调）投递到主线程执行：

```csharp
// 在 AI 回调中（非主线程安全）
DelayedActionQueue.Instance.Enqueue(
    intentId: "force_rest",
    actor: pawn,
    delaySeconds: 1.5f,  // 默认延迟，带 ±20% 随机波动
    reason: "AI 建议休息"
);

// 取消指定小人的所有待执行动作
DelayedActionQueue.Instance.CancelForPawn(pawn);

// 主线程 Tick 时自动执行
public override void GameComponentTick()
{
    // 检查到期动作并执行
}
```

`PendingAction` 记录包含：`Id`, `IntentId`, `Actor`, `Target`, `Param`, `Reason`, `TimeRemaining`, `IsCancelled`, `RiskLevel`。

## 代码约定

### 命名空间

- `RimMind.Actions` — 顶层（Mod 入口、API、Settings、IActionRule、RiskLevel）
- `RimMind.Actions.Actions` — 动作实现
- `RimMind.Actions.Queue` — 延迟队列
- `RimMind.Actions.Debug` — 调试动作

### 动作实现规范

1. **前置条件检查**：Execute 开头检查 actor.Dead、actor.Downed 等
2. **参数解析**：使用 `string.IsNullOrEmpty(param)` 和 `param.Split(',')`
3. **坐标解析**：统一使用 `ParseCell()` 工具方法
4. **Job 生成**：使用 `JobMaker.MakeJob()`，避免直接 new Job
5. **队列控制**：Job 类动作调用 `TryTakeOrderedJob(..., requestQueueing)`
6. **返回值**：成功返回 true，前置条件不满足返回 false

### 风险等级标注

```csharp
public RiskLevel RiskLevel => RiskLevel.High;  // 高风险动作必须明确标注
```

### 设置持久化

```csharp
public override void ExposeData()
{
    var list = new List<string>(DisabledIntents);
    Scribe_Collections.Look(ref list, "disabledIntents", LookMode.Value);
    DisabledIntents = list != null ? new HashSet<string>(list) : new HashSet<string>();
}
```

启动时 `ActionsSettingsValidator` 检查孤儿意图 ID 并自动清理。

## 扩展指南（自定义动作）

### 1. 实现 IActionRule

```csharp
public class MyCustomAction : IActionRule
{
    public string IntentId => "my_custom_action";
    public string DisplayName => "我的自定义动作";
    public RiskLevel RiskLevel => RiskLevel.Medium;
    public bool IsJobBased => true;  // 如果是 Job 类动作

    public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
    {
        if (actor.Dead) return false;
        // 实现动作逻辑...
        actor.jobs.TryTakeOrderedJob(job, JobTag.Misc, requestQueueing);
        return true;
    }
}
```

### 2. 注册动作

```csharp
public class MyMod : Mod
{
    public MyMod(ModContentPack content) : base(content)
    {
        RimMindActionsAPI.RegisterAction("my_custom_action", new MyCustomAction());
    }
}
```

### 3. 参数约定

| 参数类型 | 格式 | 示例 |
|----------|------|------|
| 坐标 | x,z 或 @x,z | "45,32", "@45,32" |
| 工作类型 | WorkTypeDefName | "Mining", "Cooking" |
| 工作类型+坐标 | WorkType@x,z | "Mining@45,32" |
| 键值对 | key,value | "Mining,1", "OutlanderCivil,10" |
| Def 名称 | 直接写 defName | "Catharsis", "Wander_Sad" |

## 与 RimMind-Advisor 的协作

```
RimMind-Advisor
    │
    ├── 构建 Prompt（含可用动作列表）
    │       ▼
    ├── 调用 Core API 发送 AI 请求
    │       ▼
    ├── 解析 AI 响应（<Advice> JSON）
    │       ▼
    ├── 验证动作是否允许（IsAllowed）
    │       ▼
    └── 调用 Actions API 执行
            ├── 单条：Execute()
            └── 批量：ExecuteBatch()
```

Advisor 通过 `GetActionDescriptions()` 获取所有可用动作及其风险等级，构建到 Prompt 中让 AI 选择。

## 调试

Dev 菜单（需开启开发模式）→ RimMind Actions：

- **Show Registered Intents** — 查看所有已注册意图
- **Show Job State** — 查看选中 Pawn 的 Job 状态
- **Show DelayedActionQueue** — 查看延迟队列
- **各类 Test 动作** — 直接测试特定动作
- **WorkTargets** — 查看可用工作目标列表
- **Batch 测试** — 测试批量执行逻辑

## 注意事项

1. **线程安全**：所有游戏 API 调用必须在主线程执行，后台线程使用 `DelayedActionQueue`
2. **Pawn 有效性**：Execute 中始终检查 actor.Dead / actor.Downed
3. **Map 有效性**：涉及地图的操作检查 map != null
4. **Def 存在性**：使用 `GetNamedSilentFail` 避免 Def 不存在时报错
5. **异常处理**：WorkGiver 扫描可能抛出异常，需 try-catch 包裹
