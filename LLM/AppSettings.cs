using Microsoft.Extensions.Configuration;

namespace MiniCursorAgent.LLM;

public sealed class AppSettings
{
    public string DeepSeekApiKey { get; init; } = string.Empty;
    public string DeepSeekBaseUrl { get; init; } = "https://api.deepseek.com";
    public string DeepSeekModel { get; init; } = "deepseek-v4-flash";
    public int AgentMaxSteps { get; init; } = 6;

    public static AppSettings Load(IConfiguration configuration)
    {
        var apiKey = configuration["DEEPSEEK_API_KEY"]
            ?? configuration["DeepSeek:ApiKey"]
            ?? string.Empty;

        var baseUrl = configuration["DeepSeek:BaseUrl"] ?? "https://api.deepseek.com";
        var model = configuration["DeepSeek:Model"] ?? "deepseek-v4-flash";
        var maxSteps = configuration.GetValue("Agent:MaxSteps", 6);
        maxSteps = Math.Clamp(maxSteps, 2, 12);

        return new AppSettings
        {
            DeepSeekApiKey = apiKey,
            DeepSeekBaseUrl = baseUrl,
            DeepSeekModel = model,
            AgentMaxSteps = maxSteps
        };
    }
}
