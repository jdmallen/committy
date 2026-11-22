#!/usr/bin/env bash
# Shared publish engine for JDMallen .NET apps. Publishes self-contained,
# single-file binaries (or Native AOT binaries, with aot=true) for one or more
# runtimes, then bundles each into a release archive (zip for Windows, tar.gz
# otherwise).
#
# This script is not run directly. Each app keeps a thin publish.sh wrapper
# that sets the variables below and then sources this file, e.g.:
#
#   #!/usr/bin/env bash
#   set -euo pipefail
#   repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
#   cd "$repo_root"
#
#   project="src/MyApp/MyApp.csproj"
#   exe_name="myapp"
#   default_runtimes=(linux-x64 linux-arm64 win-x64)
#
#   source "$repo_root/scripts/publish-dotnet.sh"
#
# The canonical copy of this file lives in the toolbox repo
# (https://github.com/jdmallen/toolbox, scripts/publish-dotnet.sh). Each app
# repo vendors a byte-identical copy next to its wrapper so the script works
# without a toolbox checkout (contributors, GitHub Actions). Make changes in
# the toolbox first, then re-copy; `diff` against the toolbox copy to check
# for drift.
#
# Required variables (set before sourcing):
#   project           Path to the .csproj to publish, relative to repo root.
#   exe_name          Name of the produced executable (without .exe).
#   default_runtimes  Array of RIDs to publish when none are given on the CLI.
#
# Optional variables:
#   known_runtimes      Array of RIDs accepted for validation.
#                       Defaults to the desktop RIDs from the .NET RID catalog:
#                       https://learn.microsoft.com/en-us/dotnet/core/rid-catalog
#   extra_unix_files    Array of extra files (relative to repo root) staged
#                       into non-Windows archives alongside the executable.
#   extra_publish_args  Array of additional arguments for `dotnet publish`.
#   aot                 "true" to publish Native AOT instead of single-file.
#                       Native AOT cannot cross-compile between operating
#                       systems, nor (without a cross toolchain) between
#                       architectures on Linux, so only runtimes this machine
#                       can build are published: unbuildable defaults are
#                       skipped, and an unbuildable runtime named on the CLI
#                       is an error. CI builds each on a matching runner.
#   output_dir          Output directory. Defaults to "dist".
#   version             Defaults to <Version> in the csproj; override with -v.
#
# Wrapper usage (arguments are parsed here):
#   ./publish.sh                       # publish all default runtimes
#   ./publish.sh linux-arm64           # publish a single runtime
#   ./publish.sh linux-x64 win-x64     # publish a specific set
#   ./publish.sh -v 2.2.0 linux-x64    # override the version
#   ./publish.sh -h                    # list known and default runtimes

set -euo pipefail

: "${project:?wrapper must set 'project' before sourcing publish-dotnet.sh}"
: "${exe_name:?wrapper must set 'exe_name' before sourcing publish-dotnet.sh}"
if [[ -z "${default_runtimes+x}" || ${#default_runtimes[@]} -eq 0 ]]; then
	echo "Error: wrapper must set a non-empty 'default_runtimes' array." >&2
	exit 1
fi

if [[ -z "${known_runtimes+x}" ]]; then
	known_runtimes=(
		"win-x64" "win-x86" "win-arm64"
		"linux-x64" "linux-arm" "linux-arm64"
		"linux-musl-x64" "linux-musl-arm64"
		"osx-x64" "osx-arm64"
	)
fi

if [[ -z "${extra_unix_files+x}" ]]; then extra_unix_files=(); fi
if [[ -z "${extra_publish_args+x}" ]]; then extra_publish_args=(); fi
aot="${aot:-false}"
output_dir="${output_dir:-dist}"
repo_root="${repo_root:-$PWD}"

# Pull the default version straight from the project file so the csproj remains
# the single source of truth.
if [[ -z "${version:-}" ]]; then
	# sed, not `grep -P`: macOS ships BSD grep, which has no -P.
	version="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$project" \
		| head -n1 || true)"
fi

# The OS and architecture of this machine, in RID terms.
host_os() {
	case "$(uname -s)" in
		Linux) echo "linux" ;;
		Darwin) echo "osx" ;;
		MINGW* | MSYS* | CYGWIN*) echo "win" ;;
		*) echo "unknown" ;;
	esac
}

host_arch() {
	case "$(uname -m)" in
		x86_64 | amd64) echo "x64" ;;
		aarch64 | arm64) echo "arm64" ;;
		armv7l) echo "arm" ;;
		*) echo "unknown" ;;
	esac
}

# Whether this machine can Native AOT compile for a RID: same OS, and on Linux
# the same architecture too. macOS (Xcode) and Windows (with Visual Studio's
# ARM64 build tools) build every architecture of their own OS.
can_build_aot() {
	local runtime="$1"
	local os="${runtime%%-*}"
	local arch="${runtime##*-}"
	[[ "$os" == "$(host_os)" ]] || return 1
	[[ "$os" != "linux" || "$arch" == "$(host_arch)" ]]
}

