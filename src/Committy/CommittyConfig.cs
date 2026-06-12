namespace Committy;

public enum Provider
{
	Azure,
	Anthropic,
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
/// The fully resolved committy configuration: which provider to use, that
/// provider's
/// credentials, and the default output mode.
/// </summary>
public sealed class CommittyConfig
{
	public AnthropicConfig? Anthropic { get; init; }

	public AzureConfig? Azure { get; init; }

	public required Provider Provider { get; init; }

	public bool TitlesOnly { get; init; }

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
			_ => $"Unknown provider: {Provider}",
		};
}
