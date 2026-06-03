using CliWrap;
using CliWrap.Buffered;

namespace Committy;

public class GitService
{
	/// <summary>
	/// Gets the staged diff, or <c>null</c> when nothing is staged.
	/// </summary>
	public static async Task<string?> TryGetStagedDiffAsync(
		CancellationToken cancellationToken = default)
	{
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
	/// Gets staged diff for manual execution fallback, throwing when nothing is staged.
	/// In hook usage, diff comes via stdin instead.
	/// </summary>
	public static async Task<string> GetStagedDiffAsync(CancellationToken cancellationToken = default)
	{
		return await TryGetStagedDiffAsync(cancellationToken).ConfigureAwait(false)
			?? throw new InvalidOperationException(
				"No staged changes found. Use 'git add' to stage files for commit.");
	}
}
