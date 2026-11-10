using System.Globalization;
using System.Text.RegularExpressions;

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
		string? usageError = null,
		string? azureCliPath = "/usr/bin/az",
		bool signedIn = false)
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
				usageError),
			_ => azureCliPath,
			_ => signedIn);
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

	[Fact]
	public async Task Info_ApiKeyAuth_ShowsTheAuthAndKeyRows()
	{
		_gitConfig[CommittyConfigResolver.PROVIDER_KEY] =
			new GitConfigValue("azure", "global");

		string report = await Build();

		Assert.Matches(@"auth:\s+apikey \(default\)", report);
		Assert.Matches(@"api key:\s+not set", report);
		Assert.DoesNotContain("sign in:", report);
	}

	[Fact]
	public async Task Info_AzureCli_ShowsTenantScopeAzAndSignIn()
	{
		_gitConfig[CommittyConfigResolver.AZURE_ENDPOINT_KEY] =
			new GitConfigValue("https://example.openai.azure.com", "global");
		_gitConfig[CommittyConfigResolver.AZURE_AUTH_KEY] =
			new GitConfigValue("azurecli", "global");
		_gitConfig[CommittyConfigResolver.AZURE_TENANT_KEY] =
			new GitConfigValue("contoso.com", "local");
		_environment[CommittyConfigResolver.AZURE_API_KEY_ENV] = SECRET;

		string report = await Build();

		Assert.Matches(
			@"auth:\s+azurecli, Microsoft Entra ID via the Azure CLI \(git config --global\)",
			report);
		Assert.Matches(@"tenant:\s+contoso\.com \(git config --local\)", report);
		Assert.Matches(
			$@"scope:\s+{Regex.Escape(AzureOpenAIScopes.DEFAULT)} \(default\)",
			report);
		Assert.Matches(@"az:\s+/usr/bin/az \(PATH\)", report);
		Assert.Matches(
			$@"sign in:\s+az login --tenant contoso\.com --scope {Regex.Escape(AzureOpenAIScopes.DEFAULT)}",
			report);
		Assert.DoesNotContain("api key:", report);
		Assert.DoesNotContain(SECRET, report);
		Assert.DoesNotContain("problem:", report);
	}

	[Fact]
	public async Task Info_AzureCli_NoTenant_UsesAzsCurrentTenant()
	{
		_gitConfig[CommittyConfigResolver.AZURE_AUTH_KEY] =
			new GitConfigValue("azurecli", "global");

		string report = await Build();

		Assert.Matches(@"tenant:\s+az's current tenant", report);
	}

	[Fact]
	public async Task Info_AzureCli_AzMissing_ReportsTheProblem()
	{
		_gitConfig[CommittyConfigResolver.AZURE_ENDPOINT_KEY] =
			new GitConfigValue("https://example.openai.azure.com", "global");
		_gitConfig[CommittyConfigResolver.AZURE_AUTH_KEY] =
			new GitConfigValue("azurecli", "global");

		string report = await Build(azureCliPath: null);

		Assert.Matches(@"az:\s+not found", report);
		Assert.Matches(
			$@"problem:\s+.*{Regex.Escape(CommittyConfigResolver.AZURE_AZ_PATH_KEY)}",
			report);
	}

	[Fact]
	public async Task Info_UnknownAuth_ReportsIt()
	{
		_gitConfig[CommittyConfigResolver.AZURE_AUTH_KEY] =
			new GitConfigValue("kerberos", "global");

		string report = await Build();

		Assert.Matches(
			@"auth:\s+kerberos, unknown \(expected apikey, azurecli or azureidentity\)",
			report);
		Assert.Matches(@"problem:\s+Unknown Azure auth 'kerberos'", report);
	}

	[Fact]
	public async Task Info_AzureIdentity_ShowsCredentialTenantAndSavedSignIn()
	{
		_gitConfig[CommittyConfigResolver.AZURE_ENDPOINT_KEY] =
			new GitConfigValue("https://example.openai.azure.com", "global");
		_gitConfig[CommittyConfigResolver.AZURE_AUTH_KEY] =
			new GitConfigValue("azureidentity", "global");
		_gitConfig[CommittyConfigResolver.AZURE_TENANT_KEY] =
			new GitConfigValue("contoso.com", "global");

		string report = await Build(signedIn: true);

		Assert.Matches(
			@"auth:\s+azureidentity, Microsoft Entra ID via Azure\.Identity \(git config --global\)",
			report);
		Assert.Matches(@"credential:\s+interactivebrowser \(default\)", report);
		Assert.Matches(@"tenant:\s+contoso\.com \(git config --global\)", report);
		Assert.Matches(@"signed in:\s+yes, saved in .*auth-record\.json", report);
		Assert.DoesNotContain("az:", report);
		Assert.DoesNotContain("api key:", report);
		Assert.DoesNotContain("problem:", report);
	}

	[Fact]
	public async Task Info_AzureIdentity_NotSignedIn_ReportsTheProblem()
	{
		_gitConfig[CommittyConfigResolver.AZURE_ENDPOINT_KEY] =
			new GitConfigValue("https://example.openai.azure.com", "global");
		_gitConfig[CommittyConfigResolver.AZURE_AUTH_KEY] =
			new GitConfigValue("azureidentity", "global");

		string report = await Build();

		Assert.Matches(@"signed in:\s+no \(run `committy config`\)", report);
		Assert.Matches(@"problem:\s+Not signed in to Azure yet", report);
	}

	[Fact]
	public async Task Info_AzureIdentity_NonInteractiveCredential_HasNoSignInRow()
	{
		_gitConfig[CommittyConfigResolver.AZURE_ENDPOINT_KEY] =
			new GitConfigValue("https://example.openai.azure.com", "global");
		_gitConfig[CommittyConfigResolver.AZURE_AUTH_KEY] =
			new GitConfigValue("azureidentity", "global");
		_gitConfig[CommittyConfigResolver.AZURE_CREDENTIAL_KEY] =
			new GitConfigValue("default", "local");

		string report = await Build();

		Assert.Matches(@"credential:\s+default \(git config --local\)", report);
		Assert.DoesNotContain("signed in:", report);
		Assert.DoesNotContain("problem:", report);
	}
}
