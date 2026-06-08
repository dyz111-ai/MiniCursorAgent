# Mini Cursor Agent：基于 .NET 的智能代码编辑与审查 Agent

这是一个课程作业版“Mini Cursor”项目：使用 **.NET 8 WPF + AvalonEdit + DeepSeek API + 自写 ReAct Agent Loop** 实现一个轻量级 AI 编程助手。

它支持打开单个 C# 文件，在右侧 AI 面板输入任务，然后 Agent 会按 `Thought → Action → Observation` 的循环调用工具，完成代码审查、指标分析、局部修改、完整写入和构建检查。

## 一、功能概览

- WPF 桌面界面
- AvalonEdit 代码编辑器
- DeepSeek HTTP API 调用
- ReAct Agent Loop 显示
- 简单 Memory：当前文件、当前代码、最近审查结果、最近构建结果、最近写入路径、最近对话
- 自定义工具：
  - `FileReadTool`：读取当前 C# 文件
  - `CodeReviewTool`：规则式代码审查
  - `CodeMetricsTool`：统计代码行数、方法数、复杂度等
  - `ReplaceTextTool`：小范围替换代码并自动备份
  - `FileWriteTool`：整体写入代码并自动备份
  - `BuildTool`：寻找 `.csproj` 并执行 `dotnet build`
- 错误处理：API Key 缺失、文件不存在、JSON 解析失败、写入失败、构建失败等
- 日志记录：界面显示 Agent Loop，同时写入 `logs/agent-yyyyMMdd.log`

## 二、运行环境

- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 或 Rider / VS Code
- DeepSeek API Key

> WPF 是 Windows 桌面技术，因此本项目需要在 Windows 环境运行。

## 三、配置 DeepSeek API Key

推荐使用环境变量，避免把 Key 写入代码：

```powershell
$env:DEEPSEEK_API_KEY="你的 DeepSeek API Key"
dotnet run
```

也可以修改 `appsettings.json`：

```json
{
  "DeepSeek": {
    "ApiKey": "你的 DeepSeek API Key",
    "BaseUrl": "https://api.deepseek.com",
    "Model": "deepseek-chat"
  },
  "Agent": {
    "MaxSteps": 6
  }
}
```

## 四、运行方式

在项目目录中执行：

```powershell
dotnet restore
dotnet run
```

或者用 Visual Studio 打开 `MiniCursorAgent.csproj` 后直接运行。

## 五、演示流程

1. 启动程序。
2. 点击“打开 C# 文件”。
3. 可以打开 `Samples/BadCode.cs`。
4. 右侧输入：

```text
帮我审查当前文件，并指出可以改进的地方
```

5. 观察右侧日志中的：

```text
Thought: ...
Action: FileReadTool
Observation: ...

Thought: ...
Action: CodeReviewTool
Observation: ...
```

6. 可以继续输入：

```text
帮我把同步读取文件改成异步读取，并直接修改文件
```

7. Agent 会调用 `ReplaceTextTool` 或 `FileWriteTool` 修改文件，修改前会自动生成 `.bak` 备份。

## 六、作业要求对应关系

| 作业要求 | 本项目实现 |
|---|---|
| C# / .NET 8+ | `.NET 8 WPF` |
| LLM 集成 | `DeepSeekClient` 通过 HTTP 调用 DeepSeek Chat Completions API |
| Agent Loop | `ReActAgent.RunAsync()` 实现 Thought → Action → Observation 循环 |
| 至少 3 个自定义工具 | 实现 6 个工具：FileRead、Review、Metrics、Replace、Write、Build |
| Memory | `AgentMemory` 保存当前文件、代码、最近结果和对话历史 |
| 用户交互界面 | WPF 桌面界面，含代码编辑区和 AI Agent 面板 |
| async/await | API、文件读写、构建执行均采用异步方法 |
| 错误处理 | API、文件、构建、JSON 解析均有异常处理 |
| 日志记录 | UI 日志 + 本地 logs 文件 |

## 七、核心代码位置

- `Agents/ReActAgent.cs`：Agent 核心循环
- `LLM/DeepSeekClient.cs`：DeepSeek API 调用
- `Memory/AgentMemory.cs`：简单记忆机制
- `Tools/*.cs`：自定义工具
- `MainWindow.xaml` / `MainWindow.xaml.cs`：WPF 界面

## 八、答辩重点

答辩时重点解释 `ReActAgent.RunAsync()`：

1. 构造 System Prompt，告诉 LLM 可用工具和 JSON 输出格式。
2. 把用户任务和 Memory 组织成上下文。
3. 循环调用 LLM。
4. 解析 LLM 返回的 `thought/action/actionInput`。
5. 根据 `action` 调用对应 C# 工具。
6. 把工具返回值作为 Observation 加回上下文。
7. 重复直到 LLM 输出 `FinalAnswer` 或达到最大步数。

## 九、注意事项

- 自动写入代码前会备份旧文件，备份格式为：`xxx.cs.yyyyMMddHHmmss.bak`。
- 如果不想让 Agent 修改文件，取消勾选“允许 Agent 写入文件”。
- `BuildTool` 需要当前文件所在目录或上级目录存在 `.csproj`。
- 如果只是打开孤立的 `.cs` 文件，仍可进行审查和修改，但不能执行完整项目构建。