print_help() {
	echo "Usage: $0 [-v|--version <version>] [runtime ...]"
	echo ""
	echo "Publishes self-contained binaries for the given runtime identifiers (RIDs)."
	if [[ "$aot" == "true" ]]; then
		echo "Native AOT: only runtimes this machine can build ($(host_os)-$(host_arch))."
	fi
	echo ""
	echo "Known runtimes:"
	printf '  %s\n' "${known_runtimes[@]}"
	echo ""
	echo "If no runtimes are given, publishes for the defaults:"
	printf '  %s\n' "${default_runtimes[@]}"
	echo ""
	echo "Version defaults to <Version> in the csproj (currently ${version:-unset})."
}

# Parse a leading -v/--version flag, then treat the rest as runtimes.
runtimes=()
while [[ $# -gt 0 ]]; do
	case "$1" in
		-h|--help)
			print_help
			exit 0
			;;
		-v|--version)
			version="${2:?--version requires a value}"
			shift 2
			;;
		-*)
			echo "Error: unknown option '$1'." >&2
			exit 1
			;;
		*)
			runtimes+=("$1")
			shift
			;;
	esac
done

runtimes_given=$([[ ${#runtimes[@]} -gt 0 ]] && echo "yes" || echo "")
if [[ ${#runtimes[@]} -eq 0 ]]; then
	runtimes=("${default_runtimes[@]}")
fi

if [[ -z "$version" ]]; then
	echo "Error: could not determine version (no <Version> in $project)." >&2
	exit 1
fi

# Validate every requested runtime up front so a typo fails fast.
for runtime in "${runtimes[@]}"; do
	match=""
	for known in "${known_runtimes[@]}"; do
		[[ "$runtime" == "$known" ]] && match="yes" && break
	done
	if [[ -z "$match" ]]; then
		echo "Error: unknown runtime '$runtime'." >&2
		echo "Known runtimes: ${known_runtimes[*]}" >&2
		exit 1
	fi
done

if [[ "$aot" == "true" ]]; then
	buildable=()
	for runtime in "${runtimes[@]}"; do
		if can_build_aot "$runtime"; then
			buildable+=("$runtime")
		elif [[ -n "$runtimes_given" ]]; then
			echo "Error: cannot Native AOT compile $runtime on $(host_os)-$(host_arch)." >&2
			echo "Build it on a matching machine or CI runner." >&2
			exit 1
		else
			echo "Skipping $runtime: Native AOT needs a matching machine."
		fi
	done
	if [[ ${#buildable[@]} -eq 0 ]]; then
		echo "Error: none of the runtimes can be Native AOT compiled here." >&2
		exit 1
	fi
	runtimes=("${buildable[@]}")
	publish_mode_args=(-p:PublishAot=true)
else
	publish_mode_args=(
		-p:PublishSingleFile=true
		-p:IncludeNativeLibrariesForSelfExtract=true
	)
fi

rm -rf "$output_dir"
mkdir -p "$output_dir"

echo "Publishing $exe_name v$version"
echo ""

for runtime in "${runtimes[@]}"; do
	echo ">>> Publishing $runtime"
	runtime_dir="$output_dir/$runtime"

	dotnet publish "$project" \
		--configuration Release \
		--runtime "$runtime" \
		--self-contained true \
		--output "$runtime_dir" \
		-p:Version="$version" \
		"${publish_mode_args[@]}" \
		${extra_publish_args[@]+"${extra_publish_args[@]}"} \
		--nologo \
		--verbosity minimal

	# Bundle the executable (plus any extras) into an archive named version+RID.
	if [[ "$runtime" == win-* ]]; then
		archive="$output_dir/$exe_name-$version-$runtime.zip"
		# Git Bash on Windows (as on GitHub's runners) has no zip; fall back
		# to 7-Zip, then to PowerShell.
		if command -v zip > /dev/null; then
			(cd "$runtime_dir" && zip -q "$repo_root/$archive" "$exe_name.exe")
		elif command -v 7z > /dev/null; then
			(cd "$runtime_dir" \
				&& 7z a -tzip -bso0 "$repo_root/$archive" "$exe_name.exe")
		else
			powershell.exe -NoProfile -Command \
				"Compress-Archive -Path '$runtime_dir/$exe_name.exe' -DestinationPath '$archive'"
		fi
	else
		chmod +x "$runtime_dir/$exe_name"
		stage_files=("$exe_name")
		# The ${a[@]+...} form keeps an empty array legal under `set -u` in
		# bash 3.2, which macOS ships.
		for extra in ${extra_unix_files[@]+"${extra_unix_files[@]}"}; do
			cp "$extra" "$runtime_dir/"
			stage_files+=("$(basename "$extra")")
		done
		archive="$output_dir/$exe_name-$version-$runtime.tar.gz"
		tar -C "$runtime_dir" -czf "$archive" "${stage_files[@]}"
	fi

	echo "    -> $archive ($(du -h "$archive" | cut -f1))"
	echo ""
done

echo "Done. Artifacts in $output_dir/"
ls -lh "$output_dir"/*.{tar.gz,zip} 2>/dev/null || true
