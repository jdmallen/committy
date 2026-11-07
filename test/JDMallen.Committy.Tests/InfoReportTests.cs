using System.Globalization;

namespace JDMallen.Committy.Tests;

public class InfoReportTests
{
	private const string SECRET = "sk-super-secret-value";
	private const string USAGE_PATH = "/home/me/.local/share/committy/usage.json";

	private readonly Dictionary<string, string> _environment = [];
	private readonly Dictionary<string, GitConfigValue> _gitConfig = [];

	private async Task<string> Build(
		string? repository = "/home/me/repo",
		TokenUsageTotals? totals = null,
		string? usageError = null)
	{
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		ResolvedConfig resolved =
			await CommittyConfigResolver.ResolveWithSourcesAsync(
				null,
				name => _environment.GetValueOrDefault(name),
				(key, _) => Task.FromResult(_gitConfig.GetValueOrDefault(key)));

		return InfoReport.Build(
			"committy 9.9.9",
			repository,
			resolved,
			new InfoReport.UsageReport(
				USAGE_PATH,
				usageError is null ? totals ?? new TokenUsageTotals() : null,
				usageError));
	}

	[Fact]
	public async Task Info_LocalConfig_ShowsLocalValuesAndTheirScope()
	{
		_gitConfig[CommittyConfigResolver.PROVIDER_KEY] =
			new GitConfigValue("openai", "local");
		_gitConfig[CommittyConfigResolver.OPEN_AI_BASE_URL_KEY] =
			new GitConfigValue("http://10.10.0.20:8080/v1", "local");
		_gitConfig[CommittyConfigResolver.OPEN_AI_MODEL_KEY] =
			new GitConfigValue("qwen", "global");

		string report = await Build();

		Assert.StartsWith("committy 9.9.9", report);
		Assert.Contains("repository:  /home/me/repo", report);
		Assert.Contains("provider:    openai (git config --local)", report);
		Assert.Contains(
			"base url:    http://10.10.0.20:8080/v1 (git config --local)",
			report);
		Assert.Contains("model:       qwen (git config --global)", report);
		Assert.Contains("timeout:     300s (default)", report);
		Assert.Contains("api key:     not set (optional)", report);
	}

	[Fact]
	public async Task Info_NeverShowsTheApiKey()
	{
		_gitConfig[CommittyConfigResolver.PROVIDER_KEY] =
			new GitConfigValue("anthropic", "global");
		_environment[CommittyConfigResolver.ANTHROPIC_API_KEY_ENV] = SECRET;

		string report = await Build();

		Assert.DoesNotContain(SECRET, report);
		Assert.Contains(
			"api key:     set ($ANTHROPIC_API_KEY_COMMITTY)",
			report);
		Assert.Contains(
			$"model:       {CommittyConfigResolver.DEFAULT_ANTHROPIC_MODEL} (default)",
			report);
	}

	[Fact]
	public async Task Info_MissingRequiredSetting_ReportsTheProblem()
	{
		string report = await Build();

		Assert.Contains("provider:    azure (default)", report);
		Assert.Contains(
			$"endpoint:    (not set: {CommittyConfigResolver.AZURE_ENDPOINT_KEY})",
			report);
		Assert.Contains("problem:", report);
	}

	[Fact]
	public async Task Info_OutsideARepository_SaysOnlyGlobalConfigApplies()
	{
		string report = await Build(null);

		Assert.Contains(
			"(none: only global and system git config apply)",
			report);
	}

	[Fact]
	public async Task Usage_ReportsTotalsAndPerRunAverage()
	{
		string report = await Build(
			totals: new TokenUsageTotals
			{
				Runs = 4,
				Requests = 4,
				InputTokens = 10_000,
				OutputTokens = 2_345,
				Since = new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero),
			});

		Assert.Contains($"usage log:   {USAGE_PATH}", report);
		Assert.Contains(
			"total:       12,345 tokens (10,000 in, 2,345 out) over 4 runs since 2026-11-06",
			report);
		Assert.Contains(
			"average:     3,086 tokens per run (2,500 in, 586 out)",
			report);
	}

	[Fact]
	public async Task Usage_NothingRecorded_SaysSo()
	{
		string report = await Build();

		Assert.Contains("total:       none recorded yet", report);
		Assert.DoesNotContain("average:", report);
	}

	[Fact]
	public async Task Usage_UnreadableFile_ShowsTheError()
	{
		string report = await Build(usageError: "bad json");

		Assert.Contains("total:       (unreadable: bad json)", report);
	}
}
