using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using FileConverter;
using FileConverter.ConversionJobs;
using FileConverter.Services;
using ImageMagick;

internal static class ConversionRegressionBootstrap
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        string buildDirectory = Environment.GetEnvironmentVariable("FILE_CONVERTER_TEST_BUILD");
        AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
        {
            string assemblyName = new AssemblyName(eventArgs.Name).Name;
            string path = Path.Combine(buildDirectory, assemblyName + ".dll");
            if (!File.Exists(path))
            {
                path = Path.Combine(buildDirectory, assemblyName + ".exe");
            }
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        return ConversionRegression.Run(arguments);
    }
}

internal static class ConversionRegression
{
    private static string outputDirectory;
    private static string imageInput;
    private static string audioInput;
    private static readonly List<string> Results = new List<string>();

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static int Run(string[] arguments)
    {
        int fixtureIndex = Array.IndexOf(arguments, "--process-fixture");
        if (fixtureIndex >= 0)
        {
            return RunProcessFixture(arguments, fixtureIndex);
        }

        try
        {
            outputDirectory = Path.GetFullPath(arguments[0]);
            Directory.CreateDirectory(outputDirectory);
            if (Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            {
                throw new Exception("输出目录必须为空，请为每次验证指定新的目录。");
            }
            new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            Ioc.Default.ConfigureServices(new TestServiceProvider());
            if (arguments.Contains("--pdf-metadata-benchmark"))
            {
                RunPdfMetadataBenchmark();
                return 0;
            }
            CreateInputs();
            RunOutputChecks();
            if (arguments.Contains("--lifecycle"))
            {
                RunLifecycleChecks();
            }

            WriteManifest();
            File.WriteAllLines(Path.Combine(outputDirectory, "results.txt"), Results, Encoding.UTF8);
            Console.WriteLine("通过：" + Results.Count.ToString(CultureInfo.InvariantCulture) + " 项转换验证。");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("失败：" + exception);
            return 1;
        }
    }

    private static void CreateInputs()
    {
        imageInput = Path.Combine(outputDirectory, "input.png");
        using (Bitmap bitmap = new Bitmap(320, 192))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(255, 236, 242, 249));
            graphics.FillRectangle(Brushes.SteelBlue, 12, 18, 150, 85);
            graphics.FillEllipse(Brushes.OrangeRed, 122, 63, 176, 109);
            graphics.DrawLine(Pens.Black, 0, 0, 319, 191);
            bitmap.Save(imageInput, ImageFormat.Png);
        }

