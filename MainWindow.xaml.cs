using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.Extensions.Logging;
using MiniCursorAgent.Agents;
using MiniCursorAgent.LLM;
using MiniCursorAgent.Memory;
using MiniCursorAgent.Models;
using MiniCursorAgent.Services;
using MiniCursorAgent.Tools;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace MiniCursorAgent;

public partial class MainWindow : Window
{
    private readonly AgentMemory _memory;
    private readonly RagStore _ragStore;
    private readonly ReActAgent _agent;
    private readonly EditorDiffHighlighter _diffHighlighter;
    private readonly ILogger<MainWindow> _logger;
    private string? _currentFilePath;
    private string? _diffActualContent;
    private bool _isRunning;

    // MCP console
    private readonly HttpClient _mcpHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly string McpUrl = $"http://localhost:{McpServer.Port}/mcp/";
    private int _mcpRequestId;
    private readonly List<McpToolEntry> _mcpTools = [];

    // Streaming: tokens are buffered here from the background HTTP-read thread,
    // then flushed to the UI in batches every 40ms by _streamTimer.
    private readonly StringBuilder _streamBuffer = new();
    private readonly object _streamLock = new();
    private DispatcherTimer? _streamTimer;
    private Run? _streamRun;
    private volatile bool _streamTimerStarted;

