using JDMallen.Toolbox.AI.Azure;

namespace JDMallen.Committy;

public enum Provider
{
	Azure,
	Anthropic,
	OpenAI,
}

/// <summary>How committy authenticates to Azure OpenAI.</summary>
public enum AzureAuth
{
	/// <summary>An <c>api-key</c> header (the default).</summary>
	ApiKey,

	/// <summary>
	/// A Microsoft Entra ID bearer token from the signed-in Azure CLI
	/// (<c>az account get-access-token</c>).
	/// </summary>
	AzureCli,

	/// <summary>
	/// A Microsoft Entra ID bearer token from Azure.Identity: a browser or
	/// device code sign-in remembered in an encrypted cache, or another
	/// credential named by <see cref="AzureConfig.Credential" />.
	/// </summary>
	AzureIdentity,
}

/// <summary>
/// Azure OpenAI settings. <see cref="Endpoint" /> is required, and so is
/// <see cref="ApiKey" /> when <see cref="Auth" /> is
/// <see cref="AzureAuth.ApiKey" />.
/// </summary>
public sealed class AzureConfig
{
	public string? ApiKey { get; init; }

	public AzureAuth Auth { get; init; } = AzureAuth.ApiKey;

	/// <summary>
	/// The configured <c>committy.azure.auth</c> value when it names no known
	/// method, so <see cref="Validate" /> can report it; otherwise null.
	/// </summary>
	public string? UnknownAuth { get; init; }

	/// <summary>
	/// The Azure.Identity credential for <see cref="AzureAuth.AzureIdentity" />.
	/// </summary>
	public AzureCredentialKind Credential { get; init; } =
		AzureCredentialKind.InteractiveBrowser;

	/// <summary>
	/// The configured <c>committy.azure.credential</c> value when it names no
	/// known credential, so <see cref="Validate" /> can report it; otherwise
	/// null.
	/// </summary>
	public string? UnknownCredential { get; init; }

	/// <summary>
	/// Where the browser or device code sign-in is remembered (account and
	/// tenant ids; the tokens stay in the system keyring).
	/// </summary>
	public string AuthenticationRecordPath { get; init; } =
		DefaultAuthenticationRecordPath();

	/// <summary>
	/// <c>auth-record.json</c> in committy's per-user config directory
	/// (<c>~/.config/committy</c> on Linux and macOS, <c>%APPDATA%\committy</c>
	/// on Windows).
	/// </summary>
	public static string DefaultAuthenticationRecordPath() =>
		Path.Combine(
			Environment.GetFolderPath(
				Environment.SpecialFolder.ApplicationData),
			"committy",
			"auth-record.json");

	/// <summary>
	/// The Entra tenant (GUID or domain) to get tokens from, or null for the
	/// tenant of the current <c>az</c> account (azurecli) or the credential's
	/// own default (azureidentity).
	/// </summary>
	public string? TenantId { get; init; }

	/// <summary>
	/// The token scope, or null for <see cref="AzureOpenAIScopes.DEFAULT" />.
	/// </summary>
	public string? Scope { get; init; }

	/// <summary>
	/// The full path to <c>az</c>, for when the PATH committy runs with lacks it
	/// (e.g. a git hook launched from a GUI client), or null to use PATH.
	/// </summary>
	public string? AzPath { get; init; }

	public required string Deployment { get; init; }

	public string? Endpoint { get; init; }

	/// <summary>
	/// Generation budget, or null to keep the prompt's own per-mode default
	/// (or <see cref="CommittyConfigResolver.DEFAULT_AZURE_REASONING_MAX_TOKENS" />
	/// for a reasoning deployment).
	/// </summary>
	public int? MaxTokens { get; init; }

	/// <summary>
	/// Set for a reasoning deployment (o-series, gpt-5 and later reasoning
	/// models): <c>temperature</c> is then not sent, because those models reject
	/// sampling parameters, and the budget defaults higher because the hidden
	/// reasoning is spent from it.
	/// </summary>
	public bool ReasoningModel { get; init; }

	/// <summary>The budget to send, or null for the prompt's default.</summary>
	public int? EffectiveMaxTokens =>
		MaxTokens
		?? (ReasoningModel
			? CommittyConfigResolver.DEFAULT_AZURE_REASONING_MAX_TOKENS
			: null);

	/// <summary>The Azure CLI token provider settings for this config.</summary>
	public AzureCliTokenProviderOptions TokenProviderOptions() =>
		new()
		{
			Scope = EffectiveScope,
			TenantId = string.IsNullOrWhiteSpace(TenantId)
				? null
				: TenantId.Trim(),
			AzExecutablePath = string.IsNullOrWhiteSpace(AzPath)
				? null
				: AzPath.Trim(),
		};

	/// <summary>The token scope to request.</summary>
	public string EffectiveScope =>
		string.IsNullOrWhiteSpace(Scope)
			? AzureOpenAIScopes.DEFAULT
			: Scope.Trim();

