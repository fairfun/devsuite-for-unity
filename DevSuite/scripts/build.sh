#!/usr/bin/env bash

# Exit immediately if a command exits with a non-zero status
set -e

# Target parameter check
if [ -z "$1" ]; then
    echo "Usage: $0 <BuildTarget>"
    echo "Example targets: WebGL, StandaloneLinux64, Android"
    exit 1
fi

TARGET=$1

# Get project path and scripts directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

# 1. Read Unity version from ProjectVersion.txt
VERSION_FILE="$PROJECT_DIR/ProjectSettings/ProjectVersion.txt"
if [ ! -f "$VERSION_FILE" ]; then
    echo "Error: ProjectVersion.txt not found at $VERSION_FILE"
    exit 1
fi

UNITY_VERSION=$(grep "m_EditorVersion:" "$VERSION_FILE" | awk '{print $2}')
if [ -z "$UNITY_VERSION" ]; then
    echo "Error: Could not extract Unity version from $VERSION_FILE"
    exit 1
fi

echo "Detected Unity Version: $UNITY_VERSION"

# 2. Determine OS and find Unity executable
OS_NAME="$(uname -s)"
UNITY_PATH=""

case "$OS_NAME" in
    Linux*)
        # Common Linux installation paths
        CANDIDATES=(
            "$HOME/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity"
            "/opt/unity/editors/$UNITY_VERSION/Editor/Unity"
            "/opt/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity"
        )
        ;;
    Darwin*)
        # Common macOS installation paths
        CANDIDATES=(
            "/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity"
            "$HOME/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity"
        )
        ;;
    CYGWIN*|MINGW*|MSYS*)
        # Common Windows paths when using Git Bash, MSYS or Cygwin
        CANDIDATES=(
            "C:/Program Files/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity.exe"
            "/c/Program Files/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity.exe"
        )
        ;;
    *)
        echo "Unsupported OS: $OS_NAME"
        exit 1
        ;;
esac

# Search candidates
for candidate in "${CANDIDATES[@]}"; do
    if [ -f "$candidate" ] || [ -x "$candidate" ]; then
        UNITY_PATH="$candidate"
        break
    fi
done

if [ -z "$UNITY_PATH" ]; then
    echo "Error: Could not find Unity executable for version $UNITY_VERSION."
    echo "Searched locations:"
    for candidate in "${CANDIDATES[@]}"; do
        echo "  - $candidate"
    done
    exit 1
fi

echo "Found Unity Executable: $UNITY_PATH"

# 3. Run Unity build
LOG_FILE="$PROJECT_DIR/Builds/unity_build.log"
mkdir -p "$PROJECT_DIR/Builds"

OUTPUT_PATH="Builds/$TARGET"
if [ "$TARGET" = "Android" ]; then
    OUTPUT_PATH="Builds/Android/build.apk"
elif [ "$TARGET" = "StandaloneLinux64" ]; then
    OUTPUT_PATH="Builds/Linux/xArena.x86_64"
fi

echo "Starting build for target: $TARGET..."
echo "Build logs will be written to: $LOG_FILE"

# Run Unity in batchmode
"$UNITY_PATH" \
    -batchmode \
    -quit \
    -projectPath "$PROJECT_DIR" \
    -executeMethod BuildUtil.Build \
    -buildTarget "$TARGET" \
    -outputPath "$OUTPUT_PATH" \
    -logFile "$LOG_FILE" \
    "${@:2}"

echo "Build finished! Resulting build path: $PROJECT_DIR/$OUTPUT_PATH"
echo "Check logs at $LOG_FILE for details."
