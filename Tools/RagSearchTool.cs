using MiniCursorAgent.Memory;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MiniCursorAgent.Tools;

public sealed class RagSearchTool : IAgentTool
{
    private readonly RagStore _store;

    public RagSearchTool(RagStore store) => _store = store;

    public string Name => "RagSearchTool";

    public string Description =>
        "在已索引的代码知识库中进行语义检索，返回与查询最相关的代码片段（RAG 增强检索）。" +
        "输入：{\"query\":\"搜索关键词或问题\",\"topK\":3}";

    public Task<string> ExecuteAsync(JsonElement input, AgentMemory memory, CancellationToken cancellationToken = default)
    {
        var query = ToolJson.GetString(input, "query");
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult("RagSearchTool 错误：缺少 query 参数。");

        var topK = 3;
        if (input.ValueKind == JsonValueKind.Object &&
            input.TryGetProperty("topK", out var topKEl) &&
            topKEl.TryGetInt32(out var k))
        {
            topK = Math.Clamp(k, 1, 10);
        }

        if (_store.ChunkCount == 0)
        {
            if (!string.IsNullOrEmpty(memory.CurrentCode))
                _store.Index(memory.CurrentCode, memory.CurrentFilePath ?? "current");

            if (_store.ChunkCount == 0)
                return Task.FromResult("RagSearchTool：知识库为空。请先打开一个文件以建立索引。");
        }

        var results = _store.Search(query, topK);

        if (results.Count == 0)
            return Task.FromResult($"RagSearchTool：未找到与「{query}」相关的代码片段（知识库共 {_store.ChunkCount} 个片段）。");

        var sb = new StringBuilder();
        sb.AppendLine($"RagSearchTool 检索结果（查询：「{query}」，共 {results.Count} 条）：\n");

        for (var i = 0; i < results.Count; i++)
        {
            var (text, source, startLine, score) = results[i];
            sb.AppendLine($"--- 片段 {i + 1}（来源：{Path.GetFileName(source)}，第 {startLine} 行起，相关度：{score:F3}）---");
            sb.AppendLine(text.Length > 600 ? text[..600] + "\n...(已截断)" : text);
            sb.AppendLine();
        }

        return Task.FromResult(sb.ToString());
    }
}
