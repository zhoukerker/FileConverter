param([string]$CompilerPath)

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($CompilerPath)) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $taskVsWhere) {
        $CompilerPath = (& $taskVsWhere -latest -products '*' -find 'MSBuild\**\Roslyn\csc.exe' | Select-Object -First 1)
    }
    if ([string]::IsNullOrEmpty($CompilerPath)) {
        $taskCompiler = Get-Command csc.exe -ErrorAction SilentlyContinue
        if ($null -ne $taskCompiler) { $CompilerPath = $taskCompiler.Source }
    }
}
if ([string]::IsNullOrEmpty($CompilerPath) -or !(Test-Path -LiteralPath $CompilerPath)) {
    throw '未找到 C# 编译器。请安装 Visual Studio 生成工具，或通过 -CompilerPath 指定编译器。'
}

$taskTemp = Join-Path ([System.IO.Path]::GetTempPath()) ('FileConverter-Diagnostics-' + [Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($taskTemp) | Out-Null
try {
    $taskSources = @(
        (Join-Path $PSScriptRoot 'DiagnosticsRegression.cs'),
        (Join-Path $taskRoot 'Application\FileConverter\Diagnostics\Debug.cs'),
        (Join-Path $taskRoot 'Application\FileConverter\Diagnostics\DiagnosticsData.cs')
    )
    $taskExecutable = Join-Path $taskTemp 'DiagnosticsRegression.exe'
    & $CompilerPath /nologo /noconfig /optimize+ /target:exe /reference:System.dll /reference:System.Core.dll "/out:$taskExecutable" @taskSources
    if ($LASTEXITCODE -ne 0) { throw '诊断回归编译失败。' }
    & $taskExecutable (Join-Path $taskTemp 'logs')
    if ($LASTEXITCODE -ne 0) { throw '诊断回归失败。' }
}
finally {
    # 删除前核验绝对路径和本次测试的唯一目录前缀。
    $taskResolvedTemp = [System.IO.Path]::GetFullPath($taskTemp)
    $taskTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($taskResolvedTemp.StartsWith($taskTempRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
        ([System.IO.Path]::GetFileName($taskResolvedTemp)).StartsWith('FileConverter-Diagnostics-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $taskResolvedTemp -Recurse -Force
    }
}
