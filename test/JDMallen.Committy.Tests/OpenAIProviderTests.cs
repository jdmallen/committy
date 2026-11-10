namespace JDMallen.Committy.Tests;

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
		int timeoutSeconds =
			CommittyConfigResolver.DEFAULT_OPEN_AI_TIMEOUT_SECONDS,
		int maxTokens = CommittyConfigResolver.DEFAULT_OPEN_AI_MAX_TOKENS,
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
		=> Assert.Null(Wrap(Config()).Validate());

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
		=> Assert.Contains("base URL is required", Config(baseUrl).Validate());

	[Fact]
	public void Validate_MissingModel_ReturnsError() => Assert.Contains(
		"model is required",
		Config(model: "").Validate());

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	public void Validate_NonPositiveTimeout_ReturnsError(int timeoutSeconds)
		=> Assert.Contains(
			"timeout must be greater than 0",
			Config(timeoutSeconds: timeoutSeconds).Validate());

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	public void Validate_NonPositiveMaxTokens_ReturnsError(int maxTokens)
		=> Assert.Contains(
			"max tokens must be greater than 0",
			Config(maxTokens: maxTokens).Validate());

	[Fact]
	public void Validate_MissingOpenAISection_ReturnsError()
	{
		var config = new CommittyConfig { Provider = Provider.OpenAI };

		Assert.Equal("OpenAI configuration missing.", config.Validate());
	}

	[Fact]
	public void TimeoutSeconds_OpenAI_ComesFromConfig() => Assert.Equal(
		120,
		Wrap(Config(timeoutSeconds: 120)).TimeoutSeconds);

	[Theory]
	[InlineData(Provider.Azure)]
	[InlineData(Provider.Anthropic)]
	public void TimeoutSeconds_HostedProviders_KeepTheShortDefault(
		Provider provider)
	{
		var config = new CommittyConfig { Provider = provider };

		Assert.Equal(
			CommittyConfig.DEFAULT_TIMEOUT_SECONDS,
			config.TimeoutSeconds);
	}

	[Fact]
	public void MaxTokensOverride_OpenAI_ComesFromConfig() => Assert.Equal(
		4096,
		Wrap(Config(maxTokens: 4096)).MaxTokensOverride);

	[Fact]
	public void MaxTokensOverride_Anthropic_IsNull()
	{
		var config = new CommittyConfig { Provider = Provider.Anthropic };

		Assert.Null(config.MaxTokensOverride);
	}

	[Theory]
	[InlineData(Provider.Azure, "azure")]
	[InlineData(Provider.Anthropic, "anthropic")]
	[InlineData(Provider.OpenAI, "openai")]
	public void ProviderName_RoundTripsThroughParseProvider(
		Provider provider,
		string expected)
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
	public void Build_NoOverride_UsesThePerModeDefault(
		bool titlesOnly,
		int expected)
	{
		CompletionRequest request =
			CommitMessagePrompt.Build("diff", titlesOnly);

		Assert.Equal(expected, request.MaxTokens);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Build_Override_ReplacesTheDefaultInBothModes(bool titlesOnly)
	{
		// A reasoning model spends the budget on a discarded thinking block, so
		// the 100-token titles default has to be overridable.
		CompletionRequest request =
			CommitMessagePrompt.Build("diff", titlesOnly, 2048);

		Assert.Equal(2048, request.MaxTokens);
	}

	private const string SMALL_PATCH =
		"diff --git a/src/App.cs b/src/App.cs\n"
		+ "--- a/src/App.cs\n"
		+ "+++ b/src/App.cs\n"
		+ "@@ -1 +1 @@\n"
		+ "-old\n"
		+ "+new\n";

	private static string LargePatch()
	{
		// 12 distinct files is enough to be LARGE regardless of line count.
		var builder = new System.Text.StringBuilder();

		for (int fileNumber = 0; fileNumber < 12; fileNumber++)
		{
			builder.Append(
				$"diff --git a/f{fileNumber}/Code{fileNumber}.cs b/f{fileNumber}/Code{fileNumber}.cs\n"
				+ $"--- a/f{fileNumber}/Code{fileNumber}.cs\n"
				+ $"+++ b/f{fileNumber}/Code{fileNumber}.cs\n"
				+ "@@ -1 +1 @@\n"
				+ "+added\n");
		}

		return builder.ToString();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Build_SmallPatch_EmbedsPatchAndFileList(bool titlesOnly)
	{
		CompletionRequest request =
			CommitMessagePrompt.Build(SMALL_PATCH, titlesOnly);

		Assert.Contains(SMALL_PATCH, request.UserPrompt);
		Assert.Contains(
			"CHANGE SIZE: SMALL (1 file, +1/-1 lines)",
			request.UserPrompt);
		Assert.Contains("M src/App.cs (+1/-1)", request.UserPrompt);
	}

	[Fact]
	public void Build_TitleAndBody_EndsWithSizeReminderAfterPatch()
	{
		CompletionRequest request =
			CommitMessagePrompt.Build(SMALL_PATCH, false);

		int patchIndex = request.UserPrompt.IndexOf(
			SMALL_PATCH,
			StringComparison.Ordinal);
		int reminderIndex = request.UserPrompt.IndexOf(
			"REMINDER: This is a SMALL change",
			StringComparison.Ordinal);
		Assert.True(reminderIndex > patchIndex);
		// Escaped braces in the example survive string.Format.
		Assert.Contains("/dead-letters/{id}/replay", request.UserPrompt);
	}

	[Theory]
	[InlineData(false, 800)]
	[InlineData(true, 100)]
	public void Build_LargePatch_RaisesOnlyTheTitleAndBodyBudget(
		bool titlesOnly,
		int expected)
	{
		CompletionRequest request =
			CommitMessagePrompt.Build(LargePatch(), titlesOnly);

		Assert.Equal(expected, request.MaxTokens);
		Assert.Contains("CHANGE SIZE: LARGE", request.UserPrompt);
	}

	[Fact]
	public void Build_LargePatch_OverrideStillWins()
	{
		CompletionRequest request =
			CommitMessagePrompt.Build(LargePatch(), false, 4096);

		Assert.Equal(4096, request.MaxTokens);
	}

	[Fact]
	public void DescribeChange_WithLockFile_ReportsMeaningfulCounts()
	{
		CondensedPatch condensed = PatchCondenser.Condense(
			SMALL_PATCH
			+ "diff --git a/pnpm-lock.yaml b/pnpm-lock.yaml\n"
			+ "--- a/pnpm-lock.yaml\n"
			+ "+++ b/pnpm-lock.yaml\n"
			+ "@@ -1 +1,3 @@\n"
			+ "+a\n+b\n+c\n");

		Assert.Equal(
			"CHANGE SIZE: SMALL (2 files, +4/-1 lines; 1 file and 2 lines after setting aside lock/generated files and repeats)",
			CommitMessagePrompt.DescribeChange(condensed));
	}

	[Fact]
	public void ListFiles_DescribesEachCondensing()
	{
		List<FileChange> files =
		[
			new("pnpm-lock.yaml", 'M', 10, 2, false, FileCondensing.Generated),
			new("logo.png", 'A', 0, 0, true, FileCondensing.Generated),
			new("S.fr.resx", 'M', 4, 0, false, FileCondensing.Repeated, "S.de.resx"),
			new("Big.cs", 'M', 900, 0, false, FileCondensing.Truncated),
			new("Late.cs", 'M', 3, 3, false, FileCondensing.Omitted),
		];

		Assert.Equal(
			"M pnpm-lock.yaml (+10/-2, lock/generated file, diff omitted)\n"
			+ "A logo.png (binary, diff omitted)\n"
			+ "M S.fr.resx (+4/-0, same change as S.de.resx, diff omitted)\n"
			+ "M Big.cs (+900/-0, diff truncated)\n"
			+ "M Late.cs (+3/-3, diff omitted for length)",
			CommitMessagePrompt.ListFiles(files));
	}
}
