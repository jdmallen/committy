namespace JDMallen.Committy;

/// <summary>Optional per-invocation overrides, typically sourced from CLI flags.</summary>
public sealed record ConfigOverrides(
	string? Provider = null,
	string? ApiKey = null,
	string? Endpoint = null,
	string? Deployment = null,
	string? Model = null);

/// <summary>
/// Resolves the effective <see cref="CommittyConfig" />. Settings use CLI flag,
/// then git config, then environment variable, then built-in default; API keys
/// use CLI flag, then environment variable, then git config, so a key can stay
/// out of ~/.gitconfig. Git config keys live under the <c>committy.*</c>
/// section and are written by <c>committy config</c>; the environment
/// variables (including the legacy <c>AZURE_OPENAI_*</c> ones) remain as
/// fallbacks.
/// </summary>
public static class CommittyConfigResolver
{
	public const string DEFAULT_DEPLOYMENT = "gpt-4.1-mini";
	public const string DEFAULT_ANTHROPIC_MODEL = "claude-haiku-4-5-20251001";

	/// <summary>
	/// A cold self-hosted model can spend minutes loading weights before it
	/// generates a token, so the self-hosted path waits far longer than the
	/// hosted providers' <see cref="CommittyConfig.DEFAULT_TIMEOUT_SECONDS" />.
	/// </summary>
	public const int DEFAULT_OPEN_AI_TIMEOUT_SECONDS = 300;

	/// <summary>
	/// Well above the prompt's own 100/500 budgets: a reasoning model spends most
	/// of its output on a thinking block that is discarded before parsing, so a
	/// small budget truncates the thought and leaves no message at all.
	/// </summary>
	public const int DEFAULT_OPEN_AI_MAX_TOKENS = 2048;

	/// <summary>
	/// The budget for an Azure reasoning deployment when
	/// <see cref="AZURE_MAX_TOKENS_KEY" /> is unset. Its hidden reasoning is spent
	/// from the same budget, so the prompt's 100/500 would leave no answer.
	/// </summary>
	public const int DEFAULT_AZURE_REASONING_MAX_TOKENS = 2048;

	// git config keys
	public const string PROVIDER_KEY = "committy.provider";
	public const string TITLES_ONLY_KEY = "committy.titlesonly";
	public const string AZURE_API_KEY_KEY = "committy.azure.apikey";
	public const string AZURE_ENDPOINT_KEY = "committy.azure.endpoint";
	public const string AZURE_DEPLOYMENT_KEY = "committy.azure.deployment";

	public const string AZURE_REASONING_MODEL_KEY =
		"committy.azure.reasoningmodel";

	public const string AZURE_MAX_TOKENS_KEY = "committy.azure.maxtokens";
	public const string ANTHROPIC_API_KEY_KEY = "committy.anthropic.apikey";
	public const string ANTHROPIC_MODEL_KEY = "committy.anthropic.model";
	public const string OPEN_AI_API_KEY_KEY = "committy.openai.apikey";
	public const string OPEN_AI_BASE_URL_KEY = "committy.openai.baseurl";
	public const string OPEN_AI_MODEL_KEY = "committy.openai.model";
	public const string OPEN_AI_TIMEOUT_KEY = "committy.openai.timeoutseconds";
	public const string OPEN_AI_MAX_TOKENS_KEY = "committy.openai.maxtokens";

	// environment variables
	private const string PROVIDER_ENV = "COMMITTY_PROVIDER";
	private const string TITLES_ONLY_ENV = "COMMITTY_TITLES_ONLY";
	public const string AZURE_API_KEY_ENV = "AZURE_OPENAI_API_KEY";
	private const string AZURE_ENDPOINT_ENV = "AZURE_OPENAI_ENDPOINT_HOST";
	private const string AZURE_DEPLOYMENT_ENV = "AZURE_OPENAI_DEPLOYMENT";
	public const string ANTHROPIC_API_KEY_ENV = "ANTHROPIC_API_KEY_COMMITTY";
	private const string ANTHROPIC_MODEL_ENV = "ANTHROPIC_MODEL";
	public const string OPENAI_API_KEY_ENV = "OPENAI_API_KEY_COMMITTY";
	private const string OPENAI_BASE_URL_ENV = "OPENAI_BASE_URL";
	private const string OPENAI_MODEL_ENV = "OPENAI_MODEL";

	private static bool IsTruthy(string? value) =>
		value?.Trim().ToLowerInvariant() switch
		{
			"1" or "true" or "yes" or "on" => true,
			_ => false,
		};

	public static Provider ParseProvider(string? name) =>
		name?.Trim().ToLowerInvariant() switch
		{
			"anthropic" or "claude" => Provider.Anthropic,
			"openai" or "local" => Provider.OpenAI,
			_ => Provider.Azure,
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
			Provider.OpenAI => "openai",
			_ => "azure",
		};

	/// <summary>
	/// Reads an integer setting, falling back to <paramref name="fallback" /> when
	/// unset or unparseable — a typo in git config should not be fatal.
	/// </summary>
	private static int ParsePositiveInt(string? value, int fallback) =>
		int.TryParse(value?.Trim(), out int parsed) && parsed > 0
			? parsed
			: fallback;

	/// <summary>As <see cref="ParsePositiveInt" />, but unset stays null.</summary>
	public static int? ParseOptionalPositiveInt(string? value) =>
		int.TryParse(value?.Trim(), out int parsed) && parsed > 0
			? parsed
			: null;

