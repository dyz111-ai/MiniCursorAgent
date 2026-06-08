using Microsoft.Win32;
using MiniCursorAgent.Agents;
using MiniCursorAgent.LLM;
using MiniCursorAgent.Memory;
using MiniCursorAgent.Models;
using MiniCursorAgent.Services;
using MiniCursorAgent.Tools;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MiniCursorAgent;

public partial class MainWindow : Window
{
    private readonly AgentMemory _memory = new();
    private readonly ReActAgent _agent;
    private string? _currentFilePath;
    private bool _isRunning;

    public MainWindow()
    {
        InitializeComponent();

        var settings = AppSettings.Load();
        var client = new DeepSeekClient(settings);
        var tools = new IAgentTool[]
        {
            new FileReadTool(),
            new CodeReviewTool(),
            new CodeMetricsTool(),
            new ReplaceTextTool(),
            new FileWriteTool(),
            new BuildTool()
        };

        _agent = new ReActAgent(client, tools, _memory, settings.AgentMaxSteps, AppendAgentLog);

        AppendAgentLog("Mini Cursor Agent 已启动。\n", AgentLogType.System);
        AppendAgentLog("使用前请先打开一个 .cs 文件，然后在右侧输入任务。\n", AgentLogType.System);

        if (string.IsNullOrWhiteSpace(settings.DeepSeekApiKey))
        {
            AppendAgentLog("⚠️ 提示：未检测到 DeepSeek API Key。请设置环境变量 DEEPSEEK_API_KEY，或在 appsettings.json 中填写 DeepSeek:ApiKey。\n", AgentLogType.Error);
        }
    }

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择一个 C# 文件",
            Filter = "C# 文件 (*.cs)|*.cs|所有文件 (*.*)|*.*",
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
            _currentFilePath = filePath;
            CodeEditor.Text = code;
            CurrentFileTextBlock.Text = filePath;
            SaveButton.IsEnabled = true;
            ReloadButton.IsEnabled = true;

            _memory.CurrentFilePath = filePath;
            _memory.CurrentCode = code;
            _memory.LastWritePath = null;

            AppendAgentLog($"📂 已打开文件：{filePath}\n", AgentLogType.System);
        }
        catch (Exception ex)
        {
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
            await File.WriteAllTextAsync(_currentFilePath, CodeEditor.Text, Encoding.UTF8);
            _memory.CurrentFilePath = _currentFilePath;
            _memory.CurrentCode = CodeEditor.Text;
            AppendAgentLog($"💾 已保存文件：{_currentFilePath}\n", AgentLogType.System);
        }
        catch (Exception ex)
        {
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
            MessageBox.Show("请先打开一个 C# 文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

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

            AppendAgentLog("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n", AgentLogType.Divider);
            AppendAgentLog($"👤 User: {userGoal}\n", AgentLogType.User);
            AppendAgentLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n", AgentLogType.Divider);

            var finalAnswer = await _agent.RunAsync(userGoal);
            AppendAgentLog("\n", AgentLogType.Info);
            AppendAgentLog("✅ Final Answer:\n", AgentLogType.FinalAnswer);
            AppendAgentLog(finalAnswer + "\n", AgentLogType.FinalAnswer);

            if (!string.IsNullOrWhiteSpace(_memory.LastWritePath) &&
                string.Equals(_memory.LastWritePath, _currentFilePath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(_currentFilePath))
            {
                var updated = await File.ReadAllTextAsync(_currentFilePath, Encoding.UTF8);
                CodeEditor.Text = updated;
                _memory.CurrentCode = updated;
                AppendAgentLog("🔄 编辑器内容已根据 Agent 写入结果刷新。\n", AgentLogType.System);
            }
        }
        catch (Exception ex)
        {
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

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        AgentLogTextBox.Document.Blocks.Clear();
    }

    private void AppendAgentLog(string message, AgentLogType type = AgentLogType.Info)
    {
        Dispatcher.Invoke(() =>
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 1, 0, 2) };
            var (foreground, fontWeight, fontSize) = GetLogStyle(type);

            var run = new Run(message)
            {
                Foreground = foreground,
                FontWeight = fontWeight,
                FontSize = fontSize
            };
            paragraph.Inlines.Add(run);

            AgentLogTextBox.Document.Blocks.Add(paragraph);
            AgentLogTextBox.CaretPosition = paragraph.ContentEnd;
        });

        _ = AppLogger.WriteAsync(message);
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
            _ => (new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), FontWeights.Normal, 13),
        };
    }
}
