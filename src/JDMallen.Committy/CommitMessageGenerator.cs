namespace JDMallen.Committy;

/// <summary>
/// Orchestrates commit message generation independently of any provider: build
/// the prompt, ask the configured <see cref="IChatCompletionClient" />, parse
/// the result. The provider is injected, so this class never changes when a new
/// LLM is added.
/// </summary>
/// <param name="client">The configured chat client to ask.</param>
/// <param name="maxTokensOverride">
/// Replaces the prompt's per-mode token budget when set. Reasoning models need
/// it: the thinking block comes out of the same budget and is discarded before
/// parsing.
/// </param>
/// <param name="timeoutSeconds">
/// The HTTP timeout the client was built with, quoted in the timeout error.
/// </param>
/// <param name="timeoutSettingKey">
/// The git config key that raises the timeout, named in the timeout error;
/// null when the provider has none.
/// </param>
public sealed class CommitMessageGenerator(
	IChatCompletionClient client,
	int? maxTokensOverride = null,
	int? timeoutSeconds = null,
	string? timeoutSettingKey = null)
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
				.GetCompletionAsync(request, cancellationToken);

			return new CommitSuggestions(
				CommitMessageParser.Parse(result.Text, titlesOnly),
				result.Usage);
		}
		catch (OperationCanceledException ex)
			when (!cancellationToken.IsCancellationRequested)
		{
			// The caller did not cancel, so the HTTP client's timeout did.
			throw new TimeoutException(BuildTimeoutMessage(), ex);
		}
		catch (Exception ex) when (ex is not ArgumentException
									   and not OperationCanceledException)
		{
			throw new InvalidOperationException(
				$"Failed to generate commit message suggestions: {ex.Message}",
				ex);
		}
	}

	private string BuildTimeoutMessage()
	{
		string waited = timeoutSeconds is { } seconds
			? $"within {seconds} seconds"
			: "in time";
		string hint = timeoutSettingKey is null
			? string.Empty
			: $" Raise {timeoutSettingKey} to wait longer.";

		return $"The provider did not answer {waited}.{hint}";
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
