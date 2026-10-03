param(
    [string]$BuildDirectory = (Join-Path $PSScriptRoot '..\build\artifacts\Release'),
    [string]$BaselineDirectory = (Join-Path $PSScriptRoot '..\build\baseline-source\Application\FileConverter\bin\x64\Release'),
    [string]$OutputDirectory,
    [string]$CompilerPath,
    [switch]$CompareBaseline,
    [switch]$BaselineOnly,
    [switch]$Preview,
    [int]$PreviewSeconds = 0
)

$ErrorActionPreference = 'Stop'
$uiRepository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$uiBuild = [System.IO.Path]::GetFullPath($(if ($BaselineOnly) { $BaselineDirectory } else { $BuildDirectory }))
if (!(Test-Path -LiteralPath (Join-Path $uiBuild 'FileConverter.exe'))) {
    throw "找不到已构建的 FileConverter.exe：$uiBuild"
}
if ([string]::IsNullOrEmpty($CompilerPath)) {
    $uiVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $uiVsWhere) {
        $CompilerPath = (& $uiVsWhere -latest -products '*' -find 'MSBuild\**\Roslyn\csc.exe' | Select-Object -First 1)
    }
}
if ([string]::IsNullOrEmpty($CompilerPath) -or !(Test-Path -LiteralPath $CompilerPath)) {
    throw '未找到现代 C# 编译器，请通过 -CompilerPath 指定路径。'
}
if ([string]::IsNullOrEmpty($OutputDirectory)) {
    $OutputDirectory = Join-Path $uiRepository ('build\ui-regression-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$uiOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($uiOutput) | Out-Null
$uiFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$uiNuGetRoot = if ([string]::IsNullOrEmpty($env:NUGET_PACKAGES)) { Join-Path $env:USERPROFILE '.nuget\packages' } else { $env:NUGET_PACKAGES }
$uiFacadeCandidates = @(
    (Join-Path $uiNuGetRoot 'microsoft.netframework.referenceassemblies.net48\1.0.3\build\.NETFramework\v4.8\Facades\netstandard.dll'),
    (Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll'),
    (Join-Path $uiFramework 'Facades\netstandard.dll'),
    (Join-Path (Split-Path $CompilerPath) 'netstandard.dll')
)
$uiFacade = $uiFacadeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$uiFacade) { throw '找不到 .NET Standard 兼容程序集，请先构建项目并准备 .NET Framework 开发工具包。' }
$uiReferences = @(
    (Join-Path $uiBuild 'FileConverter.exe'),
    (Join-Path $uiBuild 'CommunityToolkit.Mvvm.dll'),
    (Join-Path $uiFramework 'WPF\WindowsBase.dll'),
    (Join-Path $uiFramework 'WPF\PresentationCore.dll'),
    (Join-Path $uiFramework 'WPF\PresentationFramework.dll'),
    (Join-Path $uiFramework 'System.Xaml.dll'),
    (Join-Path $uiFramework 'Facades\System.Runtime.dll'),
    $uiFacade
)
$uiTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$uiRunner = Join-Path $uiTempRoot ('FileConverter-Ui-' + [Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($uiRunner) | Out-Null
$uiExecutable = Join-Path $uiRunner 'UiRegression.exe'
$uiCompilerArguments = @('/nologo', '/noconfig', '/target:exe', '/platform:x64', '/optimize+', '/langversion:latest', '/reference:System.dll', '/reference:System.Core.dll', "/out:$uiExecutable")
foreach ($uiReference in $uiReferences) {
    if (Test-Path -LiteralPath $uiReference) { $uiCompilerArguments += "/reference:$uiReference" }
}
$uiCompilerArguments += (Join-Path $PSScriptRoot 'UiRegression.cs')
$uiPreviousBuild = $env:FILE_CONVERTER_UI_BUILD
try {
    & $CompilerPath @uiCompilerArguments
    if ($LASTEXITCODE -ne 0) { throw '界面回归程序编译失败。' }
    $env:FILE_CONVERTER_UI_BUILD = $uiBuild
    $uiArguments = @((Join-Path $uiOutput $(if ($BaselineOnly) { 'baseline.txt' } else { 'optimized.txt' })))
    if ($BaselineOnly) { $uiArguments += '--baseline' }
    if ($Preview) { $uiArguments += '--preview' }
    if ($PreviewSeconds -gt 0) { $uiArguments += @('--preview-seconds', $PreviewSeconds.ToString()) }
    & $uiExecutable @uiArguments
    if ($LASTEXITCODE -ne 0) { throw '优化版真实界面回归失败。' }

    if ($CompareBaseline -and !$BaselineOnly) {
        $uiBaseline = [System.IO.Path]::GetFullPath($BaselineDirectory)
        if (!(Test-Path -LiteralPath (Join-Path $uiBaseline 'FileConverter.exe'))) {
            throw "找不到基线构建：$uiBaseline"
        }
        # 新进程加载基线程序集，避免混用 WPF 资源和静态服务容器。
        $env:FILE_CONVERTER_UI_BUILD = $uiBaseline
        & $uiExecutable (Join-Path $uiOutput 'baseline.txt') --baseline
        if ($LASTEXITCODE -ne 0) { throw '基线真实界面回归失败。' }
    }
}
finally {
    $env:FILE_CONVERTER_UI_BUILD = $uiPreviousBuild
    $uiResolvedRunner = [System.IO.Path]::GetFullPath($uiRunner)
    if ($uiResolvedRunner.StartsWith($uiTempRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
        ([System.IO.Path]::GetFileName($uiResolvedRunner)).StartsWith('FileConverter-Ui-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $uiResolvedRunner -Recurse -Force
    }
}
Write-Output "真实界面验证结果：$uiOutput"
