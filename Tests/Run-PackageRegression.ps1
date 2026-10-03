param(
    [string]$BuildDirectory = (Join-Path $PSScriptRoot '..\build\artifacts\Release'),
    [string]$InstallerPath
)

$ErrorActionPreference = 'Stop'
$packageRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packageBuild = [IO.Path]::GetFullPath($BuildDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $packageBuild 'FileConverter.exe') -PathType Leaf)) {
    throw "找不到已构建的 FileConverter.exe：$packageBuild。请先运行源码构建脚本。"
}
[xml]$packageManifest = Get-Content -LiteralPath (Join-Path $packageRepository 'Installer\Product.wxs') -Raw
$packageFiles = $packageManifest.SelectNodes('//*[local-name()="File"]')
$packagedLibraries = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($packageFile in $packageFiles) {
    if ($packageFile.Source -match '([^\\)]+\.dll)$') {
        [void]$packagedLibraries.Add($Matches[1])
    }
}
foreach ($runtimeLibrary in Get-ChildItem -LiteralPath $packageBuild -Filter '*.dll' -File) {
    if (-not $packagedLibraries.Contains($runtimeLibrary.Name)) {
        throw "安装清单遗漏运行库：$($runtimeLibrary.Name)"
    }
}
$nativeLibraries = @(Get-ChildItem -LiteralPath $packageBuild -Filter 'Magick.Native-*.dll' -File)
if ($nativeLibraries.Count -ne 1 -or $nativeLibraries[0].Name -ne 'Magick.Native-Q16-x64.dll') {
    throw 'x64 发布目录应只包含对应架构的 ImageMagick 原生库，请从干净目录重新构建。'
}
$packageSettings = Join-Path $packageBuild 'Settings.default.xml'
$sourceSettings = Join-Path $packageRepository 'Application\FileConverter\Settings.default.xml'
if ((Get-FileHash -LiteralPath $packageSettings).Hash -ne (Get-FileHash -LiteralPath $sourceSettings).Hash) {
    throw '构建输出的默认设置与源码不一致。'
}
[xml]$packageConfiguration = Get-Content -LiteralPath (Join-Path $packageBuild 'FileConverter.exe.config') -Raw
$satelliteLibraries = @(Get-ChildItem -LiteralPath $packageBuild -Filter 'FileConverter.resources.dll' -Recurse -File)
if ($satelliteLibraries.Count -ne 0) {
    throw '中文单语言发布目录不应包含卫星资源 DLL，请清理旧构建输出后重试。'
}
Write-Host "安装包依赖清单、目标架构、默认设置及单语言布局验证通过，共 $($packagedLibraries.Count) 个运行库。"

if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $packageCandidates = @(Get-ChildItem -LiteralPath $packageBuild -Filter 'FileConverter-setup.msi' -Recurse -File)
    if ($packageCandidates.Count -eq 0) {
        throw "输出目录中没有 FileConverter-setup.msi：$packageBuild。请先运行完整发布构建。"
    }
    if ($packageCandidates.Count -ne 1) {
        throw '输出目录中存在多个 FileConverter-setup.msi，请通过 -InstallerPath 明确指定待检查的安装包。'
    }
    $InstallerPath = $packageCandidates[0].FullName
}
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    throw "找不到生成的 MSI：$InstallerPath。请先运行完整发布构建。"
}

$packageInstaller = $null
$packageDatabase = $null
$packageView = $null
$packageLanguageView = $null
try {
    # 只读检查生成的安装包，不运行安装或注册扩展。
    $packageInstaller = New-Object -ComObject WindowsInstaller.Installer
    $packageDatabase = $packageInstaller.OpenDatabase([IO.Path]::GetFullPath($InstallerPath), 0)
    $packageLanguageView = $packageDatabase.OpenView('SELECT `Value` FROM `Property` WHERE `Property` = ''ProductLanguage''')
    $packageLanguageView.Execute()
    $packageLanguageRecord = $packageLanguageView.Fetch()
    try {
        if ($null -eq $packageLanguageRecord -or $packageLanguageRecord.StringData(1) -ne '2052') {
            throw '安装包 ProductLanguage 必须为简体中文（2052）。'
        }
    }
    finally {
        if ($packageLanguageRecord) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($packageLanguageRecord) }
    }
    $packageView = $packageDatabase.OpenView('SELECT `FileName` FROM `File`')
    $packageView.Execute()
    $installedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $languageCount = 0
    while ($packageRecord = $packageView.Fetch()) {
        try {
            $installedName = ($packageRecord.StringData(1) -split '\|')[-1]
            [void]$installedNames.Add($installedName)
            if ($installedName -eq 'FileConverter.resources.dll') {
                $languageCount++
            }
        }
        finally {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($packageRecord)
        }
    }
    $requiredRuntimeFiles = @('FileConverter.exe', 'ffmpeg.exe', 'gswin64c.exe', 'Settings.default.xml')
    foreach ($runtimeFile in Get-ChildItem -LiteralPath $packageBuild -File) {
        if (($runtimeFile.Extension -in @('.dll', '.config') -or $runtimeFile.Name -in $requiredRuntimeFiles) -and -not $installedNames.Contains($runtimeFile.Name)) {
            throw "生成的 MSI 遗漏运行文件：$($runtimeFile.Name)"
        }
    }
    if ($languageCount -ne 0) {
        throw "中文单语言 MSI 不应包含 FileConverter.resources.dll，实际存在 $languageCount 份。"
    }
    Write-Host '生成的 MSI 运行文件、ProductLanguage=2052 和无卫星 DLL 验证通过。'
}
finally {
    if ($packageLanguageView) {
        $packageLanguageView.Close()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($packageLanguageView)
    }
    if ($packageView) {
        $packageView.Close()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($packageView)
    }
    if ($packageDatabase) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($packageDatabase) }
    if ($packageInstaller) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($packageInstaller) }
}
