namespace JDMallen.Committy.Tests;

public sealed class AzureCliTests : IDisposable
{
	private const string TENANT = "contoso.onmicrosoft.com";

	private readonly string _directory = Directory.CreateTempSubdirectory(
			"committy-azurecli-tests-")
		.FullName;

	public void Dispose() => Directory.Delete(_directory, true);

	private static AzureConfig Config(
		string? tenantId = null,
		string? scope = null,
		string? azPath = null) =>
		new()
		{
			Auth = AzureAuth.AzureCli,
			Endpoint = "https://example.openai.azure.com",
			Deployment = "gpt-6-luna",
			TenantId = tenantId,
			Scope = scope,
			AzPath = azPath,
		};

	private string CreateFile(string name)
	{
		string path = Path.Combine(_directory, name);
		File.WriteAllText(path, string.Empty);

		return path;
	}

	[Theory]
	[InlineData("azurecli", AzureAuth.AzureCli)]
	[InlineData(" AzureCLI ", AzureAuth.AzureCli)]
	[InlineData("azure-cli", AzureAuth.AzureCli)]
	[InlineData("az", AzureAuth.AzureCli)]
	[InlineData("apikey", AzureAuth.ApiKey)]
	[InlineData("api-key", AzureAuth.ApiKey)]
	[InlineData(null, AzureAuth.ApiKey)]
	[InlineData("", AzureAuth.ApiKey)]
	[InlineData("azureidentity", AzureAuth.AzureIdentity)]
	[InlineData("Entra", AzureAuth.AzureIdentity)]
	[InlineData("kerberos", null)]
	public void ParseAzureAuth_KnownNamesCaseInsensitively_UnknownIsNull(
		string? name,
		AzureAuth? expected) => Assert.Equal(
		expected,
		CommittyConfigResolver.ParseAzureAuth(name));

	[Theory]
	[InlineData(AzureAuth.ApiKey)]
	[InlineData(AzureAuth.AzureCli)]
	[InlineData(AzureAuth.AzureIdentity)]
	public void AzureAuthName_RoundTrips(AzureAuth auth) => Assert.Equal(
		auth,
		CommittyConfigResolver.ParseAzureAuth(
			CommittyConfigResolver.AzureAuthName(auth)));

	[Fact]
	public void Find_Unix_ReturnsTheFirstAzOnPath()
	{
		string first = Path.Combine(_directory, "first");
		string second = Path.Combine(_directory, "second");
		Directory.CreateDirectory(first);
		Directory.CreateDirectory(second);
		string expected = Path.Combine(second, "az");
		File.WriteAllText(expected, string.Empty);
		string path = string.Join(Path.PathSeparator, first, second);

		Assert.Equal(
			expected,
			AzureCli.Find(
				name => name == "PATH" ? path : null,
				false));
	}

	[Fact]
	public void Find_Windows_FindsAzCmd()
	{
		string expected = CreateFile("az.cmd");

		Assert.Equal(
			expected,
			AzureCli.Find(name => name == "PATH" ? _directory : null, true));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public void Find_NoPath_ReturnsNull(string? path) =>
		Assert.Null(AzureCli.Find(_ => path, false));

	[Fact]
	public void Locate_ConfiguredPathWins()
	{
		string configured = CreateFile("my-az");

		Assert.Equal(
			configured,
			AzureCli.Locate(Config(azPath: configured), _ => null));
	}

	[Fact]
	public void Locate_ConfiguredPathMissing_IsNullEvenWithAzOnPath()
	{
		CreateFile("az");

		Assert.Null(
			AzureCli.Locate(
				Config(azPath: Path.Combine(_directory, "missing")),
				name => name == "PATH" ? _directory : null,
				false));
	}

	[Fact]
	public void Problem_NotOnPath_NamesTheAzPathSetting()
	{
		string? problem = AzureCli.Problem(Config(), _ => null);

		Assert.NotNull(problem);
		Assert.Contains(CommittyConfigResolver.AZURE_AZ_PATH_KEY, problem);
		Assert.Contains(AzureCli.INSTALL_URL, problem);
	}

	[Fact]
	public void Problem_ConfiguredPathMissing_SaysWhere()
	{
		string missing = Path.Combine(_directory, "missing");

		Assert.Equal(
			$"Azure CLI not found at '{missing}' ({CommittyConfigResolver.AZURE_AZ_PATH_KEY}).",
			AzureCli.Problem(Config(azPath: missing), _ => null));
	}

	[Fact]
	public void Problem_ApiKeyAuth_IsNull()
	{
		AzureConfig config = new()
		{
			Endpoint = "https://example.openai.azure.com",
			Deployment = "gpt-6-luna",
		};

		Assert.Null(AzureCli.Problem(config, _ => null));
	}

	[Fact]
	public void SignInCommand_Defaults()
		=> Assert.Equal(
			$"az login --scope {AzureOpenAIScopes.DEFAULT}",
			AzureCli.SignInCommand(Config()));

	[Fact]
	public void SignInCommand_TenantAndScope()
		=> Assert.Equal(
			$"az login --tenant {TENANT} --scope {AzureOpenAIScopes.COGNITIVE_SERVICES}",
			AzureCli.SignInCommand(
				Config(TENANT, AzureOpenAIScopes.COGNITIVE_SERVICES)));

	[Theory]
	[InlineData(TENANT, true)]
	[InlineData("00000000-0000-0000-0000-000000000000", true)]
	[InlineData("contoso & calc", false)]
	[InlineData("-tenant", false)]
	public void IsValidTenant_GuidOrDomainOnly(string tenant, bool expected)
		=> Assert.Equal(expected, AzureCli.IsValidTenant(tenant));

	[Fact]
	public void ParseAccount_ReadsUserAndTenant() => Assert.Equal(
		("me@example.com", "t-1"),
		AzureCli.ParseAccount(
			"""{ "name": "sub", "tenantId": "t-1", "user": { "name": "me@example.com", "type": "user" } }"""));

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("not json")]
	[InlineData("""{ "tenantId": "t-1" }""")]
	public void ParseAccount_Unusable_IsNull(string? json) =>
		Assert.Null(AzureCli.ParseAccount(json));

