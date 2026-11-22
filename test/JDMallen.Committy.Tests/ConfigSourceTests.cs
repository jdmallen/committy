namespace JDMallen.Committy.Tests;

public class ConfigSourceTests
{
	private readonly Dictionary<string, string> _environment = [];
	private readonly Dictionary<string, GitConfigValue> _gitConfig = [];

	private Task<ResolvedConfig> Resolve(ConfigOverrides? overrides = null) =>
		Task.FromResult(
			CommittyConfigResolver.ResolveWithSources(
				overrides,
				name => _environment.GetValueOrDefault(name),
				_gitConfig));

	[Fact]
	public async Task Sources_NameTheGitScopeTheEnvironmentAndDefaults()
	{
		_gitConfig[CommittyConfigResolver.PROVIDER_KEY] =
			new GitConfigValue("anthropic", "local");
		_environment[CommittyConfigResolver.ANTHROPIC_API_KEY_ENV] = "sk-x";

		ResolvedConfig resolved = await Resolve();

		Assert.Equal(Provider.Anthropic, resolved.Config.Provider.Kind);
		Assert.Equal(
			"git config --local",
			Source(resolved, CommittyConfigResolver.PROVIDER_KEY));
		Assert.Equal(
			"$ANTHROPIC_API_KEY_COMMITTY",
			Source(resolved, CommittyConfigResolver.ANTHROPIC_API_KEY_KEY));
		Assert.Equal(
			"default",
			Source(resolved, CommittyConfigResolver.ANTHROPIC_MODEL_KEY));
	}

	[Fact]
	public async Task Sources_FlagBeatsGitConfig()
	{
		_gitConfig[CommittyConfigResolver.PROVIDER_KEY] =
			new GitConfigValue("azure", "global");

		ResolvedConfig resolved =
			await Resolve(new ConfigOverrides("openai"));

		Assert.Equal(Provider.OpenAI, resolved.Config.Provider.Kind);
		Assert.Equal(
			"command-line flag",
			Source(resolved, CommittyConfigResolver.PROVIDER_KEY));
	}

	[Fact]
	public async Task Sources_EnvironmentFallbackIsNamed()
	{
		_environment["COMMITTY_PROVIDER"] = "openai";

		ResolvedConfig resolved = await Resolve();

		Assert.Equal(
			"$COMMITTY_PROVIDER",
			Source(resolved, CommittyConfigResolver.PROVIDER_KEY));
	}

	[Fact]
	public void ParseAll_ReadsScopeKeyAndValue()
	{
		IReadOnlyDictionary<string, GitConfigValue> values =
			GitConfigStore.ParseAll(
				"global\0committy.openai.baseurl\nhttp://host:8080/v1\0");

		Assert.Equal(
			new GitConfigValue("http://host:8080/v1", "global"),
			values[CommittyConfigResolver.OPEN_AI_BASE_URL_KEY]);
	}

	[Fact]
	public void ParseAll_LaterScopeWins()
	{
		IReadOnlyDictionary<string, GitConfigValue> values =
			GitConfigStore.ParseAll(
				"global\0committy.provider\nopenai\0"
				+ "local\0committy.provider\nanthropic\0");

		Assert.Equal(
			new GitConfigValue("anthropic", "local"),
			values[CommittyConfigResolver.PROVIDER_KEY]);
	}

	[Fact]
	public void ParseAll_KeepsNewlinesInsideAValue()
	{
		IReadOnlyDictionary<string, GitConfigValue> values =
			GitConfigStore.ParseAll(
				"local\0committy.openai.model\nline1\nline2\0");

		Assert.Equal(
			"line1\nline2",
			values[CommittyConfigResolver.OPEN_AI_MODEL_KEY].Value);
	}

	[Fact]
	public void ParseAll_KeyWithoutValueIsEmpty()
	{
		IReadOnlyDictionary<string, GitConfigValue> values =
			GitConfigStore.ParseAll("local\0committy.titlesonly\0");

		Assert.Equal(
			string.Empty,
			values[CommittyConfigResolver.TITLES_ONLY_KEY].Value);
	}

	[Fact]
	public void ParseAll_KeysAreCaseInsensitive()
	{
		IReadOnlyDictionary<string, GitConfigValue> values =
			GitConfigStore.ParseAll("global\0committy.azure.apiKey\nk\0");

		Assert.True(
			values.ContainsKey(CommittyConfigResolver.AZURE_API_KEY_KEY));
	}

	[Fact]
	public void ParseAll_EmptyOutputIsEmpty() =>
		Assert.Empty(GitConfigStore.ParseAll(string.Empty));

	[Theory]
	[InlineData("worktree", "git config --worktree")]
	[InlineData("command", "git -c")]
	[InlineData(null, "git config")]
	public void GitConfigSource_DescribesTheScope(
		string? scope,
		string expected) =>
		Assert.Equal(
			expected,
			new ConfigSource(ConfigSourceKind.GitConfig, scope).ToString());

	private static string Source(ResolvedConfig resolved, string key) =>
		resolved.Sources[key].ToString();
}
