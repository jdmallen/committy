using System.Diagnostics;

namespace JDMallen.Committy;

public static class GitService
{
	/// <summary>
	/// <c>git diff --cached</c>, pinned to the plain format
	/// <see cref="PatchCondenser" /> parses. User config can otherwise drop the
	/// <c>a/</c> and <c>b/</c> prefixes (<c>diff.noprefix</c>,
	/// <c>diff.mnemonicPrefix</c>), hand the diff to another tool
	/// (<c>diff.external</c>), add ANSI colors (<c>color.diff=always</c>), or
	/// hide files outside the current directory (<c>diff.relative</c>).
	/// </summary>
	private static readonly string[] StagedDiffArguments =
	[
		"-c", "diff.noprefix=false",
		"-c", "diff.mnemonicPrefix=false",
		"diff", "--cached", "--no-ext-diff", "--no-color", "--no-relative",
	];

	/// <summary>
	/// Gets staged diff for manual execution fallback, throwing when nothing is
	/// staged.
	/// In hook usage, diff comes via stdin instead.
	/// </summary>
	public static async Task<string> GetStagedDiffAsync(
		CancellationToken cancellationToken = default)
		=> await TryGetStagedDiffAsync(cancellationToken).ConfigureAwait(false)
			?? throw new InvalidOperationException(
				"No staged changes found. Use 'git add' to stage files for commit.");

	/// <summary>
	/// Gets the staged diff, or <c>null</c> when nothing is staged.
	/// </summary>
	public static async Task<string?> TryGetStagedDiffAsync(
		CancellationToken cancellationToken = default)
	{
		// Outside a repository, git diff falls back to --no-index mode and
		// rejects --cached as a usage error, so there is no need to spend a
		// separate git process checking for a work tree first.
		const int usageErrorExitCode = 129;

		ProcessTextOutput result = await Git.RunAsync(
				StagedDiffArguments,
				cancellationToken)
			.ConfigureAwait(false);

		if (result.ExitStatus.ExitCode == usageErrorExitCode)
		{
			throw new InvalidOperationException(
				"Not a git repository. Run committy from inside a git repository.");
		}

		if (result.ExitStatus.ExitCode != 0)
		{
			throw new InvalidOperationException(
				$"Git diff failed: {result.StandardError}");
		}

		return string.IsNullOrWhiteSpace(result.StandardOutput)
			? null
			: result.StandardOutput;
	}

	/// <summary>
	/// The repository committy is being run in, as git sees it: the work tree's
	/// top level, or the git directory for a headless repository (such as a
	/// <c>GIT_DIR=~/.cfg</c> dotfiles setup). Null outside any repository,
	/// where only global and system git config apply.
	/// </summary>
	public static async Task<string?> TryGetRepositoryAsync(
		CancellationToken cancellationToken = default)
	{
		foreach (string flag in (string[])
				 ["--show-toplevel", "--absolute-git-dir"])
		{
			ProcessTextOutput result = await Git.RunAsync(
					["rev-parse", flag],
					cancellationToken)
				.ConfigureAwait(false);
			string path = result.StandardOutput.Trim();
			if (result.ExitStatus.ExitCode == 0 && path.Length > 0)
			{
				return path;
			}
		}

		return null;
	}
}
