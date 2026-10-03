param(
    [Parameter(Mandatory = $true)]
    [string]$BaselineAssembly,
    [string]$CurrentAssembly = (Join-Path $PSScriptRoot '..\build\artifacts\Release\FileConverterExtension.dll'),
    [string]$DefaultSettings = (Join-Path $PSScriptRoot '..\Application\FileConverter\Settings.default.xml')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $BaselineAssembly -PathType Leaf)) {
    throw "找不到原版扩展 DLL：$BaselineAssembly。请先构建优化前的 Release 版本，然后通过 -BaselineAssembly 指定完整路径。"
}
if (-not (Test-Path -LiteralPath $CurrentAssembly -PathType Leaf)) {
    throw "找不到新版扩展 DLL：$CurrentAssembly。请先构建当前项目，然后通过 -CurrentAssembly 指定完整路径。"
}
if (-not (Test-Path -LiteralPath $DefaultSettings -PathType Leaf)) {
    throw "找不到默认配置 XML：$DefaultSettings。"
}

$baselinePath = (Resolve-Path -LiteralPath $BaselineAssembly).Path
$currentPath = (Resolve-Path -LiteralPath $CurrentAssembly).Path
$settingsPath = (Resolve-Path -LiteralPath $DefaultSettings).Path
$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswherePath -PathType Leaf)) {
    throw '未找到 Visual Studio 的安装定位工具，请安装包含 MSBuild 的 Visual Studio 或 Build Tools。'
}
$compilerPath = & $vswherePath -latest -products '*' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($compilerPath) -or -not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
    throw '未找到 Visual Studio Roslyn C# 编译器，请安装 .NET 桌面开发构建工具。'
}

$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('FileConverter-extension-regression-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
try {
    $testExecutable = Join-Path $temporaryDirectory 'ExtensionRegression.exe'
    $compilerArguments = @(
        '/nologo',
        '/optimize+',
        '/reference:System.Windows.Forms.dll',
        '/reference:System.Drawing.dll',
        "/out:$testExecutable",
        (Join-Path $PSScriptRoot 'ExtensionRegression.cs')
    )
    & $compilerPath @compilerArguments
    if ($LASTEXITCODE -ne 0) {
        throw "扩展回归测试编译失败，退出码 $LASTEXITCODE。"
    }

    Write-Host '正在使用独立应用域比较原版与新版扩展，测试不会安装或注册右键菜单。'
    & $testExecutable $baselinePath $currentPath $settingsPath $temporaryDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "扩展回归测试失败，退出码 $LASTEXITCODE。"
    }
    Write-Host '扩展回归测试全部通过。'
}
finally {
    # 仅删除本次在系统临时目录内创建的文件夹。
    $resolvedTemporaryPath = [IO.Path]::GetFullPath($temporaryDirectory)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedTemporaryPath.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理临时目录外的路径：$resolvedTemporaryPath。"
    }
    Remove-Item -LiteralPath $resolvedTemporaryPath -Recurse -Force
}