    public MainWindow(
        AppSettings settings,
        DeepSeekClient client,
        IEnumerable<IAgentTool> tools,
        AgentMemory memory,
        RagStore ragStore,
        ILogger<MainWindow> logger)
    {
        InitializeComponent();

        _memory = memory;
        _ragStore = ragStore;
        _logger = logger;
        _diffHighlighter = new EditorDiffHighlighter(CodeEditor);
        _agent = new ReActAgent(client, tools, memory, settings.AgentMaxSteps, AppendAgentLog, ragStore);

        _streamTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _streamTimer.Tick += (_, _) => FlushStreamBuffer();

        AppendAgentLog("Mini Cursor Agent 已启动。\n", AgentLogType.System);
        AppendAgentLog("使用前请先打开一个代码/文本文件，然后在右侧输入任务。\n", AgentLogType.System);

        if (string.IsNullOrWhiteSpace(settings.DeepSeekApiKey))
        {
            AppendAgentLog("⚠️ 提示：未检测到 DeepSeek API Key。请设置环境变量 DEEPSEEK_API_KEY，或在 appsettings.json 中填写 DeepSeek:ApiKey。\n", AgentLogType.Error);
        }
    }

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择一个代码或文本文件",
            Filter = CodeFileHelper.OpenFileDialogFilter,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await LoadFileAsync(dialog.FileName);
    }

    private async Task LoadFileAsync(string filePath)
    {
        try
        {
            var code = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            ClearDiffPreview();
            _currentFilePath = filePath;
            CodeEditor.Text = code;
            ApplySyntaxHighlighting(filePath);
            CurrentFileTextBlock.Text = filePath;
            SaveButton.IsEnabled = true;
            ReloadButton.IsEnabled = true;

            _memory.CurrentFilePath = filePath;
            _memory.CurrentCode = code;
            _memory.LastWritePath = null;

            _logger.LogInformation("已打开文件：{FilePath}", filePath);
            AppendAgentLog($"📂 已打开文件：{filePath}\n", AgentLogType.System);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开文件失败：{FilePath}", filePath);
            MessageBox.Show($"打开文件失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        await SaveCurrentFileAsync();
    }

    private async Task SaveCurrentFileAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        try
        {
            var content = _diffHighlighter.IsActive && _diffActualContent is not null
                ? _diffActualContent
                : CodeEditor.Text;

            await File.WriteAllTextAsync(_currentFilePath, content, Encoding.UTF8);
            _memory.CurrentFilePath = _currentFilePath;
            _memory.CurrentCode = content;

            if (_diffHighlighter.IsActive)
            {
                ClearDiffPreview();
                CodeEditor.Text = content;
            }

            _logger.LogInformation("已保存文件：{FilePath}", _currentFilePath);
            AppendAgentLog($"💾 已保存文件：{_currentFilePath}\n", AgentLogType.System);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存文件失败：{FilePath}", _currentFilePath);
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_currentFilePath))
        {
            await LoadFileAsync(_currentFilePath);
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            return;
        }

        var userGoal = UserInputTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(userGoal))
        {
            MessageBox.Show("请先输入任务。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            MessageBox.Show("请先打开一个文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        UserInputTextBox.Clear();

        _isRunning = true;
        SendButton.IsEnabled = false;
        OpenButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        ReloadButton.IsEnabled = false;

        try
        {
            _memory.CurrentFilePath = _currentFilePath;
            _memory.CurrentCode = CodeEditor.Text;
            _memory.AllowFileWrite = AutoWriteCheckBox.IsChecked == true;
            var beforeText = CodeEditor.Text;

            AppendAgentLog("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n", AgentLogType.Divider);
            AppendAgentLog($"👤 User: {userGoal}\n", AgentLogType.User);
            AppendAgentLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n", AgentLogType.Divider);

            _logger.LogInformation("开始执行 Agent 任务：{Goal}", userGoal);
            var finalAnswer = await _agent.RunAsync(userGoal);
            _logger.LogInformation("Agent 任务完成");

            AppendAgentLog("\n", AgentLogType.Info);
            AppendAgentLog("✅ Final Answer:\n", AgentLogType.FinalAnswer);
            AppendFormattedFinalAnswer(finalAnswer);

            if (!string.IsNullOrWhiteSpace(_memory.LastWritePath) &&
                string.Equals(_memory.LastWritePath, _currentFilePath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(_currentFilePath))
            {
                var updated = await File.ReadAllTextAsync(_currentFilePath, Encoding.UTF8);
                ShowDiffPreview(beforeText, updated);
                AppendAgentLog("🔄 编辑器已进入 Diff 预览（绿色=新增，红色=删除）。\n", AgentLogType.System);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent 运行失败");
            AppendAgentLog($"❌ 运行失败：{ex.Message}\n", AgentLogType.Error);
            MessageBox.Show($"Agent 运行失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isRunning = false;
            SendButton.IsEnabled = true;
            OpenButton.IsEnabled = true;
            SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(_currentFilePath);
            ReloadButton.IsEnabled = !string.IsNullOrWhiteSpace(_currentFilePath);
        }
    }

    private void ApplySyntaxHighlighting(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        CodeEditor.SyntaxHighlighting = string.IsNullOrWhiteSpace(extension)
            ? null
            : HighlightingManager.Instance.GetDefinitionByExtension(extension);
    }

    private void ShowDiffPreview(string oldText, string newText)
    {
        var displayLines = CodeDiffService.BuildDisplayLines(oldText, newText);
        if (!CodeDiffService.HasChanges(displayLines))
        {
            _diffActualContent = newText;
            CodeEditor.Text = newText;
            _memory.CurrentCode = newText;
            ClearDiffPreview();
            return;
        }

        _diffActualContent = newText;
        _memory.CurrentCode = newText;

        CodeEditor.Text = string.Join(Environment.NewLine, displayLines.Select(line => line.Text));
        _diffHighlighter.Apply(displayLines.Select(line => line.Kind).ToList());
        DiffBanner.Visibility = Visibility.Visible;
    }

    private void ClearDiffPreview()
    {
        _diffActualContent = null;
        _diffHighlighter.Clear();
        DiffBanner.Visibility = Visibility.Collapsed;
    }

    private void ExitDiffButton_Click(object sender, RoutedEventArgs e)
    {
        if (_diffActualContent is not null)
        {
            CodeEditor.Text = _diffActualContent;
            _memory.CurrentCode = _diffActualContent;
        }

        ClearDiffPreview();
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        AgentLogTextBox.Document.Blocks.Clear();
    }

    private void AppendAgentLog(string message, AgentLogType type = AgentLogType.Info)
    {
        if (type == AgentLogType.Streaming)
        {
            // Buffer only — never block on or flood the UI thread.
            lock (_streamLock) { _streamBuffer.Append(message); }
            // Start the flush timer once per streaming session (not once per token).
            // Background priority keeps user Input events (priority 5) ahead of us (4).
            if (!_streamTimerStarted)
            {
                _streamTimerStarted = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () => _streamTimer?.Start());
            }
            return;
        }

        // Non-streaming: synchronous dispatch so ordered log entries stay ordered.
        Dispatcher.Invoke(() =>
        {
            // Stop buffering and push any remaining streaming tokens first.
            FinalizeStreamBlock();

            var paragraph = new Paragraph { Margin = new Thickness(0, 1, 0, 2) };
            var (foreground, fontWeight, fontSize) = GetLogStyle(type);

            var textRun = new Run(message)
            {
                Foreground = foreground,
                FontWeight = fontWeight,
                FontSize = fontSize
            };
            paragraph.Inlines.Add(textRun);
            AgentLogTextBox.Document.Blocks.Add(paragraph);
            Dispatcher.BeginInvoke(ScrollAgentLogToEnd, DispatcherPriority.Loaded);
        });

        _ = AppLogger.WriteAsync(message);
    }

    // Called on the UI thread by DispatcherTimer every 40 ms while streaming.
    private void FlushStreamBuffer()
    {
        string chunk;
        lock (_streamLock)
        {
            if (_streamBuffer.Length == 0) return;
            chunk = _streamBuffer.ToString();
            _streamBuffer.Clear();
        }

        if (_streamRun is null)
        {
            var para = new Paragraph { Margin = new Thickness(0, 1, 0, 2), Tag = "streaming" };
            _streamRun = new Run
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                FontSize = 11.5
            };
            para.Inlines.Add(_streamRun);
            AgentLogTextBox.Document.Blocks.Add(para);
        }

        _streamRun.Text += chunk;
        ScrollAgentLogToEnd();
    }

    // Called on the UI thread before appending any non-streaming log entry.
    private void FinalizeStreamBlock()
    {
        _streamTimerStarted = false;
        _streamTimer?.Stop();
        FlushStreamBuffer();
        _streamRun = null;
    }

    private void AppendFormattedFinalAnswer(string content)
    {
        Dispatcher.Invoke(() =>
        {
            var foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));

            foreach (var block in MarkdownLogRenderer.CreateBlocks(content, foreground))
            {
                AgentLogTextBox.Document.Blocks.Add(block);
            }

            Dispatcher.BeginInvoke(ScrollAgentLogToEnd, DispatcherPriority.Loaded);
        });

        _ = AppLogger.WriteAsync(content + "\n");
    }

    private void ScrollAgentLogToEnd()
    {
        AgentLogTextBox.CaretPosition = AgentLogTextBox.Document.ContentEnd;
        FindScrollViewer(AgentLogTextBox)?.ScrollToEnd();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer scrollViewer)
        {
            return scrollViewer;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var result = FindScrollViewer(VisualTreeHelper.GetChild(element, i));
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private static (Brush Foreground, FontWeight Weight, double FontSize) GetLogStyle(AgentLogType type)
    {
        return type switch
        {
            AgentLogType.System => (new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), FontWeights.Normal, 12.5),
            AgentLogType.User => (new SolidColorBrush(Color.FromRgb(0x1A, 0x73, 0xE8)), FontWeights.Bold, 14),
            AgentLogType.Step => (new SolidColorBrush(Color.FromRgb(0xD9, 0x7A, 0x00)), FontWeights.Bold, 13.5),
            AgentLogType.Thought => (new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)), FontWeights.Normal, 13),
            AgentLogType.Action => (new SolidColorBrush(Color.FromRgb(0x16, 0xA7, 0x65)), FontWeights.Bold, 13),
            AgentLogType.ActionInput => (new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)), FontWeights.Normal, 12),
            AgentLogType.Observation => (new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), FontWeights.Normal, 12.5),
            AgentLogType.FinalAnswer => (new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), FontWeights.Normal, 13),
            AgentLogType.Error => (new SolidColorBrush(Color.FromRgb(0xD9, 0x30, 0x25)), FontWeights.Bold, 13),
            AgentLogType.Divider => (new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)), FontWeights.Normal, 12),
            AgentLogType.Streaming => (new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)), FontWeights.Normal, 11.5),
            _ => (new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), FontWeights.Normal, 13),
        };
    }

    // ── MCP 控制台 ──────────────────────────────────────────────────────────

    private async void McpInit_Click(object sender, RoutedEventArgs e)
    {
        McpInitButton.IsEnabled = false;
        AppendMcpLog("► 发送 initialize...\n", McpLogColor.Label);

        var request = new
        {
            jsonrpc = "2.0",
            id = ++_mcpRequestId,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "MiniCursorAgent-UI", version = "1.0" }
            }
        };

        try
        {
            var (reqJson, respJson) = await PostMcpAsync(request);
            AppendMcpLog("→ Request:\n", McpLogColor.Label);
            AppendMcpLog(reqJson + "\n", McpLogColor.Request);
            AppendMcpLog("← Response:\n", McpLogColor.Label);
            AppendMcpLog(respJson + "\n", McpLogColor.Response);

            using var doc = JsonDocument.Parse(respJson);
            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("serverInfo", out var info))
            {
                var name = info.TryGetProperty("name", out var n) ? n.GetString() : "?";
                var ver = info.TryGetProperty("version", out var v) ? v.GetString() : "?";
                McpStatusText.Text = $"localhost:{McpServer.Port}  ✓ {name} v{ver}";
                McpStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA7, 0x65));
            }
        }
        catch (Exception ex)
        {
            AppendMcpLog("✗ " + ex.Message + "\n", McpLogColor.Error);
            McpStatusText.Text = "连接失败";
            McpStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0x30, 0x25));
        }
        finally
        {
            McpInitButton.IsEnabled = true;
        }
    }

    private async void McpListTools_Click(object sender, RoutedEventArgs e)
    {
        McpListToolsButton.IsEnabled = false;
        AppendMcpLog("► 发送 tools/list...\n", McpLogColor.Label);

        var request = new
        {
            jsonrpc = "2.0",
            id = ++_mcpRequestId,
            method = "tools/list",
            @params = new { }
        };

        try
        {
            var (reqJson, respJson) = await PostMcpAsync(request);
            AppendMcpLog("→ Request:\n", McpLogColor.Label);
            AppendMcpLog(reqJson + "\n", McpLogColor.Request);
            AppendMcpLog("← Response:\n", McpLogColor.Label);
            AppendMcpLog(respJson + "\n", McpLogColor.Response);

            using var doc = JsonDocument.Parse(respJson);
            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("tools", out var toolsArr) &&
                toolsArr.ValueKind == JsonValueKind.Array)
            {
                _mcpTools.Clear();
                foreach (var t in toolsArr.EnumerateArray())
                {
                    var name = t.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var desc = t.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                    _mcpTools.Add(new McpToolEntry(name, desc));
                }

                McpToolComboBox.ItemsSource = null;
                McpToolComboBox.ItemsSource = _mcpTools;
                McpToolComboBox.DisplayMemberPath = "Display";
                if (_mcpTools.Count > 0) McpToolComboBox.SelectedIndex = 0;

                AppendMcpLog($"✓ 已加载 {_mcpTools.Count} 个工具\n", McpLogColor.Label);
            }
        }
        catch (Exception ex)
        {
            AppendMcpLog("✗ " + ex.Message + "\n", McpLogColor.Error);
        }
        finally
        {
            McpListToolsButton.IsEnabled = true;
        }
    }

    private void McpToolComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (McpToolComboBox.SelectedItem is not McpToolEntry entry) return;

        McpArgsTextBox.Text = entry.Name switch
        {
            // path 可选，不填则读当前文件
            "FileReadTool" => "{}",

            // path 可选，不填则写当前文件；content 必填
            "FileWriteTool" => """
                {
                  "path": "",
                  "content": ""
                }
                """.Trim(),

            // oldText/newText 必填；replaceAll 可选（默认 false）
            "ReplaceTextTool" => """
                {
                  "oldText": "",
                  "newText": "",
                  "replaceAll": false
                }
                """.Trim(),

            // path 可选，不填则从当前文件向上查找 .csproj
            "BuildTool" => "{}",

            "CodeReviewTool" or "CodeMetricsTool" => "{}",

            // roles 可选，不填则派发全部 4 个角色
            "DelegateSubAgent" => """
                {
                  "roles": ["security", "docs", "performance", "refactor"]
                }
                """.Trim(),

            _ => "{}"
        };
    }

    private async void McpSend_Click(object sender, RoutedEventArgs e)
    {
        if (McpToolComboBox.SelectedItem is not McpToolEntry entry)
        {
            AppendMcpLog("✗ 请先列出工具并选择一个\n", McpLogColor.Error);
            return;
        }

        JsonElement argsElement;
        try
        {
            using var argsDoc = JsonDocument.Parse(McpArgsTextBox.Text.Trim());
            argsElement = argsDoc.RootElement.Clone();
        }
        catch
        {
            AppendMcpLog("✗ 参数不是合法 JSON，请检查格式\n", McpLogColor.Error);
            return;
        }

        McpSendButton.IsEnabled = false;
        AppendMcpLog($"► 调用工具：{entry.Name}\n", McpLogColor.Label);

        var request = new
        {
            jsonrpc = "2.0",
            id = ++_mcpRequestId,
            method = "tools/call",
            @params = new
            {
                name = entry.Name,
                arguments = argsElement
            }
        };

        try
        {
            var (reqJson, respJson) = await PostMcpAsync(request);
            AppendMcpLog("→ Request:\n", McpLogColor.Label);
            AppendMcpLog(reqJson + "\n", McpLogColor.Request);
            AppendMcpLog("← Response:\n", McpLogColor.Label);
            AppendMcpLog(respJson + "\n\n", McpLogColor.Response);
        }
        catch (Exception ex)
        {
            AppendMcpLog("✗ " + ex.Message + "\n", McpLogColor.Error);
        }
        finally
        {
            McpSendButton.IsEnabled = true;
        }
    }

    private static readonly JsonSerializerOptions _jsonDisplay = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private async Task<(string ReqJson, string RespJson)> PostMcpAsync(object request)
    {
        var reqJson = JsonSerializer.Serialize(request, _jsonDisplay);
        var content = new StringContent(reqJson, Encoding.UTF8, "application/json");
        using var response = await _mcpHttpClient.PostAsync(McpUrl, content).ConfigureAwait(false);
        var rawResp = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        string prettyResp;
        try
        {
            using var doc = JsonDocument.Parse(rawResp);
            prettyResp = JsonSerializer.Serialize(doc, _jsonDisplay);
        }
        catch
        {
            prettyResp = rawResp;
        }

        return (reqJson, prettyResp);
    }

    private void AppendMcpLog(string text, McpLogColor color)
    {
        Dispatcher.Invoke(() =>
        {
            var brush = color switch
            {
                McpLogColor.Label => new SolidColorBrush(Color.FromRgb(0x9C, 0xDC, 0xFE)),
                McpLogColor.Request => new SolidColorBrush(Color.FromRgb(0xCE, 0x91, 0x78)),
                McpLogColor.Response => new SolidColorBrush(Color.FromRgb(0xB5, 0xCE, 0xA8)),
                McpLogColor.Error => new SolidColorBrush(Color.FromRgb(0xF4, 0x47, 0x47)),
                _ => new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4))
            };

            var para = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
            para.Inlines.Add(new Run(text) { Foreground = brush });
            McpLogTextBox.Document.Blocks.Add(para);
            McpLogTextBox.CaretPosition = McpLogTextBox.Document.ContentEnd;
            FindScrollViewer(McpLogTextBox)?.ScrollToEnd();
        });
    }

    private enum McpLogColor { Default, Label, Request, Response, Error }

    private sealed record McpToolEntry(string Name, string Description)
    {
        public string Display => $"{Name}  —  {Description}";
    }
}
