#!/bin/bash
# Publish the .NET application for multiple runtimes

RUNTIMES=("linux-x64" "win-x64" "osx-x64" "osx-arm64")
OUTPUT_DIR="./dist"

for runtime in "${RUNTIMES[@]}"; do
    echo "Publishing for $runtime..."
    dotnet publish -c Release -r "$runtime" -o "$OUTPUT_DIR/$runtime"
done

echo "Done! Executables are in $OUTPUT_DIR"
