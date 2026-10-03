param(
    [string]$BuildDirectory = (Join-Path $PSScriptRoot '..\build\artifacts\Release'),
    [string]$OutputDirectory,
    [string]$CompilerPath,
    [switch]$IncludeLifecycleChecks,
    [switch]$PdfMetadataBenchmark
)

$ErrorActionPreference = 'Stop'
$conversionRepository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$conversionBuild = [System.IO.Path]::GetFullPath($BuildDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $conversionBuild 'FileConverter.exe'))) {
    throw "找不到已构建的 FileConverter.exe：$conversionBuild"
}
if ([string]::IsNullOrEmpty($CompilerPath)) {
    $conversionVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $conversionVsWhere) {
        $CompilerPath = & $conversionVsWhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
    }
}
if ([string]::IsNullOrEmpty($CompilerPath) -or -not (Test-Path -LiteralPath $CompilerPath)) {
    throw "找不到现代 C# 编译器，请通过 -CompilerPath 指定其路径。"
}
if ([string]::IsNullOrEmpty($OutputDirectory)) {
    $OutputDirectory = Join-Path $conversionRepository ('build\conversion-regression-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$conversionOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($conversionOutput) | Out-Null

# 验证使用仓库随附的原生程序，不改变系统安装或用户配置。
foreach ($conversionNative in @(
    @{ Source = 'Middleware\ffmpeg\ffmpeg.exe'; Target = 'ffmpeg.exe' },
    @{ Source = 'Middleware\gs\gsdll64.dll'; Target = 'gsdll64.dll' },
    @{ Source = 'Middleware\gs\gswin64c.exe'; Target = 'gswin64c.exe' }
)) {
    $conversionNativeTarget = Join-Path $conversionBuild $conversionNative.Target
    if (-not (Test-Path -LiteralPath $conversionNativeTarget)) {
        Copy-Item -LiteralPath (Join-Path $conversionRepository $conversionNative.Source) -Destination $conversionNativeTarget
    }
}

$conversionFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$conversionNuGetRoot = if ([string]::IsNullOrEmpty($env:NUGET_PACKAGES)) { Join-Path $env:USERPROFILE '.nuget\packages' } else { $env:NUGET_PACKAGES }
$conversionFacadeCandidates = @(
    (Join-Path $conversionNuGetRoot 'microsoft.netframework.referenceassemblies.net48\1.0.3\build\.NETFramework\v4.8\Facades\netstandard.dll'),
    (Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll'),
    (Join-Path $conversionFramework 'Facades\netstandard.dll'),
    (Join-Path (Split-Path $CompilerPath) 'netstandard.dll')
)
$conversionSdkRoot = Join-Path $conversionRepository 'build\dotnet-sdk\sdk'
if (Test-Path -LiteralPath $conversionSdkRoot) {
    foreach ($conversionSdk in (Get-ChildItem -LiteralPath $conversionSdkRoot -Directory | Sort-Object Name -Descending)) {
        $conversionFacadeCandidates += (Join-Path $conversionSdk.FullName 'TestHostNetFramework\netstandard.dll')
    }
}
$conversionFacade = $conversionFacadeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $conversionFacade) {
    throw '找不到 .NET Standard 兼容程序集，请安装 .NET Framework 开发工具包或先准备本地 SDK。'
}
$conversionReferences = @(
    (Join-Path $conversionBuild 'FileConverter.exe'),
    (Join-Path $conversionBuild 'CommunityToolkit.Mvvm.dll'),
    (Join-Path $conversionBuild 'Magick.NET-Q16-AnyCPU.dll'),
    (Join-Path $conversionBuild 'Magick.NET.Core.dll'),
    (Join-Path $conversionFramework 'System.Drawing.dll'),
    (Join-Path $conversionFramework 'WPF\WindowsBase.dll'),
    (Join-Path $conversionFramework 'WPF\PresentationFramework.dll'),
    $conversionFacade,
    (Join-Path $conversionFramework 'Facades\System.Runtime.dll')
)
$conversionRunnerRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$conversionRunnerDirectory = Join-Path $conversionRunnerRoot ('FileConverterRegression-' + [System.Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($conversionRunnerDirectory) | Out-Null
$conversionTestExecutable = Join-Path $conversionRunnerDirectory 'ConversionRegression.exe'
$conversionCompilerArguments = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/langversion:latest', "/out:$conversionTestExecutable")
foreach ($conversionReference in $conversionReferences) {
    if (Test-Path -LiteralPath $conversionReference) {
        $conversionCompilerArguments += "/reference:$conversionReference"
    }
}
$conversionCompilerArguments += (Join-Path $PSScriptRoot 'ConversionRegression.cs')
$conversionPreviousBuild = $env:FILE_CONVERTER_TEST_BUILD
try {
    & $CompilerPath @conversionCompilerArguments
    if ($LASTEXITCODE -ne 0) {
        throw '转换回归验证程序编译失败。'
    }

    $conversionRunArguments = @($conversionOutput)
    if ($IncludeLifecycleChecks) {
        $conversionRunArguments += '--lifecycle'
    }
    if ($PdfMetadataBenchmark) {
        $conversionRunArguments += '--pdf-metadata-benchmark'
    }
    $env:FILE_CONVERTER_TEST_BUILD = $conversionBuild
    & $conversionTestExecutable @conversionRunArguments
    if ($LASTEXITCODE -ne 0) {
        throw "转换回归验证失败，详情位于：$conversionOutput"
    }
}
finally {
    $env:FILE_CONVERTER_TEST_BUILD = $conversionPreviousBuild
    # 仅清理本次创建且位于临时目录中的验证程序。
    $conversionResolvedRunner = [System.IO.Path]::GetFullPath($conversionRunnerDirectory)
    if (-not $conversionResolvedRunner.StartsWith($conversionRunnerRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw '验证程序清理路径超出临时目录。'
    }
    Remove-Item -LiteralPath $conversionResolvedRunner -Recurse -Force
}
Write-Output "转换回归验证完成，输出与哈希清单：$conversionOutput"
