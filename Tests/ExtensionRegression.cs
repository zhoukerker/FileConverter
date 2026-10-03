using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

// 两个独立的应用域避免 CLR 将同名、同版本的原版和新版程序集合并。
internal static class ExtensionRegression
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        if (arguments.Length > 0 && arguments[0] == "--conversion-preset")
        {
            return CaptureConversion(arguments);
        }

        if (arguments.Length != 4)
        {
            Console.Error.WriteLine("参数：原版扩展 DLL、新版扩展 DLL、默认配置 XML、测试临时目录。");
            return 1;
        }

        AppDomain baselineDomain = null;
        AppDomain optimizedDomain = null;
        try
        {
            baselineDomain = AppDomain.CreateDomain("原版扩展回归");
            optimizedDomain = AppDomain.CreateDomain("新版扩展回归");
            RegressionRunner baseline = CreateRunner(baselineDomain, arguments[0]);
            RegressionRunner optimized = CreateRunner(optimizedDomain, arguments[1]);
            VerifyMenus(baseline, optimized);
            VerifyNames(baseline, optimized);

            string originalXml = baseline.RoundTripXml(arguments[2], Path.Combine(arguments[3], "baseline.xml"));
            string optimizedXml = optimized.RoundTripXml(arguments[2], Path.Combine(arguments[3], "optimized.xml"));
            AssertEquivalentPresetXml(originalXml, optimizedXml);
            VerifyDefaultFlags(arguments[2], optimizedXml);
            optimized.VerifyDefaultFlagSerialization(arguments[3]);
            int defaultPresetCount = optimized.VerifyDefaultPresetDisplay(arguments[2]);
            optimized.VerifyDisplayMenus();
            Assert(optimized.RepeatedLoadAssemblyGrowth(arguments[2], 50) == 0, "重复 XML 加载仍生成额外程序集。");
            optimized.VerifyConcurrentXml(arguments[2], arguments[3], optimizedXml);
            Console.WriteLine(defaultPresetCount + " 个默认预设名称与标志保留，显示为中文；自定义名称保持原样，混合目录验证通过。");
            Console.WriteLine("XML 除新增保留的默认标志外逐项一致；false 标志省略；重复加载 50 次未新增程序集；12 路并发读写通过。");

            string[] menuNames = Enumerable.Range(0, 120).Select(i => "Folder" + i % 6 + "/Preset" + i).ToArray();
            // 固定菜单文案的归一不能误改用户同名预设或文件夹。
            menuNames[0] = "File Converter";
            menuNames[1] = "Configure presets...";
            menuNames[2] = "文件转换器";
            menuNames[3] = "配置转换预设…";
            menuNames[4] = "File Converter/Configure presets...";
            string[][] menuInputs = menuNames.Select(_ => new[] { "mp3" }).ToArray();
            baseline.BuildPresets(menuNames, menuInputs);
            optimized.BuildPresets(menuNames, menuInputs);
            baseline.Select(new[] { @"C:\input\file.mp3" });
            optimized.Select(new[] { @"C:\input\file.mp3" });
            MenuReport oldMenu = baseline.VerifyMenuResources();
            MenuReport newMenu = optimized.VerifyMenuResources();
            Assert(oldMenu.Snapshot == newMenu.Snapshot, "完整菜单结构不一致。");
            Assert(newMenu.RootTitle == "文件转换器" && newMenu.SettingsTitle == "配置转换预设…", "新版固定菜单必须使用简体中文。");
            Assert(newMenu.ImageCount == 4 && newMenu.AllImagesDisposed, "菜单位图未共享或未完整释放。");
            Console.WriteLine("120 个预设的完整菜单结构一致；独立位图由 " + oldMenu.ImageCount + " 减至 " + newMenu.ImageCount + "，全部随菜单释放。");

            optimized.VerifyConversionArguments(arguments[3]);
            Console.WriteLine("直接参数、长文件列表及 12 次并发启动通过；快速退出后临时文件全部清理。");

            baseline.LoadPresetsFromXml(arguments[2]);
            optimized.LoadPresetsFromXml(arguments[2]);
            baseline.Select(new[] { @"C:\input\audio.mp3" });
            optimized.Select(new[] { @"C:\input\audio.mp3" });
            Assert(baseline.Snapshot() == optimized.Snapshot(), "默认配置菜单匹配结果不一致。");
            Console.WriteLine("默认配置单文件菜单匹配（1000 次）：原版 " + baseline.MeasureRefresh(1000).ToString("F2") + " 毫秒；新版 " + optimized.MeasureRefresh(1000).ToString("F2") + " 毫秒。");

            string[] manyNames = Enumerable.Range(0, 1500).Select(i => "Preset" + i).ToArray();
            string[][] manyInputs = manyNames.Select((_, i) => Enumerable.Range(0, 24).Select(j => "ext" + ((i + j) % 48)).ToArray()).ToArray();
            string[] manyPaths = Enumerable.Range(0, 24).Select(i => @"C:\input\file.ext" + i).ToArray();
            baseline.BuildPresets(manyNames, manyInputs);
            optimized.BuildPresets(manyNames, manyInputs);
            baseline.Select(manyPaths);
            optimized.Select(manyPaths);
            Assert(baseline.Snapshot() == optimized.Snapshot(), "性能场景的输出不一致。");
            double originalMs = baseline.MeasureRefresh(8);
            double optimizedMs = optimized.MeasureRefresh(8);
            Console.WriteLine("菜单匹配基准（1500 个预设、24 种输入、8 次）：原版 " + originalMs.ToString("F2") + " 毫秒；新版 " + optimizedMs.ToString("F2") + " 毫秒；加速 " + (originalMs / optimizedMs).ToString("F1") + " 倍。");
            Console.WriteLine("基准仅衡量上述合成负载的菜单匹配，不能代表文件转换整体速度。");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("扩展回归失败：" + exception);
            return 1;
        }
        finally
        {
            if (baselineDomain != null) AppDomain.Unload(baselineDomain);
            if (optimizedDomain != null) AppDomain.Unload(optimizedDomain);
        }
    }

    private static RegressionRunner CreateRunner(AppDomain domain, string path)
    {
        RegressionRunner runner = (RegressionRunner)domain.CreateInstanceFromAndUnwrap(Assembly.GetExecutingAssembly().Location, typeof(RegressionRunner).FullName);
        runner.Initialize(path);
        return runner;
    }

    private static void VerifyMenus(RegressionRunner baseline, RegressionRunner optimized)
    {
        Random random = new Random(6381);
        string[] extensions = { "mp3", "mp4", "png", "wav", "pdf", "MP3", "", "unknown" };
        for (int scenario = 0; scenario < 600; scenario++)
        {
            int count = random.Next(1, 80);
            string[] names = new string[count];
            string[][] inputs = new string[count][];
            for (int i = 0; i < count; i++)
            {
                int number = random.Next(0, 15);
                names[i] = number == 0 ? null : number == 1 ? string.Empty : "Folder" + number % 4 + "/Preset" + number;
                inputs[i] = Enumerable.Range(0, random.Next(0, 15)).Select(_ => extensions[random.Next(extensions.Length)]).ToArray();
            }

            string[] paths = Enumerable.Range(0, random.Next(0, 20)).Select(i => @"C:\input\file" + i + "." + extensions[random.Next(extensions.Length)]).ToArray();
            baseline.BuildPresets(names, inputs);
            optimized.BuildPresets(names, inputs);
            baseline.Select(paths);
            optimized.Select(paths);
            Assert(baseline.Snapshot() == optimized.Snapshot(), "菜单状态或顺序不一致，场景 " + scenario + "。");
            Assert(baseline.CanShow() == optimized.CanShow(), "菜单显示条件不一致，场景 " + scenario + "。");
        }

        Console.WriteLine("随机菜单等价场景：600/600 通过，覆盖重复名称、重复输入类型、空名称、大小写与不支持类型。");
    }

    private static void VerifyNames(RegressionRunner baseline, RegressionRunner optimized)
    {
        string[] names = { "Normal", "A/B/C", "/Leading", "Trailing/", "A//B", "/", "中文/预设" };
        string[][] inputs = names.Select(_ => new[] { "mp3" }).ToArray();
        baseline.BuildPresets(names, inputs);
        optimized.BuildPresets(names, inputs);
        Assert(baseline.PresetPaths().SequenceEqual(optimized.PresetPaths()), "预设名称或目录解析不一致。");
        Console.WriteLine("预设目录解析边界：7/7 通过。");
    }

    private static void AssertEquivalentPresetXml(string original, string current)
    {
        XmlDocument originalDocument = new XmlDocument();
        originalDocument.LoadXml(original);
        XmlDocument currentDocument = new XmlDocument();
        currentDocument.LoadXml(current);
        // 旧扩展忽略默认标志；新版保留该信息，其他序列化内容仍须一致。
        foreach (XmlElement preset in currentDocument.SelectNodes("/Settings/ConversionPreset")) preset.RemoveAttribute("IsDefaultSettings");
        Assert(originalDocument.OuterXml == currentDocument.OuterXml, "除有意保留的默认标志外，XML 保存结果不一致。");
    }

    internal static void VerifyDefaultFlags(string sourcePath, string serialized)
    {
        XmlDocument source = new XmlDocument();
        source.Load(sourcePath);
        XmlDocument saved = new XmlDocument();
        saved.LoadXml(serialized);
        XmlNodeList expected = source.SelectNodes("/Settings/ConversionPreset");
        XmlNodeList actual = saved.SelectNodes("/Settings/ConversionPreset");
        Assert(expected.Count == actual.Count, "序列化后的预设数量改变。");
        for (int index = 0; index < expected.Count; index++)
        {
            XmlElement input = (XmlElement)expected[index];
            XmlElement output = (XmlElement)actual[index];
            bool isDefault = input.HasAttribute("IsDefaultSettings") && XmlConvert.ToBoolean(input.GetAttribute("IsDefaultSettings"));
            Assert(input.GetAttribute("Name") == output.GetAttribute("Name"), "XML/CLI 预设名称不得翻译或重排。");
            Assert(output.HasAttribute("IsDefaultSettings") == isDefault, "true 默认标志必须保留，false 或未指定标志必须省略。");
            if (isDefault) Assert(output.GetAttribute("IsDefaultSettings") == "true", "默认预设标志保存值无效。");
        }
    }

    private static int CaptureConversion(string[] arguments)
    {
        string directory = Environment.GetEnvironmentVariable("FILE_CONVERTER_REGRESSION_CAPTURE");
        if (string.IsNullOrEmpty(directory) || arguments.Length < 2) return 1;
        string listPath = arguments.Length > 3 && arguments[2] == "--input-files" ? arguments[3] : string.Empty;
        string[] paths = listPath.Length > 0 ? File.ReadAllLines(listPath) : arguments.Skip(2).ToArray();
        string temporaryPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllLines(temporaryPath, new[] { arguments[1], listPath }.Concat(paths));
        File.Move(temporaryPath, Path.ChangeExtension(temporaryPath, ".txt"));
        return 0;
    }

    internal static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

