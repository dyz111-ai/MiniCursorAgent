using MiniCursorAgent.Memory;
using System.Text.Json;

namespace MiniCursorAgent.Tools;

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    Task<string> ExecuteAsync(JsonElement input, AgentMemory memory, CancellationToken cancellationToken = default);
}
