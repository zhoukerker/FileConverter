@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0eng\Build.ps1" %*
set "fileConverterBuildExit=%errorlevel%"
if not "%fileConverterBuildExit%"=="0" echo 构建失败，请根据上面的提示检查开发环境或源码。
pause
exit /b %fileConverterBuildExit%
