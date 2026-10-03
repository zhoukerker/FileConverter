// <copyright file="ConversionJob_ImageMagick.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ConversionJobs
{
    using System;

    using FileConverter.Diagnostics;
    using ImageMagick;

    public class ConversionJob_ImageMagick : ConversionJob
    {
        private const float BaseDpiForPdfConversion = 200f;
        private const int PdfSuperSamplingRatio = 1;

        private bool isInputFilePdf;
        private int pageCount;

        public ConversionJob_ImageMagick() : base()
        {
        }

        public ConversionJob_ImageMagick(ConversionPreset conversionPreset, string inputFilePath) : base(conversionPreset, inputFilePath)
        {
        }

        protected override void Initialize()
        {
            base.Initialize();

            string applicationDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            MagickNET.SetGhostscriptDirectory(applicationDirectory);

            this.isInputFilePdf = string.Equals(System.IO.Path.GetExtension(this.InputFilePath), ".pdf", StringComparison.OrdinalIgnoreCase);

            if (this.ConversionPreset == null)
            {
                throw new Exception("转换预设无效。");
            }
        }

        protected override int GetOutputFilesCount()
        {
            if (string.Equals(System.IO.Path.GetExtension(this.InputFilePath), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                string applicationDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                MagickNET.SetGhostscriptDirectory(applicationDirectory);

                using (MagickImageCollection images = new MagickImageCollection())
                {
                    MagickReadSettings settings = new MagickReadSettings();
                    settings.Density = new Density(1, 1);
                    // 页数统计只读取元数据，不为每一页保留完整像素。
                    images.Ping(this.InputFilePath, settings);

                    return images.Count;
                }
            }

            return 1;
        }

        protected override void Convert()
        {
            if (this.ConversionPreset == null)
            {
                throw new Exception("转换预设无效。");
            }

            this.CurrentOutputFilePathIndex = 0;

            if (this.isInputFilePdf)
            {
                this.ConvertPdf();
            }
            else
            {
                this.pageCount = 1;
                MagickReadSettings readSettings = new MagickReadSettings();

                string inputExtension = System.IO.Path.GetExtension(this.InputFilePath).ToLowerInvariant();
                switch (inputExtension)
                {
                    case ".avif":
                        // 显式指定 AVIF 格式，确保正确读取。
                        readSettings.Format = MagickFormat.Avif;
                        break;

                    case ".cr2":
                        // 显式指定图片格式，避免被误识别为 TIFF。
                        readSettings.Format = MagickFormat.Cr2;
                        break;

                    case ".dng":
                        // 显式指定图片格式，避免被误识别为 TIFF。
                        readSettings.Format = MagickFormat.Dng;
                        break;

                    case ".gif":
                        // 转换时读取 GIF 第一帧。
                        // 后续可考虑让用户选择需要转换的帧。
                        readSettings.FrameIndex = 0;
                        break;

                    default:
                        break;
                }

                using (MagickImage image = new MagickImage(this.InputFilePath, readSettings))
                {
                    Debug.Log($"成功加载图片：{this.InputFilePath}。");
                    this.ConvertImage(image);
                }
            }
        }

        private void ConvertPdf()
        {
            MagickReadSettings settings = new MagickReadSettings();

            float dpi = BaseDpiForPdfConversion;
            float scaleFactor = this.ConversionPreset.GetSettingsValue<float>(ConversionPreset.ConversionSettingKeys.ImageScale);
            if (Math.Abs(scaleFactor - 1f) >= 0.005f)
            {
                Debug.Log($"应用缩放比例：{scaleFactor * 100}%。");

                dpi *= scaleFactor;
            }

            Debug.Log($"像素密度：{dpi} dpi。");
            settings.Density = new Density(dpi * PdfSuperSamplingRatio);

            this.UserState = Properties.Resources.ConversionStateReadDocument;

            using (MagickImageCollection images = new MagickImageCollection())
            {
                // 一次读取全部 PDF 页面，沿用现有渲染流程。
                images.Read(this.InputFilePath, settings);
                Debug.Log($"成功加载 PDF：{this.InputFilePath}。");

                this.pageCount = images.Count;

                this.UserState = Properties.Resources.ConversionStateConversion;

                foreach (MagickImage image in images)
                {
                    if (this.CancelIsRequested || this.State == ConversionState.Failed)
                    {
                        return;
                    }

                    Debug.Log($"写入第 {this.CurrentOutputFilePathIndex + 1}/{this.pageCount} 页。");

                    if (PdfSuperSamplingRatio > 1)
                    {
#pragma warning disable CS0162 // Unreachable code detected
                        image.Scale(new Percentage(100 / PdfSuperSamplingRatio));
#pragma warning restore CS0162 // Unreachable code detected
                    }

                    this.ConvertImage(image, true);

                    this.CurrentOutputFilePathIndex++;
                }
            }
        }

        private void ConvertImage(MagickImage image, bool ignoreScale = false)
        {
            image.Progress += this.Image_Progress;

            try
            {

                if (!ignoreScale && this.ConversionPreset.IsRelevantSetting(ConversionPreset.ConversionSettingKeys.ImageScale))
                {
                    float scaleFactor = this.ConversionPreset.GetSettingsValue<float>(ConversionPreset.ConversionSettingKeys.ImageScale);
                    if (Math.Abs(scaleFactor - 1f) >= 0.005f)
                    {
                        Debug.Log($"应用缩放比例：{scaleFactor * 100}%。");

                        image.Scale(new Percentage(scaleFactor * 100f));
                    }
                }

                if (this.ConversionPreset.IsRelevantSetting(ConversionPreset.ConversionSettingKeys.ImageRotation))
                {
                    float rotateAngleInDegrees = this.ConversionPreset.GetSettingsValue<float>(ConversionPreset.ConversionSettingKeys.ImageRotation);
                    if (Math.Abs(rotateAngleInDegrees - 0f) >= 0.05f)
                    {
                        Debug.Log($"应用旋转角度：{rotateAngleInDegrees}°。");

                        image.Rotate(rotateAngleInDegrees);
                    }
                }

                if (this.ConversionPreset.IsRelevantSetting(ConversionPreset.ConversionSettingKeys.ImageClampSizePowerOf2))
                {
                    bool clampSizeToPowerOf2 = this.ConversionPreset.GetSettingsValue<bool>(ConversionPreset.ConversionSettingKeys.ImageClampSizePowerOf2);
                    if (clampSizeToPowerOf2)
                    {
                        uint referenceSize = System.Math.Min(image.Width, image.Height);
                        uint size = 2;
                        while (size * 2 <= referenceSize)
                        {
                            size *= 2;
                        }

                        Debug.Log($"将图片边长调整为最接近的 2 的幂：{image.Width}×{image.Height} → {size}×{size}。");

                        image.Scale(size, size);
                    }
                }

                if (this.ConversionPreset.IsRelevantSetting(ConversionPreset.ConversionSettingKeys.ImageMaximumSize))
                {
                    uint maximumSize = this.ConversionPreset.GetSettingsValue<uint>(ConversionPreset.ConversionSettingKeys.ImageMaximumSize);
                    if (maximumSize > 0)
                    {
                        uint width = System.Math.Min(image.Width, maximumSize);
                        uint height = System.Math.Min(image.Height, maximumSize);

                        Debug.Log($"将图片限制在 {width}×{width} 内：{image.Width}×{image.Height} → {width}×{height}。");

                        image.Scale(width, height);
                    }
                }

                Debug.Log($"转换图片，输出路径：{this.OutputFilePath}。");
                switch (this.ConversionPreset.OutputType)
                {
                    case OutputType.Avif:
                        image.Quality = this.ConversionPreset.GetSettingsValue<uint>(ConversionPreset.ConversionSettingKeys.ImageQuality);
                        break;

                    case OutputType.Png:
                        // http://stackoverflow.com/questions/27267073/imagemagick-lossless-max-compression-for-png
                        image.Quality = 95;
                        break;

                    case OutputType.Jpg:
                        image.Quality = this.ConversionPreset.GetSettingsValue<uint>(ConversionPreset.ConversionSettingKeys.ImageQuality);
                        break;

                    case OutputType.Pdf:
                        Debug.Log($"像素密度：{BaseDpiForPdfConversion} dpi。");
                        image.Density = new Density(BaseDpiForPdfConversion);
                        break;

                    case OutputType.Webp:
                        image.Quality = this.ConversionPreset.GetSettingsValue<uint>(ConversionPreset.ConversionSettingKeys.ImageQuality);
                        break;

                    default:
                        this.ConversionFailed(string.Format(Properties.Resources.ErrorUnsupportedOutputFormat, this.ConversionPreset.OutputType));
                        return;
                }

                image.Write(this.OutputFilePath);
            }
            finally
            {
                image.Progress -= this.Image_Progress;
            }
        }

        private void Image_Progress(object sender, ProgressEventArgs eventArgs)
        {
            if (this.CancelIsRequested)
            {
                eventArgs.Cancel = true;
                return;
            }

            float alreadyCompletedPages = this.CurrentOutputFilePathIndex / (float)this.pageCount;
            this.Progress = alreadyCompletedPages + ((float)eventArgs.Progress.ToDouble() / (100f * this.pageCount));
        }
    }
}
