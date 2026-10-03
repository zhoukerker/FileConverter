using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

internal static class CoreRegression
{
    private static int assertionCount;

    private static int Main(string[] arguments)
    {
        if (arguments.Length != 4)
        {
            Console.Error.WriteLine("参数：原版构建目录、新版构建目录、测试临时目录、受控错误提示测试程序集。");
            return 1;
        }

        AppDomain baselineDomain = null;
        AppDomain currentDomain = null;
        try
        {
            CoreRunner baseline = CreateRunner(arguments[0], "原版核心回归", out baselineDomain);
            CoreRunner current = CreateRunner(arguments[1], "新版核心回归", out currentDomain);
            string[] boundaryPaths =
            {
                null, string.Empty, "file.txt", @"C:\", @"C:\folder\file.txt", @"c:\中文 文件\音乐.mp3",
                @"\\server\share\folder\file.txt", @"\\server\share\", @"C:\folder\\file.txt",
                "C:/folder/file.txt", @"C:relative.txt", @"\rooted\file.txt", @"C:\folder\",
                "C:\\folder\\line\nname.txt", @"C:\a.b\..\file", @"\\server\", "C:", ".", ".."
            };
            Random random = new Random(4817);
            List<string> paths = new List<string>(boundaryPaths);
            for (int index = 0; index < 500; index++)
            {
                string prefix = index % 3 == 0 ? @"C:\" : index % 3 == 1 ? @"\\server\share\" : string.Empty;
                paths.Add(prefix + string.Join("\\", Enumerable.Range(0, random.Next(0, 9)).Select(_ => "文件 " + random.Next(20))) + (index % 4 == 0 ? "\\" : ".txt"));
            }
            foreach (string path in paths)
            {
                foreach (string method in new[] { "GetFileName", "GetDrive", "GetDirectories" })
                {
                    Check(baseline.PathResult(method, path) == current.PathResult(method, path), "路径函数结果不一致：" + method + "，路径=" + path);
                }
            }
            Console.WriteLine("路径函数等价：" + paths.Count + " 个正常及边界路径，包含异常类型对照。");

            for (int scenario = 0; scenario < 180; scenario++)
            {
                string[] existing = Enumerable.Range(0, random.Next(0, 25)).Select(_ => PresetName(random.Next(12))).ToArray();
                string[] incoming = Enumerable.Range(0, random.Next(0, 25)).Select(_ => PresetName(random.Next(12))).ToArray();
                Check(baseline.MergeResult(existing, incoming, false) == current.MergeResult(existing, incoming, false), "预设合并结果不一致，场景 " + scenario);
            }
            Check(baseline.MergeResult(new[] { "重复", "重复", "A" }, null, false) == current.MergeResult(new[] { "重复", "重复", "A" }, null, false), "合并空设置行为不一致。");
            Check(baseline.MergeResult(new[] { "A", "A", "a", "文件夹/预设", "" }, new string[0], true) == current.MergeResult(new[] { "A", "A", "a", "文件夹/预设", "" }, new string[0], true), "设置与自身合并行为不一致。");
            Console.WriteLine("Settings.Merge 等价：182 个场景，覆盖同名去重、大小写、顺序、已有预设保留及空设置。");

            Check(current.CheckWithin24Hours(), "24 小时内的升级检查未直接返回 null。");
            Console.WriteLine("升级检查：24 小时内返回 null，未发生对空任务的 await。");
            current.VerifyUpgradeDownloads(arguments[2], arguments[3]);
            Console.WriteLine("真实主程序的本地下载、描述对象保留、取消及阻塞 UI 上下文回归通过；失败状态使用实际服务源码与受控错误提示边界验证；未执行安装器。");
            string[] cultures = current.VerifyLanguageResources();
            Check(cultures.SequenceEqual(new[] { "zh-CN" }), "支持语言必须恰好为简体中文。");
            Console.WriteLine("161 项中性资源及公开属性完整；外语文化回落简体中文，旧语言字段及 XML 值归一为 zh-CN。");
            Console.WriteLine("核心回归通过 " + assertionCount + " 项跨版本及行为断言。");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("核心回归失败：" + exception);
            return 1;
        }
        finally
        {
            if (baselineDomain != null) AppDomain.Unload(baselineDomain);
            if (currentDomain != null) AppDomain.Unload(currentDomain);
        }
    }

