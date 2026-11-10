namespace JDMallen.Committy.Tests;

public class CommittyConfigResolverTests
{
	private readonly Dictionary<string, string> _environment = [];
	private readonly Dictionary<string, string> _gitConfig = [];

	private Task<CommittyConfig> Resolve(ConfigOverrides? overrides = null) =>
		CommittyConfigResolver.ResolveAsync(
			overrides,
			name => _environment.GetValueOrDefault(name),
			(key, _) => Task.FromResult(_gitConfig.GetValueOrDefault(key)));

	[Fact]
	public async Task Settings_GitConfigBeatsEnvironment()
	{
		_gitConfig[CommittyConfigResolver.PROVIDER_KEY] = "azure";
		_environment["COMMITTY_PROVIDER"] = "anthropic";
		_gitConfig[CommittyConfigResolver.AZURE_ENDPOINT_KEY] =
			"https://config";
		_environment["AZURE_OPENAI_ENDPOINT_HOST"] = "https://env";
		_gitConfig[CommittyConfigResolver.AZURE_DEPLOYMENT_KEY] = "gpt-6-luna";
		_environment["AZURE_OPENAI_DEPLOYMENT"] = "gpt-4.1-mini";

		CommittyConfig config = await Resolve();

		Assert.Equal(Provider.Azure, config.Provider);
		Assert.Equal("https://config", config.Azure!.Endpoint);
		Assert.Equal("gpt-6-luna", config.Azure.Deployment);
	}

	[Fact]
	public async Task Settings_EnvironmentIsTheFallback()
	{
		_environment["COMMITTY_PROVIDER"] = "openai";
		_environment["OPENAI_BASE_URL"] = "http://env/v1";
		_environment["OPENAI_MODEL"] = "env-model";

		CommittyConfig config = await Resolve();

		Assert.Equal(Provider.OpenAI, config.Provider);
		Assert.Equal("http://env/v1", config.OpenAI!.BaseUrl);
		Assert.Equal("env-model", config.OpenAI.Model);
	}

	[Fact]
	public async Task Settings_FlagBeatsGitConfig()
	{
		_gitConfig[CommittyConfigResolver.PROVIDER_KEY] = "anthropic";
		_gitConfig[CommittyConfigResolver.ANTHROPIC_MODEL_KEY] = "config-model";

		CommittyConfig config =
			await Resolve(new ConfigOverrides(Model: "flag-model"));

		Assert.Equal("flag-model", config.Anthropic!.Model);
	}

	[Fact]
	public async Task Settings_BlankGitConfigFallsThrough()
	{
		_gitConfig[CommittyConfigResolver.PROVIDER_KEY] = "anthropic";
		_gitConfig[CommittyConfigResolver.ANTHROPIC_MODEL_KEY] = "  ";
		_environment["ANTHROPIC_MODEL"] = "env-model";

		CommittyConfig config = await Resolve();

		Assert.Equal("env-model", config.Anthropic!.Model);
	}

	[Theory]
	[InlineData("azure", "AZURE_OPENAI_API_KEY", "committy.azure.apikey")]
	[InlineData(
		"anthropic",
		"ANTHROPIC_API_KEY_COMMITTY",
		"committy.anthropic.apikey")]
	[InlineData("openai", "OPENAI_API_KEY_COMMITTY", "committy.openai.apikey")]
	public async Task ApiKeys_EnvironmentBeatsGitConfig(
		string provider,
		string environmentVariable,
		string gitKey)
	{
		_gitConfig[CommittyConfigResolver.PROVIDER_KEY] = provider;
		_gitConfig[gitKey] = "config-key";
		_environment[environmentVariable] = "env-key";

		CommittyConfig config = await Resolve();

		string? apiKey = config.Azure?.ApiKey
			?? config.Anthropic?.ApiKey ?? config.OpenAI?.ApiKey;
		Assert.Equal("env-key", apiKey);
	}

	[Fact]
	public async Task ApiKeys_GitConfigIsTheFallback()
	{
		_gitConfig[CommittyConfigResolver.AZURE_API_KEY_KEY] = "config-key";

		CommittyConfig config = await Resolve();

		Assert.Equal("config-key", config.Azure!.ApiKey);
	}

	[Fact]
	public async Task ApiKeys_FlagBeatsEnvironment()
	{
		_environment["AZURE_OPENAI_API_KEY"] = "env-key";

		CommittyConfig config =
			await Resolve(new ConfigOverrides(ApiKey: "flag-key"));

		Assert.Equal("flag-key", config.Azure!.ApiKey);
	}

