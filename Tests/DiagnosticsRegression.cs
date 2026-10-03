using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;

internal static class DiagnosticsRegression
{
    private static int assertionCount;

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1)
            {
                throw new ArgumentException("请指定本次测试专用的临时目录。");
            }

            Directory.CreateDirectory(args[0]);
            FileConverterExtension.PathHelpers.UserDataFolder = args[0];
            SharedDiagnosticsData(args[0]);
            ConcurrentLoggingAndRelease(args[0]);
            Console.WriteLine($"通过 {assertionCount} 项诊断断言。");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"诊断回归失败：{exception}");
            return 1;
        }
    }

    private static void SharedDiagnosticsData(string root)
    {
        const int workers = 4;
        const int messagesPerWorker = 1000;
        var data = new FileConverter.Diagnostics.DiagnosticsData("并发日志");
        string folder = Path.Combine(root, "shared");
        Directory.CreateDirectory(folder);
        data.Initialize(folder, 123);
        var failures = new ConcurrentQueue<Exception>();
        int stopReading = 0;
        Thread reader = StartThread("shared-reader", failures, () =>
        {
            while (Volatile.Read(ref stopReading) == 0)
            {
                string content = data.Content;
                if (content.Length == 0 || !content.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("并发读取必须返回完整日志快照。");
                }

                Thread.Yield();
            }
        });
        Thread[] threads = new Thread[workers];
        try
        {
            for (int index = 0; index < workers; index++)
            {
                int workerIndex = index;
                threads[index] = StartThread($"shared-{index}", failures, () =>
                {
                    for (int messageIndex = 0; messageIndex < messagesPerWorker; messageIndex++)
                    {
                        data.Log($"parallel-{workerIndex}-{messageIndex}");
                    }
                });
            }

            foreach (Thread thread in threads)
            {
                RequireJoin(thread);
            }
        }
        finally
        {
            Volatile.Write(ref stopReading, 1);
            RequireJoin(reader);
            data.Release();
            data.Release();
        }

        Check(failures.IsEmpty, "同一日志对象的并发写入和读取不得抛异常。");
        Check(data.LogMessages.Count == workers * messagesPerWorker + 1, "内存日志不得丢失并发写入。");
        Check(object.ReferenceEquals(data.Content, data.Content), "未变化的 Content 必须复用缓存。");
        string[] persisted = File.ReadAllLines(Path.Combine(folder, "Diagnostics123.log"));
        Check(persisted.Count(line => line.StartsWith("parallel-", StringComparison.Ordinal)) == workers * messagesPerWorker,
            "释放时必须把所有日志刷新到磁盘。");
        int beforeLateLog = data.LogMessages.Count;
        data.Log("释放后追加内存诊断");
        Check(data.LogMessages.Count == beforeLateLog + 1, "重复释放后追加诊断不得使用已关闭的流。");
    }

    private static void ConcurrentLoggingAndRelease(string root)
    {
        const int workerCount = 6;
        var failures = new ConcurrentQueue<Exception>();
        int addedNotifications = 0;
        EventHandler<PropertyChangedEventArgs> handler = (sender, args) =>
        {
            // 静态事件内读取集合，验证新增回调不会持有集合写锁。
            foreach (var data in FileConverter.Diagnostics.Debug.Data)
            {
                string ignored = data.Content;
            }

            Interlocked.Increment(ref addedNotifications);
        };
        FileConverter.Diagnostics.Debug.StaticPropertyChanged += handler;
        FileConverter.Diagnostics.Debug.Log("开始并发诊断回归");
        Check(FileConverter.Diagnostics.Debug.Data.Length == 1, "主线程必须只创建一个诊断对象。");

        Thread[] errorThreads = new Thread[workerCount];
        using (var startErrors = new ManualResetEventSlim())
        {
            for (int index = 0; index < workerCount; index++)
            {
                int errorCode = index + 1;
                errorThreads[index] = StartThread($"error-{index}", failures, () =>
                {
                    RequireSignal(startErrors);
                    FileConverter.Diagnostics.Debug.LogError(errorCode, "并发错误码");
                });
            }

            startErrors.Set();
            foreach (Thread thread in errorThreads)
            {
                RequireJoin(thread);
            }
        }

        int firstErrorCode = FileConverter.Diagnostics.Debug.FirstErrorCode;
        Check(firstErrorCode >= 1 && firstErrorCode <= workerCount, "必须原子保存并发产生的首个错误码。");
        FileConverter.Diagnostics.Debug.LogError(255, "后续错误码");
        Check(FileConverter.Diagnostics.Debug.FirstErrorCode == firstErrorCode, "后续错误不得覆盖首个错误码。");

        for (int index = 0; index < 1000; index++)
        {
            int shortThreadIndex = index;
            Thread shortThread = StartThread($"short-{index}", failures, () =>
                FileConverter.Diagnostics.Debug.Log($"short-thread-{shortThreadIndex}"));
            RequireJoin(shortThread);
        }

        Check(failures.IsEmpty, "连续创建短生命周期线程时必须正确处理线程编号重用。");

        int stopReading = 0;
        Thread reader = StartThread("debug-reader", failures, () =>
        {
            while (Volatile.Read(ref stopReading) == 0)
            {
                foreach (var data in FileConverter.Diagnostics.Debug.Data)
                {
                    string ignored = data.Content;
                }

                Thread.Yield();
            }
        });

        Thread[] writers = new Thread[workerCount];
        using (var minimumLogsWritten = new CountdownEvent(workerCount))
        using (var releaseWriters = new ManualResetEventSlim())
        {
            for (int index = 0; index < workerCount; index++)
            {
                int workerIndex = index;
                writers[index] = StartThread($"debug-{index}", failures, () =>
                {
                    for (int messageIndex = 0; messageIndex < 100; messageIndex++)
                    {
                        FileConverter.Diagnostics.Debug.Log($"debug-{workerIndex}-{messageIndex}");
                    }

                    minimumLogsWritten.Signal();
                    RequireSignal(releaseWriters);
                    for (int messageIndex = 100; messageIndex < 5000; messageIndex++)
                    {
                        FileConverter.Diagnostics.Debug.Log($"debug-{workerIndex}-{messageIndex}");
                    }
                });
            }

            Check(minimumLogsWritten.Wait(5000), "日志线程启动超时。");
            Thread firstRelease = StartThread("release-first", failures, FileConverter.Diagnostics.Debug.Release);
            Thread secondRelease = StartThread("release-second", failures, FileConverter.Diagnostics.Debug.Release);
            releaseWriters.Set();
            foreach (Thread writer in writers)
            {
                RequireJoin(writer);
            }

            RequireJoin(firstRelease);
            RequireJoin(secondRelease);
        }

        Volatile.Write(ref stopReading, 1);
        RequireJoin(reader);
        FileConverter.Diagnostics.Debug.StaticPropertyChanged -= handler;
        FileConverter.Diagnostics.Debug.Release();
        FileConverter.Diagnostics.Debug.Log("释放后不得重新打开日志");
        Check(failures.IsEmpty, "Log、Data 和 Release 并发时不得抛异常。");
        Check(addedNotifications >= workerCount + 1, "新线程必须发出诊断集合变化通知。");
        Check(FileConverter.Diagnostics.Debug.Data.Length == 0, "释放后诊断集合必须保持为空。");

        string[] files = Directory.GetFiles(root, "*.log", SearchOption.AllDirectories);
        string allLogs = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        var persistedLines = new HashSet<string>(allLogs.Split(new[] { Environment.NewLine }, StringSplitOptions.None), StringComparer.Ordinal);
        for (int index = 0; index < 1000; index++)
        {
            Check(persistedLines.Contains($"short-thread-{index}"), "短生命周期线程的日志不得丢失。");
        }

        for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
        {
            for (int messageIndex = 0; messageIndex < 100; messageIndex++)
            {
                string marker = $"debug-{workerIndex}-{messageIndex}";
                Check(persistedLines.Contains(marker),
                    $"释放前日志 {marker} 必须持久保存。");
            }
        }

        foreach (string file in files)
        {
            // 排他打开成功说明写入流和后台刷新已经释放。
            using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None))
            {
            }
        }

        Check(true, "释放后必须关闭所有日志文件句柄。");
    }

    private static Thread StartThread(string name, ConcurrentQueue<Exception> failures, Action action)
    {
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        }) { Name = name, IsBackground = true };
        thread.Start();
        return thread;
    }

    private static void RequireJoin(Thread thread)
    {
        if (!thread.Join(10000))
        {
            throw new TimeoutException($"线程 {thread.Name} 未按时结束，可能存在死锁。");
        }
    }

    private static void RequireSignal(ManualResetEventSlim signal)
    {
        if (!signal.Wait(5000))
        {
            throw new TimeoutException("等待测试同步信号超时。");
        }
    }

    private static void Check(bool condition, string message)
    {
        assertionCount++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

// 路径和对话框仅作为外部边界替身，实际 Debug 与 DiagnosticsData 源码直接参与编译。
namespace FileConverterExtension
{
    public static class PathHelpers
    {
        public static string UserDataFolder;
        public static string GetUserDataFolderPath => UserDataFolder;
    }
}

namespace FileConverter
{
    public static class PathHelpers
    {
        public static string GenerateUniquePath(string path)
        {
            int index = 1;
            string candidate = path;
            while (File.Exists(candidate) || Directory.Exists(candidate))
            {
                candidate = path + "." + index++;
            }

            return candidate;
        }
    }
}

namespace FileConverter.Annotations
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class NotifyPropertyChangedInvocatorAttribute : Attribute { }
}

namespace System.Windows
{
    public enum MessageBoxButton { OK }
    public enum MessageBoxImage { Error }

    public static class MessageBox
    {
        public static void Show(string message, string title, MessageBoxButton button, MessageBoxImage image) { }
    }
}
