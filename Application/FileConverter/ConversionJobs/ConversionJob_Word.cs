// <copyright file="ConversionJob_Word.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ConversionJobs
{
    using System;

    using FileConverter.Diagnostics;

    using Word = NetOffice.WordApi;

    public class ConversionJob_Word : ConversionJob_Office
    {
        private Word.Document document;
        private Word.Application application;

        public ConversionJob_Word()
        {
        }

        public ConversionJob_Word(ConversionPreset conversionPreset, string inputFilePath) : base(conversionPreset, inputFilePath)
        {
        }

        protected override ApplicationName Application => ApplicationName.Word;

        protected override int GetDocumentPageCount() => this.document.ComputeStatistics(Word.Enums.WdStatistic.wdStatisticPages);

        protected override void PrepareDocumentForExport() => this.document.Activate();

        protected override void ExportDocumentAsPdf(string outputFilePath)
        {
            this.document.ExportAsFixedFormat(outputFilePath,
                Word.Enums.WdExportFormat.wdExportFormatPDF,
                false,
                Word.Enums.WdExportOptimizeFor.wdExportOptimizeForPrint,
                Word.Enums.WdExportRange.wdExportAllDocument,
                1, 1,
                Word.Enums.WdExportItem.wdExportDocumentContent,
                true,
                true,
                Word.Enums.WdExportCreateBookmarks.wdExportCreateHeadingBookmarks,
                true);
        }

        protected override void InitializeOfficeApplicationInstanceIfNecessary()
        {
            if (this.application != null)
            {
                return;
            }

            // 初始化 Word 应用。
            Debug.Log("创建 Word 应用实例。");
            this.application = new Word.Application
            {
                Visible = false
            };
        }

        protected override void ReleaseOfficeApplicationInstanceIfNeeded() =>
            this.ReleaseOfficeApplication(ref this.application, application => application.Quit());

        protected override void CloseDocument() =>
            CloseOfficeDocument(ref this.document, document => document.Close(Word.Enums.WdSaveOptions.wdDoNotSaveChanges));

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
                Debug.Log($"加载 Word 文档：'{this.InputFilePath}'.");

                this.document = this.application.Documents.Open(this.InputFilePath, System.Reflection.Missing.Value, true);
            }

            return this.document != null;
        }
    }
}
