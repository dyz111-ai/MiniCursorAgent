using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiniCursorAgent.LLM;
using MiniCursorAgent.Memory;
using MiniCursorAgent.Tools;
using System.Windows;

namespace MiniCursorAgent;

public partial class App : Application
{
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

        services.AddSingleton<IAgentTool, FileReadTool>();
        services.AddSingleton<IAgentTool, CodeReviewTool>();
        services.AddSingleton<IAgentTool, CodeMetricsTool>();
        services.AddSingleton<IAgentTool, ReplaceTextTool>();
        services.AddSingleton<IAgentTool, FileWriteTool>();
        services.AddSingleton<IAgentTool, BuildTool>();

        services.AddSingleton<MainWindow>();

        var provider = services.BuildServiceProvider();
        var mainWindow = provider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
}
