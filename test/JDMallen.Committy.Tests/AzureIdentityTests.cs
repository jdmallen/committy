using Azure.Identity;

using JDMallen.Toolbox.AI.Azure;

namespace JDMallen.Committy.Tests;

public class AzureIdentityTests
{
	private readonly Dictionary<string, string> _gitConfig = [];

	private Task<CommittyConfig> Resolve(ConfigOverrides? overrides = null) =>
		CommittyConfigResolver.ResolveAsync(
			overrides,
			_ => null,
			(key, _) => Task.FromResult(_gitConfig.GetValueOrDefault(key)));

	private static AzureConfig IdentityConfig(
		AzureCredentialKind credential = AzureCredentialKind.InteractiveBrowser,
		string? tenantId = null,
		string? unknownCredential = null,
		AzureAuth auth = AzureAuth.AzureIdentity) =>
		new()
		{
			Auth = auth,
			Credential = credential,
			UnknownCredential = unknownCredential,
			Endpoint = "https://example.openai.azure.com",
			Deployment = "gpt-6-luna",
			TenantId = tenantId,
			AuthenticationRecordPath =
				"/home/me/.config/committy/auth-record.json",
		};

	[Theory]
	[InlineData(null, AzureCredentialKind.InteractiveBrowser)]
	[InlineData("", AzureCredentialKind.InteractiveBrowser)]
	[InlineData(" InteractiveBrowser ", AzureCredentialKind.InteractiveBrowser)]
	[InlineData("browser", AzureCredentialKind.InteractiveBrowser)]
	[InlineData("devicecode", AzureCredentialKind.DeviceCode)]
	[InlineData("device-code", AzureCredentialKind.DeviceCode)]
	[InlineData("default", AzureCredentialKind.Default)]
	[InlineData("azurecli", AzureCredentialKind.AzureCli)]
	[InlineData("azd", AzureCredentialKind.AzureDeveloperCli)]
	[InlineData("broker", null)]
	public void ParseAzureCredential_KnownNamesCaseInsensitively_UnknownIsNull(
		string? name,
		AzureCredentialKind? expected) => Assert.Equal(
		expected,
		CommittyConfigResolver.ParseAzureCredential(name));

	[Theory]
	[InlineData(AzureCredentialKind.InteractiveBrowser)]
	[InlineData(AzureCredentialKind.DeviceCode)]
	[InlineData(AzureCredentialKind.Default)]
	[InlineData(AzureCredentialKind.AzureCli)]
	[InlineData(AzureCredentialKind.AzureDeveloperCli)]
	public void AzureCredentialName_RoundTrips(AzureCredentialKind kind) =>
		Assert.Equal(
			kind,
			CommittyConfigResolver.ParseAzureCredential(
				CommittyConfigResolver.AzureCredentialName(kind)));

	[Fact]
	public async Task Resolve_AzureIdentity_DefaultsToABrowserSignIn()
	{
		_gitConfig[CommittyConfigResolver.AZURE_AUTH_KEY] = "azureidentity";

		AzureConfig azure = (await Resolve()).Azure!;

		Assert.Equal(AzureAuth.AzureIdentity, azure.Auth);
		Assert.Equal(AzureCredentialKind.InteractiveBrowser, azure.Credential);
		Assert.Null(azure.UnknownCredential);
		Assert.EndsWith(
			Path.Combine("committy", "auth-record.json"),
			azure.AuthenticationRecordPath);
	}

	[Fact]
	public async Task Resolve_CredentialFlag_BeatsGitConfig()
	{
		_gitConfig[CommittyConfigResolver.AZURE_AUTH_KEY] = "azureidentity";
		_gitConfig[CommittyConfigResolver.AZURE_CREDENTIAL_KEY] = "devicecode";

		AzureConfig azure = (await Resolve(
				new ConfigOverrides(AzureCredential: "default")))
			.Azure!;

		Assert.Equal(AzureCredentialKind.Default, azure.Credential);
	}

