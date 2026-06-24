using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiniCursorAgent.LLM;
using MiniCursorAgent.Memory;
using MiniCursorAgent.Services;
using MiniCursorAgent.Tools;
using System.Windows;

namespace MiniCursorAgent;

public partial class App : Application
{
    private ServiceProvider? _provider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        services.AddSingleton(AppSettings.Load(configuration));
        services.AddSingleton<DeepSeekClient>();
        services.AddSingleton<AgentMemory>();
        services.AddSingleton<RagStore>();

        services.AddSingleton<IAgentTool, FileReadTool>();
        services.AddSingleton<IAgentTool, CodeReviewTool>();
        services.AddSingleton<IAgentTool, CodeMetricsTool>();
        services.AddSingleton<IAgentTool, ReplaceTextTool>();
        services.AddSingleton<IAgentTool, FileWriteTool>();
        services.AddSingleton<IAgentTool, BuildTool>();
        services.AddSingleton<IAgentTool, DelegateSubAgentTool>();

        services.AddSingleton<McpServer>();
        services.AddSingleton<MainWindow>();

        _provider = services.BuildServiceProvider();

        var mcp = _provider.GetRequiredService<McpServer>();
        mcp.Start();

        _provider.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _provider?.GetService<McpServer>()?.Dispose();
        _provider?.Dispose();
        base.OnExit(e);
    }
}
