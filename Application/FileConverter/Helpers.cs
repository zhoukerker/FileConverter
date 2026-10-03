// <copyright file="Helpers.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Threading;

    using FileConverter.ConversionJobs;
    using FileConverter.Services;

    using SharpShell.Helpers;

    using Microsoft.Win32;
    using CommunityToolkit.Mvvm.DependencyInjection;

    public static class Helpers
    {
        public static readonly string[] CompatibleInputExtensions = {
            "3gp","3gpp","aac","aiff","ape","arw","avi","avif","bik","bmp","cda","cr2","dds","dng","doc","docx",
            "exr","flac","flv","gif","heic","ico","jfif","jpg","jpeg","m4a","m4b","m4v","mkv","mov","mp3","mp4",
            "mpg","mpeg","nef","odp","ods","odt","oga","ogg","ogv","opus","pdf","png","ppt","pptx","psd",
            "raf", "rm","svg","tga","tif","tiff", "ts", "vob","wav","webm","webp","wma","wmv","xls","xlsx"
        };

        public static string GetExtensionCategory(string extension)
        {
            switch (extension)
            {
                case "aac":
                case "aiff":
                case "ape":
                case "cda":
                case "flac":
                case "mp3":
                case "m4a":
                case "m4b":
                case "oga":
                case "ogg":
                case "opus":
                case "wav":
                case "wma":
                    return InputCategoryNames.Audio;

                case "3gp":
                case "3gpp":
                case "avi":
                case "bik":
                case "flv":
                case "m4v":
                case "mp4":
                case "mpg":
                case "mpeg":
                case "mov":
                case "mkv":
                case "ogv":
                case "rm":
                case "ts":
                case "vob":
                case "webm":
                case "wmv":
                    return InputCategoryNames.Video;

                case "arw":
                case "avif":
                case "bmp":
                case "cr2":
                case "dds":
                case "dng":
                case "exr":
                case "heic":
                case "ico":
                case "jfif":
                case "jpg":
                case "jpeg":
                case "nef":
                case "png":
                case "psd":
                case "raf":
                case "tga":
                case "tif":
                case "tiff":
                case "svg":
                case "xcf":
                case "webp":
                    return InputCategoryNames.Image;

                case "gif":
                    return InputCategoryNames.AnimatedImage;

                case "pdf":
                case "doc":
                case "docx":
                case "ppt":
                case "pptx":
                case "odp":
                case "ods":
                case "odt":
                case "xls":
                case "xlsx":
                    return InputCategoryNames.Document;
            }

            return InputCategoryNames.Misc;
        }

        public static bool RegisterShellExtension(string shellExtensionPath)
        {
            if (!Application.IsInAdmininstratorPrivileges)
            {
                Diagnostics.Debug.LogError("File Converter 注册资源管理器扩展需要管理员权限。");
                return false;
            }

            if (!File.Exists(shellExtensionPath))
            {
                Diagnostics.Debug.LogError($"资源管理器扩展文件 {shellExtensionPath} 不存在。");
                return false;
            }

            Diagnostics.Debug.Log($"安装并注册资源管理器扩展：{shellExtensionPath}");

            var regasm = new RegAsm();
            var success = regasm.Register64(shellExtensionPath, true);
            if (success)
            {
                Diagnostics.Debug.Log($"资源管理器扩展 {shellExtensionPath} 已安装并注册。");
                Diagnostics.Debug.Log(regasm.StandardOutput);
                return true;
            }
            else
            {
                Diagnostics.Debug.LogError(errorCode: 0x05, $"资源管理器扩展 {shellExtensionPath} 注册失败。");
                Diagnostics.Debug.LogError(regasm.StandardError);
                return false;
            }
        }

        public static bool UnregisterExtension(string shellExtensionPath)
        {
            if (!Application.IsInAdmininstratorPrivileges)
            {
                Diagnostics.Debug.LogError("File Converter 注销资源管理器扩展需要管理员权限。");
                return false;
            }

            if (!File.Exists(shellExtensionPath))
            {
                Diagnostics.Debug.LogError($"资源管理器扩展文件 {shellExtensionPath} 不存在。");
                return false;
            }

            Diagnostics.Debug.Log($"注销并卸载资源管理器扩展：{shellExtensionPath}");

            var regasm = new RegAsm();
            var success = regasm.Unregister64(shellExtensionPath);
            if (success)
            {
                Diagnostics.Debug.Log($"资源管理器扩展 {shellExtensionPath} 已卸载。");
                Diagnostics.Debug.Log(regasm.StandardOutput);
                return true;
            }
            else
            {
                Diagnostics.Debug.LogError(errorCode: 0x05, $"资源管理器扩展 {shellExtensionPath} 卸载失败。");
                Diagnostics.Debug.LogError(regasm.StandardError);
                return false;
            }
        }

        public static IEnumerable<CultureInfo> GetSupportedCultures()
        {
            // 保留兼容接口，简体中文资源直接嵌入主程序集。
            yield return CultureInfo.GetCultureInfo("zh-CN");
        }

        public static bool IsOutputTypeCompatibleWithCategory(OutputType outputType, string category)
        {
            if (category == InputCategoryNames.Misc)
            {
                // 杂项包含未分类的输入扩展名，允许其与输出格式兼容。
                return true;
            }

            switch (outputType)
            {
                case OutputType.Aac:
                case OutputType.Flac:
                case OutputType.Mp3:
                case OutputType.Ogg:
                case OutputType.Wav:
                    return category == InputCategoryNames.Audio || category == InputCategoryNames.Video;

                case OutputType.Avi:
                case OutputType.Mkv:
                case OutputType.Mp4:
                case OutputType.Ogv:
                case OutputType.Webm:
                    return category == InputCategoryNames.Video || category == InputCategoryNames.AnimatedImage;

                case OutputType.Avif:
                case OutputType.Ico:
                case OutputType.Jpg:
                case OutputType.Png:
                case OutputType.Webp:
                    return category == InputCategoryNames.Image || category == InputCategoryNames.Document || category == InputCategoryNames.AnimatedImage;

                case OutputType.Gif:
                    return category == InputCategoryNames.Image || category == InputCategoryNames.Video || category == InputCategoryNames.AnimatedImage;

                case OutputType.Pdf:
                    return category == InputCategoryNames.Image || category == InputCategoryNames.Document;

                default:
                    return false;
            }
        }

        public static Thread InstantiateThread(string name, ThreadStart threadStart)
        {
            ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
            CultureInfo currentCulture = settingsService?.Settings?.ApplicationLanguage;

            Thread thread = new Thread(threadStart);
            thread.Name = name;

            if (currentCulture != null)
            {
                thread.CurrentCulture = currentCulture;
                thread.CurrentUICulture = currentCulture;
            }

            return thread;
        }

        public static Thread InstantiateThread(string name, ParameterizedThreadStart parameterizedThreadStart)
        {
            ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
            CultureInfo currentCulture = settingsService?.Settings?.ApplicationLanguage;

            Thread thread = new Thread(parameterizedThreadStart);
            thread.Name = name;

            if (currentCulture != null)
            {
                thread.CurrentCulture = currentCulture;
                thread.CurrentUICulture = currentCulture;
            }

            return thread;
        }

        /// <summary>
        /// 检查 Microsoft Office 是否可用。
        /// </summary>
        /// <param name="application">Office 应用名称。</param>
        /// <returns>已安装对应 Office 应用时返回 true。</returns>
        /// 参考来源： http://stackoverflow.com/questions/3266675/how-to-detect-installed-version-of-ms-office/3267832#3267832
        /// 参考来源： http://www.codeproject.com/Articles/26520/Getting-Office-s-Version
        public static bool IsMicrosoftOfficeApplicationAvailable(ConversionJobs.ConversionJob_Office.ApplicationName application)
        {
            string registryKeyPattern = @"Software\Microsoft\Windows\CurrentVersion\App Paths\";
            switch (application)
            {
                case ConversionJob_Office.ApplicationName.Word:
                    registryKeyPattern += "winword.exe";
                    break;

                case ConversionJob_Office.ApplicationName.PowerPoint:
                    registryKeyPattern += "powerpnt.exe";
                    break;

                case ConversionJob_Office.ApplicationName.Excel:
                    registryKeyPattern += "excel.exe";
                    break;

                case ConversionJob_Office.ApplicationName.None:
                    return false;
            }

            // 查询后及时释放注册表句柄。
            using (RegistryKey winwordKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(registryKeyPattern, false))
            {
                string winwordPath = winwordKey?.GetValue(string.Empty) as string;
                if (!string.IsNullOrEmpty(winwordPath))
                {
                    return true;
                }
            }

            using (RegistryKey winwordKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(registryKeyPattern, false))
            {
                string winwordPath = winwordKey?.GetValue(string.Empty) as string;
                if (!string.IsNullOrEmpty(winwordPath))
                {
                    return true;
                }
            }

            return false;
        }

        public static ConversionJob_Office.ApplicationName GetOfficeApplicationCompatibleWithExtension(string extension)
        {
            switch (extension)
            {
                case "doc":
                case "docx":
                case "odt":
                    return ConversionJob_Office.ApplicationName.Word;

                case "ppt":
                case "pptx":
                case "odp":
                    return ConversionJob_Office.ApplicationName.PowerPoint;

                case "ods":
                case "xls":
                case "xlsx":
                    return ConversionJob_Office.ApplicationName.Excel;
            }

            return ConversionJob_Office.ApplicationName.None;
        }

        public static class InputCategoryNames
        {
            public const string Audio = "Audio";
            public const string Video = "Video";
            public const string Image = "Image";
            public const string AnimatedImage = "Animated Image";
            public const string Document = "Document";

            public const string Misc = "Misc";
        }

        public enum HardwareAccelerationMode
        {
            Off,
            CUDA,
            AMF
        }
    }
}