    private static CoreRunner CreateRunner(string directory, string name, out AppDomain domain)
    {
        string path = Path.Combine(Path.GetFullPath(directory), "FileConverter.exe");
        AppDomainSetup setup = new AppDomainSetup { ApplicationBase = Path.GetDirectoryName(path), ConfigurationFile = path + ".config" };
        domain = AppDomain.CreateDomain(name, null, setup);
        CoreRunner runner = (CoreRunner)domain.CreateInstanceFromAndUnwrap(Assembly.GetExecutingAssembly().Location, typeof(CoreRunner).FullName);
        runner.Initialize(path);
        return runner;
    }

    private static string PresetName(int value)
    {
        return value == 0 ? string.Empty : value == 1 ? "A" : value == 2 ? "a" : "目录" + value % 3 + "/预设" + value;
    }

    private static void Check(bool condition, string message)
    {
        assertionCount++;
        if (!condition) throw new InvalidOperationException(message);
    }
}

public sealed class CoreRunner : MarshalByRefObject
{
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    private Assembly application;
    private string applicationPath;

    public void Initialize(string path)
    {
        applicationPath = path;
        application = Assembly.LoadFrom(path);
    }

    public override object InitializeLifetimeService()
    {
        return null;
    }

    public string PathResult(string methodName, string path)
    {
        try
        {
            object result = application.GetType("FileConverter.PathHelpers").GetMethod(methodName).Invoke(null, new object[] { path });
            IEnumerable<string> directories = result as IEnumerable<string>;
            return directories != null ? "列表:" + string.Join("\u001f", directories) : result == null ? "空值" : "文本:" + result;
        }
        catch (Exception exception)
        {
            while (exception is TargetInvocationException && exception.InnerException != null) exception = exception.InnerException;
            return "异常:" + exception.GetType().FullName;
        }
    }

    public string MergeResult(string[] existing, string[] incoming, bool mergeSelf)
    {
        Type settingsType = application.GetType("FileConverter.Settings");
        object target = Activator.CreateInstance(settingsType);
        IList targetPresets = (IList)settingsType.GetProperty("ConversionPresets").GetValue(target);
        foreach (string name in existing) targetPresets.Add(CreatePreset(name, false));
        object source = incoming == null ? null : Activator.CreateInstance(settingsType);
        if (source != null)
        {
            IList sourcePresets = (IList)settingsType.GetProperty("ConversionPresets").GetValue(source);
            foreach (string name in incoming) sourcePresets.Add(CreatePreset(name, true));
        }
        if (mergeSelf) source = target;
        object result = settingsType.GetMethod("Merge").Invoke(target, new[] { source });
        return "返回原对象:" + ReferenceEquals(result, target) + "|" + string.Join("\u001f", targetPresets.Cast<object>().Select(preset => preset.GetType().GetProperty("FullName").GetValue(preset) + ":" + preset.GetType().GetProperty("IsDefaultSettings").GetValue(preset)));
    }

    private object CreatePreset(string name, bool incoming)
    {
        Type type = application.GetType("FileConverter.ConversionPreset");
        object preset = Activator.CreateInstance(type);
        type.GetProperty("FullName").SetValue(preset, name);
        type.GetProperty("IsDefaultSettings").SetValue(preset, incoming);
        return preset;
    }

