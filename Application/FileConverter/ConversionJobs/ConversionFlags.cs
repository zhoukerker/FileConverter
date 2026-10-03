// // <copyright file="ConversionFlags.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>
namespace FileConverter.ConversionJobs
{
    /// <summary>
    /// 描述并行调度时需要满足互斥条件的特殊转换状态。
    /// </summary>
    [System.Flags]
    public enum ConversionFlags
    {
        None = 0x00,

        CdDriveExtraction = 0x01,
    }
}