	[Theory]
	[InlineData("false", "true", false)]
	[InlineData("true", null, true)]
	[InlineData(null, "true", true)]
	[InlineData(null, null, false)]
	public async Task TitlesOnly_GitConfigBeatsEnvironment(
		string? gitValue,
		string? environmentValue,
		bool expected)
	{
		if (gitValue is not null)
		{
			_gitConfig[CommittyConfigResolver.TITLES_ONLY_KEY] = gitValue;
		}

		if (environmentValue is not null)
		{
			_environment["COMMITTY_TITLES_ONLY"] = environmentValue;
		}

		Assert.Equal(expected, (await Resolve()).TitlesOnly);
	}

	[Fact]
	public async Task Azure_ReasoningSettingsComeFromGitConfig()
	{
		_gitConfig[CommittyConfigResolver.AZURE_REASONING_MODEL_KEY] = "y";
		_gitConfig[CommittyConfigResolver.AZURE_MAX_TOKENS_KEY] = "4096";

		CommittyConfig config = await Resolve();

		Assert.True(config.Azure!.ReasoningModel);
		Assert.Equal(4096, config.MaxTokensOverride);
	}

	[Fact]
	public async Task AzureAuth_DefaultsToApiKey()
	{
		CommittyConfig config = await Resolve();

		Assert.Equal(AzureAuth.ApiKey, config.Azure!.Auth);
		Assert.Null(config.Azure.UnknownAuth);
		Assert.Null(config.Azure.TenantId);
	}

	[Fact]
	public async Task AzureAuth_GitConfig_ReadsAllAzureCliSettings()
	{
		_gitConfig[CommittyConfigResolver.AZURE_AUTH_KEY] = "AzureCli";
		_gitConfig[CommittyConfigResolver.AZURE_TENANT_KEY] = "contoso.com";
		_gitConfig[CommittyConfigResolver.AZURE_SCOPE_KEY] =
			AzureOpenAIScopes.COGNITIVE_SERVICES;
		_gitConfig[CommittyConfigResolver.AZURE_AZ_PATH_KEY] = "/opt/az/bin/az";

		AzureConfig azure = (await Resolve()).Azure!;

		Assert.Equal(AzureAuth.AzureCli, azure.Auth);
		Assert.Equal("contoso.com", azure.TenantId);
		Assert.Equal(AzureOpenAIScopes.COGNITIVE_SERVICES, azure.Scope);
		Assert.Equal("/opt/az/bin/az", azure.AzPath);
	}

	[Fact]
	public async Task AzureAuth_FlagsBeatGitConfig()
	{
		_gitConfig[CommittyConfigResolver.AZURE_AUTH_KEY] = "apikey";
		_gitConfig[CommittyConfigResolver.AZURE_TENANT_KEY] = "config.com";

		AzureConfig azure = (await Resolve(
				new ConfigOverrides(AzureAuth: "azurecli", TenantId: "flag.com")))
			.Azure!;

		Assert.Equal(AzureAuth.AzureCli, azure.Auth);
		Assert.Equal("flag.com", azure.TenantId);
	}

	[Fact]
	public async Task AzureAuth_Unknown_IsKeptForValidation()
	{
		_gitConfig[CommittyConfigResolver.AZURE_AUTH_KEY] = "azureidentity";

		AzureConfig azure = (await Resolve()).Azure!;

		Assert.Equal("azureidentity", azure.UnknownAuth);
		Assert.Contains("azureidentity", azure.Validate());
	}

	[Fact]
	public async Task AzureAuth_IsNotReadFromTheEnvironment()
	{
		// Only flags and git config select keyless auth; a stray export must
		// not switch a working key setup over.
		_environment["COMMITTY_AZURE_AUTH"] = "azurecli";

		Assert.Equal(AzureAuth.ApiKey, (await Resolve()).Azure!.Auth);
	}

	[Fact]
	public async Task AzureAuth_Sources_AreRecorded()
	{
		var gitConfig = new Dictionary<string, GitConfigValue>
		{
			[CommittyConfigResolver.AZURE_AUTH_KEY] =
				new("azurecli", "global"),
		};

		ResolvedConfig resolved =
			await CommittyConfigResolver.ResolveWithSourcesAsync(
				new ConfigOverrides(TenantId: "flag.com"),
				_ => null,
				(key, _) => Task.FromResult(gitConfig.GetValueOrDefault(key)));

		Assert.Equal(
			"git config --global",
			resolved.Sources[CommittyConfigResolver.AZURE_AUTH_KEY].ToString());
		Assert.Equal(
			ConfigSource.FromFlag,
			resolved.Sources[CommittyConfigResolver.AZURE_TENANT_KEY]);
		Assert.Equal(
			ConfigSource.FromDefault,
			resolved.Sources[CommittyConfigResolver.AZURE_SCOPE_KEY]);
	}
}
