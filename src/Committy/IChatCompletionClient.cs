namespace Committy;

/// <summary>
/// The single seam every LLM provider implements: given a
/// <see cref="CompletionRequest" />,
/// return the model's raw completion text. Provider-specific concerns (endpoint,
/// auth,
/// request/response shape) live in implementations. Prompt construction and
/// response
/// parsing are shared and deliberately live outside this interface, so adding a
/// provider
/// never means re-implementing them.
/// </summary>
public interface IChatCompletionClient
{
	Task<string> CompleteAsync(
		CompletionRequest request,
		CancellationToken cancellationToken = default);
}
