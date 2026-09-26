<div align="center">

# RimMind-Actions ⚡
### 专为 RimWorld 1.6 打造的复合机制动作与战地应急自救系统

[English](README.md) | **简体中文**

<p>
  <a href="https://rimworldgame.com/"><img src="https://img.shields.io/badge/RimWorld-1.6-brightgreen.svg" alt="RimWorld 1.6"></a>
  <a href="https://github.com/mcocdaa/RimWorld-RimMind-Mod-Core"><img src="https://img.shields.io/badge/核心依赖-RimMind--Core-blue.svg" alt="依赖: RimMind-Core"></a>
  <a href="#"><img src="https://img.shields.io/badge/单元测试-49%2B%20通过-success.svg" alt="单元测试"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/开源协议-MIT-yellow.svg" alt="License: MIT"></a>
</p>

<p><em>为 AI 殖民者赋予战地急救、濒死脱险与自主战术执行能力。</em></p>

</div>

---

## 📖 模块概览

**RimMind-Actions** 负责将大语言模型产生的高级战术意图转化为稳健的多步骤游戏机制动作（**Mechanisms**）。在面临战地重伤失血、致命火灾或虫巢围困等突发险情时，殖民者能够自主发起复合救助，告别原地呆立。

### 核心特性
- **`StabilizeRestCompositeTool` 复合战地急救**：自主评估队友失血速率，火线就地包扎止血，扛起伤员转运至就近医疗床并下达静养指令。
- **4 级安全门禁拦截器**：严格校验路径安全性、危险逼近距离、殖民者身体机能及玩家手动优先级，杜绝自杀式动作。
- **灭火与安全避险**：实时感知爆炸半径与蔓延火势，自主寻找掩体脱离险境。

---

## 🎮 实机特性展示

![RimMind-Actions 实机展示](docs/images/showcase.jpg)
*战地紧急施救实机场景：AI 殖民者冒着硝烟快速为倒地队友止血，并协作转运至医疗床。*

---

## 🏛️ 依赖关系与设计原则

`RimMind-Actions` 严格单向依赖 `RimMind-Core` 公共 API：

```mermaid
flowchart LR
    Advisor["RimMind-Advisor / AI 规划器"] -->|发起高级意图| Actions["RimMind-Actions"]
    Actions -->|4 级安全门禁校验| Gate{"环境与状态安全?"}
    Gate -- 通过 --> Engine["Verse JobDriver / 派发实际动作"]
    Gate -- 驳回 --> Fallback["安全取消并记录原因"]
    Actions -->|通过公共 API 注册| Core["RimMind-Core 工具注册表"]
```

---

## 🛠️ 安装与加载顺序

确保 `RimMind-Core` 位于 `RimMind-Actions` 之前：

```text
1. Harmony
2. Core (RimWorld 原版)
3. RimMind-Core
4. RimMind-Actions
```

---

## 🧪 开发者测试指南

运行单元测试：

```powershell
dotnet test RimMind-Actions/Tests/RimMindActions.Tests.csproj -c Release
```

---

## 📜 开源协议

本项目采用 [MIT License](LICENSE) 开源许可证。
