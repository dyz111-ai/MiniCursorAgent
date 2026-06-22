using System.IO;

namespace MiniCursorAgent.Services;

public static class CodeFileHelper
{
    public const string OpenFileDialogFilter =
        "代码与文本文件|*.cs;*.py;*.js;*.ts;*.jsx;*.tsx;*.java;*.cpp;*.c;*.h;*.hpp;*.go;*.rs;*.rb;*.php;*.swift;*.kt;*.sql;*.json;*.xml;*.yaml;*.yml;*.md;*.txt|所有文件 (*.*)|*.*";

    public static string GetFileTypeLabel(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return "未知";
        }

        var extension = Path.GetExtension(filePath);
        return string.IsNullOrWhiteSpace(extension) ? "未知" : extension;
    }

    public static string GetCodeFenceTag(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        return Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant() switch
        {
            "cs" => "csharp",
            "js" => "javascript",
            "ts" => "typescript",
            "jsx" => "jsx",
            "tsx" => "tsx",
            "py" => "python",
            "rb" => "ruby",
            "go" => "go",
            "rs" => "rust",
            "java" => "java",
            "cpp" or "cc" or "cxx" => "cpp",
            "c" or "h" => "c",
            "hpp" => "cpp",
            "php" => "php",
            "swift" => "swift",
            "kt" => "kotlin",
            "sql" => "sql",
            "json" => "json",
            "xml" => "xml",
            "yaml" or "yml" => "yaml",
            "md" => "markdown",
            "sh" or "bash" => "bash",
            _ => Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant()
        };
    }
}
