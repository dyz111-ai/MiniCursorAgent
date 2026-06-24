using Microsoft.Extensions.Logging;
using MiniCursorAgent.Memory;
using MiniCursorAgent.Tools;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MiniCursorAgent.Services;

public sealed class McpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Dictionary<string, IAgentTool> _tools;
    private readonly AgentMemory _memory;
    private readonly ILogger<McpServer> _logger;
    private CancellationTokenSource? _cts;

    public const int Port = 3001;

    public McpServer(IEnumerable<IAgentTool> tools, AgentMemory memory, ILogger<McpServer> logger, int port = Port)
    {
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        _memory = memory;
        _logger = logger;
        _listener.Prefixes.Add($"http://localhost:{port}/mcp/");
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        try
        {
            _listener.Start();
            _ = ListenAsync(_cts.Token);
            _logger.LogInformation("MCP Server 已启动，监听 http://localhost:{Port}/mcp/", Port);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP Server 启动失败（如需使用请以管理员身份运行，或执行 netsh add urlacl）");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _listener.Stop(); } catch { }
        _cts?.Dispose();
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = HandleRequestAsync(context, ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MCP Server 接受请求时出错");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        context.Response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
        context.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

        if (context.Request.HttpMethod == "OPTIONS")
        {
            context.Response.StatusCode = 204;
            context.Response.Close();
            return;
        }

        string responseBody;
        try
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            responseBody = await DispatchAsync(body, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP 请求处理失败");
            responseBody = MakeError(null, -32603, ex.Message);
        }

        var bytes = Encoding.UTF8.GetBytes(responseBody);
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, ct);
        context.Response.Close();
    }

    private async Task<string> DispatchAsync(string body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body))
            return MakeError(null, -32700, "Empty request body");

        using var reqDoc = JsonDocument.Parse(body);
        var root = reqDoc.RootElement;
        var id = root.TryGetProperty("id", out var idEl) ? idEl.GetRawText() : "null";
        var method = root.TryGetProperty("method", out var mEl) ? mEl.GetString() : null;

        return method switch
        {
            "initialize" => JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = JsonSerializer.Deserialize<object>(id),
                result = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "MiniCursorAgent-MCP", version = "1.0.0" }
                }
            }),

            "notifications/initialized" => string.Empty,

            "tools/list" => JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = JsonSerializer.Deserialize<object>(id),
                result = new
                {
                    tools = _tools.Values.Select(t => new
                    {
                        name = t.Name,
                        description = t.Description,
                        inputSchema = new
                        {
                            type = "object",
                            properties = new { },
                            additionalProperties = true
                        }
                    }).ToArray()
                }
            }),

            "tools/call" => await HandleToolCallAsync(root, id, ct),

            _ => MakeError(id, -32601, $"Method not found: {method}")
        };
    }

    private async Task<string> HandleToolCallAsync(JsonElement root, string id, CancellationToken ct)
    {
        if (!root.TryGetProperty("params", out var @params) ||
            !@params.TryGetProperty("name", out var nameProp))
        {
            return MakeError(id, -32602, "Missing params.name");
        }

        var toolName = nameProp.GetString() ?? string.Empty;
        if (!_tools.TryGetValue(toolName, out var tool))
            return MakeError(id, -32602, $"Tool not found: {toolName}");

        var arguments = @params.TryGetProperty("arguments", out var args) ? args : default;

        try
        {
            var result = await tool.ExecuteAsync(arguments, _memory, ct);
            return JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = JsonSerializer.Deserialize<object>(id),
                result = new
                {
                    content = new[] { new { type = "text", text = result } },
                    isError = false
                }
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = JsonSerializer.Deserialize<object>(id),
                result = new
                {
                    content = new[] { new { type = "text", text = ex.Message } },
                    isError = true
                }
            });
        }
    }

    private static string MakeError(string? id, int code, string message) =>
        JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = id is null ? (object?)null : JsonSerializer.Deserialize<object>(id),
            error = new { code, message }
        });
}
