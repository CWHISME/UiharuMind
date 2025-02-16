#!/bin/bash

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd $SCRIPT_DIR

dotnet publish ../UiharuMind.Desktop --output Tmp/Mac -r osx-arm64 --configuration Release -p:UseAppHost=true --self-contained

mkdir Output

APP_NAME="Output/UiharuMind.app"
PUBLISH_OUTPUT_DIRECTORY="Tmp/Mac/."
# PUBLISH_OUTPUT_DIRECTORY should point to the output directory of your dotnet publish command.
# One example is /path/to/your/csproj/bin/Release/netcoreapp3.1/osx-x64/publish/.
# If you want to change output directories, add `--output /my/directory/path` to your `dotnet publish` command.
INFO_PLIST="Info.plist"
ICON_FILE="../UiharuMind/Assets/Icon.png"
EXEC="Exec"

if [ -d "$APP_NAME" ]
then
    rm -rf "$APP_NAME"
fi

mkdir "$APP_NAME"

mkdir "$APP_NAME/Contents"
mkdir "$APP_NAME/Contents/MacOS"
mkdir "$APP_NAME/Contents/Resources"

cp "$INFO_PLIST" "$APP_NAME/Contents/Info.plist"
cp "$EXEC" "$APP_NAME/Contents/MacOS/$EXEC"
cp "$ICON_FILE" "$APP_NAME/Contents/Resources/Icon.png"
cp -a "$PUBLISH_OUTPUT_DIRECTORY" "$APP_NAME/Contents/MacOS"