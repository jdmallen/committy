namespace Committy;

/// <summary>
/// Orchestrates commit message generation independently of any provider: build the
/// prompt, ask the configured <see cref="IChatCompletionClient" />, parse the
/// result.
/// The provider is injected, so this class never changes when a new LLM is added.
/// </summary>
public sealed class CommitMessageGenerator(IChatCompletionClient client)
{
	public async Task<List<string>> GenerateAsync(
		string patch,
		bool titlesOnly,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(patch))
		{
			throw new ArgumentException("Patch cannot be null or empty", nameof(patch));
		}

		try
		{
			CompletionRequest request = CommitMessagePrompt.Build(patch, titlesOnly);
			string raw = await client.CompleteAsync(request, cancellationToken).ConfigureAwait(false);

			return CommitMessageParser.Parse(raw, titlesOnly);
		}
		catch (Exception ex) when (ex is not ArgumentException and not OperationCanceledException)
		{
			throw new InvalidOperationException(
				$"Failed to generate commit message suggestions: {ex.Message}",
				ex);
		}
	}
}
