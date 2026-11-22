using Azure.Identity;

using JDMallen.Toolbox.AI.Azure;

namespace JDMallen.Committy;

/// <summary>
/// Builds the right <see cref="IChatCompletionClient" /> for the resolved
/// configuration, mapping committy's config onto the toolbox client options.
/// Adding a provider means adding a case here (and a client in
/// JDMallen.Toolbox.AI); nothing else in the generation pipeline changes.
/// </summary>
public sealed class ChatCompletionClientFactory(HttpClient httpClient)
{
	/// <param name="config">The resolved configuration.</param>
	/// <param name="allowInteractiveSignIn">
	/// Whether an Azure browser or device code sign-in may prompt. False in a
	/// git hook, where a missing sign-in fails with
	/// <c>AuthenticationRequiredException</c> instead.
	/// </param>
	public IChatCompletionClient Create(
		CommittyConfig config,
		bool allowInteractiveSignIn = true) =>
		config.Provider switch
		{
			AzureConfig azure => new AzureOpenAIChatClient(
				httpClient,
				AzureOptions(azure, allowInteractiveSignIn) with
				{
					IsReasoningModel = azure.ReasoningModel,
				}),
			AnthropicConfig anthropic => new AnthropicChatClient(
				httpClient,
				new AnthropicClientOptions(anthropic.ApiKey!, anthropic.Model)),
			OpenAIConfig openAI => new OpenAICompatibleChatClient(
				httpClient,
				new OpenAICompatibleClientOptions(
					openAI.BaseUrl!,
					openAI.Model,
					openAI.ApiKey)),
		};

	/// <summary>
	/// Whether <paramref name="exception" />, or one it wraps, says an Azure
	/// browser or device code sign-in is needed (a hook may not prompt).
	/// </summary>
	public static bool IsSignInRequired(Exception? exception)
	{
		for (; exception is not null; exception = exception.InnerException)
		{
			if (exception is AuthenticationRequiredException)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Key or Entra ID options for <paramref name="azure" />. No token cache
	/// for azurecli: a run makes one request, so <c>az</c> is asked for one
	/// token at most. Azure.Identity keeps its own cache.
	/// </summary>
	private static AzureOpenAIClientOptions AzureOptions(
		AzureConfig azure,
		bool allowInteractiveSignIn) =>
		azure.Auth switch
		{
			AzureAuth.AzureIdentity => AzureOpenAIClientOptions.ForEntraId(
				azure.Endpoint!,
				azure.Deployment,
				AzureCredentialFactory.CreateTokenProvider(
					azure.CredentialOptions(allowInteractiveSignIn),
					azure.EffectiveScope)),
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
