namespace Committy.Tests;

public class OpenAIProviderTests
{
	private const string BASE_URL = "http://10.10.0.20:8080/v1";
	private const string MODEL = "thinkingcap-27b-q4km";

	/// <summary>
	/// A valid self-hosted configuration, with each field overridable so a test
	/// can vary exactly one of them.
	/// </summary>
	private static OpenAIConfig Config(
		string? baseUrl = BASE_URL,
		string model = MODEL,
		int timeoutSeconds = CommittyConfigResolver.DefaultOpenAITimeoutSeconds,
		int maxTokens = CommittyConfigResolver.DefaultOpenAIMaxTokens,
		string? apiKey = null) =>
		new()
		{
			BaseUrl = baseUrl,
			Model = model,
			TimeoutSeconds = timeoutSeconds,
			MaxTokens = maxTokens,
			ApiKey = apiKey,
		};

	private static CommittyConfig Wrap(OpenAIConfig openAI) =>
		new()
		{
			Provider = Provider.OpenAI,
			OpenAI = openAI,
		};

	[Fact]
	public void Validate_Complete_ReturnsNull()
	{
		Assert.Null(Wrap(Config()).Validate());
	}

	[Fact]
	public void Validate_MissingApiKey_IsAllowed()
	{
		// Self-hosted runners generally do not authenticate, so a blank key is a
		// valid configuration rather than an error.
		OpenAIConfig config = Config();

		Assert.Null(config.ApiKey);
		Assert.Null(config.Validate());
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Validate_MissingBaseUrl_ReturnsError(string? baseUrl)
	{
		Assert.Contains("base URL is required", Config(baseUrl: baseUrl).Validate());
	}

	[Fact]
	public void Validate_MissingModel_ReturnsError()
	{
		Assert.Contains("model is required", Config(model: "").Validate());
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	public void Validate_NonPositiveTimeout_ReturnsError(int timeoutSeconds)
	{
		Assert.Contains(
			"timeout must be greater than 0",
			Config(timeoutSeconds: timeoutSeconds).Validate());
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	public void Validate_NonPositiveMaxTokens_ReturnsError(int maxTokens)
	{
		Assert.Contains(
			"max tokens must be greater than 0",
			Config(maxTokens: maxTokens).Validate());
	}

	[Fact]
	public void Validate_MissingOpenAISection_ReturnsError()
	{
		var config = new CommittyConfig { Provider = Provider.OpenAI };

		Assert.Equal("OpenAI configuration missing.", config.Validate());
	}

	[Fact]
	public void TimeoutSeconds_OpenAI_ComesFromConfig()
	{
		Assert.Equal(120, Wrap(Config(timeoutSeconds: 120)).TimeoutSeconds);
	}

	[Theory]
	[InlineData(Provider.Azure)]
	[InlineData(Provider.Anthropic)]
	public void TimeoutSeconds_HostedProviders_KeepTheShortDefault(Provider provider)
	{
		var config = new CommittyConfig { Provider = provider };

		Assert.Equal(CommittyConfig.DefaultTimeoutSeconds, config.TimeoutSeconds);
	}

	[Fact]
	public void MaxTokensOverride_OpenAI_ComesFromConfig()
	{
		Assert.Equal(4096, Wrap(Config(maxTokens: 4096)).MaxTokensOverride);
	}

	[Theory]
	[InlineData(Provider.Azure)]
	[InlineData(Provider.Anthropic)]
	public void MaxTokensOverride_HostedProviders_IsNull(Provider provider)
	{
		var config = new CommittyConfig { Provider = provider };

		Assert.Null(config.MaxTokensOverride);
	}

	[Theory]
	[InlineData(Provider.Azure, "azure")]
	[InlineData(Provider.Anthropic, "anthropic")]
	[InlineData(Provider.OpenAI, "openai")]
	public void ProviderName_RoundTripsThroughParseProvider(Provider provider, string expected)
	{
		string name = CommittyConfigResolver.ProviderName(provider);

		Assert.Equal(expected, name);
		Assert.Equal(provider, CommittyConfigResolver.ParseProvider(name));
	}

	[Fact]
	public void Factory_OpenAI_BuildsOpenAICompatibleClient()
	{
		using var http = new HttpClient();

		IChatCompletionClient client =
			new ChatCompletionClientFactory(http).Create(Wrap(Config()));

		Assert.IsType<OpenAICompatibleChatClient>(client);
	}
}

public class CommitMessagePromptTests
{
	[Theory]
	[InlineData(true, 100)]
	[InlineData(false, 500)]
	public void Build_NoOverride_UsesThePerModeDefault(bool titlesOnly, int expected)
	{
		CompletionRequest request = CommitMessagePrompt.Build("diff", titlesOnly);

		Assert.Equal(expected, request.MaxTokens);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Build_Override_ReplacesTheDefaultInBothModes(bool titlesOnly)
	{
		// A reasoning model spends the budget on a discarded thinking block, so
		// the 100-token titles default has to be overridable.
		CompletionRequest request = CommitMessagePrompt.Build("diff", titlesOnly, 2048);

		Assert.Equal(2048, request.MaxTokens);
	}
}
