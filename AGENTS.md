# Repository Guidelines

## 项目结构与模块职责

- `Application/FileConverter/`：.NET Framework 4.8 WPF 主程序；转换实现在 `ConversionJobs/`，服务、视图模型和界面分别位于 `Services/`、`ViewModels/`、`Views/`。
- `Application/FileConverterExtension/`：资源管理器右键扩展及共享预设显示规则。
- `Installer/`：WiX 安装清单；`Middleware/`：转换工具与运行库；`Resources/`：项目图标、图片及安装素材。
- `Tests/`：独立 C# 回归程序和 PowerShell 入口；`eng/Build.ps1` 与 `Directory.Build.props` 管理构建。详细架构见 [开发指南](开发指南.md)。

## 构建、测试与本地运行

需要 Windows x64、Visual Studio 2022 的“.NET 桌面开发”组件和 .NET 8 SDK；运行环境为 .NET Framework 4.8。以下命令均在仓库根目录执行：

- `.\一键构建.cmd`：构建 Release 应用、扩展和 MSI，不自动安装或测试。
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\Build.ps1 -ApplicationOnly`：仅构建应用及扩展；添加 `-Configuration Debug` 可生成调试版。
- `.\build\artifacts\Release\FileConverter.exe --settings`：打开本地设置界面。
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Run-ConversionRegression.ps1 -IncludeLifecycleChecks`：验证转换、取消和资源清理。

输出与 `build.log` 位于 `build/artifacts/<Configuration>/`。其余回归入口及环境要求见开发指南。

## 编码风格与命名

C# 使用四空格缩进；XML、XAML、工程文件及 YAML 使用两空格。编码、换行和尾部空白遵循 `.editorconfig`、`.gitattributes`，提交前检查 `git diff --check`。类型、方法和公开属性采用 `PascalCase`，局部变量与私有字段采用 `camelCase`，延续相邻代码风格。

文档、手写注释、用户文案及提交说明使用简体中文；标识符、资源键、CLI 参数和 XML 字段保持英文。字符串集中在 `Properties/Resources.resx`，新增键时同步维护简洁访问器。优先合并重复流程，保留异常处理和资源释放。

## 测试约定

现有测试使用独立 C# 程序与断言，由 PowerShell 编译并运行；不使用 xUnit 或 NUnit，也未设置数值覆盖率门槛。文件命名为 `*Regression.cs`、`Run-*Regression.ps1`。

按改动范围选择调度、日志、转换、界面、安装包或兼容回归；新增用例覆盖正常与失败边界。核心及扩展等价测试需分别提供原版目录或 DLL。界面测试需要可交互桌面，Office 与光驱功能需对应环境；转换输出使用新的空目录。明确区分静态检查、构建成功和运行验证。

## 提交与拉取请求

上游历史混用 `feat:`、`Chore:`、`Fixes:` 和普通描述。本分支使用简短中文说明，可写 `fix: 修复取消时的临时文件清理`、`refactor: 合并 Office 导出流程`。每次提交围绕一个目的，排除 `bin/`、`obj/`、`build/` 等产物。

PR 说明问题、行为变化、兼容影响和验证命令及结果，关联相关问题；界面变化附截图，性能声明注明输入、基线与测量范围。未运行的检查及环境限制需明确说明。

## 兼容性与发布配置

仅提供简体中文；默认预设通过 `PresetDisplayNames` 翻译显示，保留存储名称、命令行名称和自定义名称。保持转换质量及源文件保护语义。依赖变化同时审阅锁文件，不屏蔽安全警告。

保留 GPL 和第三方归属；证书及 `Installer/Installer.sign` 不提交。正式发布统一版本号并填写实际安装包 URL，保留安装身份与跨语言升级兼容；同版本测试先卸载旧版。
