using MiniCursorAgent.LLM;
using MiniCursorAgent.Memory;
using MiniCursorAgent.Models;
using MiniCursorAgent.Tools;
using System.Text;

namespace MiniCursorAgent.Agents;

public sealed class AgentCoordinator
{
    private readonly DeepSeekClient _llm;
    private readonly IReadOnlyList<IAgentTool> _readOnlyTools;

    private static readonly string[] Roles = ["security", "docs", "performance", "refactor"];

    private static readonly HashSet<string> ReadOnlyToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "FileReadTool", "CodeReviewTool", "CodeMetricsTool"
    };

    public AgentCoordinator(DeepSeekClient llm, IEnumerable<IAgentTool> allTools)
    {
        _llm = llm;
        _readOnlyTools = allTools.Where(t => ReadOnlyToolNames.Contains(t.Name)).ToList();
    }

    public static IReadOnlyList<string> AllRoles => Roles;

    /// <summary>
    /// 并行启动指定专业子智能体，返回汇总分析报告。roles 为 null 时启动全部。
    /// </summary>
    public async Task<string> RunParallelAnalysisAsync(
        AgentMemory memory,
        string? userTask,
        Action<string, AgentLogType> log,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? roles = null)
    {
        if (string.IsNullOrWhiteSpace(memory.CurrentCode))
            return string.Empty;

        var activeRoles = roles?.Count > 0
            ? Roles.Where(r => roles.Contains(r, StringComparer.OrdinalIgnoreCase)).ToArray()
            : Roles;

        if (activeRoles.Length == 0) return string.Empty;

        log($"🤝 协调者：并行启动 {activeRoles.Length} 个专业子智能体...\n", AgentLogType.Step);

        var subAgentTasks = activeRoles.Select(role =>
        {
            var agent = new SubAgent(role, _llm, _readOnlyTools, memory, log, maxSteps: 3);
            return (role: agent.DisplayName, resultTask: agent.RunAsync(userTask ?? string.Empty, cancellationToken));
        }).ToList();

        string[] results;
        try
        {
            results = await Task.WhenAll(subAgentTasks.Select(x => x.resultTask));
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }

        log("✅ 协调者：所有子智能体已完成，汇总报告中...\n", AgentLogType.Step);

        var sb = new StringBuilder();
        sb.AppendLine("【多 Agent 并行分析报告】");
        sb.AppendLine($"（{activeRoles.Length} 个专业子智能体已并行完成分析）");
        sb.AppendLine();

        for (var i = 0; i < subAgentTasks.Count; i++)
        {
            sb.AppendLine($"▸ {subAgentTasks[i].role} Agent：");
            sb.AppendLine(results[i].Trim());
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
