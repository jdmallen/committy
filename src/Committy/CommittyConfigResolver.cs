namespace Committy;

/// <summary>Optional per-invocation overrides, typically sourced from CLI flags.</summary>
public sealed record ConfigOverrides(
	string? Provider = null,
	string? ApiKey = null,
	string? Endpoint = null,
	string? Deployment = null,
	string? Model = null);

/// <summary>
/// Resolves the effective <see cref="CommittyConfig" /> with the precedence
/// CLI flag → environment variable → git config → built-in default. Git config
/// keys
/// live under the <c>committy.*</c> section and are written by
/// <c>committy config</c>;
/// the legacy <c>AZURE_OPENAI_*</c> environment variables remain supported as
/// overrides.
/// </summary>
public sealed class CommittyConfigResolver(GitConfigStore gitConfig)
{
	public const string DefaultDeployment = "gpt-4.1-mini";
	public const string DefaultAnthropicModel = "claude-haiku-4-5-20251001";

	// git config keys
	public const string ProviderKey = "committy.provider";
	public const string TitlesOnlyKey = "committy.titlesonly";
	public const string AzureApiKeyKey = "committy.azure.apikey";
	public const string AzureEndpointKey = "committy.azure.endpoint";
	public const string AzureDeploymentKey = "committy.azure.deployment";
	public const string AnthropicApiKeyKey = "committy.anthropic.apikey";
	public const string AnthropicModelKey = "committy.anthropic.model";

	// environment variables
	private const string PROVIDER_ENV = "COMMITTY_PROVIDER";
	private const string TITLES_ONLY_ENV = "COMMITTY_TITLES_ONLY";
	private const string AZURE_API_KEY_ENV = "AZURE_OPENAI_API_KEY";
	private const string AZURE_ENDPOINT_ENV = "AZURE_OPENAI_ENDPOINT_HOST";
	private const string AZURE_DEPLOYMENT_ENV = "AZURE_OPENAI_DEPLOYMENT";
	private const string ANTHROPIC_API_KEY_ENV = "ANTHROPIC_API_KEY";
	private const string ANTHROPIC_MODEL_ENV = "ANTHROPIC_MODEL";

	private static bool IsTruthy(string? value) =>
		value?.Trim().ToLowerInvariant() switch
		{
			"1" or "true" or "yes" or "on" => true,
			_                              => false,
		};

	public static Provider ParseProvider(string? name) =>
		name?.Trim().ToLowerInvariant() switch
		{
			"anthropic" or "claude" => Provider.Anthropic,
			_                       => Provider.Azure,
		};

	public async Task<CommittyConfig> ResolveAsync(
		ConfigOverrides? overrides = null,
		CancellationToken cancellationToken = default)
	{
		overrides ??= new ConfigOverrides();

		string? providerName =
			overrides.Provider
			?? Environment.GetEnvironmentVariable(PROVIDER_ENV)
			?? await GitConfigStore.GetAsync(ProviderKey, cancellationToken).ConfigureAwait(false);

		Provider provider = ParseProvider(providerName);

		bool titlesOnly =
			IsTruthy(Environment.GetEnvironmentVariable(TITLES_ONLY_ENV))
			|| IsTruthy(await GitConfigStore.GetAsync(TitlesOnlyKey, cancellationToken).ConfigureAwait(false));

		return provider switch
		{
			Provider.Anthropic => new CommittyConfig
			{
				Provider = provider,
				TitlesOnly = titlesOnly,
				Anthropic = new AnthropicConfig
				{
					ApiKey = overrides.ApiKey
						?? Environment.GetEnvironmentVariable(ANTHROPIC_API_KEY_ENV)
						?? await GitConfigStore.GetAsync(AnthropicApiKeyKey, cancellationToken)
							.ConfigureAwait(false),
					Model = overrides.Model
						?? Environment.GetEnvironmentVariable(ANTHROPIC_MODEL_ENV)
						?? await GitConfigStore.GetAsync(AnthropicModelKey, cancellationToken).ConfigureAwait(false)
						?? DefaultAnthropicModel,
				},
			},
			_ => new CommittyConfig
			{
				Provider = Provider.Azure,
				TitlesOnly = titlesOnly,
				Azure = new AzureConfig
				{
					ApiKey = overrides.ApiKey
						?? Environment.GetEnvironmentVariable(AZURE_API_KEY_ENV)
						?? await GitConfigStore.GetAsync(AzureApiKeyKey, cancellationToken).ConfigureAwait(false),
					Endpoint = overrides.Endpoint
						?? Environment.GetEnvironmentVariable(AZURE_ENDPOINT_ENV)
						?? await GitConfigStore.GetAsync(AzureEndpointKey, cancellationToken).ConfigureAwait(false),
					Deployment = overrides.Deployment
						?? Environment.GetEnvironmentVariable(AZURE_DEPLOYMENT_ENV)
						?? await GitConfigStore.GetAsync(AzureDeploymentKey, cancellationToken).ConfigureAwait(false)
						?? DefaultDeployment,
				},
			},
		};
	}
}
