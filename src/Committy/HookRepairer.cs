using CliWrap;
using CliWrap.Buffered;

namespace Committy;

public enum HookRepairOutcome
{
	/// <summary>The hook was (or would be) replaced with the current trampoline.</summary>
	Updated,

	/// <summary>The hook is already the current trampoline.</summary>
	AlreadyCurrent,

	/// <summary>A non-committy hook (or custom core.hooksPath) was left untouched.</summary>
	SkippedForeign,

	/// <summary>No prepare-commit-msg hook present.</summary>
	NoHook,

	/// <summary>The repository could not be inspected.</summary>
	Error,
}

public sealed record HookRepairResult(
	string Repo,

	// ReSharper disable once NotAccessedPositionalProperty.Global
	string HookPath,
	HookRepairOutcome Outcome,
	string? Detail = null);

/// <summary>
/// Repairs stale committy hooks by replacing them with the current trampoline.
/// Only
/// touches hooks it recognizes as committy-managed (see
/// <see cref="HookInstaller.IsCommittyManaged" />); foreign hooks are reported and
/// left
/// alone. Reuses <see cref="HookInstaller" /> so there is a single source for the
/// hook text.
/// </summary>
public static class HookRepairer
{
	private static async Task<List<string>> CollectReposAsync(
		IReadOnlyList<string> scanRoots,
		IReadOnlyList<string> gitDirs,
		TextWriter output,
		CancellationToken cancellationToken)
	{
		var repos = new List<string>();

		// With no explicit targets, fall back to the repository the user is standing in.
		if (scanRoots.Count == 0 && gitDirs.Count == 0)
		{
			string? top = await GetTopLevelAsync(Directory.GetCurrentDirectory(), cancellationToken)
				.ConfigureAwait(false);

			if (top is not null)
			{
				repos.Add(top);
			}

			return repos;
		}

		foreach (string full in scanRoots.Select(Path.GetFullPath))
		{
			if (!Directory.Exists(full))
			{
				await output.WriteLineAsync($"Skipping {full}: directory does not exist.")
					.ConfigureAwait(false);

				continue;
			}

			repos.AddRange(FindRepos(full));
		}

		// --git-dir points committy straight at a git directory, including "headless"
		// layouts (a separate git dir without an in-tree .git, e.g. a ~/.cfg dotfiles
		// repo) that recursive scanning intentionally skips. git's -C resolution treats
		// such a directory as the repository, so the rest of the pipeline is unchanged.
		foreach (string full in gitDirs.Select(Path.GetFullPath))
		{
			if (!Directory.Exists(full))
			{
				await output.WriteLineAsync($"Skipping {full}: directory does not exist.")
					.ConfigureAwait(false);

				continue;
			}

			if (!await IsGitDirAsync(full, cancellationToken).ConfigureAwait(false))
			{
				await output.WriteLineAsync($"Skipping {full}: not a git directory.")
					.ConfigureAwait(false);

				continue;
			}

			repos.Add(full);
		}

		return [.. repos.Distinct()];
	}

	/// <summary>
	/// True when <paramref name="path" /> is (or resolves to) a git directory. Used to
	/// validate explicit <c>--git-dir</c> targets, which may be headless dotfiles-style
	/// repositories that have no in-tree <c>.git</c> entry.
	/// </summary>
	private static async Task<bool> IsGitDirAsync(string path, CancellationToken cancellationToken)
	{
		BufferedCommandResult result = await Cli.Wrap("git")
			.WithArguments(["-C", path, "rev-parse", "--git-dir"])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync(cancellationToken)
			.ConfigureAwait(false);

		return result.ExitCode == 0;
	}

	private static IEnumerable<string> FindRepos(string root)
	{
		var stack = new Stack<string>();
		stack.Push(root);

		while (stack.Count > 0)
		{
			string dir = stack.Pop();

			if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
			{
				yield return dir;
			}

			string[] subdirs;

			try
			{
				subdirs = Directory.GetDirectories(dir);
			}
			catch (Exception)
			{
				// Skip directories we can't read (permissions, etc.).
				continue;
			}

			foreach (string sub in subdirs.Where(sub => Path.GetFileName(sub) is not ".git"))
			{
				stack.Push(sub);
			}
		}
	}

	private static async Task<string?> GetConfigAsync(
		string repo,
		string key,
		CancellationToken cancellationToken)
	{
		BufferedCommandResult result = await Cli.Wrap("git")
			.WithArguments(["-C", repo, "config", "--get", key])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync(cancellationToken)
			.ConfigureAwait(false);

		string value = result.StandardOutput.Trim();

		return result.ExitCode == 0 && !string.IsNullOrEmpty(value) ? value : null;
	}

