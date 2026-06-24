using MiniCursorAgent.LLM;
using MiniCursorAgent.Memory;
using MiniCursorAgent.Models;
using MiniCursorAgent.Tools;
using System.Text;

namespace MiniCursorAgent.Agents;

public sealed class ReActAgent
{
    private readonly DeepSeekClient _llm;
    private readonly Dictionary<string, IAgentTool> _tools;
    private readonly AgentMemory _memory;
    private readonly int _maxSteps;
    private readonly Action<string, AgentLogType> _log;

    public ReActAgent(
        DeepSeekClient llm,
        IEnumerable<IAgentTool> tools,
        AgentMemory memory,
        int maxSteps,
        Action<string, AgentLogType> log)
    {
        _llm = llm;
        _tools = tools.ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);
        _memory = memory;
        _maxSteps = maxSteps;
        _log = log;
        _memory.LogCallback = log;
    }

    public async Task<string> RunAsync(string userGoal, CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new("system", BuildSystemPrompt())
        };

        foreach (var item in _memory.ConversationHistory)
        {
            messages.Add(item);
        }

        messages.Add(new ChatMessage("user", BuildTaskPrompt(userGoal)));
        _memory.AddConversation("user", userGoal);

        for (var step = 1; step <= _maxSteps; step++)
        {
            _log($"\n--- Step {step}/{_maxSteps} ---\n", AgentLogType.Step);

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
                _log("❌ LLM 回复解析失败：" + ex.Message + "\n", AgentLogType.Error);
                _log("原始回复：\n" + rawResponse + "\n", AgentLogType.Error);

                messages.Add(new ChatMessage("assistant", rawResponse));
                messages.Add(new ChatMessage("user", "你的上一条回复不是合法 JSON。请只返回 JSON：{\"thought\":\"...\",\"action\":\"ToolName 或 FinalAnswer\",\"actionInput\":{...}}，不要输出 Markdown。"));
                continue;
            }

            _log($"💭 Thought: {decision.Thought}\n", AgentLogType.Thought);
            _log($"🔧 Action: {decision.Action}\n", AgentLogType.Action);
            _log($"📋 ActionInput: {decision.ActionInput}\n", AgentLogType.ActionInput);

            if (decision.Action.Equals("FinalAnswer", StringComparison.OrdinalIgnoreCase))
            {
                var answer = decision.ActionInput.ValueKind == System.Text.Json.JsonValueKind.Object &&
                             decision.ActionInput.TryGetProperty("answer", out var answerElement)
                    ? answerElement.GetString() ?? string.Empty
                    : decision.ActionInput.ToString();

                _memory.AddConversation("assistant", answer);
                return answer;
            }

            if (!_tools.TryGetValue(decision.Action, out var tool))
            {
                var unknownToolObservation = $"未知工具：{decision.Action}。可用工具：{string.Join(", ", _tools.Keys)}。";
                _log("Observation: " + unknownToolObservation + "\n", AgentLogType.Observation);
                messages.Add(new ChatMessage("assistant", rawResponse));
                messages.Add(new ChatMessage("user", "Observation: " + unknownToolObservation));
                continue;
            }

            var observation = await tool.ExecuteAsync(decision.ActionInput, _memory, cancellationToken);
            _log("📤 Observation:\n" + observation + "\n", AgentLogType.Observation);

            messages.Add(new ChatMessage("assistant", rawResponse));
            messages.Add(new ChatMessage("user", $"Observation from {tool.Name}:\n{observation}"));
        }

        var fallback = "达到最大 Agent Loop 步数限制。请缩小任务范围，或增加 Agent:MaxSteps。";
        _memory.AddConversation("assistant", fallback);
        return fallback;
    }

    private string BuildSystemPrompt()
    {
        var toolDescriptions = new StringBuilder();
        foreach (var tool in _tools.Values)
        {
            toolDescriptions.AppendLine($"- {tool.Name}: {tool.Description}");
        }

        return $$"""
你是一个运行在 WPF 桌面程序中的 Mini Cursor 代码助手，面向多种编程语言与文本格式的单文件代码审查、修改和诊断。

你必须使用 ReAct 思路工作：先思考 Thought，再选择一个 Action 工具，读取 Observation 后继续循环，直到给出 FinalAnswer。

【非常重要：你的每一次回复必须只输出一个合法 JSON 对象，不要输出 Markdown，不要输出代码块围栏。】
JSON 格式只能是：
{
  "thought": "说明你当前为什么要做这一步",
  "action": "工具名或 FinalAnswer",
  "actionInput": { }
}

可用工具：
{{toolDescriptions}}
- FinalAnswer: 结束任务。输入：{"answer":"给用户的最终答复"}

行为规则：
1. 每一步只能调用一个工具。
2. 如果需要了解文件内容，先调用 FileReadTool。
3. 如果用户要求审查，通常流程是 FileReadTool -> CodeReviewTool -> CodeMetricsTool -> FinalAnswer。
4. 如果用户要求修改代码，优先使用 ReplaceTextTool 做小范围修改；只有需要整体重写时才用 FileWriteTool。
5. 写入前，必须确保 actionInput 中的新内容完整、准确，并符合当前文件的语言/格式。
6. 根据当前文件类型选择合适的审查与修改方式；不要默认按某一种语言处理。
7. BuildTool 仅适用于当前文件位于 .NET 项目（能找到 .csproj）且用户明确要求编译/构建时；其他语言项目不要强行调用。
8. 最终回答要用中文，说明：做了哪些工具调用、发现了什么问题、是否修改了文件、下一步建议。
9. 不要编造不存在的工具。只能使用上面列出的工具。
10. 你可以参考之前的对话历史；若用户说「刚才」「上次」「继续」等，应结合历史理解其意图。

【当前会话上下文】
{{_memory.BuildMemorySummary()}}
""";
    }

    private string BuildTaskPrompt(string userGoal)
    {
        return $$"""
{{userGoal}}

请从第一步开始执行 Agent Loop。记住：只输出一个合法 JSON 对象。
""";
    }
}
