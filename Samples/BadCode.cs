using System;
using System.IO;
using System.Threading.Tasks;

namespace DemoProject;

public class BadCode
{
    /// <summary>
    /// 异步加载配置文件内容并输出到控制台。
    /// </summary>
    /// <param name="path">文件路径。</param>
    public async Task LoadConfig(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("路径不能为空。", nameof(path));

        var text = await File.ReadAllTextAsync(path);
        await Task.Delay(100);
        Console.WriteLine(text);
    }

    /// <summary>
    /// 异步读取文件的全部文本内容。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <returns>文件内容；如果文件不存在或读取失败则返回空字符串。</returns>
    public async Task<string> ReadName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("路径不能为空。", nameof(path));

        try
        {
            return await File.ReadAllTextAsync(path);
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"文件未找到: {ex.Message}");
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"读取文件失败: {ex.Message}");
        }

        return string.Empty;
    }

    /// <summary>
    /// 显示一条消息。
    /// </summary>
    public void ShowMessage()
    {
        Console.WriteLine("ShowMessage 被调用");
    }
}
