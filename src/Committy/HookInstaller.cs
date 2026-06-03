using CliWrap;
using CliWrap.Buffered;

namespace Committy;

/// <summary>
/// Installs the committy git hook. The hook itself is a thin "trampoline" whose
/// only job is to invoke <c>committy prepare-commit-msg</c>; all behavior lives
/// in the binary so it can be updated centrally without re-installing per-repo
/// hooks. The trampoline text is embedded here, so installation does not depend
/// on the current working directory or any source files being present.
/// </summary>
public static class HookInstaller
{
	public const string HookName = "prepare-commit-msg";

	/// <summary>
	/// The installed hook. Intentionally minimal and version-stable: it guards
	/// against non-plain commits and a missing binary, then hands off to the
	/// binary, which owns all logic. It always exits 0 so a stale hook paired
	/// with an older binary can never abort a commit.
	/// </summary>
	private const string TRAMPOLINE_SCRIPT =
		"""
		#!/usr/bin/env bash
		# Committy trampoline hook — installed by `committy install-hook`.
		# Intentionally minimal: all behavior lives in the committy binary so it can
		# be updated centrally without re-installing per-repo hooks.

		# Skip non-plain commits (merge, squash, message, template, etc.).
		if [ -n "$2" ]; then
		    exit 0
		fi

		# If committy isn't on PATH, do nothing rather than block the commit.
		if ! command -v committy >/dev/null 2>&1; then
		    exit 0
		fi

		# Never let hook failure abort the commit; the binary writes the message in place.
		committy prepare-commit-msg "$1"
		exit 0
		""";

	/// <summary>
	/// Installs the hook either globally (as a git template) or into a single
	/// repository.
	/// </summary>
	/// <param name="global">Install as a global template for all future repositories.</param>
	/// <param name="repoPath">
	/// Target repository (defaults to the current directory).
	/// Ignored when <paramref name="global" /> is true.
	/// </param>
	/// <param name="output">Where to write progress messages.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>0 on success; non-zero on failure.</returns>
	public static async Task<int> InstallAsync(
		bool global,
		string? repoPath,
		TextWriter output,
		CancellationToken cancellationToken = default)
	{
		string hooksDir;

		if (global)
		{
			string templateDir = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				".git-templates");
			hooksDir = Path.Combine(templateDir, "hooks");
			Directory.CreateDirectory(hooksDir);

			await SetGlobalTemplateDirAsync(templateDir, cancellationToken).ConfigureAwait(false);
		}
		else
		{
			string repo = Path.GetFullPath(string.IsNullOrWhiteSpace(repoPath) ? "." : repoPath);

			if (!Directory.Exists(repo))
			{
				await output.WriteLineAsync($"Error: Repository path does not exist: {repo}")
					.ConfigureAwait(false);

				return 1;
			}

			string? gitDir = await TryGetGitDirAsync(repo, cancellationToken).ConfigureAwait(false);

			if (gitDir is null)
			{
				await output.WriteLineAsync($"Error: Not a valid git repository: {repo}")
					.ConfigureAwait(false);

				return 1;
			}

			if (!Path.IsPathRooted(gitDir))
			{
				gitDir = Path.GetFullPath(Path.Combine(repo, gitDir));
			}

			hooksDir = Path.Combine(gitDir, "hooks");
			Directory.CreateDirectory(hooksDir);
		}

		string hookPath = Path.Combine(hooksDir, HookName);

		await File.WriteAllTextAsync(
				hookPath,
				TRAMPOLINE_SCRIPT.Replace("\r\n", "\n"),
				cancellationToken)
			.ConfigureAwait(false);
		MakeExecutable(hookPath);

		await output.WriteLineAsync($"✓ Hook installed: {hookPath}").ConfigureAwait(false);

		if (global)
		{
			await output.WriteLineAsync(
					"Global hook template installed. New repositories will include this hook automatically;")
				.ConfigureAwait(false);
			await output.WriteLineAsync(
					"for existing repositories, run `git init` in each or `committy install-hook <repo>`.")
				.ConfigureAwait(false);
		}
		else
		{
			await output.WriteLineAsync(
					"Hook installed for the repository. AI suggestions will appear when you run `git commit`.")
				.ConfigureAwait(false);
		}

		return 0;
	}

	private static void MakeExecutable(string path)
	{
		if (OperatingSystem.IsWindows())
		{
			return;
		}

		const UnixFileMode mode =
			UnixFileMode.UserRead
			| UnixFileMode.UserWrite
			| UnixFileMode.UserExecute
			| UnixFileMode.GroupRead
			| UnixFileMode.GroupExecute
			| UnixFileMode.OtherRead
			| UnixFileMode.OtherExecute;

		File.SetUnixFileMode(path, mode);
	}

	private static async Task SetGlobalTemplateDirAsync(
		string templateDir,
		CancellationToken cancellationToken)
	{
		await Cli.Wrap("git")
			.WithArguments(["config", "--global", "init.templateDir", templateDir])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync(cancellationToken)
			.ConfigureAwait(false);
	}

	private static async Task<string?> TryGetGitDirAsync(
		string repo,
		CancellationToken cancellationToken)
	{
		BufferedCommandResult result = await Cli.Wrap("git")
			.WithArguments(["-C", repo, "rev-parse", "--git-dir"])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync(cancellationToken)
			.ConfigureAwait(false);

		return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput)
			? result.StandardOutput.Trim()
			: null;
	}
}
