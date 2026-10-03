// <copyright file="Application.xaml.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

/*  File Converter - This program allow you to convert file format to another.
    Copyright (C) 2026 Adrien Allard
    email: adrien.allard.pro@gmail.com

    This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or any later version.

    This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License for more details.

    You should have received a copy of the GNU General Public License along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

namespace FileConverter
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Security.Principal;
    using System.Threading;
    using System.Windows;

    using CommunityToolkit.Mvvm.DependencyInjection;

    using FileConverter.ConversionJobs;
    using FileConverter.Services;
    using FileConverter.ViewModels;
    using FileConverter.Views;
    using Microsoft.Extensions.DependencyInjection;
    using Debug = FileConverter.Diagnostics.Debug;

    public partial class Application : System.Windows.Application
    {
        private static readonly Version Version = new Version()
                                                      {
                                                          Major = 2,
                                                          Minor = 2,
                                                          Patch = 0,
                                                      };

        private bool needToRunConversionThread;
        private bool cancelAutoExit;
        private bool isSessionEnding;
        private bool verbose;
        private bool showSettings;
        private bool showHelp;

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(uint dwProcessId);

        const uint ATTACH_PARENT_PROCESS = 0x0ffffffff;

        public event EventHandler<ApplicationTerminateArgs> OnApplicationTerminate;

        public static Version ApplicationVersion => Application.Version;

        public static bool IsInAdmininstratorPrivileges
        {
            get => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        }

        public void CancelAutoExit()
        {
            this.cancelAutoExit = true;

            if (this.OnApplicationTerminate != null)
            {
                this.OnApplicationTerminate.Invoke(this, new ApplicationTerminateArgs(float.NaN));
            }
        }

        public static void AskForShutdown()
        {
            Application.Current.Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown(Debug.FirstErrorCode)));
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 从命令行启动时，将标准输出连接到父进程的控制台。
            AttachConsole(ATTACH_PARENT_PROCESS);

            this.RegisterServices();

            this.Initialize();

            // 显示启动参数指定的页面。
            INavigationService navigationService = Ioc.Default.GetRequiredService<INavigationService>();

            if (this.showHelp)
            {
                navigationService.Show(Pages.Help);
                return;
            }

            if (this.needToRunConversionThread)
            {
                navigationService.Show(Pages.Main);

                IConversionService conversionService = Ioc.Default.GetRequiredService<IConversionService>();
                conversionService.ConversionJobsTerminated += this.ConversionService_ConversionJobsTerminated;
                conversionService.ConvertFilesAsync();
            }

            if (this.showSettings)
            {
                navigationService.Show(Pages.Settings);
            }

            if (this.verbose)
            {
                navigationService.Show(Pages.Diagnostics);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);

            Debug.Log("退出应用程序。");

            IUpgradeService upgradeService = Ioc.Default.GetRequiredService<IUpgradeService>();

            if (!this.isSessionEnding && upgradeService.UpgradeVersionDescription != null && upgradeService.UpgradeVersionDescription.NeedToUpgrade)
            {
                Debug.Log($"发现文件转换器新版本：{upgradeService.UpgradeVersionDescription.LatestVersion}。");

                if (string.IsNullOrEmpty(upgradeService.UpgradeVersionDescription.InstallerPath))
                {
                    Debug.LogError("安装程序路径无效。");
                }
                else
                {
                    Debug.Log("等待安装程序下载完成。");
                    while (upgradeService.UpgradeVersionDescription.InstallerDownloadInProgress && upgradeService.UpgradeVersionDescription.NeedToUpgrade)
                    {
                        Thread.Sleep(1000);
                    }

                    string installerPath = upgradeService.UpgradeVersionDescription.InstallerPath;
                    if (!upgradeService.UpgradeVersionDescription.NeedToUpgrade)
                    {
                        Debug.Log("已取消更新。");
                    }
                    else if (!System.IO.File.Exists(installerPath))
                    {
                        Debug.LogError($"找不到更新安装程序（{installerPath}），请尝试重新启动应用程序。");
                    }
                    else
                    {
                        Debug.Log($"开始将文件转换器从 {ApplicationVersion} 更新到 {upgradeService.UpgradeVersionDescription.LatestVersion}。");
                        ProcessStartInfo startInfo = new ProcessStartInfo(installerPath) { UseShellExecute = true };
                        Debug.Log($"启动更新进程：{System.IO.Path.GetFileName(startInfo.FileName)}{startInfo.Arguments}。");
                        using (Process process = new Process { StartInfo = startInfo })
                        {
                            process.Start();
                        }
                    }
                }
            }

            (upgradeService as IDisposable)?.Dispose();
            Debug.Release();
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            base.OnSessionEnding(e);

            this.isSessionEnding = true;
            this.Shutdown();
        }

        private void RegisterServices()
        {
            var services = new ServiceCollection();

            if (this.TryFindResource("Locator") is ViewModelLocator viewModelLocator)
            {
                viewModelLocator.RegisterViewModels(services);
            }
            else
            {
                Debug.LogError("无法获取视图模型定位器。");
                Application.AskForShutdown();
            }

            if (this.TryFindResource("Upgrade") is UpgradeService upgradeService)
            {
                services.AddSingleton<IUpgradeService>(upgradeService);
            }
            else
            {
                Debug.LogError("无法获取更新服务。");
                Application.AskForShutdown();
            }

            services
              .AddSingleton<INavigationService, NavigationService>()
              .AddSingleton<IConversionService, ConversionService>()
              .AddSingleton<ISettingsService, SettingsService>();

            Ioc.Default.ConfigureServices(services.BuildServiceProvider());

            INavigationService navigationService = Ioc.Default.GetRequiredService<INavigationService>();

            navigationService.RegisterPage<HelpWindow>(Pages.Help, false, true);
            navigationService.RegisterPage<MainWindow>(Pages.Main, false, true);
            navigationService.RegisterPage<SettingsWindow>(Pages.Settings, true, true);
            navigationService.RegisterPage<DiagnosticsWindow>(Pages.Diagnostics, true, false);
            navigationService.RegisterPage<UpgradeWindow>(Pages.Upgrade, true, false);
        }

        private void Initialize()
        {
#if BUILD32
            Diagnostics.Debug.Log("File Converter v" + ApplicationVersion.ToString() + "（32 位）");
#else
            Diagnostics.Debug.Log("File Converter v" + ApplicationVersion.ToString() + "（64 位）");
#endif

            // 读取命令行参数。
            Debug.Log("读取命令行参数…");
            string[] args = Environment.GetCommandLineArgs();

            // 记录命令行参数。
            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                Debug.Log($"参数 {index}：{argument}");
            }

            Debug.Log(string.Empty);

            if (args.Length == 1)
            {
                // 未指定参数时显示帮助，说明如何使用右键菜单扩展。
                this.showHelp = true;
                return;
            }

            // 解析命令行参数。
            List<string> filePaths = new List<string>();
            string conversionPresetName = null;
            for (int index = 1; index < args.Length; index++)
            {
                string argument = args[index];
                if (string.IsNullOrEmpty(argument))
                {
                    continue;
                }

                if (argument.StartsWith("--"))
                {
                    // 双连字符开头的内容为可选参数。
                    string parameterTitle = argument.Substring(2).ToLowerInvariant();

                    switch (parameterTitle)
                    {
                        case "post-install-init":
                            ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
                            if (!settingsService.PostInstallationInitialization())
                            {
                                Debug.LogError(errorCode: 0x0F, $"安装后初始化失败。");
                            }

                            Application.AskForShutdown();
                            return;

                        case "register-shell-extension":
                            {
                                if (index >= args.Length - 1)
                                {
                                    Debug.LogError(errorCode: 0x0B, $"--register-shell-extension 后必须指定扩展程序集路径。");
                                    break;
                                }

                                string shellExtensionPath = args[index + 1];
                                index++;

                                if (!Helpers.RegisterShellExtension(shellExtensionPath))
                                {
                                    Debug.LogError(errorCode: 0x0C, $"注册右键菜单扩展失败：{shellExtensionPath}。");
                                }

                                Application.AskForShutdown();
                                return;
                            }

                        case "unregister-shell-extension":
                            {
                                if (index >= args.Length - 1)
                                {
                                    Debug.LogError(errorCode: 0x0D, $"--unregister-shell-extension 后必须指定扩展程序集路径。");
                                    break;
                                }

                                string shellExtensionPath = args[index + 1];
                                index++;

                                if (!Helpers.UnregisterExtension(shellExtensionPath))
                                {
                                    Debug.LogError(errorCode: 0x0E, $"注销右键菜单扩展失败：{shellExtensionPath}。");
                                }

                                Application.AskForShutdown();
                                return;
                            }

                        case "version":
                            Console.WriteLine(ApplicationVersion.ToString());
                            Application.AskForShutdown();
                            return;

                        case "settings":
                            this.showSettings = true;
                            break;

                        case "conversion-preset":
                            if (index >= args.Length - 1)
                            {
                                Debug.LogError(errorCode: 0x01, $"--conversion-preset 后必须指定转换预设名称。");
                                Application.AskForShutdown();
                                return;
                            }

                            conversionPresetName = args[index + 1];
                            index++;
                            break;

                        case "input-files":
                            if (index >= args.Length - 1)
                            {
                                Debug.LogError(errorCode: 0x02, $"--input-files 后必须指定输入文件清单路径。");
                                Application.AskForShutdown();
                                return;
                            }

                            string fileListPath = args[index + 1];
                            try
                            {
                                using (FileStream file = File.OpenRead(fileListPath))
                                using (StreamReader reader = new StreamReader(file))
                                {
                                    while (!reader.EndOfStream)
                                    {
                                        filePaths.Add(reader.ReadLine());
                                    }
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogError(errorCode: 0x03, $"无法读取输入文件清单：{exception}");
                                Application.AskForShutdown();
                                return;
                            }

                            index++;
                            break;

                        case "verbose":
                            {
                                this.verbose = true;
                            }

                            break;

                        default:
                            Debug.LogError($"未知应用程序参数：'--{parameterTitle}'。");
                            return;
                    }
                }
                else
                {
                    filePaths.Add(argument);
                }
            }

            this.RunConversions(filePaths, conversionPresetName);
        }

        private void RunConversions(List<string> filePaths, string conversionPresetName)
        {
            ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
            if (settingsService.Settings == null)
            {
                Debug.LogError(errorCode: 0x04, "无法加载文件转换器配置，应用程序即将退出。请编辑或删除当前用户目录下的 AppData\\Local\\FileConverter\\Settings.user.xml 后重试。");
                Application.AskForShutdown();
                return;
            }

            Debug.Assert(Debug.FirstErrorCode == 0, "初始化过程中发生错误。");

            // 检查应用程序更新。
            if (settingsService.Settings.CheckUpgradeAtStartup)
            {
                IUpgradeService upgradeService = Ioc.Default.GetRequiredService<IUpgradeService>();
                upgradeService.NewVersionAvailable += this.UpgradeService_NewVersionAvailable;
                upgradeService.CheckForUpgrade();
            }

            ConversionPreset conversionPreset = null;
            if (!string.IsNullOrEmpty(conversionPresetName))
            {
                conversionPreset = settingsService.Settings.GetPresetFromName(conversionPresetName);
                if (conversionPreset == null)
                {
                    Debug.LogError(errorCode: 0x02, $"转换预设无效：'{conversionPresetName}'。");
                    Application.AskForShutdown();
                    return;
                }
            }

            if (conversionPreset != null)
            {
                IConversionService conversionService = Ioc.Default.GetRequiredService<IConversionService>();

                // 为所选文件创建转换任务。
                Debug.Log($"按转换预设创建任务：'{conversionPreset.FullName}'");
                try
                {
                    for (int index = 0; index < filePaths.Count; index++)
                    {
                        string inputFilePath = filePaths[index];
                        ConversionJob conversionJob = ConversionJobFactory.Create(conversionPreset, inputFilePath);

                        conversionService.RegisterConversionJob(conversionJob);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError($"创建转换任务失败：{exception.Message}");
                    throw;
                }

                this.needToRunConversionThread = true;
            }
        }

        private void UpgradeService_NewVersionAvailable(object sender, UpgradeVersionDescription e)
        {
            Ioc.Default.GetRequiredService<INavigationService>().Show(Pages.Upgrade);

            IUpgradeService upgradeService = Ioc.Default.GetRequiredService<IUpgradeService>();
            upgradeService.NewVersionAvailable -= this.UpgradeService_NewVersionAvailable;
        }

        private void ConversionService_ConversionJobsTerminated(object sender, ConversionJobsTerminatedEventArgs e)
        {
            IConversionService conversionService = Ioc.Default.GetRequiredService<IConversionService>();
            conversionService.ConversionJobsTerminated -= this.ConversionService_ConversionJobsTerminated;

            ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();

            if (!settingsService.Settings.ExitApplicationWhenConversionsFinished)
            {
                return;
            }

            if (this.cancelAutoExit)
            {
                return;
            }

            if (e.AllConversionsSucceed)
            {
                float remainingTime = settingsService.Settings.DurationBetweenEndOfConversionsAndApplicationExit;
                while (remainingTime > 0f)
                {
                    if (this.OnApplicationTerminate != null)
                    {
                        this.OnApplicationTerminate.Invoke(this, new ApplicationTerminateArgs(remainingTime));
                    }

                    Thread.Sleep(1000);
                    remainingTime--;

                    if (this.cancelAutoExit)
                    {
                        return;
                    }
                }

                if (this.OnApplicationTerminate != null)
                {
                    this.OnApplicationTerminate.Invoke(this, new ApplicationTerminateArgs(remainingTime));
                }

                Application.AskForShutdown();
            }
        }
    }
}
