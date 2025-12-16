using CliWrap;
using CliWrap.Buffered;

namespace Committy;

public class GitService
{
	/// <summary>
	/// Gets staged diff for manual execution fallback.
	/// In hook usage, diff comes via stdin instead.
	/// </summary>
	public async Task<string> GetStagedDiffAsync()
	{
		var result = await Cli.Wrap("git")
			.WithArguments(["diff", "--cached"])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync();

		if (result.ExitCode != 0)
		{
			throw new InvalidOperationException($"Git diff failed: {result.StandardError}");
		}

		if (string.IsNullOrWhiteSpace(result.StandardOutput))
		{
			throw new InvalidOperationException("No staged changes found. Use 'git add' to stage files for commit.");
		}

		return result.StandardOutput;
	}
}