	[Fact]
	public void WriteSetup_SignedIn_ShowsAccountAndRole()
	{
		using var output = new StringWriter();

		AzureCli.WriteSetup(
			output,
			Config(),
			"/usr/bin/az",
			"""{ "tenantId": "t-1", "user": { "name": "me@example.com" } }""");

		var text = output.ToString();
		Assert.Contains("az: /usr/bin/az", text);
		Assert.Contains("Signed in as me@example.com (tenant t-1).", text);
		Assert.Contains(AzureCli.DATA_PLANE_ROLE, text);
	}

	[Fact]
	public void WriteSetup_NotSignedIn_PrintsTheSignInCommand()
	{
		using var output = new StringWriter();

		AzureCli.WriteSetup(
			output,
			Config(TENANT),
			"/usr/bin/az",
			null);

		Assert.Contains(
			$"Not signed in. Run: az login --tenant {TENANT} --scope {AzureOpenAIScopes.DEFAULT}",
			output.ToString());
	}

	[Fact]
	public void WriteSetup_NoAz_SaysHowToFixIt()
	{
		using var output = new StringWriter();

		AzureCli.WriteSetup(
			output,
			Config(),
			null,
			null);

		var text = output.ToString();
		Assert.Contains(CommittyConfigResolver.AZURE_AZ_PATH_KEY, text);
		Assert.Contains("Then sign in with: az login", text);
	}

	[UnixFact]
	public async Task ShowAccountAsync_ReturnsOutputOnSuccessOnly()
	{
		string signedIn = FakeAz.Create(
			_directory,
			"signed-in",
			"""{ "tenantId": "t-1", "user": { "name": "me@example.com" } }""");
		string signedOut = FakeAz.Create(
			_directory,
			"signed-out",
			"",
			1);

		Assert.Equal(
			("me@example.com", "t-1"),
			AzureCli.ParseAccount(
				await AzureCli.ShowAccountAsync(
					signedIn,
					CancellationToken.None)));
		Assert.Null(
			await AzureCli.ShowAccountAsync(signedOut, CancellationToken.None));
		Assert.Null(
			await AzureCli.ShowAccountAsync(
				Path.Combine(_directory, "missing"),
				CancellationToken.None));
	}
}

/// <summary>A fact that only runs where a <c>#!/bin/sh</c> script can.</summary>
public sealed class UnixFactAttribute : FactAttribute
{
	public UnixFactAttribute()
	{
		if (OperatingSystem.IsWindows())
		{
			Skip = "Needs a POSIX shell for the fake az script.";
		}
	}
}

/// <summary>Writes executable shell scripts that stand in for <c>az</c>.</summary>
internal static class FakeAz
{
	/// <summary>
	/// A script that prints <paramref name="output" /> and exits with
	/// <paramref name="exitCode" />, ignoring its arguments.
	/// </summary>
	public static string Create(
		string directory,
		string name,
		string output,
		int exitCode = 0)
	{
		string path = Path.Combine(directory, name);
		File.WriteAllText(
			path,
			$"#!/bin/sh\ncat <<'EOF'\n{output}\nEOF\nexit {exitCode}\n");
		if (!OperatingSystem.IsWindows())
		{
			File.SetUnixFileMode(
				path,
				UnixFileMode.UserRead
				| UnixFileMode.UserWrite
				| UnixFileMode.UserExecute);
		}

		return path;
	}
}
