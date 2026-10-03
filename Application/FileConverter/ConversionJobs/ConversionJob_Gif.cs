// <copyright file="ConversionJob_Gif.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ConversionJobs
{
    using System;
    using System.IO;

    public class ConversionJob_Gif : ConversionJob
    {
        private string intermediateFilePath = string.Empty;
        private ConversionJob pngConversionJob = null;
        private ConversionJob gifConversionJob = null;

        public ConversionJob_Gif(ConversionPreset conversionPreset, string inputFilePath) : base(conversionPreset, inputFilePath)
        {
        }

        public override void Cancel()
        {
            base.Cancel();

            this.pngConversionJob?.Cancel();
            this.gifConversionJob?.Cancel();
        }

        protected override void Initialize()
        {
            base.Initialize();

            if (this.ConversionPreset == null)
            {
                throw new Exception("转换预设无效。");
            }

            string extension = System.IO.Path.GetExtension(this.InputFilePath);
            extension = extension.ToLowerInvariant().Substring(1, extension.Length - 1);

            string inputFilePath = this.InputFilePath;

            // 非 PNG 图片先转换为 PNG，再交给 FFmpeg 编码。
            if (Helpers.GetExtensionCategory(extension) == Helpers.InputCategoryNames.Image && extension != "png")
            {
                // 生成临时中间文件路径。
                string fileName = Path.GetFileName(this.OutputFilePath);
                string tempPath = Path.GetTempPath();
                this.intermediateFilePath = PathHelpers.GenerateUniquePath(tempPath + fileName + ".png");

                // 将输入转换为 FFmpeg 能读取的 PNG。
                ConversionPreset intermediatePreset = new ConversionPreset("To compatible image", OutputType.Png, this.ConversionPreset.InputTypes.ToArray());
                this.pngConversionJob = ConversionJobFactory.Create(intermediatePreset, this.InputFilePath);
                this.pngConversionJob.PrepareConversion(this.intermediateFilePath);

                inputFilePath = this.intermediateFilePath;
            }

            // 将输入编码为 GIF。
            ConversionPreset encodingPreset = new ConversionPreset("Gif encoding", this.ConversionPreset);
            this.gifConversionJob = new ConversionJob_FFMPEG(encodingPreset, inputFilePath);
            this.gifConversionJob.PrepareConversion(this.OutputFilePath);
        }

        protected override void Convert()
        {
            if (this.ConversionPreset == null)
            {
                throw new Exception("转换预设无效。");
            }

            if (this.pngConversionJob != null)
            {
                this.UserState = Properties.Resources.ConversionStateReadIntputImage;

                Diagnostics.Debug.Log(string.Empty);
                Diagnostics.Debug.Log("将图片转换为 PNG 中间格式。");
                if (!this.StartChildConversion(this.pngConversionJob, false))
                {
                    return;
                }
            }

            Diagnostics.Debug.Log(string.Empty);
            Diagnostics.Debug.Log("将 PNG 中间图片编码为 GIF。");
            this.StartChildConversion(this.gifConversionJob);
        }

        protected override void ReleaseResources()
        {
            DeleteIntermediateFile(this.intermediateFilePath);
        }
    }
}
