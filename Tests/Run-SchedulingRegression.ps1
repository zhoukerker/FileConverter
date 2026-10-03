param(
    [string]$CompilerPath,
    [switch]$CompareBaseline
)

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

$taskTemp = Join-Path ([System.IO.Path]::GetTempPath()) ('FileConverter-Scheduling-' + [Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($taskTemp) | Out-Null
try {
    $taskSources = @(
        (Join-Path $PSScriptRoot 'SchedulingRegression.cs'),
        (Join-Path $taskRoot 'Application\FileConverter\Services\IConversionService.cs'),
        (Join-Path $taskRoot 'Application\FileConverter\Services\ConversionJobsTerminatedEventArgs.cs'),
        (Join-Path $taskRoot 'Application\FileConverter\ConversionJobs\ConversionFlags.cs'),
        (Join-Path $taskRoot 'Application\FileConverter\ConversionJobs\ConversionState.cs')
    )
    $taskCurrentSource = Join-Path $taskRoot 'Application\FileConverter\Services\ConversionService.cs'
    $taskExecutable = Join-Path $taskTemp 'SchedulingRegression.exe'
    & $CompilerPath /nologo /noconfig /optimize+ /target:exe /reference:System.dll /reference:System.Core.dll "/out:$taskExecutable" @taskSources $taskCurrentSource
    if ($LASTEXITCODE -ne 0) { throw '调度回归编译失败。' }
    & $taskExecutable
    if ($LASTEXITCODE -ne 0) { throw '调度回归失败。' }

    if ($CompareBaseline) {
        $taskBaseline = Join-Path $taskTemp 'ConversionService.baseline.cs'
        $taskBaselineSource = & git -C $taskRoot show 'HEAD:Application/FileConverter/Services/ConversionService.cs'
        if ($LASTEXITCODE -ne 0) { throw '无法读取 Git 基线。' }
        [System.IO.File]::WriteAllLines($taskBaseline, $taskBaselineSource, (New-Object System.Text.UTF8Encoding($true)))
        $taskBaselineExecutable = Join-Path $taskTemp 'SchedulingBaseline.exe'
        & $CompilerPath /nologo /noconfig /optimize+ /target:exe /reference:System.dll /reference:System.Core.dll "/out:$taskBaselineExecutable" @taskSources $taskBaseline
        if ($LASTEXITCODE -ne 0) { throw '基线调度编译失败。' }
        Write-Output 'Git 基线合成调度基准：'
        & $taskBaselineExecutable --benchmark
        if ($LASTEXITCODE -ne 0) { throw '基线调度基准失败。' }
    }
}
finally {
    # 仅删除本脚本创建且已经核验的临时目录。
    $taskResolvedTemp = [System.IO.Path]::GetFullPath($taskTemp)
    $taskTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($taskResolvedTemp.StartsWith($taskTempRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
        ([System.IO.Path]::GetFileName($taskResolvedTemp)).StartsWith('FileConverter-Scheduling-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $taskResolvedTemp -Recurse -Force
    }
}
