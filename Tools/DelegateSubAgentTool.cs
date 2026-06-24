using MiniCursorAgent.Agents;
using MiniCursorAgent.Memory;
using MiniCursorAgent.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace MiniCursorAgent.Tools;

public sealed class DelegateSubAgentTool : IAgentTool
{
    // Lazy resolution breaks the circular dependency:
    //   DelegateSubAgentTool (IAgentTool) → AgentCoordinator → IEnumerable<IAgentTool>
    // AgentCoordinator is only resolved when ExecuteAsync is first called,
    // by which point all singletons have already been constructed.
    private readonly Lazy<AgentCoordinator> _coordinator;

    public string Name => "DelegateSubAgent";
    public string Description =>
        "派发一个或多个专业子智能体并行分析当前代码，返回汇总报告。" +
        "可选角色：security（安全漏洞）、docs（文档可读性）、performance（性能优化）、refactor（重构建议）。" +
        "输入：{\"roles\":[\"security\",\"performance\"]}，不填 roles 则派发全部 4 个角色。";

    public DelegateSubAgentTool(IServiceProvider sp)
    {
        _coordinator = new Lazy<AgentCoordinator>(() => sp.GetRequiredService<AgentCoordinator>());
    }

    public async Task<string> ExecuteAsync(JsonElement input, AgentMemory memory, CancellationToken cancellationToken = default)
    {
        List<string>? roles = null;

        if (input.ValueKind == JsonValueKind.Object &&
            input.TryGetProperty("roles", out var rolesEl) &&
            rolesEl.ValueKind == JsonValueKind.Array)
        {
            roles = rolesEl.EnumerateArray()
                           .Select(r => r.GetString() ?? "")
                           .Where(r => r.Length > 0)
                           .ToList();
        }

        var log = memory.LogCallback ?? ((_, _) => { });
        return await _coordinator.Value.RunParallelAnalysisAsync(memory, null, log, cancellationToken, roles);
    }
}