        audioInput = Path.Combine(outputDirectory, "input.wav");
        const int sampleRate = 44100;
        const int sampleCount = sampleRate * 2;
        using (BinaryWriter writer = new BinaryWriter(File.Create(audioInput)))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + (sampleCount * 2));
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(sampleCount * 2);
            for (int index = 0; index < sampleCount; index++)
            {
                writer.Write((short)(10000 * Math.Sin(2 * Math.PI * 440 * index / sampleRate)));
            }
        }

        DateTime fixtureTime = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetCreationTimeUtc(imageInput, fixtureTime);
        File.SetCreationTimeUtc(audioInput, fixtureTime);
        File.SetLastWriteTimeUtc(imageInput, fixtureTime);
        File.SetLastWriteTimeUtc(audioInput, fixtureTime);
    }

    private static void RunPdfMetadataBenchmark()
    {
        const int pageCount = 40;
        string input = Path.Combine(outputDirectory, "metadata-input.pdf");
        List<string> objects = new List<string>();
        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        string children = string.Join(" ", Enumerable.Range(0, pageCount).Select(index => (3 + (index * 2)).ToString(CultureInfo.InvariantCulture) + " 0 R"));
        objects.Add("<< /Type /Pages /Count " + pageCount + " /Kids [" + children + "] >>");
        for (int index = 0; index < pageCount; index++)
        {
            int contentNumber = 4 + (index * 2);
            objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents " + contentNumber + " 0 R >>");
            string content = "0.9 0.95 1 rg 0 0 612 792 re f 0.15 0.35 0.65 rg 20 20 572 752 re f\n";
            objects.Add("<< /Length " + content.Length + " >>\nstream\n" + content + "endstream");
        }

        // 直接写出简单矢量 PDF，避免生成测试输入时分配大幅像素而污染峰值统计。
        using (FileStream stream = File.Create(input))
        using (StreamWriter writer = new StreamWriter(stream, Encoding.ASCII))
        {
            writer.Write("%PDF-1.4\n");
            List<long> offsets = new List<long>();
            for (int index = 0; index < objects.Count; index++)
            {
                writer.Flush();
                offsets.Add(stream.Position);
                writer.Write((index + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n" + objects[index] + "\nendobj\n");
            }
            writer.Flush();
            long crossReferenceOffset = stream.Position;
            writer.Write("xref\n0 " + (objects.Count + 1) + "\n0000000000 65535 f \n");
            foreach (long offset in offsets)
            {
                writer.Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
            }
            writer.Write("trailer\n<< /Size " + (objects.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + crossReferenceOffset + "\n%%EOF\n");
        }

        MagickNET.SetGhostscriptDirectory(Path.GetDirectoryName(typeof(ConversionJob).Assembly.Location));
        PdfCountJob job = new PdfCountJob(new ConversionPreset("PDF 元数据基准", OutputType.Png, "pdf"), input);
        Stopwatch elapsed = Stopwatch.StartNew();
        int actualPageCount = job.ReadPageCount();
        elapsed.Stop();
        Require(actualPageCount == pageCount, "40 页 PDF 页数统计正确");
        using (Process process = Process.GetCurrentProcess())
        {
            process.Refresh();
            string result = actualPageCount + "\t" + elapsed.ElapsedMilliseconds + "\t" + process.PeakWorkingSet64;
            File.WriteAllLines(Path.Combine(outputDirectory, "pdf-metadata-benchmark.tsv"), new[] { "页数\t耗时毫秒\t峰值工作集字节", result }, Encoding.UTF8);
            Console.WriteLine("PDF 页数统计：" + elapsed.ElapsedMilliseconds + " 毫秒，进程峰值工作集 " + (process.PeakWorkingSet64 / (1024 * 1024)) + " MiB。");
        }
    }

    private static void RunOutputChecks()
    {
        foreach (OutputType outputType in new[] { OutputType.Png, OutputType.Jpg, OutputType.Webp, OutputType.Gif, OutputType.Ico })
        {
            string output = Path.Combine(outputDirectory, "image." + outputType.ToString().ToLowerInvariant());
            ConversionPreset preset = new ConversionPreset("图片回归", outputType, "png");
            ConversionJob job = ConversionJobFactory.Create(preset, imageInput);
            job.PrepareConversion(output);
            RunJob(job);
            Require(job.State == ConversionState.Done && File.Exists(output), outputType + " 转换成功且输出存在");
            Require(File.GetLastWriteTimeUtc(output) == File.GetLastWriteTimeUtc(imageInput), outputType + " 保留源文件修改时间");
            using (MagickImage image = new MagickImage(output))
            {
                uint expectedWidth = outputType == OutputType.Ico ? 128u : 320u;
                uint expectedHeight = outputType == OutputType.Ico ? 77u : 192u;
                Require(image.Width == expectedWidth && image.Height == expectedHeight, outputType + " 默认尺寸正确");
            }
        }

        foreach (OutputType outputType in new[] { OutputType.Wav, OutputType.Mp3, OutputType.Flac })
        {
            string output = Path.Combine(outputDirectory, "audio." + outputType.ToString().ToLowerInvariant());
            ConversionPreset preset = new ConversionPreset("音频回归", outputType, "wav");
            ConversionJob job = ConversionJobFactory.Create(preset, audioInput);
            job.PrepareConversion(output);
            RunJob(job);
            Require(job.State == ConversionState.Done && new FileInfo(output).Length > 0, outputType + " 音频转换成功");
        }

        string pdfInput = Path.Combine(outputDirectory, "input.pdf");
        using (MagickImageCollection pages = new MagickImageCollection())
        {
            pages.Add(new MagickImage(imageInput));
            pages.Add(new MagickImage(imageInput));
            pages.Add(new MagickImage(imageInput));
            pages.Write(pdfInput, MagickFormat.Pdf);
        }

        DateTime fixtureTime = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetCreationTimeUtc(pdfInput, fixtureTime);
        File.SetLastWriteTimeUtc(pdfInput, fixtureTime);

        ConversionPreset pdfPreset = new ConversionPreset("PDF 回归", OutputType.Png, "pdf");
        pdfPreset.OutputFileNameTemplate = Path.Combine(outputDirectory, "pdf-page-(n:i)");
        ConversionJob pdfJob = ConversionJobFactory.Create(pdfPreset, pdfInput);
        pdfJob.PrepareConversion();
        RunJob(pdfJob);
        Require(pdfJob.State == ConversionState.Done, "多页 PDF 转换成功");
        string[] pdfOutputs = Directory.GetFiles(outputDirectory, "pdf-page-*.png");
        Require(pdfOutputs.Length == 3, "多页 PDF 页数和输出数量正确");
    }

    private static void RunLifecycleChecks()
    {
        ConversionJob flood = CreateFixtureJob("flood", "flood.wav");
        RunJob(flood, 10000);
        Require(flood.State == ConversionState.Done, "10 MB 标准输出不会堵塞转换管道");

        ConversionJob failed = CreateFixtureJob("fail", "failed.wav");
        RunJob(failed);
        Require(failed.State == ConversionState.Failed, "非零退出码即使没有错误关键字也标记失败");
        Require(!File.Exists(failed.OutputFilePath), "失败后移除不完整输出");

        string protectedInput = Path.Combine(outputDirectory, "protected-input.wav");
        File.Copy(audioInput, protectedInput);
        ConversionPreset missingOutputPreset = new ConversionPreset("无输出回归", OutputType.Wav, "wav");
        missingOutputPreset.InputPostConversionAction = InputPostConversionAction.Delete;
        missingOutputPreset.SetSettingsValue(ConversionPreset.ConversionSettingKeys.EnableFFMPEGCustomCommand, "True");
        missingOutputPreset.SetSettingsValue(ConversionPreset.ConversionSettingKeys.FFMPEGCustomCommand, "--process-fixture no-output");
        ConversionJob missingOutput = new FixtureFfmpegJob(missingOutputPreset, protectedInput);
        missingOutput.PrepareConversion(Path.Combine(outputDirectory, "missing-output.wav"));
        RunJob(missingOutput);
        Require(missingOutput.State == ConversionState.Failed, "零退出码未生成输出时仍标记失败");
        Require(File.Exists(protectedInput), "未生成输出时保留请求删除的源文件");

        string multipleInput = Path.Combine(outputDirectory, "multiple-input.wav");
        File.Copy(audioInput, multipleInput);
        ConversionPreset multiplePreset = new ConversionPreset("多输出缺失回归", missingOutputPreset);
        multiplePreset.InputPostConversionAction = InputPostConversionAction.Delete;
        multiplePreset.SetSettingsValue(ConversionPreset.ConversionSettingKeys.FFMPEGCustomCommand, "--process-fixture partial-output");
        ConversionJob multipleOutput = new FixtureFfmpegJob(multiplePreset, multipleInput);
        string firstOutput = Path.Combine(outputDirectory, "partial-first.wav");
        string secondOutput = Path.Combine(outputDirectory, "partial-second.wav");
        multipleOutput.PrepareConversion(firstOutput, secondOutput);
        RunJob(multipleOutput);
        Require(multipleOutput.State == ConversionState.Failed, "多输出仅完成一半时标记失败");
        Require(File.Exists(multipleInput), "多输出缺失时保留源文件");
        Require(!File.Exists(firstOutput) && !File.Exists(secondOutput), "多输出失败后清理本任务创建的半成品");

        string existingOutput = Path.Combine(outputDirectory, "existing-output.wav");
        File.WriteAllText(existingOutput, "已有文件内容");
        ConversionJob existingOutputJob = new FixtureFfmpegJob(missingOutputPreset, protectedInput);
        existingOutputJob.PrepareConversion(existingOutput, Path.Combine(outputDirectory, "existing-missing.wav"));
        RunJob(existingOutputJob);
        Require(existingOutputJob.State == ConversionState.Failed && File.Exists(protectedInput), "显式多输出缺失时保留源文件");
        Require(File.ReadAllText(existingOutput) == "已有文件内容", "失败清理不会删除预先存在的显式输出文件");

        ConversionJob sourceAliasJob = new FixtureFfmpegJob(missingOutputPreset, protectedInput);
        sourceAliasJob.PrepareConversion(protectedInput, Path.Combine(outputDirectory, "source-alias-missing.wav"));
        RunJob(sourceAliasJob);
        Require(sourceAliasJob.State == ConversionState.Failed && File.ReadAllBytes(protectedInput).SequenceEqual(File.ReadAllBytes(audioInput)), "输出路径与源文件重名时失败清理保留源数据");

        ConversionJob unchangedOutput = new FixtureFfmpegJob(missingOutputPreset, protectedInput);
        unchangedOutput.PrepareConversion(existingOutput);
        RunJob(unchangedOutput);
        Require(unchangedOutput.State == ConversionState.Failed && File.Exists(protectedInput), "全部显式输出已经存在但未更新时仍失败并保留源文件");
        Require(File.ReadAllText(existingOutput) == "已有文件内容", "未更新的既有输出内容保持完整");

        string rewriteInput = Path.Combine(outputDirectory, "rewrite-input.wav");
        File.Copy(audioInput, rewriteInput);
        ConversionPreset rewritePreset = new ConversionPreset("既有输出重写回归", missingOutputPreset);
        rewritePreset.InputPostConversionAction = InputPostConversionAction.Delete;
        rewritePreset.SetSettingsValue(ConversionPreset.ConversionSettingKeys.FFMPEGCustomCommand, "--process-fixture rewrite-preserve-time");
        ConversionJob rewrittenOutput = new FixtureFfmpegJob(rewritePreset, rewriteInput);
        rewrittenOutput.PrepareConversion(existingOutput);
        RunJob(rewrittenOutput);
        Require(rewrittenOutput.State == ConversionState.Done && !File.Exists(rewriteInput), "合法重写相同内容并恢复修改时间仍识别为成功");
        Require(File.ReadAllText(existingOutput) == "已有文件内容", "合法重写后的既有输出内容正确");

        CheckInPlacePostAction(InputPostConversionAction.Delete);
        CheckInPlacePostAction(InputPostConversionAction.MoveInArchiveFolder);

        string zeroOutputInput = Path.Combine(outputDirectory, "zero-output-input.wav");
        File.Copy(audioInput, zeroOutputInput);
        ConversionPreset zeroOutputPreset = new ConversionPreset("零输出回归", OutputType.Wav, "wav");
        zeroOutputPreset.InputPostConversionAction = InputPostConversionAction.Delete;
        ConversionJob zeroOutput = new ZeroOutputJob(zeroOutputPreset, zeroOutputInput);
        zeroOutput.PrepareConversion();
        RunJob(zeroOutput);
        Require(zeroOutput.State == ConversionState.Failed, "零输出任务不会误判为成功");
        Require(File.Exists(zeroOutputInput), "零输出任务保留请求删除的源文件");

        ConversionJob silent = CreateFixtureJob("silent", "silent.wav");
        Exception workerError = null;
        Thread worker = new Thread(() =>
        {
            try { silent.StartConversion(); }
            catch (Exception exception) { workerError = exception; }
        });
        worker.IsBackground = true;
        worker.Start();
        Stopwatch startup = Stopwatch.StartNew();
        while (!File.Exists(silent.OutputFilePath + ".started"))
        {
            PumpDispatcher();
            Thread.Sleep(5);
            if (startup.ElapsedMilliseconds > 5000)
            {
                throw new Exception("静默进程没有按期启动。");
            }
        }

        Stopwatch cancellation = Stopwatch.StartNew();
        silent.Cancel();
        WaitWorker(worker, 5000, silent);
        Require(workerError == null && silent.State == ConversionState.Failed, "静默进程可以取消并结束工作线程");
        Require(cancellation.ElapsedMilliseconds < 2000, "静默进程取消响应小于 2 秒");

        CheckGifFailureCleanup();
        CheckGifPostAction(InputPostConversionAction.Delete);
        CheckGifPostAction(InputPostConversionAction.MoveInArchiveFolder);
    }

    private static void CheckGifFailureCleanup()
    {
        string input = Path.Combine(outputDirectory, "failure-input.bmp");
        using (Bitmap bitmap = new Bitmap(imageInput))
        {
            bitmap.Save(input, ImageFormat.Bmp);
        }

        ConversionPreset preset = new ConversionPreset("GIF 失败清理", OutputType.Gif, "bmp");
        // 使用无效帧率触发真正的编码失败，验证前一阶段生成的中间图会被清理。
        preset.SetSettingsValue(ConversionPreset.ConversionSettingKeys.VideoFramesPerSecond, "-1");
        ConversionJob job = ConversionJobFactory.Create(preset, input);
        job.PrepareConversion(Path.Combine(outputDirectory, "failure-image.gif"));
        string intermediate = (string)typeof(ConversionJob_Gif).GetField("intermediateFilePath", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(job);
        RunJob(job);
        Require(job.State == ConversionState.Failed, "GIF 编码错误向父任务传播");
        Require(File.Exists(input) && !File.Exists(intermediate) && !File.Exists(job.OutputFilePath), "GIF 失败保留源图并删除中间文件和输出");
    }

    private static void CheckInPlacePostAction(InputPostConversionAction action)
    {
        string directory = Path.Combine(outputDirectory, "in-place-" + action);
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "original.wav");
        File.Copy(audioInput, input);
        ConversionPreset preset = new ConversionPreset("原地输出回归", OutputType.Wav, "wav");
        preset.InputPostConversionAction = action;
        preset.SetSettingsValue(ConversionPreset.ConversionSettingKeys.EnableFFMPEGCustomCommand, "True");
        preset.SetSettingsValue(ConversionPreset.ConversionSettingKeys.FFMPEGCustomCommand, "--process-fixture rewrite-preserve-time");
        ConversionJob job = new FixtureFfmpegJob(preset, input);
        job.PrepareConversion(input);
        RunJob(job);
        Require(job.State == ConversionState.Done && File.ReadAllBytes(input).SequenceEqual(File.ReadAllBytes(audioInput)), "原地重写成功后跳过 " + action + " 并完整保留输出");
        if (action == InputPostConversionAction.MoveInArchiveFolder)
        {
            Require(!Directory.Exists(Path.Combine(directory, preset.ConversionArchiveFolderName)), "原地输出不会被移入归档目录");
        }
    }

    private static void CheckGifPostAction(InputPostConversionAction action)
    {
        string directory = Path.Combine(outputDirectory, "source-action-" + action);
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "original.png");
        File.Copy(imageInput, input, true);
        ConversionPreset preset = new ConversionPreset("GIF 源文件处理", OutputType.Gif, "png");
        preset.InputPostConversionAction = action;
        ConversionJob job = ConversionJobFactory.Create(preset, input);
        job.PrepareConversion(Path.Combine(directory, "converted.gif"));
        RunJob(job);
        Require(job.State == ConversionState.Done && !File.Exists(input) && File.Exists(job.OutputFilePath), "GIF " + action + " 源文件处理只执行一次");
        if (action == InputPostConversionAction.MoveInArchiveFolder)
        {
            Require(File.Exists(Path.Combine(directory, preset.ConversionArchiveFolderName, "original.png")), "GIF 归档保留原始文件");
        }
    }

    private static ConversionJob CreateFixtureJob(string mode, string outputName)
    {
        ConversionPreset preset = new ConversionPreset("进程回归", OutputType.Wav, "wav");
        preset.SetSettingsValue(ConversionPreset.ConversionSettingKeys.EnableFFMPEGCustomCommand, "True");
        preset.SetSettingsValue(ConversionPreset.ConversionSettingKeys.FFMPEGCustomCommand, "--process-fixture " + mode);
        ConversionJob job = new FixtureFfmpegJob(preset, audioInput);
        job.PrepareConversion(Path.Combine(outputDirectory, outputName));
        return job;
    }

    private static void RunJob(ConversionJob job, int timeoutMilliseconds = 30000)
    {
        if (job.State != ConversionState.Ready)
        {
            throw new Exception("任务准备失败：" + job.ErrorMessage);
        }

        Exception workerError = null;
        Thread worker = new Thread(() =>
        {
            try { job.StartConversion(); }
            catch (Exception exception) { workerError = exception; }
        });
        worker.IsBackground = true;
        worker.Start();
        WaitWorker(worker, timeoutMilliseconds, job);
        if (workerError != null)
        {
            throw new Exception("转换工作线程抛出异常。", workerError);
        }
    }

    private static void WaitWorker(Thread worker, int timeoutMilliseconds, ConversionJob job)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (!worker.Join(5))
        {
            PumpDispatcher();
            if (elapsed.ElapsedMilliseconds > timeoutMilliseconds)
            {
                job.Cancel();
                throw new Exception("转换未在限定时间内结束：" + job.OutputFilePath);
            }
        }
        PumpDispatcher();
    }

    private static void PumpDispatcher()
    {
        DispatcherFrame frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static int RunProcessFixture(string[] arguments, int fixtureIndex)
    {
        string mode = arguments[fixtureIndex + 1];
        string output = arguments[arguments.Length - 1];
        if (mode == "no-output")
        {
            return 0;
        }
        if (mode == "partial-output")
        {
            File.WriteAllText(output, "已完成一个输出");
            return 0;
        }
        if (mode == "rewrite-preserve-time")
        {
            DateTime previousTime = File.GetLastWriteTimeUtc(output);
            byte[] previousContent = File.ReadAllBytes(output);
            File.WriteAllBytes(output, previousContent);
            File.SetLastWriteTimeUtc(output, previousTime);
            return 0;
        }
        if (mode == "silent")
        {
            File.WriteAllText(output + ".started", "已启动");
            Thread.Sleep(120000);
            return 0;
        }

        if (mode == "fail")
        {
            File.WriteAllText(output, "未完成");
            Console.Error.WriteLine("conversion stopped");
            return 9;
        }

        string block = new string('x', 4096);
        for (int index = 0; index < 2560; index++)
        {
            Console.Out.Write(block);
        }
        Console.Out.Flush();
        Console.Error.WriteLine("Duration: 00:00:02.00, start: 0.000000, bitrate: 705 kb/s");
        Console.Error.WriteLine("size= 1kB time=00:00:01.00 bitrate=705.6kbits/s");
        File.WriteAllText(output, "已完成");
        return 0;
    }

    private static void WriteManifest()
    {
        List<string> hashes = new List<string> { "文件\t字节数\tSHA256" };
        foreach (string path in Directory.GetFiles(outputDirectory).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            if (!name.StartsWith("image.", StringComparison.Ordinal) && !name.StartsWith("audio.", StringComparison.Ordinal) && !name.StartsWith("pdf-page-", StringComparison.Ordinal))
            {
                continue;
            }

            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                string hash = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
                hashes.Add(name + "\t" + stream.Length.ToString(CultureInfo.InvariantCulture) + "\t" + hash);
            }
        }
        File.WriteAllLines(Path.Combine(outputDirectory, "hashes.tsv"), hashes, Encoding.UTF8);

        // 原生渲染器可能写入当前时间，额外比较尺寸和像素以区分元数据变化。
        List<string> pixels = new List<string> { "文件\t宽度\t高度\tRGBA8像素SHA256" };
        foreach (string path in Directory.GetFiles(outputDirectory).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            if (!name.StartsWith("image.", StringComparison.Ordinal) && !name.StartsWith("pdf-page-", StringComparison.Ordinal))
            {
                continue;
            }
            using (MagickImage image = new MagickImage(path))
            using (SHA256 algorithm = SHA256.Create())
            {
                image.Depth = 8;
                string hash = BitConverter.ToString(algorithm.ComputeHash(image.ToByteArray(MagickFormat.Rgba))).Replace("-", string.Empty);
                pixels.Add(name + "\t" + image.Width + "\t" + image.Height + "\t" + hash);
            }
        }
        File.WriteAllLines(Path.Combine(outputDirectory, "pixels.tsv"), pixels, Encoding.UTF8);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
        Results.Add(message);
        Console.WriteLine("通过：" + message);
    }

    private sealed class FixtureFfmpegJob : ConversionJob_FFMPEG
    {
        public FixtureFfmpegJob(ConversionPreset preset, string input) : base(preset, input) { }
        protected override string FfmpegPath => Assembly.GetExecutingAssembly().Location;
    }

    private sealed class PdfCountJob : ConversionJob_ImageMagick
    {
        public PdfCountJob(ConversionPreset preset, string input) : base(preset, input) { }
        public int ReadPageCount() => this.GetOutputFilesCount();
    }

    private sealed class ZeroOutputJob : ConversionJob
    {
        public ZeroOutputJob(ConversionPreset preset, string input) : base(preset, input) { }
        protected override int GetOutputFilesCount() => 0;
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        private readonly ISettingsService settings = new TestSettingsService();
        public object GetService(Type serviceType) => serviceType == typeof(ISettingsService) ? this.settings : null;
    }

    private sealed class TestSettingsService : ISettingsService
    {
        public Settings Settings { get; } = new Settings();
        public bool PostInstallationInitialization() => true;
        public void SaveSettings() { }
        public void RevertSettings() { }
    }
}
