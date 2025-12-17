namespace Committy;

public class CommittyService(IAzureOpenAIService azureOpenAIService)
{
	private static bool _clipboardWarningShown;

	public async Task<List<string>> GenerateCommitMessageSuggestionsAsync(
		string patch,
		string apiKey,
		string endpoint,
		string deploymentName,
		CancellationToken cancellationToken = default)
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
			throw new ArgumentException(
				"Deployment name cannot be null or empty",
				nameof(deploymentName));
		}

		try
		{
			List<string> suggestions =
				await azureOpenAIService.GenerateCommitMessageSuggestionsAsync(
					patch,
					apiKey,
					endpoint,
					deploymentName,
					cancellationToken).ConfigureAwait(false);

			return suggestions;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException(
				$"Failed to generate commit message suggestions: {ex.Message}",
				ex);
		}
	}

	public static async Task<string> ReadPatchFromStdinAsync(CancellationToken cancellationToken = default)
	{
		if (Console.IsInputRedirected)
		{
			using var reader = new StreamReader(Console.OpenStandardInput(), Console.InputEncoding);

			return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
		}
		else
		{
			throw new InvalidOperationException(
				"No input data available. Please pipe git patch data to stdin.");
		}
	}

	public static async Task CopyToClipboardAsync(string text, CancellationToken cancellationToken = default)
	{
		try
		{
			await TextCopy.ClipboardService.SetTextAsync(text, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception)
		{
			// Don't fail the entire operation if clipboard fails
			// Only show warning once to avoid spam
			if (!_clipboardWarningShown)
			{
				await Console.Error.WriteLineAsync(
					"Warning: Clipboard functionality not available on this system. If running Linux, try installing `xsel` package.").ConfigureAwait(false);
				_clipboardWarningShown = true;
			}
		}
	}
}
