// <copyright file="ConversionJob_Office.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ConversionJobs
{
    using System;
    using System.IO;

    using FileConverter.Diagnostics;

    /// <summary>
    /// 统一管理 Office 文档导出、图片子任务和 COM 资源的释放顺序。
    /// </summary>
    public abstract class ConversionJob_Office : ConversionJob
    {
        private string intermediateFilePath = string.Empty;
        private ConversionJob pdf2ImageConversionJob;

        protected ConversionJob_Office()
        {
        }

        protected ConversionJob_Office(ConversionPreset conversionPreset, string inputFilePath) : base(conversionPreset, inputFilePath)
        {
        }

        public enum ApplicationName
        {
            None,

            Word,
            Excel,
            PowerPoint
        }

        protected abstract ApplicationName Application { get; }

        protected override bool IsCancelable() => false;

        protected override int GetOutputFilesCount()
        {
            if (this.ConversionPreset.OutputType == OutputType.Pdf || !this.TryLoadDocumentIfNecessary())
            {
                return 1;
            }

            return this.GetDocumentPageCount();
        }

        protected override void Initialize()
        {
            base.Initialize();

            if (!Helpers.IsMicrosoftOfficeApplicationAvailable(this.Application))
            {
                switch (this.Application)
                {
                    case ApplicationName.Word:
                        this.ConversionFailed(Properties.Resources.ErrorMicrosoftWordIsNotAvailable);
                        break;

                    case ApplicationName.PowerPoint:
                        this.ConversionFailed(Properties.Resources.ErrorMicrosoftPowerPointIsNotAvailable);
                        break;

                    case ApplicationName.Excel:
                        this.ConversionFailed(Properties.Resources.ErrorMicrosoftExcelIsNotAvailable);
                        break;

                    default:
                        this.ConversionFailed(Properties.Resources.ErrorMicrosoftOfficeIsNotAvailable);
                        break;
                }

                return;
            }

            if (this.State == ConversionState.Failed)
            {
                return;
            }

            if (this.ConversionPreset == null)
            {
                throw new Exception("转换预设无效。");
            }

            if (this.ConversionPreset.OutputType == OutputType.Pdf)
            {
                this.intermediateFilePath = this.OutputFilePath;
            }
            else
            {
                // 图片输出先导出临时 PDF，子任务沿用父任务已经准备的输出路径。
                string fileName = Path.GetFileNameWithoutExtension(this.InputFilePath);
                string tempPath = Path.GetTempPath();
                this.intermediateFilePath = PathHelpers.GenerateUniquePath(tempPath + fileName + ".pdf");

                ConversionPreset intermediatePreset = new ConversionPreset("Pdf to image", this.ConversionPreset, "pdf");
                this.pdf2ImageConversionJob = ConversionJobFactory.Create(intermediatePreset, this.intermediateFilePath);
                this.pdf2ImageConversionJob.PrepareConversion(this.OutputFilePaths);
            }
        }

        protected override void Convert()
        {
            if (this.ConversionPreset == null)
            {
                throw new Exception("转换预设无效。");
            }

            this.UserState = Properties.Resources.ConversionStateReadDocument;
            if (!this.TryLoadDocumentIfNecessary())
            {
                this.ConversionFailed(Properties.Resources.ErrorUnableToUseMicrosoftOffice);
                return;
            }

            this.PrepareDocumentForExport();
            this.UserState = Properties.Resources.ConversionStateConversion;

            Debug.Log($"将 {this.Application} 文档转换为 PDF。");
            this.ExportDocumentAsPdf(this.intermediateFilePath);

            Debug.Log($"关闭 {this.Application} 文档：'{this.InputFilePath}'。");
            this.CloseDocument();
            this.ReleaseOfficeApplicationInstanceIfNeeded();

            if (this.pdf2ImageConversionJob != null)
            {
                if (!File.Exists(this.intermediateFilePath))
                {
                    this.ConversionFailed(Properties.Resources.ErrorCantFindOutputFiles);
                    return;
                }

                Debug.Log("将 PDF 转换为图片。");
                this.StartChildConversion(this.pdf2ImageConversionJob);
            }
        }

        protected override void ReleaseResources()
        {
            try
            {
                this.ReleaseOfficeApplicationInstanceIfNeeded();
            }
            finally
            {
                if (this.pdf2ImageConversionJob != null)
                {
                    DeleteIntermediateFile(this.intermediateFilePath);
                }
            }
        }

        /// <summary>
        /// 先清空应用字段并关闭文档，再退出应用；任一步失败时仍释放应用包装对象。
        /// </summary>
        protected void ReleaseOfficeApplication<T>(ref T application, Action<T> quit) where T : NetOffice.COMObject
        {
            T officeApplication = application;
            application = null;
            try
            {
                this.CloseDocument();
            }
            finally
            {
                if (officeApplication != null)
                {
                    try
                    {
                        Debug.Log($"退出 {this.Application} 应用。");
                        quit(officeApplication);
                    }
                    finally
                    {
                        officeApplication.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// 先清空文档字段，关闭后始终释放 COM 包装对象，避免重复关闭。
        /// </summary>
        protected static void CloseOfficeDocument<T>(ref T document, Action<T> close) where T : NetOffice.COMObject
        {
            T officeDocument = document;
            document = null;
            if (officeDocument == null)
            {
                return;
            }

            try
            {
                close(officeDocument);
            }
            finally
            {
                officeDocument.Dispose();
            }
        }

        protected virtual void PrepareDocumentForExport()
        {
        }

        protected abstract int GetDocumentPageCount();

        protected abstract bool TryLoadDocumentIfNecessary();

        protected abstract void ExportDocumentAsPdf(string outputFilePath);

        protected abstract void CloseDocument();

        protected abstract void InitializeOfficeApplicationInstanceIfNecessary();

        protected abstract void ReleaseOfficeApplicationInstanceIfNeeded();
    }
}