	private static async Task<string?> GetTopLevelAsync(
		string dir,
		CancellationToken cancellationToken)
	{
		BufferedCommandResult result = await Cli.Wrap("git")
			.WithArguments(["-C", dir, "rev-parse", "--show-toplevel"])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync(cancellationToken)
			.ConfigureAwait(false);

		return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput)
			? result.StandardOutput.Trim()
			: null;
	}

	public static async Task<HookRepairResult> RepairRepoAsync(
		string repo,
		bool dryRun,
		bool backup,
		CancellationToken cancellationToken = default)
	{
		string? customHooksPath = await GetConfigAsync(repo, "core.hooksPath", cancellationToken)
			.ConfigureAwait(false);

		if (!string.IsNullOrEmpty(customHooksPath))
		{
			return new HookRepairResult(
				repo,
				customHooksPath,
				HookRepairOutcome.SkippedForeign,
				"custom core.hooksPath in use");
		}

		string? hooksDir = await HookInstaller.TryGetHooksDirAsync(repo, cancellationToken)
			.ConfigureAwait(false);

		if (hooksDir is null)
		{
			return new HookRepairResult(
				repo,
				string.Empty,
				HookRepairOutcome.Error,
				"could not resolve hooks directory");
		}

		string hookPath = Path.Combine(hooksDir, HookInstaller.HookName);

		if (!File.Exists(hookPath))
		{
			return new HookRepairResult(repo, hookPath, HookRepairOutcome.NoHook);
		}

		string content = await File.ReadAllTextAsync(hookPath, cancellationToken).ConfigureAwait(false);

		if (HookInstaller.IsCurrentTrampoline(content))
		{
			return new HookRepairResult(repo, hookPath, HookRepairOutcome.AlreadyCurrent);
		}

		if (!HookInstaller.IsCommittyManaged(content))
		{
			return new HookRepairResult(
				repo,
				hookPath,
				HookRepairOutcome.SkippedForeign,
				"no committy signature");
		}

		if (dryRun)
		{
			return new HookRepairResult(repo, hookPath, HookRepairOutcome.Updated);
		}

		if (backup)
		{
			File.Copy(hookPath, hookPath + ".bak", true);
		}

		await HookInstaller.WriteTrampolineAsync(hooksDir, cancellationToken).ConfigureAwait(false);

		return new HookRepairResult(repo, hookPath, HookRepairOutcome.Updated);
	}

	private static async Task ReportAsync(
		IReadOnlyList<HookRepairResult> results,
		bool dryRun,
		TextWriter output)
	{
		string verb = dryRun ? "would update" : "updated";

		foreach (HookRepairResult result in results)
		{
			switch (result.Outcome)
			{
				case HookRepairOutcome.Updated:
					await output.WriteLineAsync($"  ✓ {verb}: {result.Repo}").ConfigureAwait(false);

					break;
				case HookRepairOutcome.SkippedForeign:
					await output.WriteLineAsync($"  — skipped (foreign): {result.Repo} [{result.Detail}]")
						.ConfigureAwait(false);

					break;
				case HookRepairOutcome.Error:
					await output.WriteLineAsync($"  ! error: {result.Repo} [{result.Detail}]")
						.ConfigureAwait(false);

					break;
				case HookRepairOutcome.AlreadyCurrent:
				case HookRepairOutcome.NoHook:
					break;
				default:
#pragma warning disable CA2208
					throw new ArgumentOutOfRangeException(nameof(result.Outcome), result.Outcome, "Unexpected outcome");
#pragma warning restore CA2208
			}
		}

		int updated = results.Count(r => r.Outcome == HookRepairOutcome.Updated);
		int current = results.Count(r => r.Outcome == HookRepairOutcome.AlreadyCurrent);
		int foreign = results.Count(r => r.Outcome == HookRepairOutcome.SkippedForeign);
		int none = results.Count(r => r.Outcome == HookRepairOutcome.NoHook);
		int errors = results.Count(r => r.Outcome == HookRepairOutcome.Error);

		await output.WriteLineAsync(
				$"\n{verb}: {updated}, already current: {current}, skipped: {foreign}, no hook: {none}, errors: {errors}")
			.ConfigureAwait(false);
	}

	public static async Task<int> RunAsync(
		IReadOnlyList<string> scanRoots,
		IReadOnlyList<string> gitDirs,
		bool dryRun,
		bool backup,
		bool currentRepoRequired,
		TextWriter output,
		CancellationToken cancellationToken = default)
	{
		List<string> repos = await CollectReposAsync(scanRoots, gitDirs, output, cancellationToken)
			.ConfigureAwait(false);

		if (repos.Count == 0)
		{
			if (scanRoots.Count == 0 && gitDirs.Count == 0 && currentRepoRequired)
			{
				await output.WriteLineAsync(
						"Error: not inside a git repository. Run from a repo, or pass --scan <dir> to sweep, --git-dir <dir> for a headless repo, or --global.")
					.ConfigureAwait(false);

				return 1;
			}

			await output.WriteLineAsync("No repositories to repair.").ConfigureAwait(false);

			return 0;
		}

		var results = new List<HookRepairResult>();

		foreach (string repo in repos)
		{
			results.Add(
				await RepairRepoAsync(
						repo,
						dryRun,
						backup,
						cancellationToken)
					.ConfigureAwait(false));
		}

		await ReportAsync(results, dryRun, output).ConfigureAwait(false);

		return 0;
	}
}
