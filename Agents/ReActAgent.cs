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
    }

    public async Task<string> RunAsync(string userGoal, CancellationToken cancellationToken = default)
    {
        _memory.AddConversation("user", userGoal);

        var messages = new List<ChatMessage>
        {
            new("system", BuildSystemPrompt()),
            new("user", BuildUserPrompt(userGoal))
        };

        for (var step = 1; step <= _maxSteps; step++)
        {
            _log($"\n--- Step {step}/{_maxSteps} ---\n", AgentLogType.Step);

            var rawResponse = await _llm.ChatAsync(messages, cancellationToken);
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
你是一个运行在 WPF 桌面程序中的 Mini Cursor 代码助手，面向 C# / .NET 单文件代码审查、修改和构建诊断。

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
2. 如果需要了解代码内容，先调用 FileReadTool。
3. 如果用户要求审查，通常流程是 FileReadTool -> CodeReviewTool -> CodeMetricsTool -> FinalAnswer。
4. 如果用户要求修改代码，优先使用 ReplaceTextTool 做小范围修改；只有需要整体重写时才用 FileWriteTool。
5. 写入代码前，必须确保 actionInput 中的新代码或替换文本是完整、准确的。
6. 如果用户要求检查是否能编译，可以调用 BuildTool。BuildTool 会自动寻找当前文件上级目录中的 .csproj。
7. 最终回答要用中文，说明：做了哪些工具调用、发现了什么问题、是否修改了文件、下一步建议。
8. 不要编造不存在的工具。只能使用上面列出的工具。
""";
    }

    private string BuildUserPrompt(string userGoal)
    {
        var history = new StringBuilder();
        foreach (var item in _memory.ConversationHistory.TakeLast(8))
        {
            history.AppendLine($"{item.Role}: {TrimForPrompt(item.Content, 800)}");
        }

        return $$"""
用户任务：{{userGoal}}

当前上下文：
{{_memory.BuildMemorySummary()}}

最近对话历史：
{{history}}

请从第一步开始执行 Agent Loop。记住：只输出一个合法 JSON 对象。
""";
    }

    private static string TrimForPrompt(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...（已截断）";
    }
}
