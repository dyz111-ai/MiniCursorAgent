using System.IO;
using System.Text.Json;

namespace MiniCursorAgent.LLM;

public sealed class AppSettings
{
    public string DeepSeekApiKey { get; init; } = string.Empty;
    public string DeepSeekBaseUrl { get; init; } = "https://api.deepseek.com";
    public string DeepSeekModel { get; init; } = "deepseek-v4-flash";
    public int AgentMaxSteps { get; init; } = 6;

    public static AppSettings Load()
    {
        var apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? string.Empty;
        var baseUrl = "https://api.deepseek.com";
        var model = "deepseek-v4-flash";
        var maxSteps = 6;

        var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(appSettingsPath))
        {
            appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        }

        if (File.Exists(appSettingsPath))
        {
            using var stream = File.OpenRead(appSettingsPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            if (root.TryGetProperty("DeepSeek", out var deepSeek))
            {
                var jsonApiKey = ReadString(deepSeek, "ApiKey");
                var jsonBaseUrl = ReadString(deepSeek, "BaseUrl");
                var jsonModel = ReadString(deepSeek, "Model");

                if (string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(jsonApiKey))
                {
                    apiKey = jsonApiKey;
                }

                if (!string.IsNullOrWhiteSpace(jsonBaseUrl))
                {
                    baseUrl = jsonBaseUrl;
                }

                if (!string.IsNullOrWhiteSpace(jsonModel))
                {
                    model = jsonModel;
                }
            }

            if (root.TryGetProperty("Agent", out var agent) &&
                agent.TryGetProperty("MaxSteps", out var maxStepsElement) &&
                maxStepsElement.TryGetInt32(out var parsedMaxSteps))
            {
                maxSteps = Math.Clamp(parsedMaxSteps, 2, 12);
            }
        }

        return new AppSettings
        {
            DeepSeekApiKey = apiKey,
            DeepSeekBaseUrl = baseUrl,
            DeepSeekModel = model,
            AgentMaxSteps = maxSteps
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
