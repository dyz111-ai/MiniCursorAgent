using MiniCursorAgent.LLM;
using MiniCursorAgent.Memory;
using MiniCursorAgent.Models;
using MiniCursorAgent.Tools;
using System.Text;
using System.Text.Json;

namespace MiniCursorAgent.Agents;

public sealed class ReActAgent
{
    private readonly DeepSeekClient _llm;
    private readonly Dictionary<string, IAgentTool> _tools;
    private readonly AgentMemory _memory;
    private readonly RagStore? _ragStore;
    private readonly int _maxSteps;
    private readonly Action<string, AgentLogType> _log;

    public ReActAgent(
        DeepSeekClient llm,
        IEnumerable<IAgentTool> tools,
        AgentMemory memory,
        int maxSteps,
        Action<string, AgentLogType> log,
        RagStore? ragStore = null)
    {
        _llm = llm;
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        _memory = memory;
        _ragStore = ragStore;
        _maxSteps = maxSteps;
        _log = log;
        _memory.LogCallback = log;
    }

    public async Task<string> RunAsync(string userGoal, CancellationToken cancellationToken = default)
    {
        // 1. RAG 知识库检索
        var ragContext = _ragStore?.Search(userGoal, topK: 3);
        if (ragContext?.Count > 0)
            _log($"📚 RAG 检索到 {ragContext.Count} 条相关知识，已注入上下文。\n", AgentLogType.System);

        // 2. 组装主 Agent 消息列表
        var messages = new List<ChatMessage> { new("system", BuildSystemPrompt()) };
        foreach (var item in _memory.ConversationHistory)
            messages.Add(item);

        messages.Add(new ChatMessage("user", BuildTaskPrompt(userGoal, ragContext)));
        _memory.AddConversation("user", userGoal);

        // 4. 主 Agent ReAct 循环
        for (var step = 1; step <= _maxSteps; step++)
        {
            _log($"\n--- 主 Agent Step {step}/{_maxSteps} ---\n", AgentLogType.Step);

            _log("🔄 ", AgentLogType.Streaming);
            var rawResponse = await _llm.ChatStreamingAsync(messages,
                token => _log(token, AgentLogType.Streaming),
                cancellationToken);
            _log("\n", AgentLogType.Streaming);

            AgentDecision decision;
            try
            {
                decision = AgentDecision.Parse(rawResponse);
            }
            catch (Exception ex)
            {
                _log("❌ 解析失败：" + ex.Message + "\n", AgentLogType.Error);
                messages.Add(new ChatMessage("assistant", rawResponse));
                messages.Add(new ChatMessage("user",
                    "你的上一条回复不是合法 JSON。请只返回 JSON：" +
                    "{\"thought\":\"...\",\"action\":\"ToolName 或 FinalAnswer\",\"actionInput\":{...}}"));
                continue;
            }

            _log($"💭 Thought: {decision.Thought}\n", AgentLogType.Thought);
            _log($"🔧 Action: {decision.Action}\n", AgentLogType.Action);
            _log($"📋 ActionInput: {decision.ActionInput}\n", AgentLogType.ActionInput);

            if (decision.Action.Equals("FinalAnswer", StringComparison.OrdinalIgnoreCase))
            {
                var answer = decision.ActionInput.ValueKind == JsonValueKind.Object &&
                             decision.ActionInput.TryGetProperty("answer", out var ae)
                    ? ae.GetString() ?? string.Empty
                    : decision.ActionInput.ToString();
                _memory.AddConversation("assistant", answer);
                return answer;
            }

            if (!_tools.TryGetValue(decision.Action, out var tool))
            {
                var msg = $"未知工具：{decision.Action}。可用工具：{string.Join(", ", _tools.Keys)}。";
                _log("Observation: " + msg + "\n", AgentLogType.Observation);
                messages.Add(new ChatMessage("assistant", rawResponse));
                messages.Add(new ChatMessage("user", "Observation: " + msg));
                continue;
            }

            var observation = await tool.ExecuteAsync(decision.ActionInput, _memory, cancellationToken);
            _log("📤 Observation:\n" + observation + "\n", AgentLogType.Observation);
            messages.Add(new ChatMessage("assistant", rawResponse));
            messages.Add(new ChatMessage("user", $"Observation from {tool.Name}:\n{observation}"));
        }

        var fallback = "达到最大步数限制，请缩小任务范围或增加 Agent:MaxSteps。";
        _memory.AddConversation("assistant", fallback);
        return fallback;
    }

    private string BuildSystemPrompt()
    {
        var toolDescriptions = new StringBuilder();
        foreach (var tool in _tools.Values)
            toolDescriptions.AppendLine($"- {tool.Name}: {tool.Description}");

        return $$"""
你是 Mini Cursor Agent 的主智能体，运行在 WPF 桌面程序中，专注于代码审查、修改和诊断。

【多智能体协作】
你可以调用 DelegateSubAgent 工具，将代码分析任务委托给专业子智能体并行执行。
四个可选角色及其专长：
- security：专注安全漏洞、注入风险、权限缺失、敏感信息泄露
- docs：专注注释完整性、命名质量、可读性、代码复杂度指标
- performance：专注性能瓶颈、低效写法、异步阻塞、内存分配
- refactor：专注结构问题、代码异味、耦合度、设计模式

你的核心职责：
- 理解用户意图，自主判断当前任务是否需要专业子智能体辅助，以及需要哪几个
- 对于需要深入分析的任务，优先调用相关子智能体获取专业视角，再做决策
- 对于明确的小改动或简单问答，可直接处理而不必委托子智能体
- 综合所有信息，使用工具完成操作，给出清晰可操作的最终答复

工作格式（ReAct）：每次只输出一个合法 JSON，不要输出 Markdown 或代码围栏：
{
  "thought": "当前思考和判断",
  "action": "工具名或 FinalAnswer",
  "actionInput": { }
}

可用工具：
{{toolDescriptions}}
- FinalAnswer: 完成任务，输出最终答复。输入：{"answer":"..."}

行为规则：
1. 每步只调用一个工具。
2. 修改代码优先用 ReplaceTextTool（小改动），整体重写用 FileWriteTool。
4. 修改后建议调用 BuildTool 验证（仅限 .NET 项目且用户明确要求构建时）。
5. 最终答复用中文，涵盖：问题摘要、已执行操作、具体建议。
6. 只使用列出的工具，不要编造工具名。
7. 结合对话历史理解"继续""上次"等指代。

【当前会话上下文】
{{_memory.BuildMemorySummary()}}
""";
    }

    private string BuildTaskPrompt(
        string userGoal,
        List<(string Content, string Title, double Score)>? ragContext)
    {
        var sb = new StringBuilder();

        if (ragContext?.Count > 0)
        {
            sb.AppendLine("【相关 C# 知识库参考（RAG 自动检索）】");
            foreach (var (content, title, score) in ragContext)
            {
                sb.AppendLine($"▸ {title}（相关度 {score:F2}）");
                sb.AppendLine(content.Trim());
                sb.AppendLine();
            }
            sb.AppendLine("---");
            sb.AppendLine();
        }

        sb.AppendLine($"【用户任务】{userGoal}");
        sb.AppendLine("\n请基于以上分析报告，开始执行 Agent Loop。每次只输出一个合法 JSON 对象。");
        return sb.ToString();
    }
}
