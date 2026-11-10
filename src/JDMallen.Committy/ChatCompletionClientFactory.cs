namespace JDMallen.Committy;

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
				AzureOptions(config.Azure!) with
				{
					IsReasoningModel = config.Azure!.ReasoningModel,
				}),
			Provider.Anthropic => new AnthropicChatClient(
				httpClient,
				new AnthropicClientOptions(
					config.Anthropic!.ApiKey!,
					config.Anthropic.Model)),
			Provider.OpenAI => new OpenAICompatibleChatClient(
				httpClient,
				new OpenAICompatibleClientOptions(
					config.OpenAI!.BaseUrl!,
					config.OpenAI.Model,
					config.OpenAI.ApiKey)),
			_ => throw new InvalidOperationException(
				$"Unsupported provider: {config.Provider}"),
		};

	/// <summary>
	/// Key or Entra ID options for <paramref name="azure" />. No token cache:
	/// a run makes one request, so <c>az</c> is asked for one token at most.
	/// </summary>
	private static AzureOpenAIClientOptions AzureOptions(AzureConfig azure) =>
		azure.Auth switch
		{
			AzureAuth.AzureCli => AzureOpenAIClientOptions.ForEntraId(
				azure.Endpoint!,
				azure.Deployment,
				new AzureCliTokenProvider(azure.TokenProviderOptions())),
			_ => new AzureOpenAIClientOptions(
				azure.ApiKey!,
				azure.Endpoint!,
				azure.Deployment),
		};
}
