// <copyright file="ConversionJob_Excel.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ConversionJobs
{
    using System;

    using FileConverter.Diagnostics;

    using Excel = NetOffice.ExcelApi;

    public class ConversionJob_Excel : ConversionJob_Office
    {
        private Excel.Workbook document;
        private Excel.Application application;

        public ConversionJob_Excel()
        {
        }

        public ConversionJob_Excel(ConversionPreset conversionPreset, string inputFilePath) : base(conversionPreset, inputFilePath)
        {
        }

        protected override ApplicationName Application => ApplicationName.Excel;

        protected override int GetDocumentPageCount()
        {
            int pagesCount = 0;
            foreach (object sheet in this.document.Sheets)
            {
                Excel.Worksheet worksheet = sheet as Excel.Worksheet;
                if (worksheet != null)
                {
                    // 保留原有按工作表覆盖页数的规则，不改变输出数量。
                    pagesCount = worksheet.PageSetup.Pages.Count;
                }
            }

            return pagesCount;
        }

        protected override void PrepareDocumentForExport() => this.document.Activate();

        protected override void ExportDocumentAsPdf(string outputFilePath) =>
            this.document.ExportAsFixedFormat(Excel.Enums.XlFixedFormatType.xlTypePDF, outputFilePath);

        protected override void InitializeOfficeApplicationInstanceIfNecessary()
        {
            if (this.application != null)
            {
                return;
            }

            // 初始化 Excel 应用。
            Diagnostics.Debug.Log("创建 Excel 应用实例。");
            this.application = new Excel.Application
            {
                Visible = false
            };
        }

        protected override void ReleaseOfficeApplicationInstanceIfNeeded() =>
            this.ReleaseOfficeApplication(ref this.application, application => application.Quit());

        protected override void CloseDocument() =>
            CloseOfficeDocument(ref this.document, document => document.Close(false));

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
                Debug.Log($"加载 Excel 文档：'{this.InputFilePath}'.");

                this.document = this.application.Workbooks.Open(this.InputFilePath, System.Reflection.Missing.Value, true);
            }

            return this.document != null;
        }
    }
}