	/// <summary>
	/// The Azure.Identity settings for this config. Pass
	/// <paramref name="allowInteractiveAuthentication" /> false where a sign-in
	/// prompt would be wrong, such as inside a git hook.
	/// </summary>
	public AzureCredentialOptions CredentialOptions(
		bool allowInteractiveAuthentication) =>
		new()
		{
			Kind = Credential,
			TenantId = string.IsNullOrWhiteSpace(TenantId)
				? null
				: TenantId.Trim(),
			CacheName = "committy",
			AuthenticationRecordPath = AuthenticationRecordPath,
			AllowInteractiveAuthentication = allowInteractiveAuthentication,
		};

	/// <summary>
	/// Whether this config signs in through a browser or device code, and so
	/// needs <c>committy config</c> to sign in before a hook can run.
	/// </summary>
	public bool UsesInteractiveSignIn =>
		Auth == AzureAuth.AzureIdentity
		&& AzureCredentialFactory.IsInteractive(Credential);

	public string? Validate()
	{
		if (UnknownAuth is not null)
		{
			return $"Unknown Azure auth '{UnknownAuth}' in {CommittyConfigResolver.AZURE_AUTH_KEY} (expected apikey, azurecli or azureidentity).";
		}

		if (Auth == AzureAuth.AzureIdentity && UnknownCredential is not null)
		{
			return $"Unknown Azure credential '{UnknownCredential}' in {CommittyConfigResolver.AZURE_CREDENTIAL_KEY} (expected {CommittyConfigResolver.AZURE_CREDENTIAL_NAMES}).";
		}

		if (string.IsNullOrWhiteSpace(Endpoint))
		{
			return "Azure OpenAI endpoint is required (run `committy config` or set AZURE_OPENAI_ENDPOINT_HOST).";
		}

		return Auth switch
		{
			AzureAuth.AzureCli or AzureAuth.AzureIdentity =>
				ValidateTokenProviderOptions(),
			_ => string.IsNullOrWhiteSpace(ApiKey)
				? "Azure OpenAI API key is required (run `committy config` or set AZURE_OPENAI_API_KEY)."
				: null,
		};
	}

	/// <summary>
	/// Builds the Azure CLI token provider the same way a request would, so a
	/// bad tenant or scope is reported up front, naming the setting to fix.
	/// Azure.Identity takes the same tenant and scope forms, so its config is
	/// checked the same way.
	/// </summary>
	private string? ValidateTokenProviderOptions()
	{
		AzureCliTokenProviderOptions options = TokenProviderOptions();

		if (!IsValid(options))
		{
			// The toolbox validates scope before tenant; retrying without the
			// tenant tells the two apart.
			return IsValid(options with { TenantId = null })
				? $"{CommittyConfigResolver.AZURE_TENANT_KEY} must be a tenant GUID or domain name (got '{options.TenantId}')."
				: $"{CommittyConfigResolver.AZURE_SCOPE_KEY} must be an https URL such as {AzureOpenAIScopes.DEFAULT} (got '{options.Scope}').";
		}

		return null;

		static bool IsValid(AzureCliTokenProviderOptions candidate)
		{
			try
			{
				_ = new AzureCliTokenProvider(candidate);

				return true;
			}
			catch (ArgumentException)
			{
				return false;
			}
		}
	}
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
	public const int DEFAULT_TIMEOUT_SECONDS = 30;

	public AnthropicConfig? Anthropic { get; init; }

	public AzureConfig? Azure { get; init; }

	public OpenAIConfig? OpenAI { get; init; }

	public required Provider Provider { get; init; }

	public bool TitlesOnly { get; init; }

	/// <summary>
	/// The generation budget to use, or null to keep the prompt's own per-mode
	/// default. The OpenAI provider and Azure reasoning deployments override it,
	/// because a thinking block is spent from the same budget as the answer.
	/// </summary>
	public int? MaxTokensOverride =>
		Provider switch
		{
			Provider.OpenAI => OpenAI?.MaxTokens,
			Provider.Azure => Azure?.EffectiveMaxTokens,
			_ => null,
		};

	public int TimeoutSeconds =>
		Provider == Provider.OpenAI
			? OpenAI?.TimeoutSeconds ?? DEFAULT_TIMEOUT_SECONDS
			: DEFAULT_TIMEOUT_SECONDS;

	/// <summary>
	/// Returns a human-readable error if the selected provider is
	/// misconfigured, else null.
	/// </summary>
	public string? Validate() =>
		Provider switch
		{
			Provider.Azure => Azure is null
				? "Azure configuration missing."
				: Azure.Validate(),
			Provider.Anthropic => Anthropic is null
				? "Anthropic configuration missing."
				: Anthropic.Validate(),
			Provider.OpenAI => OpenAI is null
				? "OpenAI configuration missing."
				: OpenAI.Validate(),
			_ => $"Unknown provider: {Provider}",
		};
}
