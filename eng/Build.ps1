param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$ApplicationOnly,
    [string]$SdkDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswherePath)) {
    throw '请先安装 Visual Studio 2022 或 Build Tools，并选择“.NET 桌面开发”组件。'
}
$msbuildPath = & $vswherePath -latest -version '[17.0,)' -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuildPath) {
    throw '找不到 MSBuild，请检查 Visual Studio 的 .NET 桌面开发组件。'
}

if ([string]::IsNullOrEmpty($SdkDirectory)) {
    $localSdkRoot = Join-Path $repositoryPath 'build\dotnet-sdk\sdk'
    if (Test-Path -LiteralPath $localSdkRoot) {
        $SdkDirectory = Get-ChildItem -LiteralPath $localSdkRoot -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
}

$outputDirectory = Join-Path $repositoryPath "build\artifacts\$Configuration"
# 独立输出目录避免复用旧版多语言发布目录；不执行安装、注册或测试。
[void][IO.Directory]::CreateDirectory($outputDirectory)
$msbuildOutputPath = $outputDirectory.Replace('\', '/') + '/'
$buildLogPath = Join-Path $outputDirectory 'build.log'
$previousSdkPath = $env:MSBuildSDKsPath
try {
    $buildArguments = @('/restore', '/t:Rebuild', '/m', '/nologo', '/v:minimal', '/fl', "/p:Configuration=$Configuration", '/p:Platform=x64', "/p:OutDir=$msbuildOutputPath", "/flp:LogFile=$buildLogPath;Verbosity=normal;Encoding=UTF-8")
    if ($SdkDirectory) {
        $sdkPath = Join-Path ([IO.Path]::GetFullPath($SdkDirectory)) 'Sdks'
        if (-not (Test-Path -LiteralPath (Join-Path $sdkPath 'Microsoft.NET.Sdk\Sdk'))) {
            throw "指定目录不包含 .NET SDK：$SdkDirectory"
        }
        # SDK 定位仅作用于当前进程，结束时恢复原值。
        $env:MSBuildSDKsPath = $sdkPath
        $buildArguments += '/p:MSBuildEnableWorkloadResolver=false'
    }
    $projectPath = if ($ApplicationOnly) { 'Application\FileConverter\FileConverter.csproj' } else { 'FileConverter.sln' }
    Write-Host "正在构建简体中文版：$Configuration x64。"
    & $msbuildPath (Join-Path $repositoryPath $projectPath) @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "构建失败，退出码 $LASTEXITCODE。详细日志：$buildLogPath"
    }
    foreach ($requiredFile in @('FileConverter.exe', 'FileConverterExtension.dll', 'Settings.default.xml')) {
        if (-not (Test-Path -LiteralPath (Join-Path $outputDirectory $requiredFile) -PathType Leaf)) {
            throw "构建输出缺少 $requiredFile，请查看日志：$buildLogPath"
        }
    }
    if (-not $ApplicationOnly) {
        $installers = @(Get-ChildItem -LiteralPath $outputDirectory -Filter 'FileConverter-setup.msi' -Recurse -File)
        if ($installers.Count -ne 1) {
            throw "预期生成一个安装包，实际找到 $($installers.Count) 个。请检查输出目录和日志：$buildLogPath"
        }
        Write-Host "安装包：$($installers[0].FullName)"
    }
    Write-Host "构建完成，输出目录：$outputDirectory"
    Write-Host "详细日志：$buildLogPath"
    Write-Host '请自行测试本次输出；脚本不会安装或启动程序。'
}
finally {
    $env:MSBuildSDKsPath = $previousSdkPath
}
