# 文件转换器 · 简体中文分支

通过 Windows 资源管理器右键菜单转换和压缩图片、音视频、PDF 与 Office 文档。本分支面向中国用户和开发者，仅提供简体中文，基于 [Tichau/FileConverter](https://github.com/Tichau/FileConverter) 开发。

![右键转换操作示例](Resources/FileConverterUsage.gif)

## 分支特点

- 界面、右键菜单、安装提示、项目说明和手写代码注释采用简体中文。
- 默认预设显示中文，存储名称、命令行参数和自定义预设名称保持兼容；旧配置的语言设置自动归一为简体中文。
- 合并 Office 转换的公共流程，精简资源访问器和工程文件，移除多语言资源与无用的语言选择代码。
- 复用转换线程、菜单位图和序列化器，使用任务列表虚拟化、事件进度通知与日志缓冲，减少等待和分配。
- 保留转换格式、编码参数、默认质量与 Q16 图像精度，完善取消、资源释放和源文件保护。

当前源码沿用 `2.2.0` 版本号。本次中文化与进一步精简尚未构建或运行验证，需要由你构建后测试；此前优化的历史测量及验证范围见 [优化说明](优化说明.md)。

## 一键构建

开发环境需要 Windows x64、Visual Studio 2022 或其 Build Tools 的“.NET 桌面开发”组件，以及 .NET 8 SDK。应用运行需要 .NET Framework 4.8。WiX 5 和编译用引用程序集随依赖还原获取，无须手工安装 WiX 插件或配置签名证书。第一次构建需要能够访问 NuGet。

在仓库根目录双击 [一键构建.cmd](一键构建.cmd)，默认构建 x64 Release 主程序、右键扩展和 MSI。脚本不会安装、注册扩展、启动程序或执行测试。

输出统一写入 `build\artifacts\Release`，主程序为其中的 `FileConverter.exe`；安装包通常位于 `zh-CN\FileConverter-setup.msi`，脚本会打印实际位置。需要调试版本或仅构建应用时，在根目录终端使用：

- 调试版：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\Build.ps1 -Configuration Debug`
- 仅应用和扩展：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\Build.ps1 -ApplicationOnly`
- 指定 SDK：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\Build.ps1 -SdkDirectory "D:\dotnet\sdk\8.0.303"`

`-SdkDirectory` 指向包含 `Sdks` 子目录的 SDK 版本目录。脚本优先发现仓库内的 `build\dotnet-sdk\sdk`，否则使用系统 SDK；SDK 定位只作用于当前构建进程。构建失败时会保留错误输出并返回失败状态，详细日志写入对应输出目录的 `build.log`。

## 使用与测试

安装自己构建的 MSI 后，在资源管理器中选择文件，打开“文件转换器”菜单并选择预设。直接启动主程序可查看使用说明；启动 `FileConverter.exe --settings` 可编辑预设。

Office 文档转换依赖对应的 Microsoft Word、Excel 或 PowerPoint；音轨抽取需要可用光驱和音频 CD。应用沿用现有转换引擎，不需要额外下载安装 FFmpeg 或 Ghostscript。

建议依次检查：

1. 简体中文界面、右键菜单、默认预设、设置页及安装提示。
2. 旧配置导入、自定义中文名称、预设编辑和移动、现有命令行调用。
3. 常用图片、音视频和多页 PDF 的输出、质量及文件名。
4. 批量转换、并发上限、取消、失败，以及源文件删除或归档设置。先用文件副本验证后处理。
5. 对应 Office 软件与音频 CD 的转换，以及安装、卸载和旧版升级路径。同版本 `2.2.0` 的测试安装先卸载旧版；正式发布应统一提高版本号。

可选自动回归脚本位于 `Tests`，默认读取本次构建的独立输出目录；一键构建不调用它们。开发约定和回归入口见 [开发指南](开发指南.md)。

## 更新与反馈

[问题反馈](https://github.com/zhoukerker/FileConverter/issues)和更新检查均指向本分支。版本说明文件 `version.xml` 与 `version (x86).xml` 暂未填写安装包链接；发布时需要填写实际地址和版本号。默认一键构建仅支持 x64，沿用的 x86 工程配置不代表本分支已发布或验证 x86 版本。

目前没有通过本轮构建验证的下载包。仓库旧构建目录中的程序或 MSI 属于此前源码状态，应以一键脚本的新输出作为测试对象。

## 依赖与致谢

保留原作者 Adrien Allard（Tichau）及上游贡献者的归属。感谢 Snoopy1866、jie65535 的简体中文翻译贡献，以及上游各语言贡献者 Khidreal、hugok79、Marhc、Chachak、Davide、nikotschierske、MayaC0re、vishveshjain、Mahmoud0Sultan、Sedimentary-Rock、NeKoOuO、PeterDaveHello、CrisBalGreece、AshiVered、MrHero118、Mehrdad32、crnobog69、oogamiyuta、AidyTheWeird、Alanimdeo、vrykolakas166、thaovd、iliamak、itsmefdil、hamzaharoon1314、Zyvrec7、stohlferenc、Maerek、MrPrince419、rkalitta。

主要组件包括 [FFmpeg](https://ffmpeg.org)、[ImageMagick / Magick.NET](https://github.com/dlemstra/Magick.NET)、[Ghostscript](https://www.ghostscript.com)、[SharpShell](https://github.com/dwmkerr/sharpshell)、[NetOffice](https://github.com/NetOfficeFw/NetOffice)、[Markdown.XAML](https://github.com/theunrepentantgeek/Markdown.XAML)、[WpfAnimatedGif](https://github.com/XamlAnimatedGif/WpfAnimatedGif)，以及 Idael Cardoso 的 Ripper、yeti.mmedia。具体依赖版本见工程文件与锁文件。

现有 `Magick.NET-Q16-AnyCPU 14.10.2` 的依赖还原会报告安全公告，本次保留版本以维持输出兼容；后续升级需单独验证格式与质量。

## 许可证

项目沿用 GPL 第 3 版，见 [LICENSE.md](LICENSE.md)。原许可证、版权声明和第三方归属保持原文；中文说明用于项目维护。上游完整历史可以从 [上游仓库](https://github.com/Tichau/FileConverter)及 Git 历史查阅。
