namespace JDMallen.Committy;

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
	public async Task<CommitSuggestions> GenerateAsync(
		string patch,
		bool titlesOnly,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(patch))
		{
			throw new ArgumentException(
				"Patch cannot be null or empty",
				nameof(patch));
		}

		try
		{
			CompletionRequest request = CommitMessagePrompt.Build(
				patch,
				titlesOnly,
				maxTokensOverride);
			CompletionResult result = await client
				.GetCompletionAsync(request, cancellationToken)
				.ConfigureAwait(false);

			return new CommitSuggestions(
				CommitMessageParser.Parse(result.Text, titlesOnly),
				result.Usage);
		}
		catch (Exception ex) when (ex is not ArgumentException
									   and not OperationCanceledException)
		{
			throw new InvalidOperationException(
				$"Failed to generate commit message suggestions: {ex.Message}",
				ex);
		}
	}
}

/// <summary>The parsed suggestions and the tokens it took to get them.</summary>
/// <param name="Messages">
/// In titles-only mode, the title suggestions. Otherwise the single title+body
/// message.
/// </param>
/// <param name="Usage">
/// The request's token usage, or null when the provider didn't report it.
/// </param>
public sealed record CommitSuggestions(
	List<string> Messages,
	TokenUsage? Usage);
