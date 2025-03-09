#!/bin/bash

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd $SCRIPT_DIR

source buildMac.sh

#InternalRuntime
cp -a "$HOME/Library/Application Support/UiharuMind/EmbededModels" "$APP_NAME/Contents/MacOS/InternalEmbededModels"
#InternalEmbededModels
cp -a "$HOME/Library/Application Support/UiharuMind/Runtime" "$APP_NAME/Contents/MacOS/InternalRuntime"
#InternalModels
cp -a "$HOME/Documents/Studys/LLMModel/InternalModels" "$APP_NAME/Contents/MacOS/InternalModels"