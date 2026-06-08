using MiniCursorAgent.Models;
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

    public DeepSeekClient(AppSettings settings)
    {
        _apiKey = settings.DeepSeekApiKey;
        _model = settings.DeepSeekModel;
        _httpClient.BaseAddress = new Uri(settings.DeepSeekBaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(90);
    }

    public async Task<string> ChatAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("缺少 DeepSeek API Key。请设置环境变量 DEEPSEEK_API_KEY，或在 appsettings.json 中填写 DeepSeek:ApiKey。");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var payload = new
        {
            model = _model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
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
            return content.GetString() ?? string.Empty;
        }

        throw new InvalidOperationException("DeepSeek API 返回格式异常：" + responseText);
    }
}
