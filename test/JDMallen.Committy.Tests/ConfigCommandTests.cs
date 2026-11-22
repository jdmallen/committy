namespace JDMallen.Committy.Tests;

public class ConfigCommandTests
{
	[Theory]
	// Writing local, only global set: the global value is what applies.
	[InlineData(null, "true", "global", "false", false,
		"unset; true from global applies")]
	// Nothing set anywhere: the built-in default.
	[InlineData(null, null, null, "false", false, "unset; default false")]
	[InlineData(null, null, null, null, false, null)]
	// Set in the scope being written and in effect: just the value.
	[InlineData("gpt-4o", "gpt-4o", "local", null, false, "gpt-4o")]
	[InlineData("gpt-4o", "gpt-4o", "global", null, true, "gpt-4o")]
	// Writing global inside a repo whose local value overrides it.
	[InlineData("gpt-4o", "gpt-5", "local", null, true,
		"gpt-4o; gpt-5 from local applies here")]
	// Same value in both scopes: nothing extra to say.
	[InlineData("gpt-4o", "gpt-4o", "local", null, true, "gpt-4o")]
	// Nothing in git config, but an environment variable applies.
	[InlineData(null, "1", "$COMMITTY_TITLES_ONLY", "false", false,
		"unset; 1 from $COMMITTY_TITLES_ONLY applies")]
	public void DescribeCurrent_ShowsStoredAndInEffectValues(
		string? stored,
		string? inEffectValue,
		string? inEffectScope,
		string? builtInDefault,
		bool global,
		string? expected)
	{
		GitConfigValue? inEffect = inEffectValue is null
			? null
			: new GitConfigValue(inEffectValue, inEffectScope);

		Assert.Equal(
			expected,
			ConfigCommand.DescribeCurrent(
				stored,
				inEffect,
				builtInDefault,
				global));
	}

	/// <summary>
	/// Each listed variable must be the one the resolver actually falls back
	/// to for that key, or the prompt would name the wrong one.
	/// </summary>
	[Theory]
	[InlineData("committy.provider", null)]
	[InlineData("committy.titlesonly", null)]
	[InlineData("committy.azure.endpoint", "azure")]
	[InlineData("committy.azure.deployment", "azure")]
	[InlineData("committy.anthropic.model", "anthropic")]
	[InlineData("committy.openai.baseurl", "openai")]
	[InlineData("committy.openai.model", "openai")]
	public void SettingEnvironmentVariables_MatchWhatTheResolverReads(
		string key,
		string? provider)
	{
		string variable =
			CommittyConfigResolver.SettingEnvironmentVariables[key];
		string value = key == "committy.provider" ? "openai" : "from-env";

		ResolvedConfig resolved = CommittyConfigResolver.ResolveWithSources(
			new ConfigOverrides(provider),
			name => name == variable ? value : null,
			new Dictionary<string, GitConfigValue>());

		Assert.Equal(
			new ConfigSource(ConfigSourceKind.Environment, variable),
			resolved.Sources[key]);
	}

	[Fact]
	public void SettingEnvironmentVariables_CoversEveryTestedKey() =>
		Assert.Equal(
			7,
			CommittyConfigResolver.SettingEnvironmentVariables.Count);
}
