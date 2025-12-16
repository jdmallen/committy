namespace Committy;

public class CommitMessageGenerator
{
	private readonly ClaudeService _claudeService;

	public CommitMessageGenerator(ClaudeService claudeService)
	{
		_claudeService = claudeService;
	}

	public async Task<string> GenerateCommitMessageAsync(string patch, string apiKey)
	{
		if (string.IsNullOrWhiteSpace(patch))
		{
			throw new ArgumentException("Patch cannot be null or empty", nameof(patch));
		}

		if (string.IsNullOrWhiteSpace(apiKey))
		{
			throw new ArgumentException("API key cannot be null or empty", nameof(apiKey));
		}

		try
		{
			var commitMessage = await _claudeService.GenerateCommitMessageAsync(patch, apiKey);
			
			return commitMessage.Trim();
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to generate commit message: {ex.Message}", ex);
		}
	}
}