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
- 依赖 RimMind-Core（提供 API 和 UI 工具类 `SettingsUIHelper`）
- 被 RimMind-Advisor 调用以执行 AI 决策

**构建信息**：
- 目标框架：.NET Framework 4.8，C# 9.0，Nullable enable
- 游戏版本：RimWorld 1.6
- 程序集名：`RimMindActions`，根命名空间：`RimMind.Actions`
- 输出路径：`../1.6/Assemblies/`
- 依赖包：`Krafs.Rimworld.Ref`、`Lib.Harmony.Ref`、`Newtonsoft.Json`
- 项目引用：`RimMindCore.dll`（HintPath 指向 `../../RimMind-Core/1.6/Assemblies/`）
- 部署：设置 `RIMWORLD_DIR` 环境变量后构建自动 robocopy 到游戏 Mods 目录
- Mod 包ID：`mcocdaa.RimMindActions`，必须加载于 Harmony 和 RimMind-Core 之后

## 源码结构

```
Source/
├── RimMindActionsMod.cs          Mod 入口，注册 Harmony，初始化设置，注册内置动作
├── RimMindActionsAPI.cs          公共静态 API，动作注册与执行入口，BatchActionIntent 定义
├── Actions/
│   ├── IActionRule.cs            动作规则接口定义
│   ├── RiskLevel.cs              风险等级枚举（Low/Medium/High/Critical）
│   ├── PawnActions.cs            小人基础动作（12 个）+ WorkTargetInfo 数据类
│   ├── SocialActions.cs          社交动作（5 个）
│   ├── MoodActions.cs            心情动作（5 个）
│   ├── RelationActions.cs        派系关系动作（2 个）
│   └── EventActions.cs           事件动作（1 个）
├── Settings/
│   └── RimMindActionsSettings.cs 模组设置 + ActionsSettingsValidator（孤儿意图检测）
├── Queue/
│   └── DelayedActionQueue.cs     GameComponent，延迟动作队列 + PendingAction 数据类
└── Debug/
    └── ActionsDebugActions.cs    Dev 菜单调试动作（[StaticConstructorOnStartup]）
```

## 关键类与 API

### RimMindActionsAPI

所有子模组通过此静态类执行动作。内部使用 `Dictionary<string, IActionRule>` 存储规则。

```csharp
// 单条执行
bool Execute(string intentId, Pawn actor, Pawn? target = null, string? param = null, bool requestQueueing = false)

// 批量执行（自动处理 Job 队列逻辑，返回成功执行条数）
int ExecuteBatch(IReadOnlyList<BatchActionIntent> intents)

// 查询可用工作目标（供 Advisor 构建 Prompt）
List<WorkTargetInfo> GetWorkTargets(Pawn pawn, string workTypeDefName, int maxCount = 8)

// 查询已注册动作信息
IReadOnlyList<string> GetSupportedIntents()
IReadOnlyList<(string intentId, string displayName, RiskLevel riskLevel)> GetActionDescriptions()
RiskLevel? GetRiskLevel(string intentId)

// 检查意图是否被玩家设置允许（Settings 未初始化时放行）
bool IsAllowed(string intentId)
```

**Execute 流程**：查找 IActionRule → 检查 `RimMindAPI.ShouldSkipAction(intentId)`（Core 桥接跳过检查）→ 检查 `IsAllowed(intentId)`（玩家设置）→ 调用 `rule.Execute()`

**ExecuteBatch 队列逻辑**：
- 使用 `ReferenceEqualityComparer`（按对象引用去重 Pawn）追踪每个 Pawn 的首个 Job 类动作
- 同一 Pawn：第一个 Job 类动作 `requestQueueing=false`（打断当前任务），后续 `requestQueueing=true`（EnqueueLast 追加）
- 不同 Pawn：互不影响，全部独立执行
- 非 Job 类动作：`requestQueueing` 参数无效，直接执行

### BatchActionIntent

```csharp
public class BatchActionIntent
{
    public string   IntentId  = "";
    public Pawn     Actor     = null!;
    public Pawn?    Target;
    public string?  Param;
    public string?  Reason;    // 用于调用方显示气泡，Actions 本身不处理
}
```

### IActionRule

