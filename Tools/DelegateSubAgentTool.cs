using MiniCursorAgent.Agents;
using MiniCursorAgent.LLM;
using MiniCursorAgent.Memory;
using MiniCursorAgent.Models;
using System.Text.Json;

namespace MiniCursorAgent.Tools;

public sealed class DelegateSubAgentTool : IAgentTool
{
    private readonly DeepSeekClient _llm;
    private readonly IServiceProvider _serviceProvider;

    private static readonly HashSet<string> ReadOnlyToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "FileReadTool", "CodeReviewTool", "CodeMetricsTool", "RagSearchTool"
    };

    public string Name => "DelegateSubAgentTool";

    public string Description =>
        "将复杂子任务委托给专门的 SubAgent（子智能体）并行执行。支持角色：security（安全漏洞分析）、docs（文档质量分析）。" +
        "输入：{\"task\":\"子任务的具体描述\",\"role\":\"security|docs\"}";

    public DelegateSubAgentTool(DeepSeekClient llm, IServiceProvider serviceProvider)
    {
        _llm = llm;
        _serviceProvider = serviceProvider;
    }

    public async Task<string> ExecuteAsync(JsonElement input, AgentMemory memory, CancellationToken cancellationToken = default)
    {
        var task = ToolJson.GetString(input, "task");
        var role = ToolJson.GetString(input, "role") ?? "general";

        if (string.IsNullOrWhiteSpace(task))
            return "DelegateSubAgentTool 错误：缺少 task 参数。";

        var allTools = (_serviceProvider.GetService(typeof(IEnumerable<IAgentTool>)) as IEnumerable<IAgentTool>)
                       ?? Enumerable.Empty<IAgentTool>();

        var readOnlyTools = allTools.Where(t => ReadOnlyToolNames.Contains(t.Name));
        var logCallback = memory.LogCallback ?? ((_, _) => { });

        var subAgent = new SubAgent(role, _llm, readOnlyTools, memory, logCallback);

        try
        {
            var result = await subAgent.RunAsync(task, cancellationToken);
            return $"[SubAgent:{role}] 执行完成：\n\n{result}";
        }
        catch (Exception ex)
        {
            return $"[SubAgent:{role}] 执行失败：{ex.Message}";
        }
    }
}
