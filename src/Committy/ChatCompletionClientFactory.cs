namespace Committy;

/// <summary>
/// Builds the right <see cref="IChatCompletionClient" /> for the resolved
/// configuration.
/// Adding a provider means adding a case here (and a client class); nothing else
/// in the
/// generation pipeline changes.
/// </summary>
public sealed class ChatCompletionClientFactory(IHttpService http)
{
	public IChatCompletionClient Create(CommittyConfig config) =>
		config.Provider switch
		{
			Provider.Azure => new AzureOpenAIChatClient(http, config.Azure!),
			Provider.Anthropic => new AnthropicChatClient(http, config.Anthropic!),
			_ => throw new InvalidOperationException($"Unsupported provider: {config.Provider}"),
		};
}
