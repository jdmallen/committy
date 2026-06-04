using CliWrap;
using CliWrap.Buffered;

namespace Committy;

public static class GitService
{
	/// <summary>
	/// Gets staged diff for manual execution fallback, throwing when nothing is
	/// staged.
	/// In hook usage, diff comes via stdin instead.
	/// </summary>
	public static async Task<string> GetStagedDiffAsync(CancellationToken cancellationToken = default)
		=> await TryGetStagedDiffAsync(cancellationToken).ConfigureAwait(false)
			?? throw new InvalidOperationException(
				"No staged changes found. Use 'git add' to stage files for commit.");

	/// <summary>
	/// Gets the staged diff, or <c>null</c> when nothing is staged.
	/// </summary>
	public static async Task<string?> TryGetStagedDiffAsync(
		CancellationToken cancellationToken = default)
	{
		await EnsureInsideWorkTreeAsync(cancellationToken).ConfigureAwait(false);

		BufferedCommandResult result = await Cli.Wrap("git")
			.WithArguments(["diff", "--cached"])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync(cancellationToken)
			.ConfigureAwait(false);

		if (result.ExitCode != 0)
		{
			throw new InvalidOperationException($"Git diff failed: {result.StandardError}");
		}

		return string.IsNullOrWhiteSpace(result.StandardOutput) ? null : result.StandardOutput;
	}

	/// <summary>
	/// Throws a clear error when the working directory isn't inside a git work tree,
	/// so callers get an actionable message instead of git's raw <c>diff</c> usage output.
	/// </summary>
	private static async Task EnsureInsideWorkTreeAsync(CancellationToken cancellationToken)
	{
		BufferedCommandResult result = await Cli.Wrap("git")
			.WithArguments(["rev-parse", "--is-inside-work-tree"])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync(cancellationToken)
			.ConfigureAwait(false);

		if (result.ExitCode != 0
			|| !result.StandardOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				"Not a git repository. Run committy from inside a git repository.");
		}
	}
}
