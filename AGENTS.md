# AGENTS.md — RimMind-Actions

组合工具（Composite ToolCall）编排层。将 Core 提供的原子 ToolCall 组合为高级动作并注册到 Core 的 ToolRegistry。

## 项目定位

H2 阶段后，24 个内置动作已全部迁移至 Core Mechanism 系统。本模组现在的唯一职责是：
- 提供 `CompositeToolCallBase` 抽象基类（共享 JSON 辅助方法 + 原子调用编排）
- 实现具体组合工具（当前：`StabilizeRestCompositeTool`）
- 通过 `CompositeToolRegistrar` 反射自动发现并注册所有组合工具

依赖 Core，被 Advisor/其他模组通过 `RimMindAPI.Tools` 调用。

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
├── RimMindActionsMod.cs              Mod 入口，反射注册组合工具
├── Actions/
│   ├── CompositeToolCallBase.cs      抽象基类：ExecuteAtomicAsync + 共享 JSON 辅助
│   ├── StabilizeRestCompositeTool.cs 组合工具：undraft → force_rest
│   └── CompositeToolRegistrar.cs     反射发现并注册所有 CompositeToolCallBase 子类
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

## 组合工具清单

| Id | 原子依赖 | 说明 |
|----|----------|------|
| `actions.stabilize_rest` | `pawn.draft.toggle`, `pawn.job.set` | 解除征召（best-effort）→ 强制休息 |

## 代码约定

- 新组合工具继承 `CompositeToolCallBase`，实现 `Id`/`Definition`/`RequiredToolIds`/`ExecuteAsync`
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
