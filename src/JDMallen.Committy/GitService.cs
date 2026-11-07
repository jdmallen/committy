using CliWrap;
using CliWrap.Buffered;

namespace JDMallen.Committy;

public static class GitService
{
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
		await EnsureInsideWorkTreeAsync(cancellationToken)
			.ConfigureAwait(false);

		BufferedCommandResult result = await Cli.Wrap("git")
			.WithArguments(["diff", "--cached"])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync(cancellationToken)
			.ConfigureAwait(false);

		if (result.ExitCode != 0)
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
			BufferedCommandResult result = await Cli.Wrap("git")
				.WithArguments(["rev-parse", flag])
				.WithValidation(CommandResultValidation.None)
				.ExecuteBufferedAsync(cancellationToken)
				.ConfigureAwait(false);
			string path = result.StandardOutput.Trim();
			if (result.ExitCode == 0 && path.Length > 0)
			{
				return path;
			}
		}

		return null;
	}

	/// <summary>
	/// Throws a clear error when the working directory isn't inside a git work tree,
	/// so callers get an actionable message instead of git's raw <c>diff</c> usage output.
	/// </summary>
	private static async Task EnsureInsideWorkTreeAsync(
		CancellationToken cancellationToken)
	{
		BufferedCommandResult result = await Cli.Wrap("git")
			.WithArguments(["rev-parse", "--is-inside-work-tree"])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync(cancellationToken)
			.ConfigureAwait(false);

		if (result.ExitCode != 0
		    || !result.StandardOutput.Trim()
			    .Equals("true", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				"Not a git repository. Run committy from inside a git repository.");
		}
	}
}
