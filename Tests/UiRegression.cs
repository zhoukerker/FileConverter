using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using FileConverter.ConversionJobs;
using FileConverter.Services;
using FileConverter.ViewModels;
using FileConverter.Views;

internal static class UiRegressionBootstrap
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        string buildDirectory = Environment.GetEnvironmentVariable("FILE_CONVERTER_UI_BUILD");
        try
        {
            // 新域没有入口程序集，允许合法指定产品资源和其绑定重定向配置。
            var setup = new AppDomainSetup
            {
                ApplicationBase = buildDirectory,
                ConfigurationFile = Path.Combine(buildDirectory, "FileConverter.exe.config")
            };
            AppDomain domain = AppDomain.CreateDomain("FileConverterUiRegression", null, setup);
            var bridge = (UiRegressionBridge)domain.CreateInstanceFromAndUnwrap(Assembly.GetExecutingAssembly().Location, typeof(UiRegressionBridge).FullName);
            return bridge.Run(arguments, buildDirectory);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("界面引导失败：" + exception.GetType().FullName + "；" + exception.Message);
            return 1;
        }
    }
}

public sealed class UiRegressionBridge : MarshalByRefObject
{
    public int Run(string[] arguments, string buildDirectory)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
        {
            var name = new AssemblyName(eventArgs.Name);
            string path = string.IsNullOrEmpty(name.CultureName)
                ? Path.Combine(buildDirectory, name.Name + ".dll")
                : Path.Combine(buildDirectory, name.CultureName, name.Name + ".dll");
            if (!File.Exists(path) && !string.IsNullOrEmpty(name.CultureName))
            {
                // 兼容原版对照程序的卫星目录，当前版本的中文资源位于主程序集。
                path = Path.Combine(buildDirectory, "Languages", name.CultureName, name.Name + ".dll");
            }

            if (!File.Exists(path))
            {
                path = Path.Combine(buildDirectory, name.Name + ".exe");
            }

            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try
        {
            // 引导方法不直接引用产品类型，先完成依赖解析和资源程序集设置。
            Assembly applicationAssembly = Assembly.LoadFrom(Path.Combine(buildDirectory, "FileConverter.exe"));
            typeof(System.Windows.Application).GetProperty("ResourceAssembly").SetValue(null, applicationAssembly);
            MethodInfo run = Assembly.GetExecutingAssembly().GetType("UiRegression").GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic);
            return (int)run.Invoke(null, new object[] { arguments });
        }
        catch (Exception exception)
        {
            Exception cause = exception.InnerException ?? exception;
            Console.Error.WriteLine("界面引导失败：" + cause.GetType().FullName + "；" + cause.Message);
            return 1;
        }
    }
}

