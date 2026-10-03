// <copyright file="UpgradeService.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Services
{
    using System;
    using System.IO;
    using System.Net;
    using System.Threading.Tasks;
    using System.Xml;
    using System.Xml.Serialization;

    using CommunityToolkit.Mvvm.ComponentModel;

    using FileConverter.Annotations;
    using FileConverter.Diagnostics;

    public class UpgradeService : ObservableObject, IUpgradeService, IDisposable
    {
        private const string BaseURI = "https://raw.githubusercontent.com/zhoukerker/FileConverter/integration/";

        [NotNull]
        private readonly WebClient webClient = new WebClient();

        private UpgradeVersionDescription upgradeVersionDescription;

        public UpgradeService()
        {
            this.UpgradeVersionDescription = new UpgradeVersionDescription();
        }

        public event EventHandler<UpgradeVersionDescription> NewVersionAvailable;

        public UpgradeVersionDescription UpgradeVersionDescription
        {
            get => this.upgradeVersionDescription;
            private set
            {
                this.upgradeVersionDescription = value;
                this.OnPropertyChanged();
            }
        }

        public async Task<UpgradeVersionDescription> CheckForUpgrade()
        {
            Task<UpgradeVersionDescription> task = null;
            try
            {
#if DEBUG
                task = this.DownloadLatestVersionDescription();
#else
                long fileTime = Registry.GetValue<long>(Registry.Keys.LastUpdateCheckDate);
                DateTime lastUpdateDateTime = DateTime.FromFileTime(fileTime);

                TimeSpan durationSinceLastUpdate = DateTime.Now.Subtract(lastUpdateDateTime);
                if (durationSinceLastUpdate > new TimeSpan(1, 0, 0, 0))
                {
                    task = this.DownloadLatestVersionDescription();
                }
#endif
            }
            catch (Exception exception)
            {
                Diagnostics.Debug.Log($"检查更新失败：{exception.Message}");
            }

            if (task == null)
            {
                return null;
            }

            UpgradeVersionDescription versionDescription = await task;

            if (versionDescription == null)
            {
                return null;
            }

            Registry.SetValue(Registry.Keys.LastUpdateCheckDate, DateTime.Now.ToFileTime());

            if (versionDescription.LatestVersion <= Application.ApplicationVersion)
            {
                return null;
            }

            this.UpgradeVersionDescription = versionDescription;

            this.NewVersionAvailable?.Invoke(this, versionDescription);
            return versionDescription;
        }

        public async Task<string> DownloadChangeLog()
        {
            if (this.UpgradeVersionDescription == null)
            {
                throw new ArgumentNullException(nameof(this.UpgradeVersionDescription));
            }

            this.UpgradeVersionDescription.ChangeLog = Properties.Resources.DownloadingChangeLog;

            Uri uri = new Uri(UpgradeService.BaseURI + "CHANGELOG.md");
            try
            {
                using (WebClient client = new WebClient())
                using (Stream stream = await client.OpenReadTaskAsync(uri))
                using (StreamReader reader = new StreamReader(stream))
                {
                    this.UpgradeVersionDescription.ChangeLog = await reader.ReadToEndAsync();
                }
            }
            catch (Exception)
            {
                Debug.LogError("获取更新日志失败。");
                return null;
            }

            return this.UpgradeVersionDescription.ChangeLog;
        }

        public async Task StartUpgrade()
        {
            if (this.UpgradeVersionDescription == null)
            {
                Debug.Log("尚未检查更新，无法开始升级。");
                return;
            }

            if (string.IsNullOrWhiteSpace(this.UpgradeVersionDescription.InstallerURL))
            {
                this.UpgradeVersionDescription.NeedToUpgrade = false;
                Debug.LogError("当前版本尚未发布安装包，无法下载更新。请等待项目发布安装包后再试。");
                return;
            }

            if (!Uri.TryCreate(this.UpgradeVersionDescription.InstallerURL, UriKind.Absolute, out _))
            {
                this.UpgradeVersionDescription.NeedToUpgrade = false;
                Debug.LogError("更新安装包地址无效，无法下载。请检查项目发布的版本说明。");
                return;
            }

            try
            {
                this.UpgradeVersionDescription.NeedToUpgrade = true;
                await this.DownloadInstaller();
            }
            catch (Exception exception)
            {
                Debug.Log($"下载更新失败：{exception.Message}");
            }
        }

        public void CancelUpgrade()
        {
            if (this.UpgradeVersionDescription == null)
            {
                Debug.Log("没有正在进行的更新，无法取消。");
                return;
            }

            Debug.Log("取消应用程序更新。");
            this.UpgradeVersionDescription.NeedToUpgrade = false;
            this.webClient.CancelAsync();
        }

        public void Dispose()
        {
            this.webClient.Dispose();
        }

        private async Task<UpgradeVersionDescription> DownloadLatestVersionDescription()
        {
#if BUILD32
            Uri uri = new Uri(UpgradeService.BaseURI + "version (x86).xml");
#else
            Uri uri = new Uri(UpgradeService.BaseURI + "version.xml");
#endif

            UpgradeVersionDescription description = null;
            try
            {
                XmlRootAttribute xmlRoot = new XmlRootAttribute
                {
                    ElementName = "Version"
                };

                XmlSerializer serializer = new XmlSerializer(typeof(UpgradeVersionDescription), xmlRoot);

                XmlReaderSettings xmlReaderSettings = new XmlReaderSettings
                {
                    IgnoreWhitespace = true,
                    IgnoreComments = true
                };

                using (WebClient client = new WebClient())
                using (Stream stream = await client.OpenReadTaskAsync(uri))
                using (XmlReader xmlReader = XmlReader.Create(stream, xmlReaderSettings))
                {
                    description = (UpgradeVersionDescription)serializer.Deserialize(xmlReader);
                }
            }
            catch (Exception)
            {
                Debug.Log("获取最新版本信息失败。");
                return null;
            }

            return description;
        }

        private async Task DownloadInstaller()
        {
            if (this.UpgradeVersionDescription == null)
            {
                throw new ArgumentNullException(nameof(this.UpgradeVersionDescription));
            }

            if (this.UpgradeVersionDescription.InstallerDownloadInProgress)
            {
                throw new Exception("安装包正在下载。");
            }

            Uri uri = new Uri(this.UpgradeVersionDescription.InstallerURL, UriKind.Absolute);

            string fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = "FileConverter-setup.msi";
            }

            string tempPath = System.IO.Path.GetTempPath();
            string installerPath = System.IO.Path.Combine(tempPath, fileName);

            this.UpgradeVersionDescription.InstallerPath = installerPath;
            this.UpgradeVersionDescription.InstallerDownloadInProgress = true;
            this.UpgradeVersionDescription.InstallerDownloadProgress = 0;

            // 参考来源： https://stackoverflow.com/questions/2859790/the-request-was-aborted-could-not-create-ssl-tls-secure-channel#2904963
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            this.webClient.DownloadProgressChanged += this.WebClient_DownloadProgressChanged;

            try
            {
                // 退出流程可能等待下载；完成状态不能依赖已停止的界面调度器。
                await Task.Run(() => this.webClient.DownloadFileTaskAsync(uri, installerPath)).ConfigureAwait(false);

                this.UpgradeVersionDescription.InstallerDownloadProgress = 100;
            }
            catch (Exception exception)
            {
                if (exception is OperationCanceledException ||
                    (exception is WebException webException && webException.Status == WebExceptionStatus.RequestCanceled))
                {
                    return;
                }

                Debug.LogError("下载 File Converter 更新失败，请重试或手动下载。");
                Debug.Log(exception.ToString());
                this.UpgradeVersionDescription.NeedToUpgrade = false;
            }

            finally
            {
                this.UpgradeVersionDescription.InstallerDownloadInProgress = false;
                this.webClient.DownloadProgressChanged -= this.WebClient_DownloadProgressChanged;
            }
        }

        private void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs eventArgs)
        {
            this.UpgradeVersionDescription.InstallerDownloadProgress = eventArgs.ProgressPercentage;
        }
    }
}
