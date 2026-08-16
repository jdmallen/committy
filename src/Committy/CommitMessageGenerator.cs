namespace Committy;

/// <summary>
/// Orchestrates commit message generation independently of any provider: build the
/// prompt, ask the configured <see cref="IChatCompletionClient" />, parse the
/// result.
/// The provider is injected, so this class never changes when a new LLM is added.
/// </summary>
/// <param name="client">The configured chat client to ask.</param>
/// <param name="maxTokensOverride">
/// Replaces the prompt's per-mode token budget when set. Reasoning models need
/// it: the thinking block comes out of the same budget and is discarded before
/// parsing.
/// </param>
public sealed class CommitMessageGenerator(
	IChatCompletionClient client,
	int? maxTokensOverride = null)
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
			CompletionRequest request = CommitMessagePrompt.Build(
				patch,
				titlesOnly,
				maxTokensOverride);
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
