using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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

	/// <summary>
	/// Starts the line, just after the shebang, that records the SHA-256 of
	/// the rest of the hook, so an installed hook can later prove it is still
	/// exactly what committy wrote.
	/// </summary>
	private const string CHECKSUM_PREFIX = "# committy-hook-sha256: ";

	/// <summary>
	/// SHA-256 of every hook text committy shipped before hooks carried a
	/// checksum line, as <see cref="Normalize" />d. Only an exact match is
	/// treated as committy's, so a hand-written hook that happens to call
	/// committy is never mistaken for one. Frozen: hooks written from now on
	/// carry <see cref="CHECKSUM_PREFIX" /> instead.
	/// </summary>
	private static readonly HashSet<string> LegacyHookHashes =
	[
		// install-git-hook.sh, 7ade510 (2025-12-15)
		"dd058daf01bd8be8fcfee7544b374a2a91832468121c41ca3f6bfbe5f30fb184",
		// install-git-hook.sh, 978d162 (2025-12-15)
		"05cb1a93a5c8be0d7961dc00da799df3debcb8fb3af6ae12f2f082f894ea42d8",
		// install-git-hook.sh, f8f196a (2025-12-15)
		"4b94fb0801411b63c4deb244fbeee1268a2051d5b6522656d8409791b167d72d",
		// install-git-hook.ps1, 9abd999 (2025-12-15)
		"fe4e8cc35e14cffc39286984c124987c54897c4095d8070851bd75a53a685f37",
		// install-git-hook.sh, f6b7f07 (2025-12-17)
		"35de5b922156e036b1142271ef258efbb9a9ebb11c799fb730b447f6e46aa3ab",
		// hooks/prepare-commit-msg, bb6dd04 (2025-12-17)
		"8511ef131d92d37f3faf6cfb2fb6cee816c6753db6e07cc7c6fc032efcc548a2",
		// hooks/prepare-commit-msg, 7b5fd30 (2025-12-17)
		"625768dc0f5f114b3356f36204c768494c601a19077717f1a71fb82e3046bb11",
		// hooks/prepare-commit-msg, 1fa40e3 (2025-12-17)
		"750512082f3a5f937b2008da7220f2b4f50d094b92e1ccb0ddb6dd3170467e92",
		// hooks/prepare-commit-msg, f52cab3 (2026-05-15)
		"9ddab3f9ab1e6d137f50e295b5275fa1c741e2cd506deffb4f878efc6a37538e",
		// hooks/prepare-commit-msg, 7d6ffd7 (2026-06-03)
		"152110055adc0a625f752ac31834e056c5ffc17c6cfd7dd72ba04dc02bc56fab",
		// The trampoline without a checksum line, 7d6ffd7 (2026-06-03) to 1.9.0
		"e60a55e23dc41fb43ed79342582a582f82fd62675f814d6eb671494f109f4434",
	];

	/// <summary>
	/// The current trampoline text, with normalized newlines and its checksum
	/// line.
	/// </summary>
	public static string Trampoline { get; } =
		WithChecksum(TRAMPOLINE_SCRIPT.Replace("\r\n", "\n"));

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
	/// The global git config file to use instead of the user's (via
	/// <c>GIT_CONFIG_GLOBAL</c>), so tests can leave ~/.gitconfig alone; null
	/// for git's own.
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
		Dictionary<string, string>? environment = globalConfigFile is null
			? null
			: new Dictionary<string, string>
			{
				["GIT_CONFIG_GLOBAL"] = globalConfigFile,
			};
		ProcessTextOutput current = await Git.RunAsync(
				[
					"config", "--global", "--type=path", "--get",
					TEMPLATE_DIR_KEY,
				],
				environment,
				cancellationToken)
			.ConfigureAwait(false);

		// Exit code 1 means the key is unset; anything else is a real error,
		// such as an unreadable config file.
		if (current.ExitStatus.ExitCode is not (0 or 1))
		{
			await output.WriteLineAsync(
					$"Error: Could not read {TEMPLATE_DIR_KEY}: {current.StandardError.Trim()}",
					cancellationToken)
				.ConfigureAwait(false);

			return 1;
		}

		string existing = current.ExitStatus.ExitCode == 0
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
			ProcessTextOutput set = await Git.RunAsync(
					["config", "--global", TEMPLATE_DIR_KEY, templateDir],
					environment,
					cancellationToken)
				.ConfigureAwait(false);

			if (set.ExitStatus.ExitCode != 0)
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
	/// replaced; a foreign one only with <paramref name="force" />. Either way
	/// the old hook is backed up first (<see cref="BackUpHook" />).
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
							+ "Re-run with --force to replace it; the old hook is backed up first.",
							cancellationToken)
						.ConfigureAwait(false);

					return false;
				}
			}

			await output.WriteLineAsync(
					$"Backed up the existing hook to {BackUpHook(hookPath)}",
					cancellationToken)
				.ConfigureAwait(false);
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
	/// True when the content is a hook committy wrote and nobody has edited
	/// since: either its checksum line still matches the rest of the hook, or
	/// (for hooks older than checksum lines) it is exactly one of the texts
	/// committy shipped. Anything else, including a hand-written hook that
	/// calls committy, is foreign and is not replaced without consent.
	/// </summary>
	public static bool IsCommittyManaged(string content)
	{
		string normalized = Normalize(content);
		string[] lines = normalized.Split('\n');
		string? checksumLine = lines.FirstOrDefault(line
			=> line.StartsWith(CHECKSUM_PREFIX, StringComparison.Ordinal));

		if (checksumLine is null)
		{
			return LegacyHookHashes.Contains(Sha256(normalized));
		}

		string rest = string.Join(
			'\n',
			lines.Where(line => !ReferenceEquals(line, checksumLine)));

		return checksumLine[CHECKSUM_PREFIX.Length..].Trim() == Sha256(rest);
	}

	/// <summary>
	/// True when the content is exactly the current trampoline (no repair
	/// needed).
	/// </summary>
	public static bool IsCurrentTrampoline(string content) =>
		Normalize(content) == Normalize(Trampoline);

	/// <summary>
	/// Copies the hook at <paramref name="hookPath" /> aside before it is
	/// replaced, to <c>prepare-commit-msg.bak</c>. When that file already
	/// holds a different backup, a timestamped
	/// <c>prepare-commit-msg-saved_YYYYMMDDThhmmss.bak</c> is used instead,
	/// so an earlier backup is never overwritten. git only runs a hook with
	/// the exact name, so neither copy runs.
	/// </summary>
	/// <returns>The backup's path.</returns>
	public static string BackUpHook(string hookPath)
	{
		string backupPath = hookPath + ".bak";

		if (File.Exists(backupPath))
		{
			if (File.ReadAllBytes(backupPath)
				.AsSpan()
				.SequenceEqual(File.ReadAllBytes(hookPath)))
			{
				return backupPath;
			}

			backupPath = string.Create(
				CultureInfo.InvariantCulture,
				$"{hookPath}-saved_{DateTime.Now:yyyyMMdd'T'HHmmss}.bak");
		}

		File.Copy(hookPath, backupPath);

		return backupPath;
	}

	/// <summary>
	/// Inserts the checksum line after the shebang. The checksum covers the
	/// <see cref="Normalize" />d hook without that line.
	/// </summary>
	private static string WithChecksum(string hook)
	{
		int shebangEnd = hook.IndexOf('\n');

		return string.Concat(
			hook.AsSpan(0, shebangEnd + 1),
			CHECKSUM_PREFIX,
			Sha256(Normalize(hook)),
			hook.AsSpan(shebangEnd));
	}

	/// <summary>
	/// The hook text as compared and hashed: no byte order mark (Windows
	/// PowerShell 5 wrote one), LF line endings, no trailing spaces on any
	/// line, and no blank lines at either end.
	/// </summary>
	private static string Normalize(string content) =>
		string.Join(
				'\n',
				content.TrimStart('\uFEFF')
					.Replace("\r\n", "\n")
					.Split('\n')
					.Select(line => line.TrimEnd()))
			.Trim();

	private static string Sha256(string text) =>
		Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

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
		ProcessTextOutput result = await Git.RunAsync(
				["-C", repo, "rev-parse", "--git-dir"],
				cancellationToken)
			.ConfigureAwait(false);

		return result.ExitStatus.ExitCode == 0
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
		ProcessTextOutput result = await Git.RunAsync(
				["-C", repo, "rev-parse", "--git-path", "hooks"],
				cancellationToken)
			.ConfigureAwait(false);

		if (result.ExitStatus.ExitCode != 0)
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

		// Delete rather than overwrite, so a symlinked hook (e.g. into a
		// dotfiles repo) is replaced instead of written through. Callers back
		// the old hook up first.
		File.Delete(hookPath);
		await File.WriteAllTextAsync(hookPath, Trampoline, cancellationToken)
			.ConfigureAwait(false);
		MakeExecutable(hookPath);
	}
}
