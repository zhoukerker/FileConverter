// <copyright file="ConversionJob_FFMPEG.Converters.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ConversionJobs
{
    using System;
    using System.Globalization;

    using FileConverter.Controls;

    public partial class ConversionJob_FFMPEG
    {
        private static string Encapsulate(string optionName, string args) =>
            string.IsNullOrEmpty(args) ? string.Empty : $"{optionName} \"{args}\"";

        /// <summary>
        /// 生成调整音频声道数所需的参数。
        /// </summary>
        /// <param name="conversionPreset">转换预设。</param>
        /// <returns>声道设置对应的参数字符串。</returns>
        /// https://trac.ffmpeg.org/wiki/AudioChannelManipulation
        private static string ComputeAudioChannelArgs(ConversionPreset conversionPreset)
        {
            int channelCount = conversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.AudioChannelCount);
            return channelCount > 0 ? $"-ac {channelCount}" : string.Empty;
        }

        private static string ComputeTransformArgs(ConversionPreset conversionPreset, Helpers.HardwareAccelerationMode hwAccel = Helpers.HardwareAccelerationMode.Off)
        {
            float scaleFactor = conversionPreset.GetSettingsValue<float>(ConversionPreset.ConversionSettingKeys.VideoScale);
            string scaleArgs = string.Empty;

            if (conversionPreset.OutputType == OutputType.Mkv || conversionPreset.OutputType == OutputType.Mp4)
            {
                // 这些预设使用 H.264，视频宽高必须能被 2 整除。
                switch (hwAccel)
                {
                    case Helpers.HardwareAccelerationMode.CUDA:
                        scaleArgs = string.Format("scale_cuda=trunc(iw*{0}/2)*2:trunc(ih*{0}/2)*2:format=yuv420p", scaleFactor.ToString("#.##", CultureInfo.InvariantCulture));
                        break;
                    default:
                        scaleArgs = string.Format("scale=trunc(iw*{0}/2)*2:trunc(ih*{0}/2)*2", scaleFactor.ToString("#.##", CultureInfo.InvariantCulture));
                        break;
                }
            }
            else if (Math.Abs(scaleFactor - 1f) >= 0.005f)
            {
                scaleArgs = string.Format("scale=iw*{0}:ih*{0}", scaleFactor.ToString("#.##", CultureInfo.InvariantCulture));
            }

            float rotationAngleInDegrees = conversionPreset.GetSettingsValue<float>(ConversionPreset.ConversionSettingKeys.VideoRotation);
            string rotationArgs = string.Empty;
            if (Math.Abs(rotationAngleInDegrees - 0f) >= 0.05f)
            {
                // 转置参数：
                // 0：逆时针旋转 90 度并垂直翻转
                // 1：顺时针旋转 90 度
                // 2：逆时针旋转 90 度
                // 3：顺时针旋转 90 度并垂直翻转
                if (Math.Abs(rotationAngleInDegrees - 90f) <= 0.05f)
                {
                    rotationArgs = "transpose=2";
                }
                else if (Math.Abs(rotationAngleInDegrees - 180f) <= 0.05f)
                {
                    rotationArgs = "vflip,hflip";
                }
                else if (Math.Abs(rotationAngleInDegrees - 270f) <= 0.05f)
                {
                    rotationArgs = "transpose=1";
                }
                else
                {
                    Diagnostics.Debug.LogError($"不支持的旋转角度：{rotationAngleInDegrees}°");
                }
            }

            // 组合视频滤镜参数（scale=...、transpose=...）。
            string transformArgs = scaleArgs;
            if (!string.IsNullOrEmpty(rotationArgs))
            {
                transformArgs = string.IsNullOrEmpty(scaleArgs) ? rotationArgs : scaleArgs + "," + rotationArgs;
            }

            if (hwAccel != Helpers.HardwareAccelerationMode.CUDA && (conversionPreset.OutputType == OutputType.Mkv || conversionPreset.OutputType == OutputType.Mp4))
            {
                // MP4/MKV 的 H.264 输出统一使用 yuv420p，提高播放器兼容性：
                // http://trac.ffmpeg.org/wiki/Encode/H.264#Encodingfordumbplayers
                // 软件编码以及 AMF 等非 CUDA 硬件编码使用此路径。
                // CUDA 的 scale_cuda 路径已经设置 format=yuv420p，此处无需重复。
                transformArgs += (transformArgs.Length > 0 ? "," : string.Empty) + "format=yuv420p";
                // TODO：可考虑为像素格式提供设置项。
            }

            return transformArgs;
        }

        /// <summary>
        /// 将码率转换为 <c>mp3</c> 编码器的质量索引。
        /// </summary>
        /// <param name="bitrate">目标码率。</param>
        /// <returns>指定码率对应的 <c>mp3</c> 编码器质量索引。</returns>
        /// https://trac.ffmpeg.org/wiki/Encode/MP3
        private int MP3VBRBitrateToQualityIndex(int bitrate)
        {
            switch (bitrate)
            {
                case 245:
                    return 0;

                case 225:
                    return 1;

                case 190:
                    return 2;

                case 175:
                    return 3;

                case 165:
                    return 4;

                case 130:
                    return 5;

                case 115:
                    return 6;

                case 100:
                    return 7;

                case 85:
                    return 8;

                case 65:
                    return 9;
            }

            throw new Exception("未知的 VBR 码率。");
        }

        /// <summary>
        /// 将码率转换为 <c>vorbis</c> 编码器的质量索引。
        /// </summary>
        /// <param name="bitrate">目标码率。</param>
        /// <returns>指定码率对应的 <c>vorbis</c> 编码器质量索引。</returns>
        /// http://wiki.hydrogenaud.io/index.php?title=Recommended_Ogg_Vorbis
        private int OGGVBRBitrateToQualityIndex(int bitrate)
        {
            switch (bitrate)
            {
                case 500:
                    return 10;

                case 320:
                    return 9;

                case 256:
                    return 8;

                case 224:
                    return 7;

                case 192:
                    return 6;

                case 160:
                    return 5;

                case 128:
                    return 4;

                case 112:
                    return 3;

                case 96:
                    return 2;

                case 80:
                    return 1;

                case 64:
                    return 0;

                case 48:
                    return -1;

                case 32:
                    return -2;
            }

            throw new Exception("未知的 Ogg VBR 码率。");
        }

        /// <summary>
        /// 将视频质量索引转换为 <c>theora</c> 编码器的质量等级。
        /// </summary>
        /// <param name="quality">质量索引。</param>
        /// <returns>视频编码质量索引。</returns>
        /// 视频质量等级为 0 到 10；10 的质量和文件体积最高，0 最低。
        /// https://trac.ffmpeg.org/wiki/TheoraVorbisEncodingGuide
        private int OGVTheoraQualityToQualityIndex(int quality) => quality;

        /// <summary>
        /// 将音频编码模式转换为 <c>ffmpeg</c> 编解码器参数。
        /// </summary>
        /// <param name="encoding">音频编码模式。</param>
        /// <returns>指定编码模式对应的 <c>ffmpeg</c> 参数。</returns>
        /// https://trac.ffmpeg.org/wiki/audio%20types
        private string WAVEncodingToCodecArgument(EncodingMode encoding)
        {
            switch (encoding)
            {
                case EncodingMode.Wav8:
                    return "pcm_s8le";

                case EncodingMode.Wav16:
                    return "pcm_s16le";

                case EncodingMode.Wav24:
                    return "pcm_s24le";

                case EncodingMode.Wav32:
                    return "pcm_s32le";
            }

            throw new Exception("未知的 WAV 编码模式。");
        }

        /// <summary>
        /// 将视频质量索引转换为 MPEG4 的量化等级。
        /// </summary>
        /// <param name="quality">质量索引。</param>
        /// <returns>MPEG4 编码器使用的量化等级。</returns>
        /// 视频量化等级为 1 到 31；1 的质量和文件体积最高，31 最低。
        /// https://trac.ffmpeg.org/wiki/Encode/MPEG-4
        private int MPEG4QualityToQualityIndex(int quality) => 31 - quality;

        /// <summary>
        /// 将视频质量索引转换为 H.264 的恒定质量参数。
        /// </summary>
        /// <param name="quality">质量索引。</param>
        /// <returns>编码器使用的恒定质量参数。</returns>
        /// 量化范围为 0 到 51；0 表示无损，23 为默认值，51 的质量最低。
        /// 数值越小质量越高，通常使用 18 到 28。
        /// https://trac.ffmpeg.org/wiki/Encode/H.264
        private int H264QualityToCRF(int quality) => 51 - quality;

        /// <summary>
        /// 将图片质量索引转换为 JPG 的量化等级。
        /// </summary>
        /// <param name="quality">质量索引。</param>
        /// <returns>JPG 编码器使用的质量索引。</returns>
        /// 量化范围为 1 到 31；1 的质量最高，31 最低。
        /// http://superuser.com/questions/318845/improve-quality-of-ffmpeg-created-jpgs
        private int JPGQualityToQualityIndex(int quality) => 31 - quality;

        /// <summary>
        /// 将视频编码速度转换为 H.264 预设。
        /// </summary>
        /// <param name="encodingSpeed">视频编码速度。</param>
        /// <returns>H.264 编码器预设名称。</returns>
        private string H264EncodingSpeedToPreset(VideoEncodingSpeed encodingSpeed)
        {
            switch (encodingSpeed)
            {
                case VideoEncodingSpeed.UltraFast:
                    return "ultrafast";

                case VideoEncodingSpeed.SuperFast:
                    return "superfast";

                case VideoEncodingSpeed.VeryFast:
                    return "veryfast";

                case VideoEncodingSpeed.Faster:
                    return "faster";

                case VideoEncodingSpeed.Fast:
                    return "fast";

                case VideoEncodingSpeed.Medium:
                    return "medium";

                case VideoEncodingSpeed.Slow:
                    return "slow";

                case VideoEncodingSpeed.Slower:
                    return "slower";

                case VideoEncodingSpeed.VerySlow:
                    return "veryslow";
            }

            throw new ArgumentOutOfRangeException(nameof(encodingSpeed), encodingSpeed, "未知的 H.264 编码速度。");
        }

        /// <summary>
        /// 将视频编码速度转换为 NVENC 预设。
        /// </summary>
        /// <param name="encodingSpeed">视频编码速度。</param>
        /// <returns>NVENC 编码器预设名称。</returns>
        private string H264EncodingSpeedToNVENCPreset(VideoEncodingSpeed encodingSpeed)
        {
            switch (encodingSpeed)
            {
                case VideoEncodingSpeed.UltraFast:
                    return "p1";

                case VideoEncodingSpeed.SuperFast:
                    return "p2";

                case VideoEncodingSpeed.VeryFast:
                    return "p3";

                case VideoEncodingSpeed.Faster:
                case VideoEncodingSpeed.Fast:
                case VideoEncodingSpeed.Medium:
                    return "p4";

                case VideoEncodingSpeed.Slow:
                    return "p5";

                case VideoEncodingSpeed.Slower:
                    return "p6";

                case VideoEncodingSpeed.VerySlow:
                    return "p7";
            }

            throw new ArgumentOutOfRangeException(nameof(encodingSpeed), encodingSpeed, "未知的 H.264 编码速度。");
        }

        /// <summary>
        /// 将视频编码速度转换为 AMF 质量模式。
        /// </summary>
        /// <param name="encodingSpeed">视频编码速度。</param>
        /// <returns>AMF 编码器质量模式。</returns>
        private string H264EncodingSpeedToAMFQuality(VideoEncodingSpeed encodingSpeed)
        {
            switch (encodingSpeed)
            {
                case VideoEncodingSpeed.UltraFast:
                case VideoEncodingSpeed.SuperFast:
                case VideoEncodingSpeed.VeryFast:
                case VideoEncodingSpeed.Faster:
                case VideoEncodingSpeed.Fast:
                    return "speed";

                case VideoEncodingSpeed.Medium:
                case VideoEncodingSpeed.Slow:
                    return "balanced";

                case VideoEncodingSpeed.Slower:
                case VideoEncodingSpeed.VerySlow:
                    return "quality";
            }

            throw new ArgumentOutOfRangeException(nameof(encodingSpeed), encodingSpeed, "未知的 H.264 编码速度。");
        }

        /// <summary>
        /// 将码率转换为 <c>aac</c> 编码器的质量索引。
        /// </summary>
        /// <param name="bitrate">目标码率。</param>
        /// <returns>指定码率对应的 <c>aac</c> 编码器质量索引。</returns>
        /// 详细映射见 Resources/FFMPEG retro engineering.ods。
        private string AACBitrateToQualityIndex(int bitrate)
        {
            switch (bitrate)
            {
                case 460:
                    return "3.9";

                case 340:
                    return "3";

                case 256:
                    return "2.2";

                case 224:
                    return "1.9";

                case 192:
                    return "1.6";

                case 155:
                    return "1.3";

                case 128:
                    return "1";

                case 112:
                    return "0.9";

                case 96:
                    return "0.75";

                case 80:
                    return "0.6";

                case 64:
                    return "0.45";

                case 48:
                    return "0.3";

                case 32:
                    return "0.2";

                case 16:
                    return "0.1";
            }

            throw new Exception("未知的 VBR 码率。");
        }

        /// <summary>
        /// 将视频质量索引转换为 VP9 的恒定质量参数。
        /// </summary>
        /// <param name="quality">质量索引。</param>
        /// <returns>编码器使用的恒定质量参数。</returns>
        /// 量化范围为 0 到 63；数值越小质量越高。
        /// https://trac.ffmpeg.org/wiki/Encode/VP9
        private int WebmQualityToCRF(int quality) => 63 - quality;
    }
}
