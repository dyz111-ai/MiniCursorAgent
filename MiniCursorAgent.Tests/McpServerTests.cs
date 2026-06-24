using Microsoft.Extensions.Logging.Abstractions;
using MiniCursorAgent.Memory;
using MiniCursorAgent.Services;
using MiniCursorAgent.Tools;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace MiniCursorAgent.Tests;

public class McpServerTests : IDisposable
{
    private readonly McpServer _server;
    private readonly HttpClient _http = new();
    private const int TestPort = 13001;
    private const string Url = "http://localhost:13001/mcp/";

    public McpServerTests()
    {
        var tools = new IAgentTool[] { new CodeReviewTool(), new CodeMetricsTool() };
        var memory = new AgentMemory { CurrentCode = "int x = 1;" };
        _server = new McpServer(tools, memory, NullLogger<McpServer>.Instance, TestPort);
        _server.Start();
    }

    public void Dispose()
    {
        _server.Dispose();
        _http.Dispose();
    }

    private async Task<JsonDocument> PostAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(Url, content);
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }

    [Fact]
    public async Task Initialize_ReturnsProtocolVersion()
    {
        using var doc = await PostAsync(new
        {
            jsonrpc = "2.0", id = 1, method = "initialize",
            @params = new { protocolVersion = "2024-11-05", capabilities = new { } }
        });

        var version = doc.RootElement
            .GetProperty("result")
            .GetProperty("protocolVersion")
            .GetString();

        Assert.Equal("2024-11-05", version);
    }

    [Fact]
    public async Task Initialize_ReturnsServerInfo()
    {
        using var doc = await PostAsync(new
        {
            jsonrpc = "2.0", id = 2, method = "initialize",
            @params = new { protocolVersion = "2024-11-05", capabilities = new { } }
        });

        var serverName = doc.RootElement
            .GetProperty("result")
            .GetProperty("serverInfo")
            .GetProperty("name")
            .GetString();

        Assert.Equal("MiniCursorAgent-MCP", serverName);
    }

    [Fact]
    public async Task ToolsList_ReturnsBothRegisteredTools()
    {
        using var doc = await PostAsync(new
        {
            jsonrpc = "2.0", id = 3, method = "tools/list", @params = new { }
        });

        var tools = doc.RootElement
            .GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToList();

        Assert.Contains("CodeReviewTool", tools);
        Assert.Contains("CodeMetricsTool", tools);
    }

    [Fact]
    public async Task ToolsList_EachToolHasDescription()
    {
        using var doc = await PostAsync(new
        {
            jsonrpc = "2.0", id = 4, method = "tools/list", @params = new { }
        });

        var tools = doc.RootElement
            .GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray();

        foreach (var tool in tools)
        {
            var desc = tool.GetProperty("description").GetString();
            Assert.False(string.IsNullOrWhiteSpace(desc));
        }
    }

    [Fact]
    public async Task ToolsCall_CodeReviewTool_ReturnsTextContent()
    {
        using var doc = await PostAsync(new
        {
            jsonrpc = "2.0", id = 5, method = "tools/call",
            @params = new { name = "CodeReviewTool", arguments = new { } }
        });

        var result = doc.RootElement.GetProperty("result");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        var isError = result.GetProperty("isError").GetBoolean();

        Assert.False(isError);
        Assert.Contains("CodeReviewTool", text);
    }

    [Fact]
    public async Task ToolsCall_UnknownTool_ReturnsError()
    {
        using var doc = await PostAsync(new
        {
            jsonrpc = "2.0", id = 6, method = "tools/call",
            @params = new { name = "NonExistentTool", arguments = new { } }
        });

        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task UnknownMethod_ReturnsMethodNotFoundError()
    {
        using var doc = await PostAsync(new
        {
            jsonrpc = "2.0", id = 7, method = "unknown/method", @params = new { }
        });

        var errorCode = doc.RootElement
            .GetProperty("error")
            .GetProperty("code")
            .GetInt32();

        Assert.Equal(-32601, errorCode);
    }

    [Fact]
    public async Task Response_AlwaysEchoesRequestId()
    {
        using var doc = await PostAsync(new
        {
            jsonrpc = "2.0", id = 42, method = "tools/list", @params = new { }
        });

        var id = doc.RootElement.GetProperty("id").GetInt32();

        Assert.Equal(42, id);
    }
}
