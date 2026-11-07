namespace JDMallen.Committy.Tests;

public class ConfigSourceTests
{
	private readonly Dictionary<string, string> _environment = [];
	private readonly Dictionary<string, GitConfigValue> _gitConfig = [];

	private Task<ResolvedConfig> Resolve(ConfigOverrides? overrides = null) =>
		CommittyConfigResolver.ResolveWithSourcesAsync(
			overrides,
			name => _environment.GetValueOrDefault(name),
			(key, _) => Task.FromResult(_gitConfig.GetValueOrDefault(key)));

	[Fact]
	public async Task Sources_NameTheGitScopeTheEnvironmentAndDefaults()
	{
		_gitConfig[CommittyConfigResolver.PROVIDER_KEY] =
			new GitConfigValue("anthropic", "local");
		_environment[CommittyConfigResolver.ANTHROPIC_API_KEY_ENV] = "sk-x";

		ResolvedConfig resolved = await Resolve();

		Assert.Equal(Provider.Anthropic, resolved.Config.Provider);
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
			await Resolve(new ConfigOverrides(Provider: "openai"));

		Assert.Equal(Provider.OpenAI, resolved.Config.Provider);
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

	[Theory]
	[InlineData("local\tanthropic\n", "anthropic", "local")]
	[InlineData("global\thttp://host:8080/v1\n", "http://host:8080/v1", "global")]
	[InlineData("anthropic\n", "anthropic", null)]
	public void ParseScopedValue_SplitsScopeFromValue(
		string output,
		string value,
		string? scope) =>
		Assert.Equal(
			new GitConfigValue(value, scope),
			GitConfigStore.ParseScopedValue(output));

	[Fact]
	public void ParseScopedValue_BlankValueIsNull() =>
		Assert.Null(GitConfigStore.ParseScopedValue("local\t\n"));

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
