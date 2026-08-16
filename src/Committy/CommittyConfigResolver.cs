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
public sealed class CommittyConfigResolver
{
	public const string DefaultDeployment = "gpt-4.1-mini";
	public const string DefaultAnthropicModel = "claude-haiku-4-5-20251001";

	/// <summary>
	/// A cold self-hosted model can spend minutes loading weights before it
	/// generates a token, so the self-hosted path waits far longer than the
	/// hosted providers' <see cref="CommittyConfig.DefaultTimeoutSeconds" />.
	/// </summary>
	public const int DefaultOpenAITimeoutSeconds = 300;

	/// <summary>
	/// Well above the prompt's own 100/500 budgets: a reasoning model spends most
	/// of its output on a thinking block that is discarded before parsing, so a
	/// small budget truncates the thought and leaves no message at all.
	/// </summary>
	public const int DefaultOpenAIMaxTokens = 2048;

	// git config keys
	public const string ProviderKey = "committy.provider";
	public const string TitlesOnlyKey = "committy.titlesonly";
	public const string AzureApiKeyKey = "committy.azure.apikey";
	public const string AzureEndpointKey = "committy.azure.endpoint";
	public const string AzureDeploymentKey = "committy.azure.deployment";
	public const string AnthropicApiKeyKey = "committy.anthropic.apikey";
	public const string AnthropicModelKey = "committy.anthropic.model";
	public const string OpenAIApiKeyKey = "committy.openai.apikey";
	public const string OpenAIBaseUrlKey = "committy.openai.baseurl";
	public const string OpenAIModelKey = "committy.openai.model";
	public const string OpenAITimeoutKey = "committy.openai.timeoutseconds";
	public const string OpenAIMaxTokensKey = "committy.openai.maxtokens";

	// environment variables
	private const string PROVIDER_ENV = "COMMITTY_PROVIDER";
	private const string TITLES_ONLY_ENV = "COMMITTY_TITLES_ONLY";
	private const string AZURE_API_KEY_ENV = "AZURE_OPENAI_API_KEY";
	private const string AZURE_ENDPOINT_ENV = "AZURE_OPENAI_ENDPOINT_HOST";
	private const string AZURE_DEPLOYMENT_ENV = "AZURE_OPENAI_DEPLOYMENT";
	private const string ANTHROPIC_API_KEY_ENV = "ANTHROPIC_API_KEY_COMMITTY";
	private const string ANTHROPIC_MODEL_ENV = "ANTHROPIC_MODEL";
	private const string OPENAI_API_KEY_ENV = "OPENAI_API_KEY_COMMITTY";
	private const string OPENAI_BASE_URL_ENV = "OPENAI_BASE_URL";
	private const string OPENAI_MODEL_ENV = "OPENAI_MODEL";

	private static bool IsTruthy(string? value) =>
		value?.Trim().ToLowerInvariant() switch
		{
			"1" or "true" or "yes" or "on" => true,
			_                              => false,
		};

	public static Provider ParseProvider(string? name) =>
		name?.Trim().ToLowerInvariant() switch
		{
			"anthropic" or "claude"          => Provider.Anthropic,
			"openai" or "local"              => Provider.OpenAI,
			_                                => Provider.Azure,
		};

	/// <summary>
	/// The canonical git config spelling for a provider, so
	/// <c>committy config</c> writes a value <see cref="ParseProvider" /> reads
	/// back identically.
	/// </summary>
	public static string ProviderName(Provider provider) =>
		provider switch
		{
			Provider.Anthropic => "anthropic",
			Provider.OpenAI    => "openai",
			_                  => "azure",
		};

	/// <summary>
	/// Reads an integer setting, falling back to <paramref name="fallback" /> when
	/// unset or unparseable — a typo in git config should not be fatal.
	/// </summary>
	private static int ParsePositiveInt(string? value, int fallback) =>
		int.TryParse(value?.Trim(), out int parsed) && parsed > 0 ? parsed : fallback;

	public static async Task<CommittyConfig> ResolveAsync(
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
			|| IsTruthy(
				await GitConfigStore.GetAsync(TitlesOnlyKey, cancellationToken).ConfigureAwait(false));

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
						?? await GitConfigStore.GetAsync(AnthropicModelKey, cancellationToken)
							.ConfigureAwait(false)
						?? DefaultAnthropicModel,
				},
			},
			Provider.OpenAI => new CommittyConfig
			{
				Provider = provider,
				TitlesOnly = titlesOnly,
				OpenAI = new OpenAIConfig
				{
					// Optional: self-hosted runners generally accept anonymous
					// requests, so a missing key is not a misconfiguration.
					ApiKey = overrides.ApiKey
						?? Environment.GetEnvironmentVariable(OPENAI_API_KEY_ENV)
						?? await GitConfigStore.GetAsync(OpenAIApiKeyKey, cancellationToken)
							.ConfigureAwait(false),
					// --endpoint doubles as the base URL; the two mean the same
					// thing to the user and it keeps the flag surface small.
					BaseUrl = overrides.Endpoint
						?? Environment.GetEnvironmentVariable(OPENAI_BASE_URL_ENV)
						?? await GitConfigStore.GetAsync(OpenAIBaseUrlKey, cancellationToken)
							.ConfigureAwait(false),
					Model = overrides.Model
						?? Environment.GetEnvironmentVariable(OPENAI_MODEL_ENV)
						?? await GitConfigStore.GetAsync(OpenAIModelKey, cancellationToken)
							.ConfigureAwait(false)
						?? string.Empty,
					TimeoutSeconds = ParsePositiveInt(
						await GitConfigStore.GetAsync(OpenAITimeoutKey, cancellationToken)
							.ConfigureAwait(false),
						DefaultOpenAITimeoutSeconds),
					MaxTokens = ParsePositiveInt(
						await GitConfigStore.GetAsync(OpenAIMaxTokensKey, cancellationToken)
							.ConfigureAwait(false),
						DefaultOpenAIMaxTokens),
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
						?? await GitConfigStore.GetAsync(AzureApiKeyKey, cancellationToken)
							.ConfigureAwait(false),
					Endpoint = overrides.Endpoint
						?? Environment.GetEnvironmentVariable(AZURE_ENDPOINT_ENV)
						?? await GitConfigStore.GetAsync(AzureEndpointKey, cancellationToken)
							.ConfigureAwait(false),
					Deployment = overrides.Deployment
						?? Environment.GetEnvironmentVariable(AZURE_DEPLOYMENT_ENV)
						?? await GitConfigStore.GetAsync(AzureDeploymentKey, cancellationToken)
							.ConfigureAwait(false)
						?? DefaultDeployment,
				},
			},
		};
	}
}
