using CliWrap;
using CliWrap.Buffered;

namespace JDMallen.Committy;

/// <summary>
/// Installs the committy git hook. The hook itself is a thin "trampoline" whose
/// only job is to invoke <c>committy prepare-commit-msg</c>; all behavior lives
/// in the binary so it can be updated centrally without re-installing per-repo
/// hooks. The trampoline text is embedded here, so installation does not depend
/// on the current working directory or any source files being present.
/// </summary>
public static class HookInstaller
{
	public const string HOOK_NAME = "prepare-commit-msg";

	private const string TEMPLATE_DIR_KEY = "init.templateDir";

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

	/// <summary>The current trampoline text, with normalized newlines.</summary>
	public static string Trampoline => TRAMPOLINE_SCRIPT.Replace("\r\n", "\n");

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
	/// <param name="force">
	/// Replace a hook committy did not write, keeping it as <c>.bak</c>.
	/// </param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>0 on success; non-zero on failure.</returns>
	public static Task<int> InstallAsync(
		bool global,
		string? repoPath,
		TextWriter output,
		bool force = false,
		CancellationToken cancellationToken = default) =>
		global
			? InstallGlobalAsync(
				output,
				force,
				Path.Combine(
					Environment.GetFolderPath(
						Environment.SpecialFolder.UserProfile),
					".git-templates"),
				null,
				cancellationToken)
			: InstallLocalAsync(
				repoPath,
				output,
				force,
				cancellationToken);

