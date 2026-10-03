// <copyright file="ConversionJob_PowerPoint.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ConversionJobs
{
    using System;

    using FileConverter.Diagnostics;

    using PowerPoint = NetOffice.PowerPointApi;

    public class ConversionJob_PowerPoint : ConversionJob_Office
    {
        private PowerPoint.Presentation document;
        private PowerPoint.Application application;

        public ConversionJob_PowerPoint()
        {
        }

        public ConversionJob_PowerPoint(ConversionPreset conversionPreset, string inputFilePath) : base(conversionPreset, inputFilePath)
        {
        }

        protected override ApplicationName Application => ApplicationName.PowerPoint;

        protected override int GetDocumentPageCount() => this.document.Slides.Count;

        protected override void ExportDocumentAsPdf(string outputFilePath) =>
            this.document.ExportAsFixedFormat(outputFilePath, PowerPoint.Enums.PpFixedFormatType.ppFixedFormatTypePDF);

        protected override void InitializeOfficeApplicationInstanceIfNecessary()
        {
            if (this.application != null)
            {
                return;
            }

            // 初始化 PowerPoint 应用。
            Debug.Log("创建 PowerPoint 应用实例。");
            this.application = new PowerPoint.Application();
        }

        protected override void ReleaseOfficeApplicationInstanceIfNeeded() =>
            this.ReleaseOfficeApplication(ref this.application, application => application.Quit());

        protected override void CloseDocument() =>
            CloseOfficeDocument(ref this.document, document => document.Close());

        protected override bool TryLoadDocumentIfNecessary()
        {
            try
            {
                this.InitializeOfficeApplicationInstanceIfNecessary();
            }
            catch (Exception exception)
            {
                Debug.Log(exception.ToString());
                Debug.Log("初始化 Office 应用失败。");
            }

            if (this.application == null)
            {
                return false;
            }

            if (this.document == null)
            {
                Debug.Log($"加载 PowerPoint 文档：'{this.InputFilePath}'.");

                this.document = this.application.Presentations.Open(this.InputFilePath, readOnly: NetOffice.OfficeApi.Enums.MsoTriState.msoTrue, untitled: NetOffice.OfficeApi.Enums.MsoTriState.msoFalse, withWindow: NetOffice.OfficeApi.Enums.MsoTriState.msoFalse);
            }

            return this.document != null;
        }
    }
}
