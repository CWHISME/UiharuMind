@echo off
rem 调试版发布：与 buildWin.bat 同一套步骤，只把配置换成 Debug，产物落 Tmp\WinDebug。
rem 用途：对比 Release 与 Debug 的界面手感（Debug 关掉代码优化，才复现得出「debug 下很卡」）。
rem 刻意关掉 ReadyToRun：R2R 会把 IL 预编译成 native，绕开 JIT，Debug 就快回去了。
setlocal

cd /d "%~dp0"

set TARGET_RUNTIME=win-x64
set STAGE_DIR=Tmp\WinDebug

rem 版本号唯一来源：Directory.Build.props 的 <Version>
for /f "delims=" %%V in ('dotnet msbuild ..\UiharuMind.Desktop\UiharuMind.Desktop.csproj -getProperty:Version -nologo') do set APP_VERSION=%%V
if "%APP_VERSION%"=="" (
    echo 取不到 ^<Version^>，中止 1>&2
    exit /b 1
)

if exist "%STAGE_DIR%" rd /s /q "%STAGE_DIR%"
dotnet publish ..\UiharuMind.Desktop\UiharuMind.Desktop.csproj --output "%STAGE_DIR%\UiharuMind" -r %TARGET_RUNTIME% --configuration Debug -p:PublishReadyToRun=false || exit /b 1

rem 自包含发布会摊出全部 RID 的原生库，只留目标 RID
if exist "%STAGE_DIR%\UiharuMind\runtimes" (
    for /d %%D in ("%STAGE_DIR%\UiharuMind\runtimes\*") do (
        if /I not "%%~nxD"=="%TARGET_RUNTIME%" rd /s /q "%%D"
    )
)

echo 已发布 Debug 版（%APP_VERSION%）：%STAGE_DIR%\UiharuMind\UiharuMind.Desktop.exe

endlocal