	[Fact]
	public async Task Resolve_UnknownCredential_IsKeptForValidation()
	{
		_gitConfig[CommittyConfigResolver.AZURE_AUTH_KEY] = "azureidentity";
		_gitConfig[CommittyConfigResolver.AZURE_CREDENTIAL_KEY] = "broker";

		AzureConfig azure = (await Resolve()).Azure!;

		Assert.Equal("broker", azure.UnknownCredential);
		Assert.Contains(
			CommittyConfigResolver.AZURE_CREDENTIAL_KEY,
			azure.Validate());
	}

	[Fact]
	public void Validate_AzureIdentity_NeedsNoApiKey() =>
		Assert.Null(IdentityConfig().Validate());

	[Fact]
	public void Validate_UnknownCredential_IgnoredForOtherAuth() =>
		Assert.Null(
			IdentityConfig(
					unknownCredential: "broker",
					auth: AzureAuth.AzureCli)
				.Validate());

	[Fact]
	public void Validate_AzureIdentity_BadTenant_NamesTheTenantKey()
	{
		string? error = IdentityConfig(tenantId: "contoso & calc").Validate();

		Assert.NotNull(error);
		Assert.StartsWith(CommittyConfigResolver.AZURE_TENANT_KEY, error);
	}

	[Fact]
	public void CredentialOptions_MapTheConfig()
	{
		AzureCredentialOptions options =
			IdentityConfig(AzureCredentialKind.DeviceCode, " contoso.com ")
				.CredentialOptions(false);

		Assert.Equal(AzureCredentialKind.DeviceCode, options.Kind);
		Assert.Equal("contoso.com", options.TenantId);
		Assert.Equal("committy", options.CacheName);
		Assert.Equal(
			"/home/me/.config/committy/auth-record.json",
			options.AuthenticationRecordPath);
		Assert.False(options.AllowInteractiveAuthentication);
	}

	[Fact]
	public void CredentialOptions_BlankTenant_IsNull() =>
		Assert.Null(
			IdentityConfig(tenantId: "  ").CredentialOptions(true).TenantId);

	[Theory]
	[InlineData(AzureCredentialKind.InteractiveBrowser, true)]
	[InlineData(AzureCredentialKind.DeviceCode, true)]
	[InlineData(AzureCredentialKind.Default, false)]
	[InlineData(AzureCredentialKind.AzureCli, false)]
	public void UsesInteractiveSignIn_OnlyForBrowserAndDeviceCode(
		AzureCredentialKind kind,
		bool expected) =>
		Assert.Equal(expected, IdentityConfig(kind).UsesInteractiveSignIn);

	[Fact]
	public void UsesInteractiveSignIn_FalseForOtherAuth() =>
		Assert.False(
			IdentityConfig(auth: AzureAuth.AzureCli).UsesInteractiveSignIn);

	[Fact]
	public void Factory_AzureIdentity_BuildsWithoutSigningIn()
	{
		using var http = new HttpClient();

		IChatCompletionClient client = new ChatCompletionClientFactory(http)
			.Create(
				new CommittyConfig
				{
					Provider = Provider.Azure,
					Azure = IdentityConfig(),
				},
				false);

		Assert.IsType<AzureOpenAIChatClient>(client);
	}

	[Fact]
	public void IsSignInRequired_FindsAWrappedAuthenticationRequired() =>
		Assert.True(
			ChatCompletionClientFactory.IsSignInRequired(
				new InvalidOperationException(
					"Failed to generate commit message suggestions",
					new AuthenticationRequiredException(
						"Interactive authentication is needed",
						default))));

	[Fact]
	public void IsSignInRequired_OtherFailures_AreNot() =>
		Assert.False(
			ChatCompletionClientFactory.IsSignInRequired(
				new InvalidOperationException(
					"boom",
					new CredentialUnavailableException("no keyring"))));
}
