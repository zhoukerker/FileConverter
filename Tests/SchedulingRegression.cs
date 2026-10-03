using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;

internal static class SchedulingRegression
{
    private static int assertionCount;

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--benchmark")
            {
                Benchmark();
                return 0;
            }

            EmptyAndFailedQueues();
            WorkerLimitAndReuse();
            ExclusiveFlagsAndWakeup();
            CancellationWaitsForCleanup();
            DuplicateStartsAreIgnored();
            ClipboardFinishesBeforeTermination();
            ExecutionAndPreparationFailures();
            Console.WriteLine($"通过 {assertionCount} 项调度断言。");
            Benchmark();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"调度回归失败：{exception}");
            return 1;
        }
    }

    private static void EmptyAndFailedQueues()
    {
        var empty = NewService(4);
        Check(WaitForCompletion(empty).AllConversionsSucceed, "空队列必须正常结束。");

        var failed = NewService(4);
        var job = new FileConverter.ConversionJobs.ConversionJob("failed");
        job.PrepareAction = item => item.State = FileConverter.ConversionJobs.ConversionState.Failed;
        failed.RegisterConversionJob(job);
        Check(!WaitForCompletion(failed).AllConversionsSucceed, "准备失败不得报告成功。");
        Check(job.ExecutionCount == 0, "准备失败的任务不得执行。");
    }

    private static void WorkerLimitAndReuse()
    {
        const int limit = 4;
        var service = NewService(limit);
        var threadIds = new ConcurrentDictionary<int, byte>();
        int running = 0;
        int maximum = 0;
        var jobs = new List<FileConverter.ConversionJobs.ConversionJob>();
        for (int index = 0; index < 120; index++)
        {
            var job = new FileConverter.ConversionJobs.ConversionJob($"job-{index}");
            job.ConvertAction = item =>
            {
                threadIds.TryAdd(Thread.CurrentThread.ManagedThreadId, 0);
                int current = Interlocked.Increment(ref running);
                UpdateMaximum(ref maximum, current);
                Thread.Sleep(3);
                Interlocked.Decrement(ref running);
            };
            jobs.Add(job);
            service.RegisterConversionJob(job);
        }

        Check(WaitForCompletion(service).AllConversionsSucceed, "批量转换必须成功。");
        Check(maximum == limit, "必须实际使用配置的并发数且不得超限。");
        Check(threadIds.Count == limit, "工作线程必须复用。");
        Check(jobs.All(item => item.ExecutionCount == 1), "每个任务必须只执行一次。");
    }

    private static void ExclusiveFlagsAndWakeup()
    {
        var service = NewService(3);
        using (var firstStarted = new ManualResetEventSlim())
        using (var clearFlags = new ManualResetEventSlim())
        using (var allowFirstToFinish = new ManualResetEventSlim())
        using (var secondStarted = new ManualResetEventSlim())
        using (var completed = new ManualResetEventSlim())
        {
            var first = new FileConverter.ConversionJobs.ConversionJob("exclusive");
            first.PrepareAction = item => item.StateFlags = FileConverter.ConversionJobs.ConversionFlags.CdDriveExtraction;
            first.ConvertAction = item =>
            {
                firstStarted.Set();
                RequireSignal(clearFlags, "等待释放互斥标志超时。");
                item.StateFlags = FileConverter.ConversionJobs.ConversionFlags.None;
                RequireSignal(allowFirstToFinish, "等待首任务结束超时。");
            };
            var second = new FileConverter.ConversionJobs.ConversionJob("normal");
            second.ConvertAction = item => secondStarted.Set();
            service.RegisterConversionJob(first);
            service.RegisterConversionJob(second);
            service.ConversionJobsTerminated += (sender, args) => completed.Set();
            service.ConvertFilesAsync();

            try
            {
                RequireSignal(firstStarted, "互斥任务未开始。");
                Check(!secondStarted.Wait(100), "互斥标志释放前不得启动其他任务。");
                clearFlags.Set();
                RequireSignal(secondStarted, "标志变化后未唤醒等待线程。");
                Check(!completed.IsSet, "首任务仍运行时不得终止整个队列。");
            }
            finally
            {
                clearFlags.Set();
                allowFirstToFinish.Set();
            }

            RequireSignal(completed, "互斥队列未结束。");
        }

        var exclusiveService = NewService(8);
        int running = 0;
        int maximum = 0;
        for (int index = 0; index < 24; index++)
        {
            var job = new FileConverter.ConversionJobs.ConversionJob($"exclusive-{index}");
            job.PrepareAction = item => item.StateFlags = FileConverter.ConversionJobs.ConversionFlags.CdDriveExtraction;
            job.ConvertAction = item =>
            {
                int current = Interlocked.Increment(ref running);
                UpdateMaximum(ref maximum, current);
                Thread.Sleep(2);
                Interlocked.Decrement(ref running);
            };
            exclusiveService.RegisterConversionJob(job);
        }

        Check(WaitForCompletion(exclusiveService).AllConversionsSucceed, "互斥批量转换必须结束。");
        Check(maximum == 1, "任务预留阶段也必须保持光驱互斥。");
    }

    private static void CancellationWaitsForCleanup()
    {
        var service = NewService(1);
        using (var cleanupStarted = new ManualResetEventSlim())
        using (var finishCleanup = new ManualResetEventSlim())
        using (var completed = new ManualResetEventSlim())
        {
            bool succeeded = true;
            var job = new FileConverter.ConversionJobs.ConversionJob("cancelled");
            job.ConvertAction = item =>
            {
                item.State = FileConverter.ConversionJobs.ConversionState.Failed;
                cleanupStarted.Set();
                RequireSignal(finishCleanup, "等待取消清理超时。");
            };
            service.RegisterConversionJob(job);
            service.ConversionJobsTerminated += (sender, args) =>
            {
                succeeded = args.AllConversionsSucceed;
                completed.Set();
            };
            service.ConvertFilesAsync();
            try
            {
                RequireSignal(cleanupStarted, "取消清理未开始。");
                Check(!completed.Wait(100), "取消清理完成前不得发出终止事件。");
            }
            finally
            {
                finishCleanup.Set();
            }

            RequireSignal(completed, "取消队列未结束。");
            Check(!succeeded, "取消任务不得报告成功。");
        }
    }

    private static void DuplicateStartsAreIgnored()
    {
        var service = NewService(1);
        using (var started = new ManualResetEventSlim())
        using (var finish = new ManualResetEventSlim())
        using (var completed = new ManualResetEventSlim())
        {
            int terminationCount = 0;
            var job = new FileConverter.ConversionJobs.ConversionJob("single");
            job.ConvertAction = item =>
            {
                started.Set();
                RequireSignal(finish, "等待重复启动检查超时。");
            };
            service.RegisterConversionJob(job);
            service.ConversionJobsTerminated += (sender, args) =>
            {
                Interlocked.Increment(ref terminationCount);
                completed.Set();
            };
            service.ConvertFilesAsync();
            RequireSignal(started, "单任务未启动。");
            service.ConvertFilesAsync();
            bool registrationRejected = false;
            try
            {
                service.RegisterConversionJob(new FileConverter.ConversionJobs.ConversionJob("late"));
            }
            catch (InvalidOperationException)
            {
                registrationRejected = true;
            }
            finally
            {
                finish.Set();
            }

            RequireSignal(completed, "重复启动检查未结束。");
            Check(job.ExecutionCount == 1 && terminationCount == 1, "重复启动不得重复执行或重复终止。");
            Check(registrationRejected, "运行时必须保护任务快照。");
        }
    }

    private static void ClipboardFinishesBeforeTermination()
    {
        var settings = new FileConverter.Services.TestSettingsService();
        settings.Settings.CopyFilesInClipboardAfterConversion = true;
        var service = new FileConverter.Services.ConversionService(settings);
        var paths = new List<string>();
        bool clipboardCompleted = false;
        System.Windows.Forms.Clipboard.SetFileDropListAction = list =>
        {
            Thread.Sleep(20);
            paths.AddRange(list.Cast<string>());
            clipboardCompleted = true;
        };
        try
        {
            service.RegisterConversionJob(new FileConverter.ConversionJobs.ConversionJob("same"));
            service.RegisterConversionJob(new FileConverter.ConversionJobs.ConversionJob("same"));
            service.RegisterConversionJob(new FileConverter.ConversionJobs.ConversionJob("other"));
            Check(WaitForCompletion(service).AllConversionsSucceed, "剪贴板队列必须成功。");
            Check(clipboardCompleted, "终止事件必须在剪贴板写入完成后发出。");
            Check(paths.SequenceEqual(new[] { "same", "other" }), "剪贴板路径必须去重并保留顺序。");
        }
        finally
        {
            System.Windows.Forms.Clipboard.SetFileDropListAction = null;
        }
    }

    private static void ExecutionAndPreparationFailures()
    {
        int initialErrors = FileConverter.Diagnostics.Debug.Errors.Count;
        var service = NewService(1);
        var failed = new FileConverter.ConversionJobs.ConversionJob("throws");
        failed.ConvertAction = item => throw new InvalidOperationException("转换测试异常");
        var next = new FileConverter.ConversionJobs.ConversionJob("next");
        service.RegisterConversionJob(failed);
        service.RegisterConversionJob(next);
        Check(!WaitForCompletion(service).AllConversionsSucceed, "执行异常必须报告失败。");
        Check(next.ExecutionCount == 1, "单项异常后必须继续处理剩余任务。");

        var preparation = NewService(1);
        var invalid = new FileConverter.ConversionJobs.ConversionJob("prepare-throws");
        invalid.PrepareAction = item => throw new InvalidOperationException("准备测试异常");
        preparation.RegisterConversionJob(invalid);
        Check(!WaitForCompletion(preparation).AllConversionsSucceed, "准备异常必须发出失败终止事件。");
        Check(FileConverter.Diagnostics.Debug.Errors.Count == initialErrors + 2, "两个异常都必须保留日志。");
    }

    private static void Benchmark()
    {
        const int count = 160;
        var service = NewService(4);
        for (int index = 0; index < count; index++)
        {
            service.RegisterConversionJob(new FileConverter.ConversionJobs.ConversionJob($"benchmark-{index}"));
        }

        var elapsed = Stopwatch.StartNew();
        Check(WaitForCompletion(service, 30000).AllConversionsSucceed, "调度基准必须成功。");
        elapsed.Stop();
        Console.WriteLine($"合成调度基准：{count} 个空转换任务，耗时 {elapsed.Elapsed.TotalMilliseconds:F1} ms。");
    }

    private static FileConverter.Services.ConversionService NewService(int limit)
    {
        var settings = new FileConverter.Services.TestSettingsService();
        settings.Settings.MaximumNumberOfSimultaneousConversions = limit;
        return new FileConverter.Services.ConversionService(settings);
    }

    private static FileConverter.Services.ConversionJobsTerminatedEventArgs WaitForCompletion(
        FileConverter.Services.ConversionService service,
        int timeout = 5000)
    {
        using (var finished = new ManualResetEventSlim())
        {
            FileConverter.Services.ConversionJobsTerminatedEventArgs result = null;
            service.ConversionJobsTerminated += (sender, args) =>
            {
                result = args;
                finished.Set();
            };
            service.ConvertFilesAsync();
            Check(finished.Wait(timeout), "转换终止事件超时。");
            return result;
        }
    }

    private static void RequireSignal(ManualResetEventSlim signal, string message)
    {
        if (!signal.Wait(5000))
        {
            throw new TimeoutException(message);
        }
    }

    private static void UpdateMaximum(ref int maximum, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref maximum);
            if (value <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref maximum, value, current) != current);
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