```csharp
public interface IActionRule
{
    string IntentId { get; }           // 唯一标识，如 "force_rest"
    string DisplayName { get; }        // 显示名称（使用 Translate()）
    RiskLevel RiskLevel { get; }       // 风险等级
    bool IsJobBased => false;          // 是否为 Job 类动作（影响批量执行逻辑）

    bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false);
}
```

**IsJobBased 判断规则**：
- 动作最终调用 `pawn.jobs.TryTakeOrderedJob` → 返回 `true`
- 动作直接修改状态（add_thought、inspire 等）→ 保持默认 `false`
- `ExecuteBatch` 依赖此值决定同一小人多步序列是否使用 `requestQueueing=true`

### 风险等级

| 等级 | 含义 | 示例 |
|------|------|------|
| Low | 无副作用，可随时撤销 | move_to, cancel_job, assign_work, undraft |
| Medium | 生存/社交/工作类轻微副作用 | force_rest, draft, tend_pawn, rescue_pawn, eat_food, set_work_priority, social_dining, social_relax, give_item, romance_accept, add_thought |
| High | 重大行为改变 | arrest_pawn, drop_weapon, romance_breakup, recruit_agree, adjust_faction, inspire_work, inspire_fight, inspire_trade |
| Critical | 不可逆或影响全局 | trigger_mental_state, trigger_incident |

### 内置动作清单（25 个）

> **Job** 列：`Y` = IsJobBased=true（调用 TryTakeOrderedJob），`-` = 直接修改状态

#### PawnActions（12 个）
| intentId | 风险 | Job | 说明 | param | target | 实现要点 |
|----------|------|-----|------|-------|--------|----------|
| force_rest | Medium | Y | 强制休息 | `@x,z`（可选，指定床位坐标，`@` 前缀必需） | 可选（取其 OwnedBed） | 优先级：param坐标床 > target的床 > RestUtility自动寻床；无床时 Wait_MaintainPosture |
| assign_work | Low | Y | 指定工作 | `WorkTypeDefName` 或 `WorkType@x,z` | - | @坐标模式：遍历 WorkGiver 在该格子找 Thing/Cell 生成 Job；自动模式：取第一个有效目标 |
| move_to | Low | Y | 移动到坐标 | `x,z` | - | 坐标越界检查 |
| eat_food | Medium | Y | 吃指定食物 | 食物关键词（可选，大小写不敏感匹配 defName 或 Label，留空找最近可食用食物） | - | 先搜背包再搜地图（按距离最近）；检查 CanReserveAndReach；自动计算 Ingest stackCount |
| draft | Medium | - | 征召 | - | - | 检查 drafter != null |
| undraft | Low | - | 解除征召 | - | - | 检查 drafter != null |
| tend_pawn | Medium | Y | 救治目标 | - | **必填** | JobDefOf.TendPatient |
| rescue_pawn | Medium | Y | 救援倒地目标 | - | **必填**，需 Downed | JobDefOf.Rescue |
| arrest_pawn | High | Y | 逮捕目标 | - | **必填** | JobDefOf.Arrest |
| cancel_job | Low | - | 中止当前任务 | - | - | EndCurrentJob(Incompletable)；检查 curJob != null |
| set_work_priority | Medium | - | 调整工作优先级 | `WorkType,priority`（0-4） | - | priority 钳位到 [0,4]；检查 workSettings != null |
| drop_weapon | High | - | 丢弃武器 | - | - | TryDropEquipment 到 actor.Position |

#### SocialActions（5 个）
| intentId | 风险 | Job | 说明 | param | target | 实现要点 |
|----------|------|-----|------|-------|--------|----------|
| social_dining | Medium | - | 社交聚餐 | - | 目标小人 | 优先 ShareMeal 互动，fallback ChatFriendly；检查 CanInteractNowWith；双方获得 Catharsis thought |
| social_relax | Medium | - | 社交休闲 | - | - | 给 actor Catharsis thought + 设置当前小时 Timetable 为 Joy |
| give_item | Medium | - | 赠送物品 | 物品关键词（大小写不敏感匹配 Label 或 defName） | 受赠小人 | 从 actor 背包找物品，掉落在 target 附近（非 actor 脚下） |
| romance_accept | Medium | Y | 发起恋爱 | - | 目标小人 | 距离内直接 TryInteractWith(RomanceAttempt)；距离外先 Goto |
| romance_breakup | High | Y | 分手 | - | 目标小人 | 距离内直接 TryInteractWith(Breakup)；距离外先 Goto |

