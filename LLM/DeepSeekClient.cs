using Microsoft.Extensions.Logging;
using MiniCursorAgent.Models;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniCursorAgent.LLM;

public sealed class DeepSeekClient
{
    private readonly HttpClient _httpClient = new();
    private readonly string _model;
    private readonly string _apiKey;
    private readonly ILogger<DeepSeekClient> _logger;

    public DeepSeekClient(AppSettings settings, ILogger<DeepSeekClient> logger)
    {
        _apiKey = settings.DeepSeekApiKey;
        _model = settings.DeepSeekModel;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(settings.DeepSeekBaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(90);
    }

    public async Task<string> ChatAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("缺少 DeepSeek API Key。请设置环境变量 DEEPSEEK_API_KEY，或在 appsettings.json 中填写 DeepSeek:ApiKey。");
        }

        var messageList = messages.ToList();
        _logger.LogDebug("向 DeepSeek API 发送请求 (model={Model}, messages={Count})", _model, messageList.Count);

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var payload = new
        {
            model = _model,
            messages = messageList.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            temperature = 0.2,
            max_tokens = 4096
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("DeepSeek API 请求失败：{StatusCode} {Reason}", (int)response.StatusCode, response.ReasonPhrase);
            throw new InvalidOperationException($"DeepSeek API 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}\n{responseText}");
        }

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;

        if (root.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content))
        {
            var result = content.GetString() ?? string.Empty;
            _logger.LogDebug("DeepSeek API 响应成功，返回 {Length} 字符", result.Length);
            return result;
        }

        throw new InvalidOperationException("DeepSeek API 返回格式异常：" + responseText);
    }

    public async Task<string> ChatStreamingAsync(
        IEnumerable<ChatMessage> messages,
        Action<string> onToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("缺少 DeepSeek API Key。请设置环境变量 DEEPSEEK_API_KEY，或在 appsettings.json 中填写 DeepSeek:ApiKey。");
        }

        var messageList = messages.ToList();
        _logger.LogDebug("向 DeepSeek API 发送流式请求 (model={Model}, messages={Count})", _model, messageList.Count);

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var payload = new
        {
            model = _model,
            messages = messageList.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            temperature = 0.2,
            max_tokens = 4096,
            stream = true
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }),
            Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("DeepSeek API 流式请求失败：{StatusCode} {Reason}", (int)response.StatusCode, response.ReasonPhrase);
            throw new InvalidOperationException($"DeepSeek API 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}\n{errorText}");
        }

        var sb = new StringBuilder();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line is null) break;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var data = line[6..];
            if (data == "[DONE]") break;

            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var content))
                {
                    var token = content.GetString();
                    if (!string.IsNullOrEmpty(token))
                    {
                        sb.Append(token);
                        onToken(token);
                    }
                }
            }
            catch (JsonException) { }
        }

        var result = sb.ToString();
        _logger.LogDebug("DeepSeek 流式响应完成，共 {Length} 字符", result.Length);
        return result;
    }
}
