# AGENTS.md — RimMind-Actions

组合工具（Composite ToolCall）编排层。将 Core 提供的原子 ToolCall 组合为高级动作并注册到 Core 的 ToolRegistry。

## 项目定位

H2 阶段后，24 个内置动作已全部迁移至 Core Mechanism 系统。本模组的职责是：
- 提供 `CompositeToolCallBase` 抽象基类（懒加载缓存 `ToolDefinition`、共享 JSON 辅助方法、殖民者权限校验 + 原子调用编排）
- 实现 11 个高层组合动作与查询工具（求生调度、蓝图施工、房间规划、资源标记、工作台清单、区域管理、装备穿戴与可建结构查询）
- 通过 `ActionPermissionGuard`、`ResourceSafetyGuard` 与 `ResourceSafetyPolicy` 执行三态自治权限校验与殖民地资源熔断保护
- 通过 `CompositeToolRegistrar` 反射自动发现并注册所有组合工具

仅依赖 Core 公共边界（`RimMind.Presentation.Api`、`RimMind.Application`、`RimMind.Domain`），严禁引用 `RimMind.Infrastructure.*`；连续施工意图统一通过 `RimMindAPI.Agent.StartIntentSession` 挂接。

## 构建

| 项 | 值 |
|----|-----|
| Target | net48, C#9.0, Nullable enable |
| Output | `../1.6/Assemblies/` |
| Assembly | RimMindActions, RootNS: RimMind.Actions |
| 依赖 | Krafs.Rimworld.Ref, Lib.Harmony.Ref, Newtonsoft.Json, RimMindCore (Domain/Application) |

## 源码结构

```
Source/
├── RimMindActionsMod.cs                              Mod 入口，反射注册组合工具
├── Actions/
│   ├── CompositeToolCallBase.cs                      抽象基类：Definition 懒缓存 + ExecuteAtomicAsync + 共享 JSON 辅助
│   ├── CompositeToolRegistrar.cs                     反射发现并注册所有 CompositeToolCallBase 子类
│   ├── StabilizeRestCompositeTool.cs                 组合工具：undraft → force_rest
│   ├── EmergencyFleeCompositeTool.cs                 组合工具：紧急撤离危险区
│   ├── EatAndRecreationCompositeTool.cs              组合工具：休整调理（中断非紧急作业 → 强制休息）
│   ├── TriagePatientCompositeTool.cs                 组合工具：战地救护包扎（病患卧床 + 医生中断杂务）
│   ├── PlaceBlueprintCompositeTool.cs                组合工具：放置单建筑蓝图并经 RimMindAPI.Agent.StartIntentSession 挂接施工租约
│   ├── PlanRoomProjectCompositeTool.cs               组合工具：批量规划微型房间蓝图并挂接连续施工租约
│   ├── DesignateResourcesCompositeTool.cs            组合工具：自动标记地图采集/伐木/采矿目标
│   ├── AddWorktableBillCompositeTool.cs              组合工具：工作台生产清单创建与防重校验
│   ├── ManageZoneCompositeTool.cs                    组合工具：肥力评估与种植区/储物区自动划定
│   ├── ManageEquipmentCompositeTool.cs               组合工具：严寒/威胁下自主穿戴保暖或防具装备
│   └── QueryBuildableStructuresCompositeTool.cs      组合查询：已解锁可建建筑、库存材料与基建瓶颈检测
├── Debug/
│   └── ActionsBehaviorAutotestSuite.cs               游戏内组合工具行为自测套件
└── Policies/
    ├── ActionPermissionGuard.cs                      跨地图执行者解析、三态自治权限校验与本地化回退辅助
    ├── ResourceSafetyGuard.cs                        地图实时库存/蓝图数统计与安全守卫桥接
    └── ResourceSafetyPolicy.cs                       纯策略：木材/钢铁保底红线与并发蓝图上限熔断
```

## 关键 API

```csharp
// 组合工具调用方（通过 Core ToolRegistry）
RimMindAPI.Tools.FindById("actions.stabilize_rest")

// 新增组合工具：继承 CompositeToolCallBase，自动被 CompositeToolRegistrar 发现
public sealed class MyCompositeTool : CompositeToolCallBase { ... }
```

### CompositeToolCallBase 共享辅助方法

| 方法 | 用途 |
|------|------|
| `ExecuteAtomicAsync(toolId, argsJson, parentArgs, ct)` | 调用子原子工具，规整 child ToolCallId/ToolName |
| `TryGetArgument<T>(json, key, out T)` | 从参数 JSON 安全读取强类型字段 |
| `TryGetArgumentOrError<T>(json, key, traceId)` | 安全读取强类型字段，失败直接返回 `Result<T, RimMindError>.Err` |
| `AreRequiredToolsRegistered()` | 检查所有 `RequiredToolIds` 是否已注册，供前置验证 |
| `BuildArgumentsJson(params (key,value)[])` | 构造子调用紧凑 JSON 参数 |
| `BuildStepSummary(params (name,result)[])` | 构造标准 `{"ok":bool,"content":str}` 汇总 |
| `GuardColonistTarget(pawnId, domain, args)` | LLM 提供的目标 pawn 必须是 `IsActiveSpawnedColonist`，并执行自治域权限检查；任何原子调用前调用 |
| `IsPreApproved(args)` | 只读 `ToolCallArgs.IsPlayerApproved` 可信通道；参数 JSON 中的 `pre_approved` / `is_approved` 一律忽略 |