// 只替换调度器之外的边界；实际被测代码始终来自项目中的 ConversionService.cs。
namespace CommunityToolkit.Mvvm.ComponentModel
{
    public class ObservableObject
    {
        protected void OnPropertyChanged(string propertyName) { }
    }
}

namespace CommunityToolkit.Mvvm.DependencyInjection { }

namespace FileConverter
{
    public static class Helpers
    {
        public static Thread InstantiateThread(string name, ThreadStart action) => new Thread(action) { Name = name };
        public static Thread InstantiateThread(string name, ParameterizedThreadStart action) => new Thread(action) { Name = name };
    }
}

namespace FileConverter.Diagnostics
{
    public static class Debug
    {
        public static readonly ConcurrentQueue<string> Errors = new ConcurrentQueue<string>();
        public static void Log(string message) { }
        public static void LogError(string message) => Errors.Enqueue(message);
    }
}

namespace FileConverter.Services
{
    public sealed class TestSettings
    {
        public int MaximumNumberOfSimultaneousConversions { get; set; } = 1;
        public bool CopyFilesInClipboardAfterConversion { get; set; }
    }

    public interface ISettingsService
    {
        TestSettings Settings { get; }
    }

    public sealed class TestSettingsService : ISettingsService
    {
        public TestSettings Settings { get; } = new TestSettings();
    }
}