	/// <summary>
	/// Installs the hook into the global git template, so <c>git init</c> and
	/// <c>git clone</c> copy it into new repositories. An existing
	/// <c>init.templateDir</c> is used as is; only when it is unset does
	/// committy create <paramref name="defaultTemplateDir" /> and point the
	/// setting at it.
	/// </summary>
	/// <param name="defaultTemplateDir">The directory to use for the global template if none is set.</param>
	/// <param name="globalConfigFile">
	/// The global git config file to use instead of the user's, so tests can
	/// leave ~/.gitconfig alone; null for git's own.
	/// </param>
	/// <param name="output">Where to write progress messages.</param>
	/// <param name="force">Replace a hook committy did not write, keeping it as <c>.bak</c>.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	public static async Task<int> InstallGlobalAsync(
		TextWriter output,
		bool force,
		string defaultTemplateDir,
		string? globalConfigFile,
		CancellationToken cancellationToken = default)
	{
		BufferedCommandResult current = await GlobalGit(globalConfigFile)
			.WithArguments(
			[
				"config", "--global", "--type=path", "--get",
				TEMPLATE_DIR_KEY,
			])
			.ExecuteBufferedAsync(cancellationToken)
			.ConfigureAwait(false);

		// Exit code 1 means the key is unset; anything else is a real error,
		// such as an unreadable config file.
		if (current.ExitCode is not (0 or 1))
		{
			await output.WriteLineAsync(
					$"Error: Could not read {TEMPLATE_DIR_KEY}: {current.StandardError.Trim()}",
					cancellationToken)
				.ConfigureAwait(false);

			return 1;
		}

		string existing = current.ExitCode == 0
			? current.StandardOutput.Trim()
			: string.Empty;
		string templateDir = existing.Length > 0
			? Path.GetFullPath(existing)
			: defaultTemplateDir;

		if (existing.Length > 0)
		{
			await output.WriteLineAsync(
					$"Using the existing {TEMPLATE_DIR_KEY}: {templateDir}",
					cancellationToken)
				.ConfigureAwait(false);
		}

		if (!await WriteHookAsync(
					Path.Combine(templateDir, "hooks"),
					force,
					output,
					cancellationToken)
				.ConfigureAwait(false))
		{
			return 1;
		}

		if (existing.Length == 0)
		{
			BufferedCommandResult set = await GlobalGit(globalConfigFile)
				.WithArguments(
					["config", "--global", TEMPLATE_DIR_KEY, templateDir])
				.ExecuteBufferedAsync(cancellationToken)
				.ConfigureAwait(false);

			if (set.ExitCode != 0)
			{
				await output.WriteLineAsync(
						$"Error: Could not set {TEMPLATE_DIR_KEY}: {set.StandardError.Trim()}",
						cancellationToken)
					.ConfigureAwait(false);

				return 1;
			}
		}

		await output.WriteLineAsync(
				"Global hook template installed. New repositories will include this hook automatically;",
				cancellationToken)
			.ConfigureAwait(false);
		await output.WriteLineAsync(
				"for existing repositories, run `git init` in each or `committy install-hook <repo>`.",
				cancellationToken)
			.ConfigureAwait(false);

		return 0;
	}

	private static async Task<int> InstallLocalAsync(
		string? repoPath,
		TextWriter output,
		bool force,
		CancellationToken cancellationToken)
	{
		string repo = Path.GetFullPath(
			string.IsNullOrWhiteSpace(repoPath) ? "." : repoPath);

		if (!Directory.Exists(repo))
		{
			await output.WriteLineAsync(
					$"Error: Repository path does not exist: {repo}",
					cancellationToken)
				.ConfigureAwait(false);

			return 1;
		}

		string? gitDir = await TryGetGitDirAsync(repo, cancellationToken)
			.ConfigureAwait(false);

		if (gitDir is null)
		{
			await output.WriteLineAsync(
					$"Error: Not a valid git repository: {repo}",
					cancellationToken)
				.ConfigureAwait(false);

			return 1;
		}

		if (!Path.IsPathRooted(gitDir))
		{
			gitDir = Path.GetFullPath(Path.Combine(repo, gitDir));
		}

		string defaultHooksDir = Path.Combine(gitDir, "hooks");

		// Respect a custom core.hooksPath (e.g. a repo-tracked ".githooks" dir); git
		// never looks in <gitdir>/hooks once that's set, so writing there would
		// silently install a hook that never runs.
		string hooksDir = await TryGetHooksDirAsync(repo, cancellationToken)
				.ConfigureAwait(false)
			?? defaultHooksDir;

		if (!PathsEqual(hooksDir, defaultHooksDir))
		{
			await output.WriteLineAsync(
					$"Note: {repo} uses a custom core.hooksPath ({hooksDir}). "
					+ "If that directory is tracked by git, this hook will be visible to "
					+ "other contributors too.",
					cancellationToken)
				.ConfigureAwait(false);
		}

		if (!await WriteHookAsync(
					hooksDir,
					force,
					output,
					cancellationToken)
				.ConfigureAwait(false))
		{
			return 1;
		}

		await output.WriteLineAsync(
				"Hook installed for the repository. AI suggestions will appear when you run `git commit`.",
				cancellationToken)
			.ConfigureAwait(false);

		return 0;
	}

	/// <summary>
	/// Writes the trampoline into <paramref name="hooksDir" /> unless a hook
	/// committy did not write is already there. A stale committy hook is
	/// replaced; a foreign one only with <paramref name="force" />, after
	/// copying it to <c>.bak</c>.
	/// </summary>
	/// <returns>False when a foreign hook was left in place.</returns>
	private static async Task<bool> WriteHookAsync(
		string hooksDir,
		bool force,
		TextWriter output,
		CancellationToken cancellationToken)
	{
		string hookPath = Path.Combine(hooksDir, HOOK_NAME);

		if (File.Exists(hookPath))
		{
			string content = await File
				.ReadAllTextAsync(hookPath, cancellationToken)
				.ConfigureAwait(false);

			if (IsCurrentTrampoline(content))
			{
				await output.WriteLineAsync(
						$"✓ Hook already up to date: {hookPath}",
						cancellationToken)
					.ConfigureAwait(false);

				return true;
			}

			if (!IsCommittyManaged(content))
			{
				if (!force)
				{
					await output.WriteLineAsync(
							$"Error: {hookPath} already exists and was not written by committy. "
							+ $"Re-run with --force to replace it; the old hook is kept as {HOOK_NAME}.bak.",
							cancellationToken)
						.ConfigureAwait(false);

					return false;
				}

				File.Copy(hookPath, hookPath + ".bak", true);

				// Delete rather than overwrite, so a symlinked hook (e.g. into
				// a dotfiles repo) is replaced instead of written through.
				File.Delete(hookPath);
				await output.WriteLineAsync(
						$"Backed up the existing hook to {hookPath}.bak",
						cancellationToken)
					.ConfigureAwait(false);
			}
		}

		await WriteTrampolineAsync(hooksDir, cancellationToken)
			.ConfigureAwait(false);
		await output.WriteLineAsync(
				$"✓ Hook installed: {hookPath}",
				cancellationToken)
			.ConfigureAwait(false);

		return true;
	}

	/// <summary>
	/// git with buffered, unvalidated output, reading
	/// <paramref name="globalConfigFile" /> as its global config when set.
	/// </summary>
	private static Command GlobalGit(string? globalConfigFile) =>
		Cli.Wrap("git")
			.WithValidation(CommandResultValidation.None)
			.WithEnvironmentVariables(environment =>
			{
				if (globalConfigFile is not null)
				{
					environment.Set("GIT_CONFIG_GLOBAL", globalConfigFile);
				}
			});

	/// <summary>
	/// True when the content looks like a committy-managed hook of any version. Used
	/// to
	/// decide whether <c>repair-hooks</c> may safely overwrite it; foreign hooks (no
	/// committy signature) are always left alone.
	/// </summary>
	public static bool IsCommittyManaged(string content)
	{
		string lower = content.ToLowerInvariant();

		return lower.Contains("committy")
			&& (lower.Contains("prepare-commit-msg")
				|| lower.Contains("--no-git")
				|| lower.Contains("ai-powered commit message hook"));
	}

	/// <summary>
	/// True when the content is exactly the current trampoline (no repair
	/// needed).
	/// </summary>
	public static bool IsCurrentTrampoline(string content) =>
		content.Replace("\r\n", "\n").Trim() == Trampoline.Trim();

	private static bool PathsEqual(string a, string b) =>
		string.Equals(
			Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
			Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
			OperatingSystem.IsWindows()
				? StringComparison.OrdinalIgnoreCase
				: StringComparison.Ordinal);

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

	private static async Task<string?> TryGetGitDirAsync(
		string repo,
		CancellationToken cancellationToken)
	{
		BufferedCommandResult result = await Cli.Wrap("git")
			.WithArguments(["-C", repo, "rev-parse", "--git-dir"])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync(cancellationToken)
			.ConfigureAwait(false);

		return result.ExitCode == 0
			&& !string.IsNullOrWhiteSpace(result.StandardOutput)
				? result.StandardOutput.Trim()
				: null;
	}

	/// <summary>
	/// Resolves the absolute hooks directory for a repository, or null if not
	/// a repo.
	/// </summary>
	public static async Task<string?> TryGetHooksDirAsync(
		string repo,
		CancellationToken cancellationToken = default)
	{
		BufferedCommandResult result = await Cli.Wrap("git")
			.WithArguments(["-C", repo, "rev-parse", "--git-path", "hooks"])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync(cancellationToken)
			.ConfigureAwait(false);

		if (result.ExitCode != 0)
		{
			return null;
		}

		string path = result.StandardOutput.Trim();

		if (string.IsNullOrEmpty(path))
		{
			return null;
		}

		return Path.IsPathRooted(path)
			? path
			: Path.GetFullPath(Path.Combine(repo, path));
	}

	/// <summary>
	/// Writes the trampoline into <paramref name="hooksDir" /> and marks it
	/// executable.
	/// </summary>
	public static async Task WriteTrampolineAsync(
		string hooksDir,
		CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(hooksDir);

		string hookPath = Path.Combine(hooksDir, HOOK_NAME);

		await File.WriteAllTextAsync(hookPath, Trampoline, cancellationToken)
			.ConfigureAwait(false);
		MakeExecutable(hookPath);
	}
}