#### MoodActions（5 个）
| intentId | 风险 | Job | 说明 | param | target | 实现要点 |
|----------|------|-----|------|-------|--------|----------|
| inspire_work | High | - | 触发工作灵感（Frenzy_Work） | - | - | TryStartInspiration |
| inspire_fight | High | - | 触发战斗灵感（Frenzy_Shoot） | - | - | TryStartInspiration |
| inspire_trade | High | - | 触发交易灵感（Inspired_Trade） | - | - | TryStartInspiration |
| add_thought | Medium | - | 添加 Thought | ThoughtDef defName | - | 使用 GetNamedSilentFail |
| trigger_mental_state | Critical | - | 触发精神崩溃 | MentalStateDef defName | - | **安全限制**：仅对玩家殖民者（Faction==OfPlayer）、非战斗中（!InMentalState）触发 |

#### RelationActions（2 个）
| intentId | 风险 | Job | 说明 | param | target | 实现要点 |
|----------|------|-----|------|-------|--------|----------|
| recruit_agree | High | - | 同意招募 | - | 招募者（recruiter） | actor 为被招募 NPC；从 Lord 移除 → SetFaction(OfPlayer) → 清除 guest 状态 → 发送招募成功信件 |
| adjust_faction | High | - | 修改派系关系 | `FactionDef,delta`（delta 钳位 [-100,100]） | - | 不允许修改玩家自身派系 |

#### EventActions（1 个）
| intentId | 风险 | Job | 说明 | param | target | 实现要点 |
|----------|------|-----|------|-------|--------|----------|
| trigger_incident | Critical | - | 触发事件 | IncidentDef defName | - | 检查 CanFireNow → TryExecute；parms.forced=true；地图取 actor.Map ?? Find.AnyPlayerHomeMap |

### WorkTargetInfo（PawnActions.cs 内）

供 Advisor 构建候选 prompt 的数据结构：

```csharp
public class WorkTargetInfo
{
    public string Label   = "";   // AI 可读目标名称，如"花岗岩"、"电炉 → 简单餐×5"
    public string DefName = "";   // Thing.def.defName（Cell-based 为空）
    public IntVec3 Position;      // 地图坐标
    public float Distance;        // 到小人的距离（格）

    public string ToParam(string workTypeDefName) => $"{workTypeDefName}@{Position.x},{Position.z}";

    public override string ToString()
        => "RimMind.Actions.Prompt.WorkTargetInfo".Translate(Label, DefName, $"{Position.x}", $"{Position.z}", $"{Distance:F0}");
}
```

**GetWorkTargets 支持三类 WorkGiver**：
- Thing-based（Mining/Hauling/Tend/Hunt 等）：返回具体 Thing
- DoBill-based（Cooking/Crafting/Art/Smithing 等）：返回工作台 + 首条活跃账单名称
- Cell-based（Growing 种植等）：返回地块坐标 + 区域名称；Growing 有兜底逻辑直接读 Zone_Growing

**GetJoyFoodLabels**（EatFoodAction 内静态方法，未通过 RimMindActionsAPI 暴露，需直接调用 `EatFoodAction.GetJoyFoodLabels(pawn, max)`）：返回地图上小人可食用的 joy>0 食物名称列表（去重，最多 N 种），供 Advisor 构建候选。

## 动作执行流程

```
Advisor 或其他调用方
    │
    ├── 构建 BatchActionIntent 列表
    │       ▼
    ├── RimMindActionsAPI.ExecuteBatch(intents)
    │       ▼
    ├── 遍历 intents：
    │   ├── 查找 IActionRule（找不到则 Warning 跳过）
    │   ├── 判断 IsJobBased
    │   │   ├── true → 同 Pawn 首个 requestQueueing=false，后续=true
    │   │   └── false → requestQueueing 无效，直接执行
    │   └── rule.Execute(actor, target, param, requestQueueing)
    │           ▼
    │       生成 Job 或直接修改状态
    └── 返回成功执行条数

注意：ExecuteBatch 不检查 ShouldSkipAction / IsAllowed，
这些检查仅在单条 Execute() 中执行。
批量调用方（如 Advisor）应自行在调用前过滤。
```

## 延迟执行队列

`DelayedActionQueue` 是 GameComponent，用于将动作从后台线程（AI 回调）投递到主线程执行：