internal static class UiRegression
{
    private static readonly List<string> Results = new List<string>();
    private static int assertionCount;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int Run(string[] arguments)
    {
        MainWindow window = null;
        try
        {
            bool baseline = arguments.Contains("--baseline");
            bool preview = arguments.Contains("--preview");
            int previewSecondsIndex = Array.IndexOf(arguments, "--preview-seconds");
            int previewSeconds = previewSecondsIndex >= 0 ? int.Parse(arguments[previewSecondsIndex + 1], CultureInfo.InvariantCulture) : 0;
            string resultFile = arguments[0];
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            var provider = new TestServiceProvider();
            Ioc.Default.ConfigureServices(provider);

            // 只截获本测试创建应用时排队的启动回调，隔离用户配置和注册表操作。
            var startupOperations = new List<DispatcherOperation>();
            DispatcherHookEventHandler captureStartup = (sender, eventArgs) => startupOperations.Add(eventArgs.Operation);
            Dispatcher.CurrentDispatcher.Hooks.OperationPosted += captureStartup;
            var application = new FileConverter.Application();
            Dispatcher.CurrentDispatcher.Hooks.OperationPosted -= captureStartup;
            foreach (DispatcherOperation operation in startupOperations)
            {
                operation.Abort();
            }

            application.InitializeComponent();
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            provider.CreateJobs(1000);
            Console.WriteLine("正在采样窗口创建前的托管内存。");
            long memoryBeforeWindow = GC.GetTotalMemory(true);
            Console.WriteLine("正在构造真实主窗口。");
            window = new MainWindow();
            Console.WriteLine("正在显示真实主窗口。");
            window.Show();
            Console.WriteLine("正在完成首屏布局。");
            var itemsControl = WaitForItemsControl(window);
            PumpLayout(window);
            int initialContainers = GeneratedContainerCount(itemsControl);
            int initialControls = VisualChildren<FileConverter.Controls.ConversionJobControl>(window).Count();
            Label queueLabel = VisualChildren<Label>(window).Single(label => label.FontSize == 18);
            Check(string.Equals(queueLabel.Content as string, "要转换的文件", StringComparison.Ordinal), "真实队列标题必须正确加载中文语言资源。");
            var main = (MainViewModel)provider.GetService(typeof(MainViewModel));
            Button[] headerButtons = VisualChildren<Button>(window).Where(button =>
                object.ReferenceEquals(button.Command, main.ShowDiagnosticsCommand) ||
                object.ReferenceEquals(button.Command, main.ShowSettingsCommand)).ToArray();
            Check(headerButtons.Length == 2, "必须显示诊断与设置按钮。");
            foreach (Button button in headerButtons)
            {
                var image = VisualChildren<Image>(button).Single();
                Check(image.Source is BitmapSource bitmap && bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0,
                    "顶部图标必须从真实产品资源中成功加载。");
            }

            Console.WriteLine("正在采样窗口创建后的托管内存。");
            long memoryAfterWindow = GC.GetTotalMemory(true);
            Record("模式", baseline ? "baseline" : "optimized");
            Record("任务数量", itemsControl.Items.Count);
            Record("初始已生成容器", initialContainers);
            Record("初始任务控件", initialControls);
            Record("队列标题", queueLabel.Content);
            Record("顶部图标已加载", true);
            Record("窗口前托管内存字节", memoryBeforeWindow);
            Record("窗口后托管内存字节", memoryAfterWindow);
            Record("窗口新增托管内存字节", memoryAfterWindow - memoryBeforeWindow);
            Check(itemsControl.Items.Count == 1000, "真实主窗口必须绑定全部 1000 个任务。");
            if (baseline)
            {
                Check(initialContainers >= 900, "基线必须保留旧版全部任务容器的行为。");
            }
            else
            {
                Check(initialContainers > 0 && initialContainers < 100, "优化版只能为可见区域创建任务容器。");
                Check(initialControls == initialContainers, "每个可见容器必须绑定一个真实任务控件。");
            }

            var initialRows = VisualChildren<FileConverter.Controls.ConversionJobControl>(window).ToList();
            var firstRow = RowForJob(window, provider.Jobs[0]);
            Button firstCancelButton = VisualChildren<Button>(firstRow).Single();
            Check(firstCancelButton.Visibility == Visibility.Visible && firstCancelButton.IsEnabled,
                "首任务的取消按钮必须绑定其真实命令状态。");

            ScrollViewer scrollViewer = VisualChildren<ScrollViewer>(window).First();
            scrollViewer.ScrollToEnd();
            PumpUntil(window, () => itemsControl.ItemContainerGenerator.ContainerFromIndex(999) != null);
            var lastRow = RowForJob(window, provider.Jobs[999]);
            Check(lastRow != null && lastRow.IsVisible, "滚动到底必须显示最后一个任务。");
            int bottomContainers = GeneratedContainerCount(itemsControl);
            Record("底部已生成容器", bottomContainers);
            if (!baseline)
            {
                Check(bottomContainers < 100, "滚动到底后必须继续保持虚拟化。");
            }

            Button lastCancelButton = VisualChildren<Button>(lastRow).Single();
            Check(lastCancelButton.Visibility == Visibility.Visible && !lastCancelButton.IsEnabled,
                "回收后的行必须绑定末任务不可取消的命令，不能沿用首任务状态。");
            Check(object.ReferenceEquals(lastCancelButton.Command, provider.Jobs[999].CancelCommand),
                "末任务按钮必须直接绑定末任务命令。");
            Record("末任务显示", true);
            Record("末任务取消按钮禁用", true);

            // 再次往返确保回收池参与，随后检查复用容器的数据上下文。
            scrollViewer.ScrollToHome();
            PumpUntil(window, () => itemsControl.ItemContainerGenerator.ContainerFromIndex(0) != null);
            scrollViewer.ScrollToEnd();
            PumpUntil(window, () => itemsControl.ItemContainerGenerator.ContainerFromIndex(999) != null);
            lastRow = RowForJob(window, provider.Jobs[999]);
            lastCancelButton = VisualChildren<Button>(lastRow).Single();
            Check(!lastCancelButton.IsEnabled && object.ReferenceEquals(lastRow.DataContext, provider.Jobs[999]),
                "多次回收之后必须保持任务和取消状态一致。");
            bool reused = VisualChildren<FileConverter.Controls.ConversionJobControl>(window).Any(initialRows.Contains);
            Record("发现初始行控件复用", reused);
            if (!baseline)
            {
                Check(reused, "Recycling 模式必须实际复用行控件。");
            }

            Record("通过断言", assertionCount);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultFile)));
            File.WriteAllLines(resultFile, Results, new System.Text.UTF8Encoding(true));
            Console.WriteLine($"通过 {assertionCount} 项真实 WPF 界面断言。");

            if (preview || previewSeconds > 0)
            {
                provider.Jobs[0].SetPreviewReady();
                provider.Jobs[999].SetPreviewReady();
                scrollViewer.ScrollToHome();
                PumpLayout(window);
                Console.WriteLine(preview ? "预览已打开，关闭主窗口即可结束。" : $"界面保留 {previewSeconds} 秒供观察。");
                var watch = Stopwatch.StartNew();
                while (window.IsVisible && (preview || watch.Elapsed.TotalSeconds < previewSeconds))
                {
                    PumpDispatcher();
                    Thread.Sleep(10);
                }
            }

            window.Close();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"界面回归失败：{exception}");
            window?.Close();
            return 1;
        }
    }

    private static ItemsControl WaitForItemsControl(Window window)
    {
        ItemsControl result = null;
        PumpUntil(window, () =>
        {
            result = VisualChildren<ItemsControl>(window).FirstOrDefault(control => control.Items.Count == 1000);
            return result != null;
        });
        return result;
    }

    private static int GeneratedContainerCount(ItemsControl items)
    {
        int count = 0;
        for (int index = 0; index < items.Items.Count; index++)
        {
            if (items.ItemContainerGenerator.ContainerFromIndex(index) != null)
            {
                count++;
            }
        }

        return count;
    }

    private static FileConverter.Controls.ConversionJobControl RowForJob(DependencyObject root, ConversionJob job) =>
        VisualChildren<FileConverter.Controls.ConversionJobControl>(root).SingleOrDefault(row => object.ReferenceEquals(row.DataContext, job));

    private static IEnumerable<T> VisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root == null)
        {
            yield break;
        }

        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T matched)
            {
                yield return matched;
            }

            foreach (T descendant in VisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void PumpUntil(Window window, Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        do
        {
            PumpLayout(window);
            if (condition())
            {
                return;
            }

            Thread.Sleep(1);
        }
        while (timeout.ElapsedMilliseconds < 10000);
        throw new TimeoutException("界面布局或滚动没有在期限内完成。");
    }

    private static void PumpLayout(Window window)
    {
        window.UpdateLayout();
        PumpDispatcher();
        window.UpdateLayout();
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Record(string name, object value)
    {
        string line = name + "=" + Convert.ToString(value, CultureInfo.InvariantCulture);
        Results.Add(line);
        Console.WriteLine(line);
    }

    private static void Check(bool condition, string message)
    {
        assertionCount++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeConversionJob : ConversionJob
    {
        private static readonly PropertyInfo StateProperty = typeof(ConversionJob).GetProperty(nameof(State));
        private static readonly PropertyInfo OutputPathsProperty = typeof(ConversionJob).GetProperty("OutputFilePaths", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly bool canCancel;

        public FakeConversionJob(int index, bool canCancel)
            : base(new FileConverter.ConversionPreset("界面回归", FileConverter.OutputType.Png, "png"), $@"C:\界面验证\输入\任务-{index + 1:D4}.png")
        {
            this.canCancel = canCancel;
            OutputPathsProperty.SetValue(this, new[] { $@"C:\界面验证\输出\任务-{index + 1:D4}.png" });
            StateProperty.SetValue(this, index == 0 || index == 999 ? ConversionState.InProgress : ConversionState.Ready);
            this.UserState = index == 0 || index == 999 ? "界面状态验证" : "等待转换";
            this.Progress = index == 0 || index == 999 ? 0.4f : 0f;
            this.StartTime = DateTime.Now;
        }

        public void SetPreviewReady()
        {
            StateProperty.SetValue(this, ConversionState.Ready);
            this.Progress = 0f;
            this.UserState = "等待转换";
        }

        protected override bool IsCancelable() => this.canCancel && this.State == ConversionState.InProgress;

        public override void Cancel() { }
    }

    private sealed class TestConversionService : IConversionService
    {
        private readonly List<ConversionJob> jobs = new List<ConversionJob>();

        public event EventHandler<ConversionJobsTerminatedEventArgs> ConversionJobsTerminated { add { } remove { } }
        public ReadOnlyCollection<ConversionJob> ConversionJobs => this.jobs.AsReadOnly();
        public void ConvertFilesAsync() => throw new InvalidOperationException("界面验证不得启动转换。");
        public void RegisterConversionJob(ConversionJob job) => this.jobs.Add(job);
    }

    private sealed class TestNavigationService : INavigationService
    {
        public void RegisterPage<T>(string pageKey, bool cancelAutoExit, bool mainWindow) where T : Window { }
        public void Show(string pageKey) { }
        public void Close(string pageKey, bool alreadyClosing) { }
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        private readonly TestConversionService conversions = new TestConversionService();
        private readonly TestNavigationService navigation = new TestNavigationService();
        private MainViewModel main;

        public readonly List<FakeConversionJob> Jobs = new List<FakeConversionJob>();

        public void CreateJobs(int count)
        {
            for (int index = 0; index < count; index++)
            {
                var job = new FakeConversionJob(index, index == 0);
                this.Jobs.Add(job);
                this.conversions.RegisterConversionJob(job);
            }
        }

        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IConversionService))
            {
                return this.conversions;
            }

            if (serviceType == typeof(INavigationService))
            {
                return this.navigation;
            }

            if (serviceType == typeof(MainViewModel))
            {
                return this.main ?? (this.main = new MainViewModel());
            }

            return null;
        }
    }
}
