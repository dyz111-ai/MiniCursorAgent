using MiniCursorAgent.LLM;
using MiniCursorAgent.Memory;
using MiniCursorAgent.Models;
using MiniCursorAgent.Tools;
using System.Text.Json;

namespace MiniCursorAgent.Agents;

public sealed class SubAgent
{
    private readonly DeepSeekClient _llm;
    private readonly Dictionary<string, IAgentTool> _tools;
    private readonly AgentMemory _parentMemory;
    private readonly Action<string, AgentLogType> _log;
    private readonly int _maxSteps;

    public string Role { get; }
    public string DisplayName => Role switch
    {
        "security"    => "安全分析",
        "docs"        => "文档质量",
        "performance" => "性能分析",
        "refactor"    => "重构建议",
        _             => Role
    };

    public SubAgent(
        string role,
        DeepSeekClient llm,
        IEnumerable<IAgentTool> readOnlyTools,
        AgentMemory parentMemory,
        Action<string, AgentLogType> log,
        int maxSteps = 3)
    {
        Role = role;
        _llm = llm;
        _tools = readOnlyTools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        _parentMemory = parentMemory;
        _log = log;
        _maxSteps = maxSteps;
    }

    public async Task<string> RunAsync(string task, CancellationToken cancellationToken = default)
    {
        _log($"[{DisplayName} Agent] 启动...\n", AgentLogType.Step);

        var subMemory = new AgentMemory
        {
            CurrentFilePath = _parentMemory.CurrentFilePath,
            CurrentCode = _parentMemory.CurrentCode,
            AllowFileWrite = false
        };

        var messages = new List<ChatMessage>
        {
            new("system", BuildSystemPrompt()),
            new("user", $"任务：{task}\n\n请开始分析。每次只输出一个合法 JSON 对象。")
        };

        for (var step = 1; step <= _maxSteps; step++)
        {
            _log("🔄 ", AgentLogType.Streaming);
            var rawResponse = await _llm.ChatStreamingAsync(messages,
                token => _log(token, AgentLogType.Streaming),
                cancellationToken);
            _log("\n", AgentLogType.Streaming);

            AgentDecision decision;
            try { decision = AgentDecision.Parse(rawResponse); }
            catch
            {
                messages.Add(new ChatMessage("assistant", rawResponse));
                messages.Add(new ChatMessage("user", "回复格式不是合法 JSON，请只输出 JSON 对象。"));
                continue;
            }

            _log($"[{DisplayName}] 💭 {decision.Thought}\n", AgentLogType.Thought);
            _log($"[{DisplayName}] 🔧 {decision.Action}\n", AgentLogType.Action);

            if (decision.Action.Equals("FinalAnswer", StringComparison.OrdinalIgnoreCase))
            {
                var answer = decision.ActionInput.ValueKind == JsonValueKind.Object &&
                             decision.ActionInput.TryGetProperty("answer", out var ae)
                    ? ae.GetString() ?? string.Empty
                    : decision.ActionInput.ToString();
                _log($"[{DisplayName}] ✅ 分析完成。\n", AgentLogType.Step);
                return answer;
            }

            if (!_tools.TryGetValue(decision.Action, out var tool))
            {
                var obs = $"未知工具：{decision.Action}。可用：{string.Join(", ", _tools.Keys)}";
                messages.Add(new ChatMessage("assistant", rawResponse));
                messages.Add(new ChatMessage("user", "Observation: " + obs));
                continue;
            }

            var observation = await tool.ExecuteAsync(decision.ActionInput, subMemory, cancellationToken);
            var snippet = observation.Length > 400 ? observation[..400] + "..." : observation;
            _log($"[{DisplayName}] 📤 {snippet}\n", AgentLogType.Observation);

            messages.Add(new ChatMessage("assistant", rawResponse));
            messages.Add(new ChatMessage("user", $"Observation from {tool.Name}:\n{observation}"));
        }

        return $"（已达最大分析步数 {_maxSteps}，以上为部分结论）";
    }

