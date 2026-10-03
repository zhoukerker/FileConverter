param(
    [Parameter(Mandatory = $true)]
    [string]$BaselineDirectory,
    [Alias('BuildDirectory')]
    [string]$CurrentDirectory = (Join-Path $PSScriptRoot '..\build\artifacts\Release')
)

$ErrorActionPreference = 'Stop'
foreach ($coreBuildDirectory in @($BaselineDirectory, $CurrentDirectory)) {
    if (-not (Test-Path -LiteralPath (Join-Path $coreBuildDirectory 'FileConverter.exe') -PathType Leaf)) {
        throw "找不到主程序：$coreBuildDirectory。请先构建原版和当前 Release 版本。"
    }
}
$coreBaseline = (Resolve-Path -LiteralPath $BaselineDirectory).Path
$coreCurrent = (Resolve-Path -LiteralPath $CurrentDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $coreCurrent 'FileConverter.exe.config') -PathType Leaf)) {
    throw '当前构建缺少 FileConverter.exe.config，无法验证实际的程序集绑定配置。'
}
$coreVswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $coreVswhere -PathType Leaf)) {
    throw '未找到 Visual Studio 的安装定位工具，请安装包含 MSBuild 的 Visual Studio 或 Build Tools。'
}
$coreCompiler = & $coreVswhere -latest -products '*' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($coreCompiler) -or -not (Test-Path -LiteralPath $coreCompiler -PathType Leaf)) {
    throw '未找到 Visual Studio Roslyn C# 编译器，请安装 .NET 桌面开发构建工具。'
}
$coreTemporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$coreTemporaryDirectory = Join-Path $coreTemporaryRoot ('FileConverter-core-regression-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $coreTemporaryDirectory | Out-Null
try {
    $coreExecutable = Join-Path $coreTemporaryDirectory 'CoreRegression.exe'
    & $coreCompiler /nologo /optimize+ "/out:$coreExecutable" (Join-Path $PSScriptRoot 'CoreRegression.cs')
    if ($LASTEXITCODE -ne 0) {
        throw "核心回归测试编译失败，退出码 $LASTEXITCODE。"
    }

    $coreRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $coreNuget = if ([string]::IsNullOrEmpty($env:NUGET_PACKAGES)) { Join-Path $env:USERPROFILE '.nuget\packages' } else { $env:NUGET_PACKAGES }
    $coreFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $coreFacade = @(
        (Join-Path $coreNuget 'microsoft.netframework.referenceassemblies.net48\1.0.3\build\.NETFramework\v4.8\Facades\netstandard.dll'),
        (Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll'),
        (Join-Path $coreFramework 'Facades\netstandard.dll')
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $coreFacade) {
        throw '缺少用于编译实际升级服务源码的 .NET Standard 引用程序集，请先恢复项目依赖或安装 .NET Framework 开发工具包。'
    }
    $coreBoundary = Join-Path $coreTemporaryDirectory 'UpgradeFailureBoundary.dll'
    $coreBoundaryArguments = @(
        '/nologo', '/target:library', '/optimize+', '/define:UPGRADE_BOUNDARY',
        "/out:$coreBoundary",
        "/reference:$coreCurrent\CommunityToolkit.Mvvm.dll",
        "/reference:$coreFacade",
        ("/reference:" + (Join-Path (Split-Path $coreFacade -Parent) 'System.Runtime.dll')),
        (Join-Path $PSScriptRoot 'CoreRegression.cs'),
        (Join-Path $coreRepository 'Application\FileConverter\Services\UpgradeService.cs'),
        (Join-Path $coreRepository 'Application\FileConverter\Services\UpgradeVersionDescription.cs'),
        (Join-Path $coreRepository 'Application\FileConverter\Services\IUpgradeService.cs'),
        (Join-Path $coreRepository 'Application\FileConverter\Version.cs')
    )
    & $coreCompiler @coreBoundaryArguments
    if ($LASTEXITCODE -ne 0) {
        throw "升级失败边界测试编译失败，退出码 $LASTEXITCODE。"
    }
    Write-Host '正在比较真实主程序并验证本地升级下载、取消及语言资源；测试不会执行安装器。'
    & $coreExecutable $coreBaseline $coreCurrent $coreTemporaryDirectory $coreBoundary
    if ($LASTEXITCODE -ne 0) {
        throw "核心回归测试失败，退出码 $LASTEXITCODE。"
    }
    Write-Host '核心回归测试全部通过。'
}
finally {
    # 清理前确认本次生成的绝对路径仍属于系统临时目录。
    $coreResolvedDirectory = [IO.Path]::GetFullPath($coreTemporaryDirectory)
    if (-not $coreResolvedDirectory.StartsWith($coreTemporaryRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理临时目录外的路径：$coreResolvedDirectory。"
    }
    Remove-Item -LiteralPath $coreResolvedDirectory -Recurse -Force
}