	/// <summary>
	/// Reads a yes/no answer as git config's canonical "true"/"false", or null
	/// when it is neither, so <c>committy config</c> can ask again.
	/// </summary>
	public static string? NormalizeBool(string? value) =>
		value?.Trim().ToLowerInvariant() switch
		{
			"y" or "yes" or "true" or "1" or "on" => "true",
			"n" or "no" or "false" or "0" or "off" => "false",
			_ => null,
		};

	public static Task<CommittyConfig> ResolveAsync(
		ConfigOverrides? overrides = null,
		CancellationToken cancellationToken = default) =>
		ResolveAsync(
			overrides,
			Environment.GetEnvironmentVariable,
			GitConfigStore.GetAsync,
			cancellationToken);

	/// <summary>
	/// Resolves against injected lookups, so tests can supply environment
	/// variables and git config without touching the real ones.
	/// </summary>
	public static async Task<CommittyConfig> ResolveAsync(
		ConfigOverrides? overrides,
		Func<string, string?> getEnvironmentVariable,
		Func<string, CancellationToken, Task<string?>> getGitConfig,
		CancellationToken cancellationToken = default)
	{
		overrides ??= new ConfigOverrides();

		Provider provider = ParseProvider(
			await Setting(overrides.Provider, PROVIDER_KEY, PROVIDER_ENV)
				.ConfigureAwait(false));

		bool titlesOnly = IsTruthy(
			await Setting(null, TITLES_ONLY_KEY, TITLES_ONLY_ENV)
				.ConfigureAwait(false));

		return provider switch
		{
			Provider.Anthropic => new CommittyConfig
			{
				Provider = provider,
				TitlesOnly = titlesOnly,
				Anthropic = new AnthropicConfig
				{
					ApiKey = await Key(
							overrides.ApiKey,
							ANTHROPIC_API_KEY_ENV,
							ANTHROPIC_API_KEY_KEY)
						.ConfigureAwait(false),
					Model = await Setting(
								overrides.Model,
								ANTHROPIC_MODEL_KEY,
								ANTHROPIC_MODEL_ENV)
							.ConfigureAwait(false)
						?? DEFAULT_ANTHROPIC_MODEL,
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
					ApiKey = await Key(
							overrides.ApiKey,
							OPENAI_API_KEY_ENV,
							OPEN_AI_API_KEY_KEY)
						.ConfigureAwait(false),

					// --endpoint doubles as the base URL; the two mean the same
					// thing to the user and it keeps the flag surface small.
					BaseUrl = await Setting(
							overrides.Endpoint,
							OPEN_AI_BASE_URL_KEY,
							OPENAI_BASE_URL_ENV)
						.ConfigureAwait(false),
					Model = await Setting(
								overrides.Model,
								OPEN_AI_MODEL_KEY,
								OPENAI_MODEL_ENV)
							.ConfigureAwait(false)
						?? string.Empty,
					TimeoutSeconds = ParsePositiveInt(
						await GitOnly(OPEN_AI_TIMEOUT_KEY)
							.ConfigureAwait(false),
						DEFAULT_OPEN_AI_TIMEOUT_SECONDS),
					MaxTokens = ParsePositiveInt(
						await GitOnly(OPEN_AI_MAX_TOKENS_KEY)
							.ConfigureAwait(false),
						DEFAULT_OPEN_AI_MAX_TOKENS),
				},
			},
			_ => new CommittyConfig
			{
				Provider = Provider.Azure,
				TitlesOnly = titlesOnly,
				Azure = new AzureConfig
				{
					ApiKey = await Key(
							overrides.ApiKey,
							AZURE_API_KEY_ENV,
							AZURE_API_KEY_KEY)
						.ConfigureAwait(false),
					Endpoint = await Setting(
							overrides.Endpoint,
							AZURE_ENDPOINT_KEY,
							AZURE_ENDPOINT_ENV)
						.ConfigureAwait(false),
					Deployment = await Setting(
								overrides.Deployment,
								AZURE_DEPLOYMENT_KEY,
								AZURE_DEPLOYMENT_ENV)
							.ConfigureAwait(false)
						?? DEFAULT_DEPLOYMENT,
					ReasoningModel = NormalizeBool(
							await GitOnly(AZURE_REASONING_MODEL_KEY)
								.ConfigureAwait(false))
						== "true",
					MaxTokens = ParseOptionalPositiveInt(
						await GitOnly(AZURE_MAX_TOKENS_KEY)
							.ConfigureAwait(false)),
				},
			},
		};

		async Task<string?> GitOnly(string gitKey) =>
			NullIfBlank(
				await getGitConfig(gitKey, cancellationToken)
					.ConfigureAwait(false));

		// API keys: flag, then the environment variable, then git config, so a
		// secret can live in the shell (or a CI secret) instead of ~/.gitconfig.
		async Task<string?> Key(string? flag, string envVar, string gitKey) =>
			NullIfBlank(flag)
			?? NullIfBlank(getEnvironmentVariable(envVar))
			?? NullIfBlank(
				await getGitConfig(gitKey, cancellationToken)
					.ConfigureAwait(false));

		// Settings: flag, then git config, then the environment variable as a
		// fallback, so a stray shell export never silently beats `committy config`.
		async Task<string?> Setting(string? flag, string gitKey, string envVar)
			=>
				NullIfBlank(flag)
				?? NullIfBlank(
					await getGitConfig(gitKey, cancellationToken)
						.ConfigureAwait(false))
				?? NullIfBlank(getEnvironmentVariable(envVar));
	}

	private static string? NullIfBlank(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
