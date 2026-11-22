using System.Diagnostics;

namespace JDMallen.Committy;

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
/// Only touches hooks it recognizes as committy-managed (see
/// <see cref="HookInstaller.IsCommittyManaged" />); foreign hooks are reported
/// and left alone. Reuses <see cref="HookInstaller" /> so there is a single source
/// for the hook text.
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
			string? top = await GetTopLevelAsync(
				Directory.GetCurrentDirectory(),
				cancellationToken);

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
				await output.WriteLineAsync(
					$"Skipping {full}: directory does not exist.",
					cancellationToken);

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
				await output.WriteLineAsync(
					$"Skipping {full}: directory does not exist.",
					cancellationToken);

				continue;
			}

			if (!await IsGitDirAsync(full, cancellationToken))
			{
				await output.WriteLineAsync(
					$"Skipping {full}: not a git directory.",
					cancellationToken);

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
	private static async Task<bool> IsGitDirAsync(
		string path,
		CancellationToken cancellationToken)
	{
		ProcessTextOutput result = await Git.RunAsync(
			["-C", path, "rev-parse", "--git-dir"],
			cancellationToken);

		return result.ExitStatus.ExitCode == 0;
	}

	/// <summary>
	/// Unreadable directories are skipped, and so are directory symlinks
	/// (and junctions): following one that points back up the tree would find
	/// the same repositories again at every level.
	/// </summary>
	private static readonly EnumerationOptions ScanOptions = new()
	{
		IgnoreInaccessible = true,
		AttributesToSkip = FileAttributes.ReparsePoint,
	};

	private static IEnumerable<string> FindRepos(string root)
	{
		var stack = new Stack<string>();
		stack.Push(root);

		while (stack.Count > 0)
		{
			string dir = stack.Pop();

			if (Directory.Exists(Path.Combine(dir, ".git"))
				|| File.Exists(Path.Combine(dir, ".git")))
			{
				yield return dir;
			}

			string[] subdirs;

			try
			{
				subdirs = Directory.GetDirectories(dir, "*", ScanOptions);
			}
			catch (Exception ex)
				when (ex is IOException or UnauthorizedAccessException)
			{
				// Gone since it was listed, or unreadable itself.
				continue;
			}

			foreach (string sub in subdirs.Where(sub
						 => Path.GetFileName(sub) is not ".git"))
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
		ProcessTextOutput result = await Git.RunAsync(
			["-C", repo, "config", "--get", key],
			cancellationToken);

		string value = result.StandardOutput.Trim();

		return result.ExitStatus.ExitCode == 0 && !string.IsNullOrEmpty(value)
			? value
			: null;
	}

	private static async Task<string?> GetTopLevelAsync(
		string dir,
		CancellationToken cancellationToken)
	{
		ProcessTextOutput result = await Git.RunAsync(
			["-C", dir, "rev-parse", "--show-toplevel"],
			cancellationToken);

		return result.ExitStatus.ExitCode == 0
			&& !string.IsNullOrWhiteSpace(result.StandardOutput)
				? result.StandardOutput.Trim()
				: null;
	}

	public static async Task<HookRepairResult> RepairRepoAsync(
		string repo,
		bool dryRun,
		CancellationToken cancellationToken = default)
	{
		string? customHooksPath = await GetConfigAsync(
			repo,
			"core.hooksPath",
			cancellationToken);

		if (!string.IsNullOrEmpty(customHooksPath))
		{
			return new HookRepairResult(
				repo,
				customHooksPath,
				HookRepairOutcome.SkippedForeign,
				"custom core.hooksPath in use");
		}

		string? hooksDir = await HookInstaller
			.TryGetHooksDirAsync(repo, cancellationToken);

		if (hooksDir is null)
		{
			return new HookRepairResult(
				repo,
				string.Empty,
				HookRepairOutcome.Error,
				"could not resolve hooks directory");
		}

		string hookPath = Path.Combine(hooksDir, HookInstaller.HOOK_NAME);

		if (!File.Exists(hookPath))
		{
			return new HookRepairResult(
				repo,
				hookPath,
				HookRepairOutcome.NoHook);
		}

		string content = await File
			.ReadAllTextAsync(hookPath, cancellationToken);

		if (HookInstaller.IsCurrentTrampoline(content))
		{
			return new HookRepairResult(
				repo,
				hookPath,
				HookRepairOutcome.AlreadyCurrent);
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
			return new HookRepairResult(
				repo,
				hookPath,
				HookRepairOutcome.Updated);
		}

		HookInstaller.BackUpHook(hookPath);

		await HookInstaller.WriteTrampolineAsync(hooksDir, cancellationToken);

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
					await output.WriteLineAsync($"  ✓ {verb}: {result.Repo}");

					break;
				case HookRepairOutcome.SkippedForeign:
					await output.WriteLineAsync(
						$"  — skipped (foreign): {result.Repo} [{result.Detail}]");

					break;
				case HookRepairOutcome.Error:
					await output.WriteLineAsync(
						$"  ! error: {result.Repo} [{result.Detail}]");

					break;
				case HookRepairOutcome.AlreadyCurrent:
				case HookRepairOutcome.NoHook:
					break;
				default:
#pragma warning disable CA2208
					throw new ArgumentOutOfRangeException(
						nameof(result.Outcome),
						result.Outcome,
						"Unexpected outcome");
#pragma warning restore CA2208
			}
		}

		Dictionary<HookRepairOutcome, int> counts = results
			.CountBy(result => result.Outcome)
			.ToDictionary();

		await output.WriteLineAsync(
			$"\n{verb}: {Count(HookRepairOutcome.Updated)}, already current: {Count(HookRepairOutcome.AlreadyCurrent)}, skipped: {Count(HookRepairOutcome.SkippedForeign)}, no hook: {Count(HookRepairOutcome.NoHook)}, errors: {Count(HookRepairOutcome.Error)}");

		return;

		int Count(HookRepairOutcome outcome) =>
			counts.GetValueOrDefault(outcome);
	}

	public static async Task<int> RunAsync(
		IReadOnlyList<string> scanRoots,
		IReadOnlyList<string> gitDirs,
		bool dryRun,
		bool currentRepoRequired,
		TextWriter output,
		CancellationToken cancellationToken = default)
	{
		List<string> repos = await CollectReposAsync(
			scanRoots,
			gitDirs,
			output,
			cancellationToken);

		if (repos.Count == 0)
		{
			if (scanRoots.Count == 0
				&& gitDirs.Count == 0
				&& currentRepoRequired)
			{
				await output.WriteLineAsync(
					"Error: not inside a git repository. Run from a repo, or pass --scan <dir> to sweep, --git-dir <dir> for a headless repo, or --global.",
					cancellationToken);

				return 1;
			}

			await output.WriteLineAsync(
				"No repositories to repair.",
				cancellationToken);

			return 0;
		}

		var results = new List<HookRepairResult>();

		foreach (string repo in repos)
		{
			results.Add(
				await RepairRepoAsync(
					repo,
					dryRun,
					cancellationToken));
		}

		await ReportAsync(results, dryRun, output);

		return 0;
	}
}
