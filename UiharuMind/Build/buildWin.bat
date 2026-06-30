@echo off
setlocal

set TARGET_RUNTIME=win-x64
set PUBLISH_OUTPUT_DIRECTORY=Tmp\UiharuMind-Win-x64

dotnet publish ../UiharuMind.Desktop --output "%PUBLISH_OUTPUT_DIRECTORY%" -r %TARGET_RUNTIME% --configuration Release --self-contained /p:PublishSingleFile=true

if exist "%PUBLISH_OUTPUT_DIRECTORY%\runtimes" (
    for /d %%D in ("%PUBLISH_OUTPUT_DIRECTORY%\runtimes\*") do (
        if /I not "%%~nxD"=="%TARGET_RUNTIME%" rd /s /q "%%D"
    )
)

endlocal