namespace FileConverter.ConversionJobs
{
    public sealed class ConversionJob : INotifyPropertyChanged
    {
        private ConversionFlags flags;

        public ConversionJob(string outputPath) => this.OutputFilePath = outputPath;
        public event PropertyChangedEventHandler PropertyChanged;
        public string OutputFilePath { get; }
        public ConversionState State { get; set; }
        public int ExecutionCount { get; private set; }
        public Action<ConversionJob> PrepareAction { get; set; }
        public Action<ConversionJob> ConvertAction { get; set; }

        public ConversionFlags StateFlags
        {
            get => this.flags;
            set
            {
                this.flags = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.StateFlags)));
            }
        }

        public void PrepareConversion()
        {
            this.State = ConversionState.Ready;
            this.PrepareAction?.Invoke(this);
        }

        public bool CanStartConversion(ConversionFlags activeFlags) => (activeFlags & ConversionFlags.CdDriveExtraction) == 0;

        public void StartConversion()
        {
            if (this.State != ConversionState.Ready)
            {
                throw new InvalidOperationException("测试任务状态不正确。");
            }

            this.ExecutionCount++;
            this.State = ConversionState.InProgress;
            this.ConvertAction?.Invoke(this);
            this.StateFlags = ConversionFlags.None;
            if (this.State != ConversionState.Failed)
            {
                this.State = ConversionState.Done;
            }
        }
    }
}

namespace System.Windows.Forms
{
    public static class Clipboard
    {
        public static Action<StringCollection> SetFileDropListAction;
        public static void SetFileDropList(StringCollection paths) => SetFileDropListAction?.Invoke(paths);
    }
}