```csharp
// 在 AI 回调中（非主线程安全）
DelayedActionQueue.Instance.Enqueue(
    intentId: "force_rest",
    actor: pawn,
    target: null,          // 可选，目标小人
    param: null,           // 可选，附加参数
    reason: "AI 建议休息",
    delaySeconds: 1.5f     // 默认延迟，带 ±20% 随机波动
);

// 取消指定小人的所有待执行动作
DelayedActionQueue.Instance.CancelForPawn(pawn);

// 获取队列调试信息（仅供 Dev 菜单使用）
List<string> debugInfo = DelayedActionQueue.Instance.GetPendingDebugInfo();
```

**PendingAction 记录**：`Id`(GUID), `IntentId`, `Actor`, `Target`, `Param`, `Reason`, `TimeRemaining`, `IsCancelled`, `RiskLevel`

**关键行为**：
- Tick 频率：60 ticks/s（`dt = 1f/60f`）
- 自动清理：Actor 为 null / Dead / Destroyed / IsCancelled 的条目
- 到期执行：调用 `RimMindActionsAPI.Execute()`，异常被 catch 并 Log.Error
- **不跨存档持久化**：`ExposeData()` 为空，加载存档时队列自然清空，Advisor 下次 tick 重新评估
- 单例模式：`_instance` 在构造函数中赋值，通过 `DelayedActionQueue.Instance` 访问

## 代码约定

### 命名空间

| 命名空间 | 内容 |
|----------|------|
| `RimMind.Actions` | 顶层（Mod 入口、API、Settings、IActionRule、RiskLevel、BatchActionIntent、ReferenceEqualityComparer） |
| `RimMind.Actions.Actions` | 动作实现 + WorkTargetInfo |
| `RimMind.Actions.Queue` | 延迟队列 + PendingAction |
| `RimMind.Actions.Debug` | 调试动作 |

### 动作实现规范

1. **前置条件检查**：Execute 开头检查 actor.Dead、actor.Downed、actor.Map 等
2. **参数解析**：使用 `string.IsNullOrEmpty(param)` 和 `param.Split(',')`
3. **坐标解析**：各动作内私有 `ParseCell(string)` 方法（目前 ForceRestAction 和 AssignWorkAction 各有一份，未提取公共方法）
4. **Job 生成**：使用 `JobMaker.MakeJob()`，避免直接 new Job
5. **队列控制**：Job 类动作调用 `pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc, requestQueueing)`
6. **Def 查找**：使用 `DefDatabase<T>.GetNamedSilentFail(defName)` 避免 Def 不存在时报错；社交/恋爱动作使用 `GetNamed(defName, false)` 查找 InteractionDef
7. **返回值**：成功返回 true，前置条件不满足返回 false
8. **DisplayName**：使用 `"...".Translate()` 绑定翻译键

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

**ActionsSettingsValidator**（`[StaticConstructorOnStartup]`）：在所有 Mod 构造完毕后检查设置中的孤儿意图 ID（对应 mod 未加载），仅输出 Warning **保留条目**（重新加载对应 mod 后设置自动恢复生效），不自动清理。

### 翻译键约定

- 显示名称：`RimMind.Actions.DisplayName.{PascalCase}`（如 `RimMind.Actions.DisplayName.ForceRest`）
- 设置 UI：`RimMind.Actions.Settings.{Name}`
- 风险提示：`RimMind.Actions.UI.Risk.{Level}`
- Prompt 上下文：`RimMind.Actions.Prompt.{Name}`
- AI 动作描述：`RimMind.Actions.Desc.{intentId}`（供 Advisor 构建 Prompt 使用，Actions 代码内部不直接引用）

## 扩展指南（自定义动作）

### 1. 实现 IActionRule

