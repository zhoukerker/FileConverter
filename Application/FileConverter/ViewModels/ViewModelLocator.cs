// <copyright file="ViewModelLocator.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

/*
  在 Application.xaml 中：
  <Application.Resources>
      <vm:ViewModelLocator xmlns:vm="clr-namespace:FileConverter"
                           x:Key="Locator" />
  </Application.Resources>

  在视图中：
  DataContext="{Binding Source={StaticResource Locator}, Path=ViewModelName}"
*/

namespace FileConverter.ViewModels
{
    using CommunityToolkit.Mvvm.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// 保存应用中的视图模型引用，
    /// 并提供视图绑定的访问入口。
    /// </summary>
    public class ViewModelLocator
    {
        /// <summary>
        /// 初始化视图模型定位器。
        /// </summary>
        public ViewModelLocator()
        {
        }

        public HelpViewModel Help => Ioc.Default.GetRequiredService<HelpViewModel>();

        public MainViewModel Main => Ioc.Default.GetRequiredService<MainViewModel>();

        public UpgradeViewModel Upgrade => Ioc.Default.GetRequiredService<UpgradeViewModel>();

        public SettingsViewModel Settings => Ioc.Default.GetRequiredService<SettingsViewModel>();

        public DiagnosticsViewModel Diagnostics => Ioc.Default.GetRequiredService<DiagnosticsViewModel>();

        internal void RegisterViewModels(ServiceCollection services)
        {
            services
                .AddSingleton<HelpViewModel>()
                .AddSingleton<MainViewModel>()
                .AddSingleton<UpgradeViewModel>()
                .AddSingleton<SettingsViewModel>()
                .AddSingleton<DiagnosticsViewModel>();
        }
    }
}
