namespace Committy;

public enum Provider
{
	Azure,
	Anthropic,
	OpenAI,
}

/// <summary>
/// Azure OpenAI settings. <see cref="ApiKey" /> and
/// <see cref="Endpoint" /> are required.
/// </summary>
public sealed class AzureConfig
{
	public string? ApiKey { get; init; }

	public required string Deployment { get; init; }

	public string? Endpoint { get; init; }

	public string? Validate() =>
		string.IsNullOrWhiteSpace(ApiKey)
			? "Azure OpenAI API key is required (run `committy config` or set AZURE_OPENAI_API_KEY)."
			: string.IsNullOrWhiteSpace(Endpoint)
				? "Azure OpenAI endpoint is required (run `committy config` or set AZURE_OPENAI_ENDPOINT_HOST)."
				: null;
}

/// <summary>Anthropic (Claude) settings. <see cref="ApiKey" /> is required.</summary>
public sealed class AnthropicConfig
{
	public string? ApiKey { get; init; }

	public required string Model { get; init; }

	public string? Validate() =>
		string.IsNullOrWhiteSpace(ApiKey)
			? "Anthropic API key is required (run `committy config` or set ANTHROPIC_API_KEY_COMMITTY)."
			: null;
}

/// <summary>
/// Settings for any endpoint speaking the OpenAI <c>/v1/chat/completions</c> API
/// — openai.com, or a self-hosted runner such as llama.cpp, llama-swap, Ollama,
/// or vLLM. <see cref="BaseUrl" /> and <see cref="Model" /> are required and
/// deployment-specific; <see cref="ApiKey" /> is not, because self-hosted runners
/// generally accept anonymous requests.
/// </summary>
public sealed class OpenAIConfig
{
	public string? ApiKey { get; init; }

	/// <summary>
	/// The API root including the version segment, e.g.
	/// "http://10.10.0.20:8080/v1".
	/// </summary>
	public string? BaseUrl { get; init; }

	/// <summary>
	/// Generation budget. Higher than the prompt's own defaults because a
	/// reasoning model spends most of its output on a thinking block that is
	/// discarded before the commit message is parsed out.
	/// </summary>
	public required int MaxTokens { get; init; }

	public required string Model { get; init; }

	/// <summary>
	/// HTTP timeout. Higher than the hosted providers' because a self-hosted
	/// runner may have to load its weights from disk before generating a token.
	/// </summary>
	public required int TimeoutSeconds { get; init; }

	public string? Validate() =>
		string.IsNullOrWhiteSpace(BaseUrl)
			? "OpenAI base URL is required (run `committy config` or set OPENAI_BASE_URL)."
			: string.IsNullOrWhiteSpace(Model)
				? "OpenAI model is required (run `committy config` or set OPENAI_MODEL)."
				: TimeoutSeconds <= 0
					? $"OpenAI timeout must be greater than 0 (got {TimeoutSeconds})."
					: MaxTokens <= 0
						? $"OpenAI max tokens must be greater than 0 (got {MaxTokens})."
						: null;
}

/// <summary>
/// The fully resolved committy configuration: which provider to use, that
/// provider's
/// credentials, and the default output mode.
/// </summary>
public sealed class CommittyConfig
{
	/// <summary>
	/// The HTTP timeout for the hosted providers, which answer in seconds. Only
	/// the self-hosted path needs to override it.
	/// </summary>
	public const int DefaultTimeoutSeconds = 30;

	public AnthropicConfig? Anthropic { get; init; }

	public AzureConfig? Azure { get; init; }

	public OpenAIConfig? OpenAI { get; init; }

	public required Provider Provider { get; init; }

	public bool TitlesOnly { get; init; }

	/// <summary>
	/// The generation budget to use, or null to keep the prompt's own per-mode
	/// default. Only the OpenAI provider overrides it, because a thinking block
	/// is spent from the same budget as the answer.
	/// </summary>
	public int? MaxTokensOverride => Provider == Provider.OpenAI ? OpenAI?.MaxTokens : null;

	public int TimeoutSeconds =>
		Provider == Provider.OpenAI
			? OpenAI?.TimeoutSeconds ?? DefaultTimeoutSeconds
			: DefaultTimeoutSeconds;

	/// <summary>
	/// Returns a human-readable error if the selected provider is
	/// misconfigured, else null.
	/// </summary>
	public string? Validate() =>
		Provider switch
		{
			Provider.Azure => Azure is null ? "Azure configuration missing." : Azure.Validate(),
			Provider.Anthropic => Anthropic is null
				? "Anthropic configuration missing."
				: Anthropic.Validate(),
			Provider.OpenAI => OpenAI is null ? "OpenAI configuration missing." : OpenAI.Validate(),
			_ => $"Unknown provider: {Provider}",
		};
}
