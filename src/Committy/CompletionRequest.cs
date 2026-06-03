namespace Committy;

/// <summary>
/// A provider-agnostic chat completion request: a system prompt, a user prompt,
/// and generation limits. Every <see cref="IChatCompletionClient" /> maps this
/// onto
/// its own wire format.
/// </summary>
public sealed record CompletionRequest(
	string SystemPrompt,
	string UserPrompt,
	int MaxTokens,
	double Temperature = 0.1);