[Serializable]
public sealed class MenuReport
{
    public string Snapshot;
    public string RootTitle;
    public string SettingsTitle;
    public int ImageCount;
    public bool AllImagesDisposed;
}

public sealed class RegressionRunner : MarshalByRefObject
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    // 固定期望文案作为独立对照，不调用产品翻译函数生成预期结果。
    private static readonly Dictionary<string, string> ExpectedDisplayParts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        { "To Mkv", "转换为 MKV" }, { "To Mp4", "转换为 MP4" }, { "To Mp4 (low quality)", "转换为 MP4 （低质量）" },
        { "To Webm", "转换为 WEBM" }, { "To Ogv", "转换为 OGV" }, { "To Avi", "转换为 AVI" },
        { "To Gif", "转换为 GIF" }, { "To Gif (low quality)", "转换为 GIF （低质量）" },
        { "To Ogg", "转换为 OGG" }, { "To Flac", "转换为 FLAC" }, { "To Wav", "转换为 WAV" }, { "To Mp3", "转换为 MP3" }, { "To Aac", "转换为 AAC" },
        { "Extract DVD to Mp4", "提取 DVD 为 MP4" }, { "Extract DVD to Mkv", "提取 DVD 为 MKV" },
        { "Extract DVD to Flac", "提取 DVD 为 FLAC" }, { "Extract DVD to Ogg", "提取 DVD 为 OGG" }, { "Extract DVD to Mp3", "提取 DVD 为 MP3" },
        { "Extract CDA to Flac", "提取 CD 音轨为 FLAC" }, { "Extract CDA to Ogg", "提取 CD 音轨为 OGG" }, { "Extract CDA to Mp3", "提取 CD 音轨为 MP3" },
        { "To Png", "转换为 PNG" }, { "To Png (paged)", "转换为 PNG （逐页）" }, { "To Webp", "转换为 WEBP" },
        { "To Avif", "转换为 AVIF" }, { "To Jpg", "转换为 JPG" }, { "To Ico", "转换为 ICO" }, { "To Pdf", "转换为 PDF" },
        { "Scale 75%", "缩放至 75%" }, { "Scale 25%", "缩放至 25%" }, { "Scale 720p", "缩放至 720p" }, { "Scale 1080p", "缩放至 1080p" },
        { "Rotate left", "向左旋转" }, { "Rotate right", "向右旋转" }, { "Tempo - Pitch", "速度与音调" },
        { "To Mp3 (25% slower)", "转换为 MP3 （减速 25%）" }, { "To Mp3 (25% faster)", "转换为 MP3 （加速 25%）" },
        { "To Mp3 (pitched -1st)", "转换为 MP3 （降低 1 个半音）" }, { "To Mp3 (pitched +1st)", "转换为 MP3 （升高 1 个半音）" }
    };
    private Assembly assembly;
    private Type extension;
    private Type preset;
    private object instance;

    public void Initialize(string path)
    {
        assembly = Assembly.LoadFrom(path);
        extension = assembly.GetType("FileConverterExtension.FileConverterExtension", true);
        preset = assembly.GetType("FileConverterExtension.PresetReference", true);
        instance = Activator.CreateInstance(extension);
    }

    public override object InitializeLifetimeService()
    {
        return null;
    }

    public void BuildPresets(string[] names, string[][] inputs, bool[] defaultFlags = null)
    {
        Array result = Array.CreateInstance(preset, names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            object value = Activator.CreateInstance(preset, true);
            preset.GetProperty("FullName").SetValue(value, names[i]);
            preset.GetProperty("InputTypes").SetValue(value, inputs[i]);
            if (defaultFlags != null) preset.GetProperty("IsDefaultSettings").SetValue(value, defaultFlags[i]);
            result.SetValue(value, i);
        }

        SetPresets(result);
    }

    public void LoadPresetsFromXml(string path)
    {
        SetPresets(LoadXml(path));
    }

    private void SetPresets(Array result)
    {
        extension.GetField("presetReferences", PrivateInstance).SetValue(instance, result);
        MethodInfo build = extension.GetMethod("BuildPresetIndex", PrivateInstance);
        if (build != null) build.Invoke(instance, null);
    }

    public void Select(string[] paths)
    {
        Select(instance, paths);
    }

    private void Select(object target, string[] paths)
    {
        Type current = extension;
        FieldInfo field = null;
        while (current != null && field == null)
        {
            field = current.GetField("selectedItemPaths", PrivateInstance);
            current = current.BaseType;
        }

        IList selectedPaths = (IList)field.GetValue(target);
        selectedPaths.Clear();
        foreach (string path in paths) selectedPaths.Add(path);
    }

    public string Snapshot()
    {
        extension.GetMethod("RefreshPresetList", PrivateInstance).Invoke(instance, null);
        IEnumerable entries = (IEnumerable)extension.GetField("menuEntries", PrivateInstance).GetValue(instance);
        List<string> result = new List<string>();
        foreach (object entry in entries)
        {
            Type type = entry.GetType();
            object reference = type.GetField("PresetReference").GetValue(entry);
            string name = (string)preset.GetProperty("FullName").GetValue(reference);
            result.Add((name == null ? "<null>" : name) + ":" + type.GetField("Enabled").GetValue(entry) + ":" + type.GetField("ExtensionRefCount").GetValue(entry));
        }

        return string.Join("|", result);
    }

    public bool CanShow()
    {
        return (bool)extension.GetMethod("CanShowMenu", PrivateInstance).Invoke(instance, null);
    }

    public string[] PresetPaths()
    {
        IEnumerable values = (IEnumerable)extension.GetField("presetReferences", PrivateInstance).GetValue(instance);
        return values.Cast<object>().Select(value => preset.GetProperty("Name").GetValue(value) + "|" + string.Join("|", (string[])preset.GetProperty("Folders").GetValue(value))).ToArray();
    }

    private Array LoadXml(string path)
    {
        MethodInfo method = assembly.GetType("FileConverterExtension.XmlHelpers").GetMethod("LoadFromFile").MakeGenericMethod(preset.MakeArrayType());
        object[] arguments = { "Settings", path, null };
        method.Invoke(null, arguments);
        return (Array)arguments[2];
    }

    public string RoundTripXml(string path, string outputPath)
    {
        Array values = LoadXml(path);
        MethodInfo method = assembly.GetType("FileConverterExtension.XmlHelpers").GetMethod("SaveToFile").MakeGenericMethod(preset.MakeArrayType());
        method.Invoke(null, new object[] { "Settings", outputPath, values });
        return File.ReadAllText(outputPath);
    }

    public void VerifyDefaultFlagSerialization(string directory)
    {
        string inputPath = Path.Combine(directory, "default-flags-input.xml");
        File.WriteAllText(inputPath, "<Settings><ConversionPreset Name='内置预设' IsDefaultSettings='true'><InputTypes>mp3</InputTypes></ConversionPreset><ConversionPreset Name='自定义预设' IsDefaultSettings='false'><InputTypes>mp3</InputTypes></ConversionPreset><ConversionPreset Name='旧自定义预设'><InputTypes>mp3</InputTypes></ConversionPreset></Settings>");
        string serialized = RoundTripXml(inputPath, Path.Combine(directory, "default-flags-output.xml"));
        ExtensionRegression.VerifyDefaultFlags(inputPath, serialized);
    }

    public int VerifyDefaultPresetDisplay(string path)
    {
        XmlDocument document = new XmlDocument();
        document.Load(path);
        XmlNodeList sourcePresets = document.SelectNodes("/Settings/ConversionPreset");
        Array values = LoadXml(path);
        ExtensionRegression.Assert(values.Length == sourcePresets.Count && values.Length > 0, "默认预设加载数量必须与当前 XML 一致。");
        MethodInfo display = assembly.GetType("FileConverterExtension.PresetDisplayNames", true).GetMethod("GetName", BindingFlags.Public | BindingFlags.Static);
        PropertyInfo flag = preset.GetProperty("IsDefaultSettings");
        PropertyInfo displayName = preset.GetProperty("DisplayName");
        ExtensionRegression.Assert(flag != null && displayName != null && display != null, "默认标志与共享显示名称接口缺失。");
        for (int index = 0; index < values.Length; index++)
        {
            object value = values.GetValue(index);
            string fullName = ((XmlElement)sourcePresets[index]).GetAttribute("Name");
            string[] parts = fullName.Split('/');
            ExtensionRegression.Assert(parts.All(ExpectedDisplayParts.ContainsKey), "默认预设缺少独立中文显示对照：" + fullName);
            ExtensionRegression.Assert((string)preset.GetProperty("FullName").GetValue(value) == fullName && (bool)flag.GetValue(value), "默认预设 CLI/XML 名称或标志改变：" + fullName);
            ExtensionRegression.Assert((string)displayName.GetValue(value) == ExpectedDisplayParts[parts[parts.Length - 1]], "默认预设叶项中文显示错误：" + fullName);
            string expected = string.Join("/", parts.Select(part => ExpectedDisplayParts[part]));
            ExtensionRegression.Assert((string)display.Invoke(null, new object[] { fullName, true }) == expected, "共享显示名称未翻译完整默认路径：" + fullName);
        }
        foreach (string custom in new[] { null, string.Empty, "To Mp4", "Scale 75%/To Mp4", "Tempo - Pitch/To Mp3 (25% slower)", "File Converter", "Configure presets...", "自定义/中文名称" })
        {
            ExtensionRegression.Assert((string)display.Invoke(null, new object[] { custom, false }) == custom, "自定义名称必须逐字保留：" + custom);
        }
        ExtensionRegression.Assert(display.Invoke(null, new object[] { null, true }) == null && (string)display.Invoke(null, new object[] { string.Empty, true }) == string.Empty, "空名称的显示语义不得改变。");
        return values.Length;
    }

    public void VerifyDisplayMenus()
    {
        string[] names = { "Scale 75%/To Mp4", "Rotate left/To Jpg", "Tempo - Pitch/To Mp3 (25% slower)" };
        BuildPresets(names, names.Select(_ => new[] { "mp3" }).ToArray(), names.Select(_ => true).ToArray());
        Select(new[] { @"C:\input\file.mp3" });
        InspectMenu(menu =>
        {
            ToolStripMenuItem root = (ToolStripMenuItem)menu.Items[0];
            foreach (string name in names)
            {
                string[] parts = name.Split('/');
                ToolStripMenuItem folder = (ToolStripMenuItem)root.DropDownItems.Find(parts[0], false).Single();
                ExtensionRegression.Assert(folder.Name == parts[0] && folder.Text == ExpectedDisplayParts[parts[0]], "默认目录只翻译显示文本，查找键必须保留。");
                ExtensionRegression.Assert(folder.DropDownItems[0].Text.TrimEnd('\u200B') == ExpectedDisplayParts[parts[1]], "默认叶菜单未正确显示中文。");
            }
        });

        // 两种插入顺序都须保留混合目录中的用户原名。
        foreach (bool customFirst in new[] { false, true })
        {
            string[] mixedNames = customFirst ? new[] { "Scale 75%/To Png", "Scale 75%/To Mp4" } : new[] { "Scale 75%/To Mp4", "Scale 75%/To Png" };
            bool[] flags = customFirst ? new[] { false, true } : new[] { true, false };
            BuildPresets(mixedNames, mixedNames.Select(_ => new[] { "mp3" }).ToArray(), flags);
            InspectMenu(menu =>
            {
                ToolStripMenuItem root = (ToolStripMenuItem)menu.Items[0];
                ToolStripMenuItem folder = (ToolStripMenuItem)root.DropDownItems.Find("Scale 75%", false).Single();
                ExtensionRegression.Assert(folder.Name == "Scale 75%" && folder.Text == "Scale 75%", "混合自定义预设的目录必须保留用户原始名称。");
                for (int index = 0; index < mixedNames.Length; index++)
                {
                    string leaf = mixedNames[index].Substring(mixedNames[index].LastIndexOf('/') + 1);
                    string expected = flags[index] ? ExpectedDisplayParts[leaf] : leaf;
                    ExtensionRegression.Assert(folder.DropDownItems[index].Text.TrimEnd('\u200B') == expected, "默认与自定义预设的叶菜单显示规则混淆。");
                }
            });
        }
    }

    public int RepeatedLoadAssemblyGrowth(string path, int iterations)
    {
        LoadXml(path);
        int before = AppDomain.CurrentDomain.GetAssemblies().Length;
        for (int i = 0; i < iterations; i++) LoadXml(path);
        return AppDomain.CurrentDomain.GetAssemblies().Length - before;
    }

    public void VerifyConcurrentXml(string path, string directory, string expected)
    {
        Parallel.For(0, 12, index =>
        {
            string output = Path.Combine(directory, "parallel-xml-" + index + ".xml");
            ExtensionRegression.Assert(RoundTripXml(path, output) == expected, "并发 XML 读写出现输出差异。");
        });
    }

    public MenuReport VerifyMenuResources()
    {
        return InspectMenu(null);
    }

    private MenuReport InspectMenu(Action<ContextMenuStrip> inspect)
    {
        string registryPath = @"Software\FileConverterExtensionRegression\" + Guid.NewGuid().ToString("N");
        try
        {
            using (RegistryKey registry = Registry.CurrentUser.CreateSubKey(registryPath))
            {
                registry.SetValue("DisplayPresetIcons", "True");
                assembly.GetType("FileConverterExtension.PathHelpers").GetField("fileConverterRegistryKey", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, registry);
                HashSet<Image> images = new HashSet<Image>();
                string snapshot;
                string rootTitle;
                string settingsTitle;
                using (ContextMenuStrip menu = (ContextMenuStrip)extension.GetMethod("CreateMenu", PrivateInstance).Invoke(instance, null))
                {
                    ExtensionRegression.Assert(menu.Items.Count == 1, "扩展菜单应只有一个固定根项。");
                    ToolStripMenuItem root = (ToolStripMenuItem)menu.Items[0];
                    rootTitle = root.Text;
                    settingsTitle = root.DropDownItems[root.DropDownItems.Count - 1].Text;
                    snapshot = MenuSnapshot(menu.Items, images);
                    inspect?.Invoke(menu);
                }

                bool allDisposed = true;
                foreach (Image image in images)
                {
                    bool disposed = false;
                    try { int width = image.Width; }
                    catch (ArgumentException) { disposed = true; }
                    allDisposed &= disposed;
                    if (!disposed) image.Dispose();
                }

                return new MenuReport { Snapshot = snapshot, RootTitle = rootTitle, SettingsTitle = settingsTitle, ImageCount = images.Count, AllImagesDisposed = allDisposed };
            }
        }
        finally
        {
            assembly.GetType("FileConverterExtension.PathHelpers").GetField("fileConverterRegistryKey", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            Registry.CurrentUser.DeleteSubKey(registryPath, false);
        }
    }

    public void VerifyConversionArguments(string directory)
    {
        string captureDirectory = Path.Combine(directory, "process-capture");
        Directory.CreateDirectory(captureDirectory);
        string previousDirectory = Environment.GetEnvironmentVariable("FILE_CONVERTER_REGRESSION_CAPTURE");
        Environment.SetEnvironmentVariable("FILE_CONVERTER_REGRESSION_CAPTURE", captureDirectory);
        FieldInfo executablePath = assembly.GetType("FileConverterExtension.PathHelpers").GetField("fileConverterPath", BindingFlags.Static | BindingFlags.NonPublic);
        object previousPath = executablePath.GetValue(null);
        executablePath.SetValue(null, Assembly.GetExecutingAssembly().Location);
        const string presetName = "音频/测试预设";
        try
        {
            MethodInfo convert = extension.GetMethod("ConvertFiles", PrivateInstance);
            string[] shortPaths = { @"C:\input\普通.mp3", @"C:\input\含有 空格.wav", @"C:\input\MUSIC.MP3" };
            Select(shortPaths);
            convert.Invoke(instance, new object[] { presetName });
            string[] shortCapture = WaitForCaptures(captureDirectory, 1).Select(File.ReadAllLines).Single();
            ExtensionRegression.Assert(shortCapture[0] == presetName && shortCapture[1] == string.Empty && shortCapture.Skip(2).SequenceEqual(shortPaths), "直接参数的预设、中文或空格路径不一致。");

            string[] longPaths = Enumerable.Range(0, 220).Select(i => @"C:\input\中文 空格" + new string('x', 48) + i + ".mp3").ToArray();
            Parallel.For(0, 12, _ =>
            {
                object target = Activator.CreateInstance(extension);
                Select(target, longPaths);
                convert.Invoke(target, new object[] { presetName });
            });

            string[][] captures = WaitForCaptures(captureDirectory, 13).Select(File.ReadAllLines).Where(lines => lines[1].Length > 0).ToArray();
            ExtensionRegression.Assert(captures.Length == 12 && captures.Select(lines => lines[1]).Distinct().Count() == 12, "并发转换未创建独立的临时文件。");
            foreach (string[] capture in captures)
            {
                ExtensionRegression.Assert(capture[0] == presetName && capture.Skip(2).SequenceEqual(longPaths), "长文件列表发生覆盖、截断或顺序变化。");
            }

            Stopwatch timeout = Stopwatch.StartNew();
            while (captures.Any(lines => File.Exists(lines[1])) && timeout.ElapsedMilliseconds < 10000) Thread.Sleep(20);
            ExtensionRegression.Assert(captures.All(lines => !File.Exists(lines[1])), "子进程快速退出后仍有临时文件残留。");
        }
        finally
        {
            executablePath.SetValue(null, previousPath);
            Environment.SetEnvironmentVariable("FILE_CONVERTER_REGRESSION_CAPTURE", previousDirectory);
        }
    }

    private static string[] WaitForCaptures(string directory, int count)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        string[] files;
        do
        {
            files = Directory.GetFiles(directory, "*.txt");
            if (files.Length >= count) return files;
            Thread.Sleep(20);
        }
        while (timeout.ElapsedMilliseconds < 15000);

        throw new InvalidOperationException("等待测试子进程完成超时，实际结果 " + files.Length + "/" + count + "。");
    }

    public double MeasureRefresh(int iterations)
    {
        MethodInfo method = extension.GetMethod("RefreshPresetList", PrivateInstance);
        method.Invoke(instance, null);
        Stopwatch timer = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) method.Invoke(instance, null);
        return timer.Elapsed.TotalMilliseconds;
    }

    private static string MenuSnapshot(ToolStripItemCollection items, HashSet<Image> images, int depth = 0)
    {
        List<string> result = new List<string>();
        for (int index = 0; index < items.Count; index++)
        {
            ToolStripItem item = items[index];
            if (item.Image != null) images.Add(item.Image);
            ToolStripMenuItem menu = item as ToolStripMenuItem;
            string text = item.Text;
            // 只允许固定根项和最后的设置项发生翻译，用户预设文本仍逐字比较。
            if (depth == 0 && index == 0 && text == "文件转换器") text = "File Converter";
            if (depth == 1 && index == items.Count - 1 && text == "配置转换预设…") text = "Configure presets...";
            result.Add(text + ":" + item.Enabled + (menu == null ? "" : "[" + MenuSnapshot(menu.DropDownItems, images, depth + 1) + "]"));
        }

        return string.Join("|", result);
    }
}
