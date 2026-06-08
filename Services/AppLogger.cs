using System.IO;
using System.Text;

namespace MiniCursorAgent.Services;

public static class AppLogger
{
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public static async Task WriteAsync(string message)
    {
        try
        {
            var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            var logFile = Path.Combine(logDirectory, $"agent-{DateTime.Now:yyyyMMdd}.log");

            await Lock.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:HH:mm:ss}] {message}", Encoding.UTF8);
            }
            finally
            {
                Lock.Release();
            }
        }
        catch
        {
            // 日志失败不应影响主流程。
        }
    }
}
