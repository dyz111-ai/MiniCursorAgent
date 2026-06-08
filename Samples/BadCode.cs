using System;
using System.IO;
using System.Threading.Tasks;

namespace DemoProject;

public class BadCode
{
    public async Task LoadConfig(string path)
    {
        var text = await File.ReadAllTextAsync(path);
        await Task.Delay(100);
        Console.WriteLine(text);
    }

    public async Task<string> ReadName(string path)
    {
        try
        {
            return await File.ReadAllTextAsync(path);
        }
        catch (IOException ex)
        {
            Console.WriteLine($"读取文件失败: {ex.Message}");
        }

        return "";
    }

    public void TodoMethod()
    {
        Console.WriteLine("TodoMethod 被调用");
    }
}
