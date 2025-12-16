using CliWrap;
using CliWrap.Buffered;
using System.Text;

namespace Committy;

public class GitService
{
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

	public async Task<bool> HasStagedChangesAsync()
	{
		try
		{
			var status = await GetRepoStatusAsync();
			
			// Check if any lines have staged changes (first column, position 0)
			// Format: XY filename where X = staged status, Y = worktree status
			// Staged indicators: M A R C D T U (Modified, Added, Renamed, Copied, Deleted, Type changed, Updated)
			var lines = status.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
			return lines.Any(line => line.Length >= 2 && 
			                line[0] != ' ' &&    // Not unstaged-only
			                line[0] != '?' &&    // Not untracked
			                line[0] != '!');     // Not ignored
		}
		catch
		{
			return false;
		}
	}

	public async Task CommitAsync(string message)
	{
		var result = await Cli.Wrap("git")
			.WithArguments(["commit", "-m", message])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync();

		if (result.ExitCode != 0)
		{
			throw new InvalidOperationException($"Git commit failed: {result.StandardError}");
		}
	}

	public async Task<string> GetRepoStatusAsync()
	{
		var result = await Cli.Wrap("git")
			.WithArguments(["status", "--porcelain"])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync();

		if (result.ExitCode != 0)
		{
			throw new InvalidOperationException($"Git status failed: {result.StandardError}");
		}

		return result.StandardOutput;
	}

	public async Task<bool> IsGitRepositoryAsync()
	{
		var result = await Cli.Wrap("git")
			.WithArguments(["rev-parse", "--git-dir"])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync();

		return result.ExitCode == 0;
	}
}