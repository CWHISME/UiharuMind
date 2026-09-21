@echo off
rem 发布参数（自包含、R2R、单文件）收在 UiharuMind.Desktop.csproj 里，这里只给 RID。
setlocal

cd /d "%~dp0"

set TARGET_RUNTIME=win-x64
set STAGE_DIR=Tmp\Win

rem 版本号唯一来源：Directory.Build.props 的 <Version>
for /f "delims=" %%V in ('dotnet msbuild ..\UiharuMind.Desktop\UiharuMind.Desktop.csproj -getProperty:Version -nologo') do set APP_VERSION=%%V
if "%APP_VERSION%"=="" (
    echo 取不到 ^<Version^>，中止 1>&2
    exit /b 1
)

if exist "%STAGE_DIR%" rd /s /q "%STAGE_DIR%"
dotnet publish ..\UiharuMind.Desktop\UiharuMind.Desktop.csproj --output "%STAGE_DIR%\UiharuMind" -r %TARGET_RUNTIME% --configuration Release || exit /b 1

rem 自包含发布会摊出全部 RID 的原生库，只留目标 RID
if exist "%STAGE_DIR%\UiharuMind\runtimes" (
    for /d %%D in ("%STAGE_DIR%\UiharuMind\runtimes\*") do (
        if /I not "%%~nxD"=="%TARGET_RUNTIME%" rd /s /q "%%D"
    )
)

if not exist Output md Output
set ARCHIVE=Output\UiharuMind-%APP_VERSION%-%TARGET_RUNTIME%.zip
if exist "%ARCHIVE%" del /q "%ARCHIVE%"
powershell -NoProfile -Command "Compress-Archive -Path '%STAGE_DIR%\UiharuMind' -DestinationPath '%ARCHIVE%'" || exit /b 1
echo 已打包：%ARCHIVE%

endlocal
