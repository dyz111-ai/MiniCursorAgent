using MiniCursorAgent.LLM;
using MiniCursorAgent.Memory;
using MiniCursorAgent.Models;
using MiniCursorAgent.Tools;
using System.Text.Json;

namespace MiniCursorAgent.Agents;

public sealed class SubAgent
{
    private readonly DeepSeekClient _llm;
    private readonly Dictionary<string, IAgentTool> _tools;
    private readonly AgentMemory _parentMemory;
    private readonly Action<string, AgentLogType> _log;
    private const int MaxSteps = 4;

    public string Role { get; }

    public SubAgent(
        string role,
        DeepSeekClient llm,
        IEnumerable<IAgentTool> readOnlyTools,
        AgentMemory parentMemory,
        Action<string, AgentLogType> log)
    {
        Role = role;
        _llm = llm;
        _tools = readOnlyTools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        _parentMemory = parentMemory;
        _log = log;
    }

    public async Task<string> RunAsync(string task, CancellationToken cancellationToken = default)
    {
        _log($"[SubAgent:{Role}] 开始执行子任务...\n", AgentLogType.Step);

        var subMemory = new AgentMemory
        {
            CurrentFilePath = _parentMemory.CurrentFilePath,
            CurrentCode = _parentMemory.CurrentCode,
            AllowFileWrite = false
        };

        var messages = new List<ChatMessage>
        {
            new("system", BuildSubSystemPrompt()),
            new("user", task + "\n\n请从第一步开始执行。记住：只输出一个合法 JSON 对象。")
        };

        for (var step = 1; step <= MaxSteps; step++)
        {
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
            catch
            {
                messages.Add(new ChatMessage("assistant", rawResponse));
                messages.Add(new ChatMessage("user", "你的上一条回复不是合法 JSON，请只返回 JSON。"));
                continue;
            }

            _log($"[SubAgent:{Role}] 💭 {decision.Thought}\n", AgentLogType.Thought);
            _log($"[SubAgent:{Role}] 🔧 {decision.Action}\n", AgentLogType.Action);

            if (decision.Action.Equals("FinalAnswer", StringComparison.OrdinalIgnoreCase))
            {
                var answer = decision.ActionInput.ValueKind == JsonValueKind.Object &&
                             decision.ActionInput.TryGetProperty("answer", out var ae)
                    ? ae.GetString() ?? string.Empty
                    : decision.ActionInput.ToString();

                _log($"[SubAgent:{Role}] ✅ 子任务完成。\n", AgentLogType.Step);
                return answer;
            }

            if (!_tools.TryGetValue(decision.Action, out var tool))
            {
                var obs = $"未知工具：{decision.Action}。可用工具：{string.Join(", ", _tools.Keys)}";
                messages.Add(new ChatMessage("assistant", rawResponse));
                messages.Add(new ChatMessage("user", "Observation: " + obs));
                continue;
            }

            var observation = await tool.ExecuteAsync(decision.ActionInput, subMemory, cancellationToken);
            _log($"[SubAgent:{Role}] 📤 {observation[..Math.Min(300, observation.Length)]}...\n", AgentLogType.Observation);

            messages.Add(new ChatMessage("assistant", rawResponse));
            messages.Add(new ChatMessage("user", $"Observation from {tool.Name}:\n{observation}"));
        }

        return $"[SubAgent:{Role}] 已达到最大步数（{MaxSteps}）限制。";
    }

    private string BuildSubSystemPrompt() => Role switch
    {
        "security" => """
你是一个专注于代码安全分析的 SubAgent（子智能体）。你的唯一目标是发现代码中的安全漏洞和风险。
重点关注：SQL注入、XSS跨站脚本、硬编码密钥/密码、不安全的反序列化、权限检查缺失、路径遍历、敏感信息泄露等。

你必须使用 ReAct 格式工作。每次只输出一个合法 JSON 对象，格式如下：
{"thought":"分析当前情况","action":"工具名或FinalAnswer","actionInput":{}}

可用工具：
- FileReadTool: 读取当前代码文件。输入：{"path":"可选"}
- CodeReviewTool: 静态代码审查。输入：{}
- FinalAnswer: 结束，输入：{"answer":"安全分析报告"}
""",
        "docs" => """
你是一个专注于代码文档质量分析的 SubAgent（子智能体）。你的目标是评估代码的可读性和文档完整性。
重点关注：缺少注释的复杂逻辑、函数命名不清晰、缺少参数说明、魔法数字未说明、TODO/FIXME未处理等。

你必须使用 ReAct 格式工作。每次只输出一个合法 JSON 对象：
{"thought":"分析当前情况","action":"工具名或FinalAnswer","actionInput":{}}

可用工具：
- FileReadTool: 读取当前代码文件。输入：{"path":"可选"}
- CodeReviewTool: 静态代码审查（含TODO/FIXME检测）。输入：{}
- FinalAnswer: 结束，输入：{"answer":"文档质量分析报告"}
""",
        _ => """
你是一个代码质量分析的 SubAgent（子智能体）。只输出 JSON：
{"thought":"...","action":"工具名或FinalAnswer","actionInput":{}}
可用工具：FileReadTool、CodeReviewTool、RagSearchTool、FinalAnswer。
"""
    };
}
