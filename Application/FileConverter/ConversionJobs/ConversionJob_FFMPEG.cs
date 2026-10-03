// <copyright file="ConversionJob_FFMPEG.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ConversionJobs
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using CommunityToolkit.Mvvm.DependencyInjection;
    using FileConverter.Controls;
    using FileConverter.Services;

    public partial class ConversionJob_FFMPEG : ConversionJob
    {
        private static readonly Regex DurationRegex = new Regex(@"Duration:\s*([0-9][0-9]):([0-9][0-9]):([0-9][0-9])\.([0-9][0-9]),.*bitrate:\s*([0-9]+) kb\/s", RegexOptions.CultureInvariant);
        private static readonly Regex ProgressRegex = new Regex(@"size=\s*([0-9]+).*time=([0-9][0-9]):([0-9][0-9]):([0-9][0-9]).([0-9][0-9])\s+bitrate=\s*([0-9]+.[0-9])", RegexOptions.CultureInvariant);

        private TimeSpan fileDuration;
        private readonly object processSync = new object();
        private Process activeProcess;

        private ProcessStartInfo ffmpegProcessStartInfo;

        private readonly List<FFMpegPass> ffmpegArgumentStringByPass = new List<FFMpegPass>();

        private readonly ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();

        public ConversionJob_FFMPEG() : base()
        {
        }

        public ConversionJob_FFMPEG(ConversionPreset conversionPreset, string inputFilePath) : base(conversionPreset, inputFilePath)
        {
        }

        public static VideoEncodingSpeed[] VideoEncodingSpeeds => new[]
           {
               VideoEncodingSpeed.UltraFast,
               VideoEncodingSpeed.SuperFast,
               VideoEncodingSpeed.VeryFast,
               VideoEncodingSpeed.Faster,
               VideoEncodingSpeed.Fast,
               VideoEncodingSpeed.Medium,
               VideoEncodingSpeed.Slow,
               VideoEncodingSpeed.Slower,
               VideoEncodingSpeed.VerySlow,
           };

        protected virtual string FfmpegPath
        {
            get
            {
                string applicationDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                return System.IO.Path.Combine(applicationDirectory, "ffmpeg.exe");
            }
        }

        public override void Cancel()
        {
            base.Cancel();

            if (this.CancelIsRequested)
            {
                lock (this.processSync)
                {
                    this.StopActiveProcess();
                }
            }
        }

        protected override void Initialize()
        {
            base.Initialize();

            if (this.ConversionPreset == null)
            {
                throw new Exception("转换预设无效。");
            }

            this.ffmpegProcessStartInfo = null;
            this.ffmpegArgumentStringByPass.Clear();
            this.fileDuration = TimeSpan.Zero;

            string ffmpegPath = this.FfmpegPath;
            if (!System.IO.File.Exists(ffmpegPath))
            {
                this.ConversionFailed(Properties.Resources.ErrorCantFindFFMPEG);
                Diagnostics.Debug.Log($"找不到 FFmpeg 可执行文件（{ffmpegPath}），请尝试重新安装程序。");
                return;
            }

            this.ffmpegProcessStartInfo = new ProcessStartInfo(ffmpegPath)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            this.FillFFMpegArgumentsList();
        }

        protected virtual void FillFFMpegArgumentsList()
        {
            const string baseArgs = "-n -progress pipe:1";

            bool customCommandEnabled = this.ConversionPreset.GetSettingsValue<bool>(ConversionPreset.ConversionSettingKeys.EnableFFMPEGCustomCommand);
            if (customCommandEnabled)
            {
                // 自定义命令优先于其他转换设置。
                string customCommand = this.ConversionPreset.GetSettingsValue<string>(ConversionPreset.ConversionSettingKeys.FFMPEGCustomCommand) ?? string.Empty;

                string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {customCommand} \"{this.OutputFilePath}\"";
                this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));

                return;
            }

            // 在 Windows 上读取元数据需要此选项，来源： http://jonhall.info/how_to/create_id3_tags_using_ffmpeg
            const string MP3MetadataArgs = "-id3v2_version 3 -write_id3v1 1";

            // AAC 没有标准标签格式，使用兼容的 ApeV2，来源： http://eolindel.free.fr/foobar/tags.shtml
            const string AACMetadataArgs = "-write_apetag 1";

            switch (this.ConversionPreset.OutputType)
            {
                case OutputType.Aac:
                    {
                        string channelArgs = ConversionJob_FFMPEG.ComputeAudioChannelArgs(this.ConversionPreset);

                        // https://trac.ffmpeg.org/wiki/Encode/AAC
                        int audioEncodingBitrate = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.AudioBitrate);
                        string encoderArgs = $"-c:a aac -q:a {this.AACBitrateToQualityIndex(audioEncodingBitrate)} {channelArgs} {AACMetadataArgs}";

                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Avi:
                    {
                        // https://trac.ffmpeg.org/wiki/Encode/MPEG-4
                        int videoEncodingQuality = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.VideoQuality);
                        int audioEncodingBitrate = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.AudioBitrate);

                        string transformArgs = ConversionJob_FFMPEG.ComputeTransformArgs(this.ConversionPreset);

                        string audioArgs = "-an";
                        if (this.ConversionPreset.GetSettingsValue<bool>(ConversionPreset.ConversionSettingKeys.EnableAudio))
                        {
                            audioArgs = $"-c:a libmp3lame -qscale:a {this.MP3VBRBitrateToQualityIndex(audioEncodingBitrate)}";
                        }

                        // 汇总最终参数。
                        string videoFilteringArgs = ConversionJob_FFMPEG.Encapsulate("-vf", transformArgs);
                        string encoderArgs = $"-c:v mpeg4 -vtag xvid -qscale:v {this.MPEG4QualityToQualityIndex(videoEncodingQuality)} {audioArgs} {videoFilteringArgs} {MP3MetadataArgs}";
                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Flac:
                    {
                        string channelArgs = ConversionJob_FFMPEG.ComputeAudioChannelArgs(this.ConversionPreset);

                        // http://taer-naguur.blogspot.fr/2013/11/flac-audio-encoding-with-ffmpeg.html
                        string encoderArgs = $"-compression_level 12 {channelArgs}";
                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Gif:
                    {
                        // http://blog.pkh.me/p/21-high-quality-gif-with-ffmpeg.html
                        string fileName = Path.GetFileName(this.InputFilePath);
                        string tempPath = Path.GetTempPath();
                        string paletteFilePath = PathHelpers.GenerateUniquePath(tempPath + fileName + " - palette.png");

                        string transformArgs = ConversionJob_FFMPEG.ComputeTransformArgs(this.ConversionPreset);

                        // 帧率。
                        int framesPerSecond = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.VideoFramesPerSecond);
                        if (!string.IsNullOrEmpty(transformArgs))
                        {
                            transformArgs += ",";
                        }

                        transformArgs += $"fps={framesPerSecond}";

                        // 生成调色板。
                        string encoderArgs = $"-vf \"{transformArgs},palettegen\"";
                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{paletteFilePath}\"";
                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass("Indexing colors", arguments, paletteFilePath));

                        // 生成 GIF。
                        encoderArgs = $"-i \"{paletteFilePath}\" -lavfi \"{transformArgs},paletteuse\"";
                        arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";
                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Ico:
                    {
                        string encoderArgs = string.Empty;
                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Jpg:
                    {
                        int encodingQuality = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.ImageQuality);

                        float scaleFactor = this.ConversionPreset.GetSettingsValue<float>(ConversionPreset.ConversionSettingKeys.ImageScale);
                        string scaleArgs = string.Empty;
                        if (Math.Abs(scaleFactor - 1f) >= 0.005f)
                        {
                            scaleArgs = $"-vf scale=iw*{scaleFactor.ToString("#.##", CultureInfo.InvariantCulture)}:ih*{scaleFactor.ToString("#.##", CultureInfo.InvariantCulture)}";
                        }

                        string encoderArgs = $"-q:v {this.JPGQualityToQualityIndex(encodingQuality)} {scaleArgs}";

                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Mp3:
                    {
                        string channelArgs = ConversionJob_FFMPEG.ComputeAudioChannelArgs(this.ConversionPreset);

                        string encoderArgs = string.Empty;
                        EncodingMode encodingMode = this.ConversionPreset.GetSettingsValue<EncodingMode>(ConversionPreset.ConversionSettingKeys.AudioEncodingMode);
                        int encodingQuality = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.AudioBitrate);
                        switch (encodingMode)
                        {
                            case EncodingMode.Mp3VBR:
                                encoderArgs = $"-codec:a libmp3lame -q:a {this.MP3VBRBitrateToQualityIndex(encodingQuality)} {channelArgs} {MP3MetadataArgs}";
                                break;

                            case EncodingMode.Mp3CBR:
                                encoderArgs = $"-codec:a libmp3lame -b:a {encodingQuality}k {channelArgs} {MP3MetadataArgs}";
                                break;

                            default:
                                break;
                        }

                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Mkv:
                case OutputType.Mp4:
                    {
                        // https://trac.ffmpeg.org/wiki/Encode/H.264
                        // https://trac.ffmpeg.org/wiki/Encode/AAC
                        int videoEncodingQuality = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.VideoQuality);
                        VideoEncodingSpeed videoEncodingSpeed = this.ConversionPreset.GetSettingsValue<VideoEncodingSpeed>(ConversionPreset.ConversionSettingKeys.VideoEncodingSpeed);
                        int audioEncodingBitrate = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.AudioBitrate);

                        Helpers.HardwareAccelerationMode hwAccel = settingsService.Settings.HardwareAccelerationMode;

                        string transformArgs = ConversionJob_FFMPEG.ComputeTransformArgs(this.ConversionPreset, hwAccel);
                        string videoFilteringArgs = ConversionJob_FFMPEG.Encapsulate("-vf", transformArgs);

                        string audioArgs = "-an";
                        if (this.ConversionPreset.GetSettingsValue<bool>(ConversionPreset.ConversionSettingKeys.EnableAudio))
                        {
                            audioArgs = $"-c:a aac -qscale:a {this.AACBitrateToQualityIndex(audioEncodingBitrate)}";
                        }

                        string videoCodec = "libx264";
                        string videoCodecArgs = $"-preset {this.H264EncodingSpeedToPreset(videoEncodingSpeed)} -crf {this.H264QualityToCRF(videoEncodingQuality)}";
                        string hwAccelArg = string.Empty;

                        switch (hwAccel)
                        {
                            case Helpers.HardwareAccelerationMode.CUDA:
                                videoCodec = "h264_nvenc";
                                int nvencQP = this.H264QualityToCRF(videoEncodingQuality);
                                videoCodecArgs = $"-preset {this.H264EncodingSpeedToNVENCPreset(videoEncodingSpeed)} -rc constqp -qp {nvencQP}";

                                hwAccelArg = "-hwaccel cuda -hwaccel_output_format cuda";
                                break;

                            case Helpers.HardwareAccelerationMode.AMF:
                                int amfQP = this.H264QualityToCRF(videoEncodingQuality);
                                int amfBFrameQP = Math.Min(51, amfQP + 2);
                                videoCodec = "h264_amf";
                                videoCodecArgs = $"-usage transcoding -quality {this.H264EncodingSpeedToAMFQuality(videoEncodingSpeed)} -qp_i {amfQP} -qp_p {amfQP} -qp_b {amfBFrameQP}";
                                break;
                        }

                        string encoderArgs = $"-c:v {videoCodec} {videoCodecArgs} {audioArgs} {videoFilteringArgs}";

                        string arguments = $"{baseArgs} {hwAccelArg} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Ogg:
                    {
                        string channelArgs = ConversionJob_FFMPEG.ComputeAudioChannelArgs(this.ConversionPreset);

                        int encodingQuality = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.AudioBitrate);
                        string encoderArgs = $"-vn -codec:a libvorbis -qscale:a {this.OGGVBRBitrateToQualityIndex(encodingQuality)} {channelArgs}";
                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Ogv:
                    {
                        // https://trac.ffmpeg.org/wiki/TheoraVorbisEncodingGuide
                        int videoEncodingQuality = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.VideoQuality);
                        int audioEncodingBitrate = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.AudioBitrate);

                        string transformArgs = ConversionJob_FFMPEG.ComputeTransformArgs(this.ConversionPreset);
                        string videoFilteringArgs = ConversionJob_FFMPEG.Encapsulate("-vf", transformArgs);

                        string audioArgs = "-an";
                        if (this.ConversionPreset.GetSettingsValue<bool>(ConversionPreset.ConversionSettingKeys.EnableAudio))
                        {
                            audioArgs = $"-codec:a libvorbis -qscale:a {this.OGGVBRBitrateToQualityIndex(audioEncodingBitrate)}";
                        }

                        string encoderArgs = $"-codec:v libtheora -qscale:v {this.OGVTheoraQualityToQualityIndex(videoEncodingQuality)} {audioArgs} {videoFilteringArgs}";

                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Png:
                    {
                        float scaleFactor = this.ConversionPreset.GetSettingsValue<float>(ConversionPreset.ConversionSettingKeys.ImageScale);
                        string scaleArgs = string.Empty;
                        if (Math.Abs(scaleFactor - 1f) >= 0.005f)
                        {
                            scaleArgs = $"-vf scale=iw*{scaleFactor.ToString("#.##", CultureInfo.InvariantCulture)}:ih*{scaleFactor.ToString("#.##", CultureInfo.InvariantCulture)}";
                        }

                        // http://www.howtogeek.com/203979/is-the-png-format-lossless-since-it-has-a-compression-parameter/
                        string encoderArgs = $"-compression_level 100 {scaleArgs}";

                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Wav:
                    {
                        string channelArgs = ConversionJob_FFMPEG.ComputeAudioChannelArgs(this.ConversionPreset);

                        EncodingMode encodingMode = this.ConversionPreset.GetSettingsValue<EncodingMode>(ConversionPreset.ConversionSettingKeys.AudioEncodingMode);
                        string encoderArgs = $"-acodec {this.WAVEncodingToCodecArgument(encodingMode)} {channelArgs}";
                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Webm:
                    {
                        // https://trac.ffmpeg.org/wiki/Encode/VP9
                        int videoEncodingQuality = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.VideoQuality);
                        int audioEncodingQuality = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.AudioBitrate);

                        string encodingArgs = string.Empty;
                        if (videoEncodingQuality == 63)
                        {
                            // 最高质量设置使用无损压缩。
                            encodingArgs = $"-lossless 1";
                        }
                        else
                        {
                            encodingArgs = $"-crf {this.WebmQualityToCRF(videoEncodingQuality)} -b:v 0";
                        }

                        string transformArgs = ConversionJob_FFMPEG.ComputeTransformArgs(this.ConversionPreset);
                        string videoFilteringArgs = ConversionJob_FFMPEG.Encapsulate("-vf", transformArgs);

                        string audioArgs = "-an";
                        if (this.ConversionPreset.GetSettingsValue<bool>(ConversionPreset.ConversionSettingKeys.EnableAudio))
                        {
                            audioArgs = $"-c:a libvorbis -qscale:a {this.OGGVBRBitrateToQualityIndex(audioEncodingQuality)}";
                        }

                        string encoderArgs = $"-c:v libvpx-vp9 {encodingArgs} {audioArgs} {videoFilteringArgs}";

                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                default:
                    throw new NotImplementedException("尚未实现此输出类型的转换器：" +
                                                      this.ConversionPreset.OutputType);
            }

            if (this.ffmpegArgumentStringByPass.Count == 0)
            {
                throw new Exception("未生成 FFmpeg 参数。");
            }

            for (int index = 0; index < this.ffmpegArgumentStringByPass.Count; index++)
            {
                if (string.IsNullOrEmpty(this.ffmpegArgumentStringByPass[index].Arguments))
                {
                    throw new Exception("FFmpeg 进程参数无效。");
                }
            }
        }

        protected override void Convert()
        {
            if (this.ConversionPreset == null)
            {
                throw new Exception("转换预设无效。");
            }

            for (int index = 0; index < this.ffmpegArgumentStringByPass.Count; index++)
            {
                if (this.CancelIsRequested || this.State == ConversionState.Failed)
                {
                    return;
                }

                FFMpegPass currentPass = this.ffmpegArgumentStringByPass[index];

                this.UserState = currentPass.Name;
                this.ffmpegProcessStartInfo.Arguments = currentPass.Arguments;

                Diagnostics.Debug.Log($"执行命令：{this.ffmpegProcessStartInfo.FileName} {this.ffmpegProcessStartInfo.Arguments}。");
                Diagnostics.Debug.Log(string.Empty);

                try
                {
                    using (Process exeProcess = Process.Start(this.ffmpegProcessStartInfo))
                    {
                        lock (this.processSync)
                        {
                            this.activeProcess = exeProcess;
                            if (this.CancelIsRequested)
                            {
                                this.StopActiveProcess();
                            }
                        }

                        try
                        {
                            // 同时排空两个输出管道，防止进度输出填满管道后阻塞编码器。
                            Task drainOutput = exeProcess.StandardOutput.BaseStream.CopyToAsync(Stream.Null, 4096);
                            string result;
                            while ((result = exeProcess.StandardError.ReadLine()) != null)
                            {
                                this.ParseFFMPEGOutput(result);
                                Diagnostics.Debug.Log($"FFmpeg 输出：{result}");
                            }

                            exeProcess.WaitForExit();
                            drainOutput.GetAwaiter().GetResult();

                            if (exeProcess.ExitCode != 0)
                            {
                                this.ConversionFailed(Properties.Resources.ErrorFailedToLaunchFFMPEG);
                                return;
                            }
                        }
                        finally
                        {
                            lock (this.processSync)
                            {
                                this.StopActiveProcess();
                                this.activeProcess = null;
                            }
                        }
                    }
                }
                catch
                {
                    this.ConversionFailed(Properties.Resources.ErrorFailedToLaunchFFMPEG);
                    throw;
                }
            }

            Diagnostics.Debug.Log(string.Empty);
        }

        protected override void ReleaseResources()
        {
            for (int index = 0; index < this.ffmpegArgumentStringByPass.Count; index++)
            {
                FFMpegPass currentPass = this.ffmpegArgumentStringByPass[index];

                if (string.IsNullOrEmpty(currentPass.FileToDelete))
                {
                    continue;
                }

                Diagnostics.Debug.Log($"删除临时中间文件：{currentPass.FileToDelete}。");

                DeleteIntermediateFile(currentPass.FileToDelete);
            }
        }

        private void StopActiveProcess()
        {
            if (this.activeProcess == null)
            {
                return;
            }

            try
            {
                if (!this.activeProcess.HasExited)
                {
                    this.activeProcess.Kill();
                }
            }
            catch (InvalidOperationException)
            {
                // 进程可能已经在取消请求到达前退出。
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                Diagnostics.Debug.Log($"无法停止 FFmpeg 进程：{exception}。");
            }
        }

        private void ParseFFMPEGOutput(string input)
        {
            Match match = input.IndexOf("Duration:", StringComparison.Ordinal) >= 0 ? DurationRegex.Match(input) : Match.Empty;
            if (match.Success && match.Groups.Count >= 6)
            {
                int hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                int minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                int seconds = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                int milliseconds = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) * 10;
                this.fileDuration = new TimeSpan(0, hours, minutes, seconds, milliseconds);
                return;
            }

            if (this.fileDuration.Ticks > 0 && input.IndexOf("time=", StringComparison.Ordinal) >= 0)
            {
                match = ProgressRegex.Match(input);
                if (match.Success && match.Groups.Count >= 7)
                {
                    int hours = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                    int minutes = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                    int seconds = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
                    int milliseconds = int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture) * 10;
                    TimeSpan actualConvertedDuration = new TimeSpan(0, hours, minutes, seconds, milliseconds);

                    this.Progress = actualConvertedDuration.Ticks / (float)this.fileDuration.Ticks;
                    return;
                }
            }

            if (input.IndexOf("Exiting.", StringComparison.Ordinal) < 0 &&
                input.IndexOf("Error", StringComparison.Ordinal) < 0 &&
                input.IndexOf("Unsupported dimensions", StringComparison.Ordinal) < 0 &&
                input.IndexOf("No such file or directory", StringComparison.Ordinal) < 0)
            {
                return;
            }

            // 仅对疑似错误的日志移除文件名，避免正常日志产生多余字符串。
            string inputWithoutFileNames = input.Replace(this.InputFilePath, string.Empty).Replace(this.OutputFilePath, string.Empty);

            if (inputWithoutFileNames.Contains("Exiting.") || inputWithoutFileNames.Contains("Error") || inputWithoutFileNames.Contains("Unsupported dimensions") || inputWithoutFileNames.Contains("No such file or directory"))
            {
                if (inputWithoutFileNames.StartsWith("Error while decoding stream") && inputWithoutFileNames.EndsWith("Invalid data found when processing input"))
                {
                    // 传输流开头出现损坏帧属于正常情况。
                    // https://trac.ffmpeg.org/ticket/1622
                }
                else
                {
                    this.ConversionFailed(input);
                }
            }
        }

        private struct FFMpegPass
        {
            public string Name;
            public string Arguments;
            public string FileToDelete;

            public FFMpegPass(string name, string arguments, string fileToDelete)
            {
                this.Name = name;
                this.Arguments = arguments;
                this.FileToDelete = fileToDelete;
            }

            public FFMpegPass(string arguments)
            {
                this.Name = "Conversion";
                this.Arguments = arguments;
                this.FileToDelete = string.Empty;
            }
        }
    }
}
