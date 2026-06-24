namespace MiniCursorAgent.Memory;

public static class KnowledgeBase
{
    public record Entry(string Id, string Title, string Content);

    public static readonly IReadOnlyList<Entry> Entries = new Entry[]
    {
        new("null-001", "空引用异常防范 NullReferenceException",
            """
            【问题】访问 null 对象的成员会抛 NullReferenceException。
            【规范】
            - 用 null 条件运算符 ?. 安全访问：var name = user?.Name ?? "Unknown";
            - 启用 #nullable enable，让编译器静态检测可空性
            - 方法入口验证参数：ArgumentNullException.ThrowIfNull(param);
            - 避免返回 null，改用空集合（Array.Empty<T>()）或 Optional 模式
            【示例错误】string s = null; int len = s.Length; // NullReferenceException
            【示例正确】int len = s?.Length ?? 0;
            """),

        new("dispose-001", "IDisposable 与资源释放",
            """
            【问题】未释放 IDisposable 对象（文件句柄、数据库连接、HttpClient）会导致资源泄漏。
            【规范】
            - 所有 IDisposable 对象用 using 包裹，保证自动释放
            - C# 8+ using 声明：using var stream = new FileStream(path, FileMode.Open);
            - 不要裸 new SqlConnection() 而不关闭，会耗尽连接池
            - 自己实现 IDisposable 时遵循 Dispose(bool) 标准模式
            【示例错误】var r = new StreamReader(f); var s = r.ReadToEnd(); // 未释放
            【示例正确】using var r = new StreamReader(f); var s = r.ReadToEnd();
            """),

        new("async-001", "async/await 死锁与常见陷阱",
            """
            【问题】在同步上下文中对异步方法调用 .Result 或 .Wait() 会死锁。
            【规范】
            - 禁止 asyncMethod().Result / asyncMethod().Wait()，始终用 await
            - async void 只用于事件处理程序，其他情况用 async Task / async Task<T>
            - 库代码中用 ConfigureAwait(false) 避免捕获 SynchronizationContext
            - 避免 async 方法内部 Task.Run 包裹同步代码（除非确实是 CPU 密集）
            【示例错误】var data = GetDataAsync().Result; // 可能死锁
            【示例正确】var data = await GetDataAsync();
            """),

        new("exception-001", "异常处理反模式",
            """
            【问题】错误的异常处理会吞掉错误信息，或破坏堆栈跟踪。
            【规范】
            - 禁止空 catch 块 catch { } 或 catch(Exception) { }（吞掉所有异常）
            - 重新抛出异常用 throw; 而非 throw ex;（throw ex 会丢失原始堆栈）
            - catch 具体类型，而非总是捕获 Exception
            - 不要用异常控制正常业务流程（性能差，语义不清）
            【示例错误】try { ... } catch { } // 吞掉异常，问题永远查不到
            【示例错误】catch(Exception ex) { throw ex; } // 丢失堆栈
            【示例正确】catch(Exception) { throw; } // 保留原始堆栈
            """),

        new("string-001", "字符串比较与处理",
            """
            【问题】不指定比较规则可能导致平台相关的行为或性能问题。
            【规范】
            - 比较字符串用 string.Equals(a, b, StringComparison.Ordinal) 而非 ==（对文化敏感场景）
            - 路径/文件名比较用 OrdinalIgnoreCase
            - 大量拼接字符串用 StringBuilder，不要在循环里 s += "..."
            - 用 string.IsNullOrWhiteSpace() 而非 s == null || s == "" || s.Trim() == ""
            【示例错误】if (filePath == otherPath) // 大小写敏感，跨平台行为不一致
            【示例正确】if (string.Equals(filePath, otherPath, StringComparison.OrdinalIgnoreCase))
            """),

        new("linq-001", "LINQ 延迟执行陷阱",
            """
            【问题】IEnumerable<T> 是延迟执行的，多次枚举会多次执行查询，甚至引发异常。
            【规范】
            - 需要多次使用查询结果时，调用 .ToList() 或 .ToArray() 物化
            - 避免在 LINQ 查询中有副作用（如修改外部变量）
            - 检查集合非空用 .Any() 而非 .Count() > 0（Count 会遍历全部）
            - 大数据集使用 .Where() 过滤后再 .Select() 投影，减少中间对象
            【示例错误】var q = list.Where(x => x.Valid); if(q.Any()) DoA(q.First()); // 枚举两次
            【示例正确】var q = list.Where(x => x.Valid).ToList(); if(q.Any()) DoA(q[0]);
            """),

        new("naming-001", "C# 命名规范",
            """
            【规范】
            - 类、方法、属性、public 字段：PascalCase（UserService, GetName）
            - 私有/保护字段：_camelCase 下划线前缀（_userName, _logger）
            - 局部变量、参数：camelCase（userName, maxCount）
            - 接口：I 前缀 + PascalCase（IDisposable, IUserRepository）
            - 常量：PascalCase（MaxRetryCount）或 UPPER_SNAKE_CASE
            - 泛型参数：T 开头（TKey, TValue）
            - 异步方法：Async 后缀（GetUserAsync, SaveAsync）
            【示例错误】private string UserName; public int get_count() {}
            【示例正确】private string _userName; public int GetCount() {}
            """),

        new("secret-001", "硬编码敏感信息安全风险",
            """
            【问题】密码、API Key、数据库连接字符串硬编码在源码中，一旦代码泄露后果严重。
            【规范】
            - 禁止 const string ApiKey = "sk-xxxx"; 这类写法
            - 使用环境变量：Environment.GetEnvironmentVariable("API_KEY")
            - 使用配置文件（appsettings.json）并加入 .gitignore
            - 生产环境使用 Azure Key Vault、AWS Secrets Manager 等密钥管理服务
            【示例错误】var client = new HttpClient(); client.DefaultHeaders.Add("key", "sk-abc123");
            【示例正确】var key = config["DeepSeek:ApiKey"] ?? Environment.GetEnvironmentVariable("API_KEY");
            """),

        new("thread-001", "线程安全与并发问题",
            """
            【问题】多线程访问共享状态而不同步，会导致数据竞争和不可预测行为。
            【规范】
            - 共享可变状态必须用 lock、Interlocked 或并发集合保护
            - 锁对象用私有 readonly object _lock = new();，不要 lock(this)
            - 优先用 ConcurrentDictionary、ConcurrentQueue 代替手动加锁集合
            - volatile 只保证可见性，不保证原子性，复合操作仍需 lock
            - 无状态或不可变对象天然线程安全
            【示例错误】private int _count; void Inc() { _count++; } // 多线程不安全
            【示例正确】private int _count; void Inc() { Interlocked.Increment(ref _count); }
            """),

        new("event-001", "事件订阅内存泄漏",
            """
            【问题】订阅事件后未取消订阅，publisher 持有 subscriber 引用，subscriber 无法被 GC。
            【规范】
            - 订阅事件的类应实现 IDisposable，在 Dispose 中取消订阅
            - 短生命周期对象订阅长生命周期对象的事件时特别危险
            - 考虑使用弱事件模式（WeakEventManager）或 IObservable
            【示例错误】class View { View(Model m) { m.Changed += OnChanged; } } // 从不取消
            【示例正确】class View : IDisposable {
              View(Model m) { _m = m; m.Changed += OnChanged; }
              public void Dispose() { _m.Changed -= OnChanged; } }
            """),

        new("collection-001", "集合遍历与修改",
            """
            【问题】在 foreach 中修改正在遍历的集合会抛 InvalidOperationException。
            【规范】
            - 需要删除元素时，先收集要删除的项，再统一删除
            - 或用 for 倒序遍历删除：for(int i=list.Count-1; i>=0; i--)
            - 用 .Where().ToList() 过滤后替换原集合
            - 字典遍历同样不允许在 foreach 中修改
            【示例错误】foreach(var x in list) { if(x.Bad) list.Remove(x); } // 抛异常
            【示例正确】list.RemoveAll(x => x.Bad); // 或 list = list.Where(x => !x.Bad).ToList();
            """),

        new("cast-001", "类型转换安全",
            """
            【问题】直接强制类型转换失败时抛 InvalidCastException，难以调试。
            【规范】
            - 用 is 模式匹配替代 as 再判空：if(obj is MyClass c) { c.Do(); }
            - as 转换失败返回 null，直接强转失败抛异常，按需选择
            - 数值类型转换可能溢出：用 checked { } 块或 Math.Clamp/Convert.To*
            - 避免 object -> int 的频繁装箱拆箱影响性能
            【示例错误】var c = (MyClass)obj; c.Do(); // obj不是MyClass时崩溃
            【示例正确】if(obj is MyClass c) { c.Do(); } else { /* 处理不匹配 */ }
            """),

        new("solid-001", "单一职责与方法复杂度",
            """
            【问题】方法过长、嵌套过深会导致代码难以理解和测试。
            【规范】
            - 单个方法超过 30 行通常需要拆分（提取私有方法）
            - 嵌套层数超过 3 层是重构信号，用卫语句（guard clause）提前 return
            - 一个类只负责一件事（单一职责原则）
            - 圈复杂度（if/else/for/while/switch 数量）过高时拆分逻辑
            【示例错误】public void Process() { if(a){ if(b){ if(c){ ... } } } }
            【示例正确】public void Process() {
              if(!a) return; if(!b) return; if(!c) return; DoCore(); }
            """),

        new("magic-001", "魔法数字与魔法字符串",
            """
            【问题】代码中直接出现无含义的数字或字符串，维护时难以理解其意图。
            【规范】
            - 用命名常量 const 或 static readonly 替代魔法数字
            - 相关常量用枚举（enum）或静态类组织
            - 配置值（超时时间、重试次数等）放到配置文件
            【示例错误】if(status == 3) Retry(5); Thread.Sleep(30000);
            【示例正确】const int StatusPending = 3; const int MaxRetry = 5;
                        const int RetryDelayMs = 30_000;
                        if(status == StatusPending) Retry(MaxRetry);
            """),

        new("logging-001", "日志记录规范",
            """
            【规范】
            - 使用结构化日志（ILogger<T>），不要用 Console.WriteLine 或 Debug.Print
            - 日志级别：Trace/Debug（调试信息）、Info（正常事件）、Warn（潜在问题）、Error（错误）
            - 日志消息用模板而非字符串拼接：_logger.LogError("用户 {UserId} 登录失败", userId)
            - 不要把敏感信息（密码、Token）写入日志
            - 异常日志传入 Exception 对象：_logger.LogError(ex, "操作失败")
            【示例错误】Console.WriteLine("Error: " + ex.Message);
            【示例正确】_logger.LogError(ex, "操作 {Operation} 失败，用户 {UserId}", opName, userId);
            """),
    };
}
