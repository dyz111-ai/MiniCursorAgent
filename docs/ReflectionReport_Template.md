# 反思报告模板

## 1. 项目简介

本项目实现了一个基于 .NET 8 WPF 的 Mini Cursor 代码助手。系统能够打开单个 C# 文件，通过 DeepSeek API 进行推理，并在 Agent Loop 中调用本地工具完成代码读取、审查、指标分析、修改和构建诊断。

## 2. Agent 内部工作原理

### 2.1 核心组件

本项目由四个核心组件组成：

1. LLM：`DeepSeekClient`，负责理解用户目标并决定下一步动作。
2. Agent Loop：`ReActAgent.RunAsync()`，负责 Thought → Action → Observation 循环。
3. Tools：`Tools` 文件夹下的多个工具类，负责执行确定性的本地操作。
4. Memory：`AgentMemory`，负责保存当前文件、代码和最近结果。

### 2.2 ReAct 循环说明

Agent 每一步要求 LLM 返回一个 JSON 对象：

```json
{
  "thought": "当前思考",
  "action": "工具名或 FinalAnswer",
  "actionInput": {}
}
```

程序解析 JSON 后，如果 `action` 是工具名，则调用对应工具；如果是 `FinalAnswer`，则结束循环并输出最终答案。

## 3. 核心循环代码逐行解读

可以重点解释 `Agents/ReActAgent.cs` 中的 `RunAsync()` 方法：

1. 将用户任务加入 Memory。
2. 创建 `messages`，其中包括 System Prompt 和用户任务。
3. 使用 `for` 循环限制最大执行步数。
4. 每一步调用 `_llm.ChatAsync()` 获取模型输出。
5. 使用 `AgentDecision.Parse()` 解析模型返回的 JSON。
6. 在界面输出 Thought、Action 和 ActionInput。
7. 如果 Action 是 `FinalAnswer`，返回最终结果。
8. 如果 Action 是工具名，从 `_tools` 字典中找到对应工具。
9. 调用 `tool.ExecuteAsync()` 执行本地操作。
10. 将 Observation 追加回 messages，让模型基于工具结果继续推理。

## 4. 工具设计决策

本项目没有直接让 LLM 自由操作文件系统，而是把文件操作封装成固定工具，降低风险：

- `FileReadTool`：只负责读取文件。
- `CodeReviewTool`：只负责规则式审查。
- `CodeMetricsTool`：只负责指标统计。
- `ReplaceTextTool`：只负责小范围替换。
- `FileWriteTool`：只负责完整写入。
- `BuildTool`：只负责执行 `dotnet build`。

这种设计可以让 Agent 的行动边界更清晰，也便于答辩时解释每个工具的作用。

## 5. Memory 设计

项目采用简单短期记忆，而不是向量数据库。原因是本项目只处理单文件场景，主要需要保存当前编辑器内容、最近审查结果和对话历史。`AgentMemory` 保留最近 20 条对话，避免 Prompt 过长。

## 6. AI 使用情况说明

本项目可以如实说明：

- 项目框架和部分代码由 AI 辅助生成。
- 我对 UI、Agent Loop、工具设计、错误处理和 README 进行了理解、调整和测试。
- 答辩时我可以解释核心循环、工具调用和 Memory 的实现逻辑。

## 7. 不足与改进

当前版本主要不足：

1. 只支持单个 C# 文件，不支持完整项目级代码索引。
2. `CodeReviewTool` 是规则式检查，不如 Roslyn 精确。
3. 修改代码依赖 LLM 生成替换文本，复杂修改时可能失败。
4. 目前没有 Git diff 预览界面。

后续可以加入 Roslyn 分析、多文件项目搜索、Git diff 预览和流式输出。