```csharp
public class MyCustomAction : IActionRule
{
    public string IntentId => "my_custom_action";
    public string DisplayName => "RimMind.Actions.DisplayName.MyCustomAction".Translate();
    public RiskLevel RiskLevel => RiskLevel.Medium;
    public bool IsJobBased => true;

    public bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing = false)
    {
        if (actor.Dead) return false;
        var job = JobMaker.MakeJob(MyJobDef, target);
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

| 参数类型 | 格式 | 示例 | 使用动作 |
|----------|------|------|----------|
| 坐标 | `x,z` | `"45,32"` | move_to |
| 床位坐标 | `@x,z`（`@` 前缀必需） | `"@45,32"` | force_rest |
| 工作类型 | WorkTypeDefName | `"Mining"` | assign_work, set_work_priority |
| 工作类型+坐标 | `WorkType@x,z` | `"Mining@45,32"` | assign_work |
| 键值对 | `key,value` | `"Mining,1"`, `"OutlanderCivil,10"` | set_work_priority, adjust_faction |
| Def 名称 | 直接写 defName | `"Catharsis"`, `"Wander_Sad"` | add_thought, trigger_mental_state, trigger_incident |
| 食物关键词 | 大小写不敏感 | `"Chocolate"` | eat_food |
| 物品关键词 | 大小写不敏感 | `"Medicine"` | give_item |

## 与 RimMind-Advisor 的协作

```
RimMind-Advisor
    │
    ├── GetActionDescriptions() → 获取动作列表 + 风险等级 → 构建 Prompt
    ├── GetWorkTargets() → 获取具体工作目标 → 写入 Prompt
    ├── GetJoyFoodLabels() → 获取愉悦食物列表 → 写入 Prompt
    │       ▼
    ├── 调用 Core API 发送 AI 请求
    │       ▼
    ├── 解析 AI 响应（<Advice> JSON）
    │       ▼
    ├── IsAllowed() 验证动作是否被玩家允许
    │       ▼
    └── 调用 Actions API 执行
            ├── 单条：Execute()
            ├── 批量：ExecuteBatch()
            └── 延迟：DelayedActionQueue.Enqueue()
```

## 调试

Dev 菜单（需开启开发模式）→ RimMind Actions：

**状态检查**：
- Show Registered Intents — 查看所有已注册意图及风险等级
- Show Job State (selected) — 查看选中 Pawn 的 Job 状态 + 是否 AI 可接管（IsAdvisorIdle 判定）
- Show DelayedActionQueue — 查看延迟队列

**动作测试**（均需先选中小人）：
- PawnAction 系列：force_rest / assign_work(Mining/Construction/Cooking) / move_to / draft / undraft / cancel_job / set_work_priority / drop_weapon
- SocialAction 系列：social_relax / social_dining
- MoodAction 系列：inspire_work/fight/trade / add_thought(Catharsis/SleptOutside) / trigger_mental_state
- RelationAction 系列：adjust_faction(+10/-10)
- EventAction 系列：trigger_incident(ResourcePodCrash)

**工作目标查询**：
- WorkTargets: list Mining/Construction/Cooking/Crafting/Growing/Hauling targets
- assign_work: nearest Mining target（精确坐标模式）

**批量测试**：
- Batch: move_to + social_relax same pawn
- Batch: multi-pawn force_rest + inspire_work
- Batch: 5-step job sequence

**延迟队列测试**：
- DelayedQueue: enqueue force_rest in 3s
- DelayedQueue: cancel all for selected pawn

## 注意事项

1. **线程安全**：所有游戏 API 调用必须在主线程执行，后台线程使用 `DelayedActionQueue`
2. **Pawn 有效性**：Execute 中始终检查 actor.Dead / actor.Downed / actor.Map
3. **Map 有效性**：涉及地图的操作检查 map != null
4. **Def 存在性**：使用 `DefDatabase<T>.GetNamedSilentFail(defName)` 避免 Def 不存在时报错；社交/恋爱动作使用 `GetNamed(defName, false)` 查找 InteractionDef
5. **异常处理**：WorkGiver 扫描（PotentialWorkThingsGlobal/PotentialWorkCellsGlobal/HasJobOnThing/HasJobOnCell）可能抛出异常，需 try-catch 包裹
6. **ParseCell 重复**：ForceRestAction 和 AssignWorkAction 各有私有 ParseCell，新增坐标解析动作时应注意
7. **Harmony ID**：`mcocdaa.RimMindActions`，当前无 Patch（PatchAll 保留扩展能力）
8. **ReferenceEqualityComparer**：ExecuteBatch 内部使用，按对象引用比较 Pawn（不依赖 Pawn.Equals 重写）
9. **ExecuteBatch 不检查 IsAllowed/ShouldSkipAction**：批量执行直接调用 `rule.Execute()`，跳过单条 Execute 中的 ShouldSkipAction 和 IsAllowed 检查。调用方（如 Advisor）应自行在构建 intents 前过滤