    public bool CheckWithin24Hours()
    {
        Type registryType = application.GetType("FileConverter.Registry");
        FieldInfo registryInstance = registryType.GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = registryInstance.GetValue(null);
        object registry = Activator.CreateInstance(registryType);
        // 测试注册信息只保存在内存，禁止终结器写回用户的 Registry.xml。
        GC.SuppressFinalize(registry);
        IDictionary entries = (IDictionary)registryType.GetField("registryEntries", InstancePrivate).GetValue(registry);
        entries["LastUpdateCheckDate"] = DateTime.Now.ToFileTime().ToString(CultureInfo.InvariantCulture);
        registryInstance.SetValue(null, registry);
        object service = CreateUpgradeService();
        try
        {
            Task task = (Task)service.GetType().GetMethod("CheckForUpgrade").Invoke(service, null);
            if (!task.Wait(3000)) throw new InvalidOperationException("24 小时内的检查意外执行了耗时操作。");
            task.GetAwaiter().GetResult();
            return task.GetType().GetProperty("Result").GetValue(task) == null;
        }
        finally
        {
            DisposeService(service);
            registryInstance.SetValue(null, previous);
        }
    }

    public void VerifyUpgradeDownloads(string directory, string boundaryPath)
    {
        string sourceDirectory = Path.Combine(directory, "installer-source");
        Directory.CreateDirectory(sourceDirectory);
        string sourcePath = Path.Combine(sourceDirectory, "core-regression-" + Guid.NewGuid().ToString("N") + ".msi");
        byte[] content = Enumerable.Range(0, 65536).Select(index => (byte)(index % 251)).ToArray();
        File.WriteAllBytes(sourcePath, content);
        try
        {
            VerifyFileDownload(sourcePath, content, false);
            VerifyFileDownload(sourcePath, content, true);
            VerifyFailedDownload(Path.Combine(sourceDirectory, "missing-" + Guid.NewGuid().ToString("N") + ".msi"), boundaryPath);
            VerifyCancelledDownload();
        }
        finally
        {
            application.GetType("FileConverter.Diagnostics.Debug").GetMethod("Release").Invoke(null, null);
        }
    }

    private void VerifyFileDownload(string sourcePath, byte[] expected, bool blockContext)
    {
        object service = CreateUpgradeService();
        object description = Description(service);
        Set(description, "InstallerURL", new Uri(sourcePath).AbsoluteUri);
        Set(description, "NeedToUpgrade", true);
        SynchronizationContext previous = SynchronizationContext.Current;
        BlockedContext blocked = blockContext ? new BlockedContext() : null;
        if (blocked != null) SynchronizationContext.SetSynchronizationContext(blocked);
        try
        {
            // 退出流程等待内部下载标志，因此直接验证内部任务能在 UI 不处理 Post 时完成。
            MethodInfo method = service.GetType().GetMethod(blockContext ? "DownloadInstaller" : "StartUpgrade", blockContext ? InstancePrivate : BindingFlags.Instance | BindingFlags.Public);
            Task task = (Task)method.Invoke(service, null);
            Ensure(task.Wait(5000), "本地下载在阻塞 UI 上下文中未完成。");
            task.GetAwaiter().GetResult();
            Ensure(ReferenceEquals(Description(service), description), "下载完成后错误清空了升级描述。");
            Ensure(!(bool)Get(description, "InstallerDownloadInProgress") && (bool)Get(description, "InstallerDownloadDone"), "下载完成状态不正确。");
            Ensure((bool)Get(description, "NeedToUpgrade"), "成功下载后丢失升级请求。");
            string installerPath = (string)Get(description, "InstallerPath");
            Ensure(File.ReadAllBytes(installerPath).SequenceEqual(expected), "本地安装包下载内容不一致。");
            if (blocked != null) Ensure(blocked.Posts == 0, "内部下载仍依赖阻塞的 UI 回调。");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            DeleteInstaller(description);
            DisposeService(service);
        }
    }

