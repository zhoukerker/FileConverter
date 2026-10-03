// <copyright file="SettingsService.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

using FileConverter.Properties;

namespace FileConverter.Services
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Windows;

    using CommunityToolkit.Mvvm.ComponentModel;

    using Debug = FileConverter.Diagnostics.Debug;

    public partial class SettingsService : ObservableObject, ISettingsService
    {
        public SettingsService()
        {
            // 加载设置。
            Debug.Log("正在加载设置……");
            this.Settings = this.Load();
        }

        public Settings Settings
        {
            get;
            private set;
        }

        private string UserSettingsTemporaryFilePath
        {
            get
            {
                string path = FileConverterExtension.PathHelpers.GetUserDataFolderPath;
                path = Path.Combine(path, "Settings.temp.xml");
                return path;
            }
        }

        public bool PostInstallationInitialization()
        {
            Debug.Log("执行安装后的初始化。");

            Settings defaultSettings = null;

            // 加载默认设置。
            if (File.Exists(FileConverterExtension.PathHelpers.DefaultSettingsFilePath))
            {
                try
                {
                    XmlHelpers.LoadFromFile<Settings>("Settings", FileConverterExtension.PathHelpers.DefaultSettingsFilePath, out defaultSettings);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"无法加载 File Converter 默认设置：{exception.Message}");
                    return false;
                }
            }
            else
            {
                Debug.LogError($"路径 {FileConverterExtension.PathHelpers.DefaultSettingsFilePath} 中没有默认设置，请尝试重新安装应用程序。");
                return false;
            }

            // 加载已有的用户设置。
            Settings userSettings = null;
            if (File.Exists(FileConverterExtension.PathHelpers.UserSettingsFilePath))
            {
                try
                {
                    XmlHelpers.LoadFromFile<Settings>("Settings", FileConverterExtension.PathHelpers.UserSettingsFilePath, out userSettings);
                }
                catch (Exception)
                {
                    File.Delete(FileConverterExtension.PathHelpers.UserSettingsFilePath);
                }

                if (userSettings != null)
                {
                    if (userSettings.SerializationVersion != Settings.Version)
                    {
                        this.MigrateSettingsToCurrentVersion(userSettings);

                        Debug.Log($"File Converter 设置已从版本 {userSettings.SerializationVersion} 迁移到版本 {Settings.Version}。");
                        userSettings.SerializationVersion = Settings.Version;
                    }

                    // 移除默认设置。
                    if (userSettings.ConversionPresets != null)
                    {
                        for (int index = userSettings.ConversionPresets.Count - 1; index >= 0; index--)
                        {
                            if (userSettings.ConversionPresets[index].IsDefaultSettings)
                            {
                                userSettings.ConversionPresets.RemoveAt(index);
                            }
                        }
                    }
                }
            }

            Settings settings = userSettings != null ? userSettings.Merge(defaultSettings) : defaultSettings;
            return this.Save(settings);
        }

        public void SaveSettings()
        {
            this.Save(this.Settings);
        }

        public void RevertSettings()
        {
            // 重新加载原预设以撤销修改。
            this.Settings = this.Load();
        }

        private Settings Load()
        {
            Settings settings = null;
            if (File.Exists(FileConverterExtension.PathHelpers.UserSettingsFilePath))
            {
                Settings userSettings = null;
                try
                {
                    var stopwatch = new Stopwatch();
                    stopwatch.Start();
                    XmlHelpers.LoadFromFile<Settings>("Settings", FileConverterExtension.PathHelpers.UserSettingsFilePath, out userSettings);
                    stopwatch.Stop();
                    Debug.Log($"设置加载耗时：{stopwatch.Elapsed.TotalMilliseconds} 毫秒");

                    settings = userSettings;
                }
                catch (Exception)
                {
                    MessageBoxResult messageBoxResult =
                        MessageBox.Show(Resources.ErrorCantLoadSettings,
                            Resources.Error,
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Exclamation);

                    if (messageBoxResult == MessageBoxResult.Yes)
                    {
                        File.Delete(FileConverterExtension.PathHelpers.UserSettingsFilePath);
                        return this.Load();
                    }
                    else if (messageBoxResult == MessageBoxResult.No)
                    {
                        return null;
                    }
                }

                if (userSettings != null && userSettings.SerializationVersion != Settings.Version)
                {
                    this.MigrateSettingsToCurrentVersion(userSettings);

                    Debug.Log($"File Converter 设置已从版本 {userSettings.SerializationVersion} 迁移到版本 {Settings.Version}。");
                    userSettings.SerializationVersion = Settings.Version;
                    this.Save(userSettings);
                }
            }
            else
            {
                // 加载默认设置。
                if (File.Exists(FileConverterExtension.PathHelpers.DefaultSettingsFilePath))
                {
                    try
                    {
                        XmlHelpers.LoadFromFile<Settings>("Settings", FileConverterExtension.PathHelpers.DefaultSettingsFilePath, out Settings defaultSettings);
                        settings = defaultSettings;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError($"无法加载 File Converter 默认设置：{exception.Message}");
                    }
                }
                else
                {
                    Debug.LogError($"路径 {FileConverterExtension.PathHelpers.DefaultSettingsFilePath} 中没有默认设置，请尝试重新安装应用程序。");
                }
            }

            return settings;
        }

        private bool Save(Settings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Clean();

            // 先将设置写入临时文件，保存注册表项成功后再提交设置文件。
            XmlHelpers.SaveToFile("Settings", this.UserSettingsTemporaryFilePath, settings);

            // 将临时设置文件复制到正式设置路径。
            File.Copy(this.UserSettingsTemporaryFilePath, FileConverterExtension.PathHelpers.UserSettingsFilePath, true);
            File.Delete(this.UserSettingsTemporaryFilePath);

            return true;
        }
    }
}