    private string BuildSystemPrompt() => Role switch
    {
        "security" => """
你是专注于【代码安全】的专业分析子智能体。
目标：发现所有安全漏洞和安全隐患，给出具体行号和修复建议。

重点检查：
- 硬编码密码/API Key/Token/连接字符串
- SQL 注入（字符串拼接 SQL）
- 路径遍历（未校验用户输入的文件路径）
- XSS 跨站脚本（未转义的用户内容输出）
- 不安全的反序列化
- 敏感信息写入日志
- 缺少输入验证/长度校验
- 过于宽松的权限设置或缺少鉴权检查

工作方式（ReAct 格式，每次只输出一个 JSON）：
{"thought":"当前分析思路","action":"工具名或FinalAnswer","actionInput":{}}

可用工具：
- FileReadTool：读取当前文件代码。输入：{}
- CodeReviewTool：运行静态审查规则。输入：{}
- FinalAnswer：输出分析报告。输入：{"answer":"安全分析报告，含具体问题和修复建议"}
""",

        "docs" => """
你是专注于【文档与可读性】的专业分析子智能体。
目标：评估代码的文档完整度、命名质量和可读性，给出具体改进建议。

重点检查：
- 公共类/方法是否缺少注释（XML doc 或普通注释）
- 函数/变量命名是否清晰自解释（避免 a, b, tmp, data 等模糊名称）
- 是否有魔法数字/魔法字符串（应定义为命名常量）
- TODO / FIXME 是否遗留处理
- 复杂业务逻辑是否缺少说明注释
- 方法是否过长（超过 30 行需考虑拆分）
- 嵌套层次是否过深（超过 3 层需考虑重构）

工作方式（ReAct 格式，每次只输出一个 JSON）：
{"thought":"当前分析思路","action":"工具名或FinalAnswer","actionInput":{}}

可用工具：
- FileReadTool：读取当前文件代码。输入：{}
- CodeReviewTool：检测 TODO/FIXME/长行等问题。输入：{}
- CodeMetricsTool：获取代码行数、圈复杂度等指标。输入：{}
- FinalAnswer：输出分析报告。输入：{"answer":"文档质量报告，含具体改进建议"}
""",

        "performance" => """
你是专注于【性能与效率】的专业分析子智能体。
目标：找出代码中的性能瓶颈和低效写法，给出优化建议。

重点检查：
- 循环中的重复计算（应提取到循环外）
- LINQ 延迟执行导致的多次枚举（应 .ToList() 物化）
- 字符串在循环中用 += 拼接（应用 StringBuilder）
- 不必要的装箱拆箱（值类型频繁转为 object）
- 同步方法阻塞 async 上下文（.Result / .Wait()）
- 大对象频繁创建导致 GC 压力
- 数据库/文件 IO 未使用异步 API
- 集合检查用 .Count() > 0 而非 .Any()

工作方式（ReAct 格式，每次只输出一个 JSON）：
{"thought":"当前分析思路","action":"工具名或FinalAnswer","actionInput":{}}

可用工具：
- FileReadTool：读取当前文件代码。输入：{}
- CodeMetricsTool：获取圈复杂度和行数等量化指标。输入：{}
- FinalAnswer：输出分析报告。输入：{"answer":"性能分析报告，含瓶颈定位和优化建议"}
""",

        "refactor" => """
你是专注于【代码结构与重构】的专业分析子智能体。
目标：识别代码异味和设计问题，给出重构方向建议。

重点检查：
- 违反单一职责原则（一个类/方法做了太多事）
- 重复代码（相似逻辑出现多次，应提取公共方法）
- 过深的继承层次或不必要的继承
- 高耦合（类之间依赖过多，应依赖接口而非实现）
- 开关语句/if-else 链（可用多态或策略模式替代）
- 特性嫉妒（方法大量访问另一个类的数据，职责归属有问题）
- 过长的参数列表（超过 4 个参数考虑封装为对象）
- Dead code（永远不会执行的代码段）

工作方式（ReAct 格式，每次只输出一个 JSON）：
{"thought":"当前分析思路","action":"工具名或FinalAnswer","actionInput":{}}

可用工具：
- FileReadTool：读取当前文件代码。输入：{}
- CodeReviewTool：静态规则审查。输入：{}
- CodeMetricsTool：量化复杂度数据。输入：{}
- FinalAnswer：输出分析报告。输入：{"answer":"重构建议报告，含优先级排序的改进项"}
""",

        _ => """
你是通用代码分析子智能体。每次只输出 JSON：
{"thought":"...","action":"工具名或FinalAnswer","actionInput":{}}
可用工具：FileReadTool、CodeReviewTool、FinalAnswer。
"""
    };
}