    private void VerifyFailedDownload(string missingPath, string boundaryPath)
    {
        // 服务和下载逻辑来自项目真实源码，仅把模态提示替换成可检查的日志边界。
        Assembly boundary = Assembly.LoadFrom(boundaryPath);
        object service = Activator.CreateInstance(boundary.GetType("FileConverter.Services.UpgradeService"));
        object description = Description(service);
        Set(description, "InstallerURL", new Uri(missingPath).AbsoluteUri);
        try
        {
            Task task = (Task)service.GetType().GetMethod("StartUpgrade").Invoke(service, null);
            Ensure(task.Wait(5000), "失败下载没有结束。");
            task.GetAwaiter().GetResult();
            Ensure(!(bool)Get(description, "InstallerDownloadInProgress") && !(bool)Get(description, "NeedToUpgrade"), "下载失败后未恢复进行中标志或升级请求。");
            Ensure((int)boundary.GetType("FileConverter.Diagnostics.Debug").GetField("ErrorCount").GetValue(null) == 1, "预期下载失败没有报告错误。");
        }
        finally
        {
            DeleteInstaller(description);
            DisposeService(service);
        }
    }

    private void VerifyCancelledDownload()
    {
        TcpListener portFinder = new TcpListener(IPAddress.Loopback, 0);
        portFinder.Start();
        int port = ((IPEndPoint)portFinder.LocalEndpoint).Port;
        portFinder.Stop();
        string url = "http://localhost:" + port + "/";
        object service = CreateUpgradeService();
        object description = Description(service);
        Set(description, "InstallerURL", url + "core-cancel-" + Guid.NewGuid().ToString("N") + ".msi");
        using (HttpListener server = new HttpListener())
        using (ManualResetEventSlim started = new ManualResetEventSlim())
        using (ManualResetEventSlim release = new ManualResetEventSlim())
        {
            server.Prefixes.Add(url);
            server.Start();
            Task serve = Task.Run(() =>
            {
                HttpListenerContext request = server.GetContext();
                request.Response.ContentLength64 = 1024 * 1024;
                request.Response.OutputStream.Write(new byte[4096], 0, 4096);
                request.Response.OutputStream.Flush();
                started.Set();
                release.Wait(10000);
                request.Response.Close();
            });
            try
            {
                Task download = (Task)service.GetType().GetMethod("StartUpgrade").Invoke(service, null);
                Ensure(started.Wait(5000), "本地取消测试未建立下载连接。");
                service.GetType().GetMethod("CancelUpgrade").Invoke(service, null);
                Ensure(download.Wait(5000), "取消后下载任务没有结束。");
                download.GetAwaiter().GetResult();
                Ensure(!(bool)Get(description, "InstallerDownloadInProgress") && !(bool)Get(description, "NeedToUpgrade"), "取消后未恢复下载标志或升级请求。");
            }
            finally
            {
                release.Set();
                server.Stop();
                try { serve.Wait(5000); } catch (AggregateException) { }
                DeleteInstaller(description);
                DisposeService(service);
            }
        }
    }

