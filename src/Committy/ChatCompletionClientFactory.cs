namespace Committy;

/// <summary>
/// Builds the right <see cref="IChatCompletionClient" /> for the resolved
/// configuration, mapping committy's config onto the toolbox client options.
/// Adding a provider means adding a case here (and a client in
/// JDMallen.Toolbox.AI); nothing else in the generation pipeline changes.
/// </summary>
public sealed class ChatCompletionClientFactory(HttpClient httpClient)
{
	public IChatCompletionClient Create(CommittyConfig config) =>
		config.Provider switch
		{
			Provider.Azure => new AzureOpenAIChatClient(
				httpClient,
				new AzureOpenAIClientOptions(
					config.Azure!.ApiKey!,
					config.Azure.Endpoint!,
					config.Azure.Deployment)),
			Provider.Anthropic => new AnthropicChatClient(
				httpClient,
				new AnthropicClientOptions(
					config.Anthropic!.ApiKey!,
					config.Anthropic.Model)),
			_ => throw new InvalidOperationException($"Unsupported provider: {config.Provider}"),
		};
}
