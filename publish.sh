#!/bin/bash
# Publish the .NET application for multiple runtimes
# Cleans up extraneous files, keeping only the executable

RUNTIMES=("linux-x64" "win-x64" "osx-x64" "osx-arm64")
OUTPUT_DIR="./dist"

for runtime in "${RUNTIMES[@]}"; do
    echo "Publishing for $runtime..."
    RUNTIME_DIR="$OUTPUT_DIR/$runtime"
    dotnet publish -c Release -r "$runtime" -o "$RUNTIME_DIR"
    
    echo "Cleaning up $RUNTIME_DIR..."
    # Find and keep only the executable (committy or committy.exe on Windows)
    if [[ "$runtime" == "win-x64" ]]; then
        # On Windows, keep committy.exe
        find "$RUNTIME_DIR" -type f ! -name "committy.exe" -delete
        find "$RUNTIME_DIR" -type d -empty -delete
    else
        # On Unix-like systems, keep committy (without extension)
        find "$RUNTIME_DIR" -type f ! -name "committy" -delete
        find "$RUNTIME_DIR" -type d -empty -delete
    fi
    
    # Make executable readable/executable on Unix
    if [[ "$runtime" != "win-x64" ]]; then
        chmod +x "$RUNTIME_DIR/committy"
    fi
    
    echo "✓ $runtime: $(du -sh $RUNTIME_DIR)"
done

echo ""
echo "Done! Executables are in $OUTPUT_DIR:"
ls -lh "$OUTPUT_DIR"/*/committy* 2>/dev/null || ls -lh "$OUTPUT_DIR"/*/*
