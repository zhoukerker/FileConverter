// <copyright file="MainViewModel.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ViewModels
{
    using System;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Threading;
    using System.Windows;
    using System.Windows.Input;
    using System.Windows.Threading;

    using CommunityToolkit.Mvvm.ComponentModel;
    using CommunityToolkit.Mvvm.DependencyInjection;
    using CommunityToolkit.Mvvm.Input;

    using FileConverter.ConversionJobs;
    using FileConverter.Services;

    using Application = FileConverter.Application;

    /// <summary>
    /// 提供主视图的数据绑定属性。
    /// </summary>
    public class MainViewModel : ObservableRecipient
    {
        private static readonly PropertyChangedEventArgs ConversionJobsChangedEventArgs = new PropertyChangedEventArgs(nameof(ConversionJobs));
        private readonly Dispatcher dispatcher;
        private readonly Action notifyConversionJobsChanged;
        private string informationMessage;
        private int conversionJobsUpdatePending;

        private RelayCommand showSettingsCommand;
        private RelayCommand showDiagnosticsCommand;
        private RelayCommand<CancelEventArgs> closeCommand;

        /// <summary>
        /// 初始化主视图模型。
        /// </summary>
        public MainViewModel()
        {
            IConversionService conversionService = Ioc.Default.GetRequiredService<IConversionService>();
            this.ConversionJobs = new ObservableCollection<ConversionJob>(conversionService.ConversionJobs);
            foreach (ConversionJob job in this.ConversionJobs)
            {
                PropertyChangedEventManager.AddHandler(job, this.ConversionJob_PropertyChanged, string.Empty);
            }

            Application application = Application.Current as Application;
            this.dispatcher = application.Dispatcher;
            this.notifyConversionJobsChanged = this.NotifyConversionJobsChanged;
            WeakEventManager<Application, ApplicationTerminateArgs>.AddHandler(application, nameof(Application.OnApplicationTerminate), this.Application_OnApplicationTerminate);
        }

        public string InformationMessage
        {
            get => this.informationMessage;

            private set
            {
                this.SetProperty(ref this.informationMessage, value);
            }
        }

        public ObservableCollection<ConversionJob> ConversionJobs { get; }

        public ICommand ShowSettingsCommand
        {
            get
            {
                if (this.showSettingsCommand == null)
                {
                    this.showSettingsCommand = new RelayCommand(() => Ioc.Default.GetRequiredService<INavigationService>().Show(Pages.Settings));
                }

                return this.showSettingsCommand;
            }
        }

        public ICommand ShowDiagnosticsCommand
        {
            get
            {
                if (this.showDiagnosticsCommand == null)
                {
                    this.showDiagnosticsCommand = new RelayCommand(() => Ioc.Default.GetRequiredService<INavigationService>().Show(Pages.Diagnostics));
                }

                return this.showDiagnosticsCommand;
            }
        }

        public ICommand CloseCommand
        {
            get
            {
                if (this.closeCommand == null)
                {
                    this.closeCommand = new RelayCommand<CancelEventArgs>(this.Close);
                }

                return this.closeCommand;
            }
        }

        private void Close(CancelEventArgs args)
        {
            INavigationService navigationService = Ioc.Default.GetRequiredService<INavigationService>();
            navigationService.Close(Pages.Main, args != null);
        }

        private void ConversionJob_PropertyChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName != nameof(ConversionJob.State) && eventArgs.PropertyName != nameof(ConversionJob.Progress))
            {
                return;
            }

            if (!this.dispatcher.HasShutdownStarted && Interlocked.Exchange(ref this.conversionJobsUpdatePending, 1) == 0)
            {
                // 合并后台线程的连续进度通知，避免重复扫描整个任务队列。
                this.dispatcher.BeginInvoke(DispatcherPriority.Background, this.notifyConversionJobsChanged);
            }
        }

        private void NotifyConversionJobsChanged()
        {
            Interlocked.Exchange(ref this.conversionJobsUpdatePending, 0);
            this.OnPropertyChanged(ConversionJobsChangedEventArgs);
        }

        private void Application_OnApplicationTerminate(object sender, ApplicationTerminateArgs eventArgs)
        {
            string message;
            if (float.IsNaN(eventArgs.RemainingTimeBeforeTermination))
            {
                message = string.Empty;
            }
            else
            {
                int remainingSeconds = (int)eventArgs.RemainingTimeBeforeTermination;
                message = remainingSeconds >= 2
                    ? string.Format(Properties.Resources.ApplicationWillTerminateInMultipleSeconds, remainingSeconds)
                    : remainingSeconds == 1
                        ? Properties.Resources.ApplicationWillTerminateInOneSecond
                        : Properties.Resources.ApplicationIsTerminating;
            }

            if (this.dispatcher.CheckAccess())
            {
                this.InformationMessage = message;
            }
            else if (!this.dispatcher.HasShutdownStarted)
            {
                this.dispatcher.BeginInvoke((Action)(() => this.InformationMessage = message));
            }
        }
    }
}
