namespace Committy;

public class CommittyService
{
	private readonly IAzureOpenAIService _azureOpenAIService;
	private static bool _clipboardWarningShown = false;

	public CommittyService(IAzureOpenAIService azureOpenAIService)
	{
		_azureOpenAIService = azureOpenAIService;
	}

	public async Task<List<string>> GenerateCommitMessageSuggestionsAsync(string patch, string apiKey, string endpoint, string deploymentName)
	{
		if (string.IsNullOrWhiteSpace(patch))
		{
			throw new ArgumentException("Patch cannot be null or empty", nameof(patch));
		}

		if (string.IsNullOrWhiteSpace(apiKey))
		{
			throw new ArgumentException("API key cannot be null or empty", nameof(apiKey));
		}

		if (string.IsNullOrWhiteSpace(endpoint))
		{
			throw new ArgumentException("Endpoint cannot be null or empty", nameof(endpoint));
		}

		if (string.IsNullOrWhiteSpace(deploymentName))
		{
			throw new ArgumentException("Deployment name cannot be null or empty", nameof(deploymentName));
		}

		try
		{
			var suggestions = await _azureOpenAIService.GenerateCommitMessageSuggestionsAsync(patch, apiKey, endpoint, deploymentName);
			
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
		catch (Exception)
		{
			// Don't fail the entire operation if clipboard fails
			// Only show warning once to avoid spam
			if (!_clipboardWarningShown)
			{
				Console.Error.WriteLine("Warning: Clipboard functionality not available (xsel/xclip not installed)");
				_clipboardWarningShown = true;
			}
		}
	}
}