#!/bin/bash

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd $SCRIPT_DIR

TARGET_RUNTIME="osx-arm64"
PUBLISH_OUTPUT_DIRECTORY="Tmp/Mac"

dotnet publish ../UiharuMind.Desktop --output "$PUBLISH_OUTPUT_DIRECTORY" -r "$TARGET_RUNTIME" --configuration Release -p:UseAppHost=true --self-contained

if [ -d "$PUBLISH_OUTPUT_DIRECTORY/runtimes" ]; then
    find "$PUBLISH_OUTPUT_DIRECTORY/runtimes" -mindepth 1 -maxdepth 1 -type d ! -name "$TARGET_RUNTIME" -exec rm -rf {} +
fi

mkdir -p Output

APP_NAME="Output/UiharuMind.app"
# PUBLISH_OUTPUT_DIRECTORY should point to the output directory of your dotnet publish command.
# One example is /path/to/your/csproj/bin/Release/netcoreapp3.1/osx-x64/publish/.
# If you want to change output directories, add `--output /my/directory/path` to your `dotnet publish` command.
INFO_PLIST="Info.plist"
ICON_FILE="../UiharuMind/Assets/Icon.png"
EXEC="Exec"

# Extract version from App.axaml.cs
APP_VERSION=$(grep "public static Version Version" ../UiharuMind/App.axaml.cs | grep -o 'new Version([0-9, ]*' | sed 's/new Version(//' | tr -d ' ' | tr ',' '.')
APP_SHORT_VERSION=$APP_VERSION

if [ -d "$APP_NAME" ]
then
    rm -rf "$APP_NAME"
fi

mkdir "$APP_NAME"

mkdir "$APP_NAME/Contents"
mkdir "$APP_NAME/Contents/MacOS"
mkdir "$APP_NAME/Contents/Resources"

# Patch Info.plist with correct version
if command -v /usr/libexec/PlistBuddy &> /dev/null; then
    TEMP_INFO_PLIST=$(mktemp)
    cp "$INFO_PLIST" "$TEMP_INFO_PLIST"
    /usr/libexec/PlistBuddy -c "Set CFBundleVersion $APP_VERSION" "$TEMP_INFO_PLIST"
    /usr/libexec/PlistBuddy -c "Set CFBundleShortVersionString $APP_SHORT_VERSION" "$TEMP_INFO_PLIST"
    cp "$TEMP_INFO_PLIST" "$APP_NAME/Contents/Info.plist"
    rm "$TEMP_INFO_PLIST"
else
    cp "$INFO_PLIST" "$APP_NAME/Contents/Info.plist"
fi
cp "$EXEC" "$APP_NAME/Contents/MacOS/$EXEC"
cp "$ICON_FILE" "$APP_NAME/Contents/Resources/Icon.png"
cp -a "$PUBLISH_OUTPUT_DIRECTORY/." "$APP_NAME/Contents/MacOS"
