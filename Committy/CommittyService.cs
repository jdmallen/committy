namespace Committy;

public class CommittyService
{
	private readonly ClaudeService _claudeService;

	public CommittyService(ClaudeService claudeService)
	{
		_claudeService = claudeService;
	}

	public async Task<List<string>> GenerateCommitMessageSuggestionsAsync(string patch, string apiKey)
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
			var suggestions = await _claudeService.GenerateCommitMessageSuggestionsAsync(patch, apiKey);
			
			return suggestions;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to generate commit message suggestions: {ex.Message}", ex);
		}
	}

	public async Task<string> ReadPatchFromStdinAsync()
	{
		if (Console.IsInputRedirected)
		{
			using var reader = new StreamReader(Console.OpenStandardInput(), Console.InputEncoding);
			return await reader.ReadToEndAsync();
		}
		else
		{
			throw new InvalidOperationException("No input data available. Please pipe git patch data to stdin.");
		}
	}

	public async Task CopyToClipboardAsync(string text)
	{
		try
		{
			await TextCopy.ClipboardService.SetTextAsync(text);
		}
		catch (Exception ex)
		{
			// Don't fail the entire operation if clipboard fails
			Console.Error.WriteLine($"Warning: Failed to copy to clipboard: {ex.Message}");
		}
	}
}