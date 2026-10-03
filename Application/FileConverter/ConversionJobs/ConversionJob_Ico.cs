// <copyright file="ConversionJob_Ico.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ConversionJobs
{
    using System;
    using System.IO;

    public class ConversionJob_Ico : ConversionJob
    {
        private string intermediateFilePath;
        private ConversionJob pngConversionJob;
        private ConversionJob icoConversionJob;

        public ConversionJob_Ico(ConversionPreset conversionPreset, string inputFilePath) : base(conversionPreset, inputFilePath)
        {
        }

        public override void Cancel()
        {
            base.Cancel();

            this.pngConversionJob?.Cancel();
            this.icoConversionJob?.Cancel();
        }

        protected override void Initialize()
        {
            base.Initialize();

            if (this.ConversionPreset == null)
            {
                throw new Exception("转换预设无效。");
            }

            // 生成临时中间文件路径。
            string fileName = Path.GetFileName(this.OutputFilePath);
            string tempPath = Path.GetTempPath();
            this.intermediateFilePath = PathHelpers.GenerateUniquePath(tempPath + fileName + ".png");

            // 将输入转换为 FFmpeg 能读取的 PNG。
            ConversionPreset intermediatePreset = new ConversionPreset("To compatible image", OutputType.Png, this.ConversionPreset.InputTypes.ToArray());
            intermediatePreset.SetSettingsValue(ConversionPreset.ConversionSettingKeys.ImageClampSizePowerOf2, "True");
            intermediatePreset.SetSettingsValue(ConversionPreset.ConversionSettingKeys.ImageMaximumSize, "256");
            this.pngConversionJob = ConversionJobFactory.Create(intermediatePreset, this.InputFilePath);
            this.pngConversionJob.PrepareConversion(this.intermediateFilePath);

            // 将 PNG 编码为图标。
            ConversionPreset encodingPreset = new ConversionPreset("Ico encoding", this.ConversionPreset, "png");
            this.icoConversionJob = new ConversionJob_FFMPEG(encodingPreset, this.intermediateFilePath);
            this.icoConversionJob.PrepareConversion(this.OutputFilePath);
        }

        protected override void Convert()
        {
            if (this.ConversionPreset == null)
            {
                throw new Exception("转换预设无效。");
            }

            Diagnostics.Debug.Log(string.Empty);
            Diagnostics.Debug.Log("将图片转换为 PNG 中间格式。");
            if (!this.StartChildConversion(this.pngConversionJob, false))
            {
                return;
            }

            Diagnostics.Debug.Log(string.Empty);
            Diagnostics.Debug.Log("将 PNG 中间图片编码为图标。");
            this.StartChildConversion(this.icoConversionJob, false);
        }

        protected override void ReleaseResources()
        {
            DeleteIntermediateFile(this.intermediateFilePath);
        }
    }
}