### 地图与执行者解析

- 使用 `ActionPermissionGuard.ResolveActorAndMap(args, ...)`：跨地图按 id 解析执行者，并在**执行者所在地图**上动作；禁止用 `Find.CurrentMap`（玩家正在看的地图）执行写操作。
- 指定了 pawn id 但解析不到活跃站立殖民者时返回错误，禁止静默换成其他殖民者；仅在完全未提供 id 时回退到当前地图。
- 不得改写玩家设置：不调用 `workSettings.SetPriority`、不对物品 `SetForbidden(false)`；不注入 `Catharsis` 等心情 Thought。

## 组合工具清单

| Id | 类名 | 原子依赖 / 机制 | 说明 |
|----|------|----------|------|
| `actions.stabilize_rest` | `StabilizeRestCompositeTool` | `pawn.draft.toggle`, `pawn.job.set` | 解除征召（best-effort）→ 强制休息 |
| `actions.emergency_flee` | `EmergencyFleeCompositeTool` | `pawn.job.set`, `pawn.draft.toggle` | 紧急避险：中断危险作业 → 解除征召 → 强制隐蔽休息 |
| `actions.eat_and_recreation` | `EatAndRecreationCompositeTool` | `pawn.job.set` | 休整调理：中断低优作业 → 安排休息恢复精力与需求 |
| `actions.triage_patient` | `TriagePatientCompositeTool` | `pawn.job.set` | 战地救护：引导伤员紧急卧床并中断医护人员杂务以优先包扎 |
| `actions_place_blueprint` | `PlaceBlueprintCompositeTool` | `GenConstruct`, `RimMindAPI.Agent.StartIntentSession` | 放置单建筑蓝图、自动匹配库存材料并挂接 `Blueprint_Build` → `Frame` 连续施工租约 |
| `actions_plan_room_project` | `PlanRoomProjectCompositeTool` | `GenConstruct`, `RimMindAPI.Agent.StartIntentSession` | 批量规划微型房间（墙体/门/床/光源）蓝图并挂接复合施工租约 |
| `actions_designate_resources` | `DesignateResourcesCompositeTool` | `DesignationDefOf` (`HarvestPlant`/`CutPlant`/`Mine`) | 自动检索并标记附近成熟野生作物、树木或裸露矿脉 |
| `actions_add_worktable_bill` | `AddWorktableBillCompositeTool` | `Bill_Production`, `Building_WorkTable` | 在殖民地工作台创建带防重与库存目标上限的生产清单 |
| `actions_manage_zone` | `ManageZoneCompositeTool` | `Zone_Growing`, `Zone_Stockpile` | 评估土壤肥力与平整度，自动划定种植区或储物区 |
| `actions_manage_equipment` | `ManageEquipmentCompositeTool` | `JobDefOf.Wear`, `ApparelUtility` | 根据气温与威胁自主指派小人穿戴御寒衣物或防弹护甲 |
| `actions_query_buildable_structures` | `QueryBuildableStructuresCompositeTool` | `DefDatabase<ThingDef>`, `ResourceSafetyGuard` | 查询已解锁可建建筑、材料消耗、库存余量与当前基建瓶颈 |

## 代码约定

- 新组合工具继承 `CompositeToolCallBase`，实现 `Id`/`BuildDefinition`/`RequiredToolIds`/`ExecuteAsync`（基类通过 `BuildDefinition()` 懒加载缓存 `Definition` 实例）
- 参数解析用 `TryGetArgument<T>`，失败返回 `Result.Err(RimMindError)`
- 子调用参数构造用 `BuildArgumentsJson`，汇总用 `BuildStepSummary`
- 多步原子调用之间插入 `ct.ThrowIfCancellationRequested()`
- 明确部分失败语义（best-effort 步骤加注释说明）
- 翻译键前缀: `RimMind.Actions.*`
- Harmony ID: `mcocdaa.RimMindActions`

## 操作边界

### 必须做
- 新组合工具声明正确的 `RequiredToolIds`
- 验证失败用 `Result<ToolResult, RimMindError>.Err(...)`，禁止 `Result.Ok(ToolResult.Fail(...))`
- 每个新类编写单元测试

### 绝对禁止
- 硬编码注册组合工具（用反射 registrar）
- 在 `ExecuteAsync` 中吞掉 `OperationCanceledException`
- 重新引入已废弃的 `RimMindActionsAPI`/`ActionsBridge`/`RimMindActionsSettings`

## 历史说明

H2 阶段（2026-05）将 24 个内置动作迁移至 Core Mechanism 系统。原 `ActionsBridge`、`RimMindActionsAPI`、`BatchActionIntent`、`ActionResult`、`WorkTargetInfo`、`RimMindActionsSettings`、`DelayedActionQueue` 已于本次清理移除（备份至 `Refs/backup/RimMind-Actions/`）。运行时 Core 使用 `NullAgentActionBridge` 作为 `IAgentActionBridge` 默认实现。
