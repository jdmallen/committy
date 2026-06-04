#!/usr/bin/env bash
# Publish the .NET application for one or more runtimes
# Cleans up extraneous files, keeping only the executable
#
# Usage:
#   ./publish.sh                       # publish all default runtimes
#   ./publish.sh linux-arm64           # publish a single runtime
#   ./publish.sh linux-x64 win-x64     # publish a specific set of runtimes
#
# Run with -h or --help to list the known and default runtimes.

# Known runtime identifiers (RIDs) from the .NET RID catalog's "Known RIDs" list:
# https://learn.microsoft.com/en-us/dotnet/core/rid-catalog
# Validation only accepts these. The defaults below build the desktop targets;
# mobile RIDs are accepted but won't produce a "committy" executable to keep.
KNOWN_RUNTIMES=(
  # Windows
  "win-x64" "win-x86" "win-arm64"
  # Linux
  "linux-x64" "linux-musl-x64" "linux-musl-arm64" "linux-arm" "linux-arm64"
  "linux-bionic-arm64" "linux-loongarch64"
  # macOS
  "osx" "osx-x64" "osx-arm64"
  # iOS
  "ios-arm64" "iossimulator-arm64" "iossimulator-x64"
  # Android
  "android-arm64" "android-arm" "android-x64" "android-x86"
)

DEFAULT_RUNTIMES=("linux-x64" "linux-arm64" "win-x64" "win-arm64" "osx-x64" "osx-arm64")
OUTPUT_DIR="./dist"

if [[ "$1" == "-h" || "$1" == "--help" ]]; then
  echo "Usage: $0 [runtime ...]"
  echo ""
  echo "Publishes the application for the given runtime identifiers (RIDs)."
  echo ""
  echo "Known runtimes:"
  printf '  %s\n' "${KNOWN_RUNTIMES[@]}"
  echo ""
  echo "If no runtimes are given, publishes for the defaults:"
  printf '  %s\n' "${DEFAULT_RUNTIMES[@]}"
  exit 0
fi

# Use the runtimes passed on the command line, or fall back to the defaults.
if [[ $# -gt 0 ]]; then
  RUNTIMES=("$@")
else
  RUNTIMES=("${DEFAULT_RUNTIMES[@]}")
fi

# Validate every requested runtime against the known set before publishing
# anything, so a typo fails fast instead of part-way through the build.
for runtime in "${RUNTIMES[@]}"; do
  match=""
  for known in "${KNOWN_RUNTIMES[@]}"; do
    if [[ "$runtime" == "$known" ]]; then
      match="yes"
      break
    fi
  done

  if [[ -z "$match" ]]; then
    echo "Error: unknown runtime '$runtime'." >&2
    echo "Known runtimes: ${KNOWN_RUNTIMES[*]}" >&2
    exit 1
  fi
done

for runtime in "${RUNTIMES[@]}"; do
  echo "Publishing for $runtime..."
  RUNTIME_DIR="$OUTPUT_DIR/$runtime"
  dotnet publish -c Release -r "$runtime" -o "$RUNTIME_DIR"

  echo "Cleaning up $RUNTIME_DIR..."
  # Find and keep only the executable (committy or committy.exe on Windows)
  if [[ "$runtime" == win-* ]]; then
    # On Windows, keep committy.exe
    find "$RUNTIME_DIR" -type f ! -name "committy.exe" -delete
    find "$RUNTIME_DIR" -type d -empty -delete
  else
    # On Unix-like systems, keep committy (without extension)
    find "$RUNTIME_DIR" -type f ! -name "committy" -delete
    find "$RUNTIME_DIR" -type d -empty -delete
  fi

  # Make executable readable/executable on Unix
  if [[ "$runtime" != win-* ]]; then
    chmod +x "$RUNTIME_DIR/committy"
  fi

  echo "✓ $runtime: $(du -sh $RUNTIME_DIR)"
done

echo ""
echo "Done! Executables are in $OUTPUT_DIR:"
ls -lh "$OUTPUT_DIR"/*/committy* 2>/dev/null || ls -lh "$OUTPUT_DIR"/*/*