    public string[] VerifyLanguageResources()
    {
        string directory = Path.GetDirectoryName(applicationPath);
        Ensure(Directory.GetFiles(directory, "FileConverter.resources.dll", SearchOption.AllDirectories).Length == 0, "单语言输出目录不应残留卫星资源 DLL。");

        Type resourcesType = application.GetType("FileConverter.Properties.Resources", true);
        PropertyInfo managerProperty = resourcesType.GetProperty("ResourceManager", BindingFlags.Public | BindingFlags.Static);
        PropertyInfo cultureProperty = resourcesType.GetProperty("Culture", BindingFlags.Public | BindingFlags.Static);
        Ensure(managerProperty != null && cultureProperty != null && cultureProperty.CanRead && cultureProperty.CanWrite, "资源管理器和文化覆盖公开接口必须保留。");
        ResourceManager manager = (ResourceManager)managerProperty.GetValue(null);
        Ensure(manager != null, "公开资源管理器不能为空。");
        ResourceSet neutralSet = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false);
        Ensure(neutralSet != null, "主程序集缺少简体中文中性资源。");
        Dictionary<string, string> neutral = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in neutralSet)
        {
            string value = entry.Value as string;
            Ensure(!string.IsNullOrEmpty(value), "中性文案缺失：" + entry.Key);
            neutral.Add((string)entry.Key, value);
        }
        Ensure(neutral.Count == 161, "完整中性文案应包含 161 个键，实际为 " + neutral.Count + "。");
        Ensure(neutral["Settings"] == "设置" && neutral["ConversionQueueTitle"] == "要转换的文件", "中性资源必须为简体中文。");
        PropertyInfo[] stringProperties = resourcesType.GetProperties(BindingFlags.Public | BindingFlags.Static).Where(property => property.PropertyType == typeof(string)).ToArray();
        Ensure(stringProperties.Length == 161 && new HashSet<string>(neutral.Keys, StringComparer.Ordinal).SetEquals(stringProperties.Select(property => property.Name)), "161 个同名公开文案属性必须完整保留。");

        object previousCulture = cultureProperty.GetValue(null);
        CultureInfo previousUiCulture = Thread.CurrentThread.CurrentUICulture;
        try
        {
            foreach (CultureInfo culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("zh-CN"), CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.GetCultureInfo("zh-TW") })
            {
                Thread.CurrentThread.CurrentUICulture = culture;
                cultureProperty.SetValue(null, culture);
                foreach (KeyValuePair<string, string> entry in neutral)
                {
                    Ensure(manager.GetString(entry.Key, culture) == entry.Value, "指定文化未回落完整简体中文资源：" + culture.Name + "/" + entry.Key);
                    Ensure(manager.GetString(entry.Key) == entry.Value, "线程文化未回落完整简体中文资源：" + culture.Name + "/" + entry.Key);
                }
                foreach (PropertyInfo property in stringProperties)
                {
                    Ensure((string)property.GetValue(null) == neutral[property.Name], "公开文案属性未保持中文：" + culture.Name + "/" + property.Name);
                }
                cultureProperty.SetValue(null, null);
                foreach (PropertyInfo property in stringProperties)
                {
                    Ensure((string)property.GetValue(null) == neutral[property.Name], "取消文化覆盖后文案属性未回落中文：" + culture.Name + "/" + property.Name);
                }
            }
        }
        finally
        {
            cultureProperty.SetValue(null, previousCulture);
            Thread.CurrentThread.CurrentUICulture = previousUiCulture;
        }

        IEnumerable supported = (IEnumerable)application.GetType("FileConverter.Helpers").GetMethod("GetSupportedCultures").Invoke(null, null);
        string[] enumerated = supported.Cast<CultureInfo>().Select(culture => culture.Name).ToArray();
        Ensure(enumerated.SequenceEqual(new[] { "zh-CN" }), "支持语言必须恰好为 zh-CN。");
        VerifyLanguageSettings();
        return enumerated;
    }

    private void VerifyLanguageSettings()
    {
        Type settingsType = application.GetType("FileConverter.Settings", true);
        PropertyInfo language = settingsType.GetProperty("ApplicationLanguage");
        PropertyInfo languageName = settingsType.GetProperty("ApplicationLanguageName");
        CultureInfo previousCulture = Thread.CurrentThread.CurrentCulture;
        CultureInfo previousUiCulture = Thread.CurrentThread.CurrentUICulture;
        try
        {
            foreach (string name in new[] { null, string.Empty, " ", "not-a-culture", "en-US", "ja-JP", "zh-TW", "zh-CN" })
            {
                object settings = Activator.CreateInstance(settingsType);
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
                languageName.SetValue(settings, name);
                EnsureChineseLanguage(settings, language, languageName, "旧语言名称：" + name);
            }
            foreach (CultureInfo culture in new[] { null, CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.GetCultureInfo("zh-TW") })
            {
                object settings = Activator.CreateInstance(settingsType);
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
                language.SetValue(settings, culture);
                EnsureChineseLanguage(settings, language, languageName, "旧文化对象：" + culture?.Name);
            }

            // 通过真实 XML 反序列化验证旧配置，而非只调用属性设置器。
            XmlSerializer serializer = new XmlSerializer(settingsType);
            foreach (string element in new[] { string.Empty, "<ApplicationLanguageName />", "<ApplicationLanguageName xsi:nil='true' />", "<ApplicationLanguageName>not-a-culture</ApplicationLanguageName>", "<ApplicationLanguageName>en-US</ApplicationLanguageName>", "<ApplicationLanguageName>ja-JP</ApplicationLanguageName>", "<ApplicationLanguageName>zh-TW</ApplicationLanguageName>" })
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
                string xml = "<Settings xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'>" + element + "</Settings>";
                object settings;
                using (StringReader reader = new StringReader(xml)) settings = serializer.Deserialize(reader);
                settingsType.GetMethod("OnDeserializationComplete").Invoke(settings, null);
                EnsureChineseLanguage(settings, language, languageName, "旧语言 XML：" + element);
                using (StringWriter writer = new StringWriter(CultureInfo.InvariantCulture))
                {
                    serializer.Serialize(writer, settings);
                    XmlDocument saved = new XmlDocument();
                    saved.LoadXml(writer.ToString());
                    XmlNode savedLanguage = saved.SelectSingleNode("//*[local-name()='ApplicationLanguageName']");
                    Ensure(savedLanguage != null && savedLanguage.InnerText == "zh-CN", "保存配置时必须保留语言字段并规范为 zh-CN。");
                }
            }
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previousCulture;
            Thread.CurrentThread.CurrentUICulture = previousUiCulture;
        }
    }

    private static void EnsureChineseLanguage(object settings, PropertyInfo language, PropertyInfo languageName, string scenario)
    {
        Ensure(((CultureInfo)language.GetValue(settings)).Name == "zh-CN" && (string)languageName.GetValue(settings) == "zh-CN", "语言字段未规范为简体中文：" + scenario);
        Ensure(Thread.CurrentThread.CurrentCulture.Name == "zh-CN" && Thread.CurrentThread.CurrentUICulture.Name == "zh-CN", "当前线程文化未规范为简体中文：" + scenario);
    }

    private object CreateUpgradeService()
    {
        return Activator.CreateInstance(application.GetType("FileConverter.Services.UpgradeService"));
    }

    private static object Description(object service)
    {
        return Get(service, "UpgradeVersionDescription");
    }

    private static object Get(object target, string property)
    {
        return target.GetType().GetProperty(property).GetValue(target);
    }

    private static void Set(object target, string property, object value)
    {
        target.GetType().GetProperty(property).SetValue(target, value);
    }

    private static void DeleteInstaller(object description)
    {
        string path = (string)Get(description, "InstallerPath");
        if (!string.IsNullOrEmpty(path)) File.Delete(path);
    }

    private static void DisposeService(object service)
    {
        IDisposable disposable = service as IDisposable;
        if (disposable != null) disposable.Dispose();
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class BlockedContext : SynchronizationContext
    {
        public int Posts;

        public override void Post(SendOrPostCallback callback, object state)
        {
            // 模拟 OnExit 在 UI 线程等待的阶段，不执行发送到界面的回调。
            Interlocked.Increment(ref Posts);
        }
    }

}

#if UPGRADE_BOUNDARY
// 这些边界只参与独立的失败测试程序集，主程序与业务服务源码保持原样。
namespace FileConverter.Diagnostics
{
    public static class Debug
    {
        public static int ErrorCount;
        public static void Log(string message) { }
        public static void LogError(string message) { Interlocked.Increment(ref ErrorCount); }
    }
}

namespace FileConverter.Annotations
{
    [AttributeUsage(AttributeTargets.All)]
    public sealed class NotNullAttribute : Attribute { }
}

namespace FileConverter.Properties
{
    public static class Resources
    {
        public static string DownloadingChangeLog => "正在下载更新日志";
    }
}

namespace FileConverter
{
    public static class Application
    {
        public static Version ApplicationVersion => new Version { Major = 2, Minor = 2, Patch = 0 };
    }

    public static class Registry
    {
        public static class Keys
        {
            public const string LastUpdateCheckDate = "LastUpdateCheckDate";
        }

        public static T GetValue<T>(string key) { throw new InvalidOperationException("失败下载测试不得读取用户注册信息。"); }
        public static void SetValue<T>(string key, T value) { throw new InvalidOperationException("失败下载测试不得写入用户注册信息。"); }
    }
}
#endif
