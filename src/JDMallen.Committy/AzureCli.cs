using System.Text.Json;

using CliWrap;
using CliWrap.Buffered;

namespace JDMallen.Committy;

/// <summary>
/// Facts about the Azure CLI that committy needs before a request: where
/// <c>az</c> is, the sign-in command for a config, and who is signed in.
/// Fetching a token happens in the toolbox's
/// <see cref="AzureCliTokenProvider" />, on the request itself.
/// </summary>
public static class AzureCli
{
	public const string INSTALL_URL =
		"https://learn.microsoft.com/cli/azure/install-azure-cli";

	public const string DATA_PLANE_ROLE = "Cognitive Services OpenAI User";

	// On Windows, az is az.cmd (a batch wrapper); the toolbox runs it via
	// cmd.exe, which resolves it the same way.
	private static readonly string[] WindowsNames =
		["az.cmd", "az.exe", "az.bat"];

	private static readonly string[] UnixNames = ["az"];

	/// <summary>
	/// The first <c>az</c> on <c>PATH</c>, as the toolbox would launch it, or
	/// null when there is none.
	/// </summary>
	public static string? Find(
		Func<string, string?> getEnvironmentVariable,
		bool? isWindows = null)
	{
		string? path = getEnvironmentVariable("PATH");
		if (string.IsNullOrEmpty(path))
		{
			return null;
		}

		string[] names = isWindows ?? OperatingSystem.IsWindows()
			? WindowsNames
			: UnixNames;

		return path
			.Split(
				Path.PathSeparator,
				StringSplitOptions.RemoveEmptyEntries
				| StringSplitOptions.TrimEntries)
			.SelectMany(
				_ => names,
				Path.Combine)
			.FirstOrDefault(File.Exists);
	}

	/// <summary>
	/// The <c>az</c> committy would run for <paramref name="azure" />:
	/// <see cref="AzureConfig.AzPath" /> when it exists, else the first on
	/// PATH; null when neither is there.
	/// </summary>
	public static string? Locate(
		AzureConfig azure,
		Func<string, string?> getEnvironmentVariable,
		bool? isWindows = null) =>
		azure.TokenProviderOptions().AzExecutablePath is { } configured
			? File.Exists(configured) ? configured : null
			: Find(getEnvironmentVariable, isWindows);

	/// <summary>
	/// Why the Azure CLI cannot be used for <paramref name="azure" />, or null
	/// when it can (or the config does not use it). Checked before a request,
	/// so a git hook launched with a bare PATH says which setting fixes it.
	/// </summary>
	public static string? Problem(
		AzureConfig azure,
		Func<string, string?> getEnvironmentVariable,
		bool? isWindows = null)
		=> azure.Auth == AzureAuth.AzureCli
			&& Locate(azure, getEnvironmentVariable, isWindows) is null
				? NotFoundMessage(azure)
				: null;

	/// <summary>
	/// That <c>az</c> is missing, and the setting or install that fixes it.
	/// </summary>
	public static string NotFoundMessage(AzureConfig azure) =>
		azure.TokenProviderOptions().AzExecutablePath is { } configured
			? $"Azure CLI not found at '{configured}' ({CommittyConfigResolver.AZURE_AZ_PATH_KEY})."
			: $"Azure CLI ('az') not found on PATH. Install it ({INSTALL_URL}), or set {CommittyConfigResolver.AZURE_AZ_PATH_KEY} to its full path.";

	/// <summary>
	/// Whether <paramref name="tenant" /> is a tenant GUID or domain name the
	/// toolbox's token provider accepts.
	/// </summary>
	public static bool IsValidTenant(string tenant) =>
		new AzureConfig
		{
			Auth = AzureAuth.AzureCli,
			Deployment = "-",
			Endpoint = "https://-",
			TenantId = tenant,
		}.Validate() is null;

	/// <summary>
	/// <c>az login</c> for <paramref name="azure" />'s scope and tenant, so the
	/// cached sign-in covers the token committy asks for.
	/// </summary>
	public static string SignInCommand(AzureConfig azure)
	{
		AzureCliTokenProviderOptions options = azure.TokenProviderOptions();

		return options.TenantId is null
			? $"az login --scope {options.Scope}"
			: $"az login --tenant {options.TenantId} --scope {options.Scope}";
	}

	/// <summary>
	/// Runs the read-only <c>az account show</c> and returns its JSON, or null
	/// when it fails (typically: not signed in).
	/// </summary>
	public static async Task<string?> ShowAccountAsync(
		string az,
		CancellationToken cancellationToken)
	{
		// Same reason as the toolbox: az.cmd is a batch file.
		bool viaCommandShell = OperatingSystem.IsWindows()
			&& (az.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
				|| az.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));
		string[] arguments = ["account", "show", "--output", "json"];

		using var timeout =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(30));

		try
		{
			BufferedCommandResult result = await Cli.Wrap(
					viaCommandShell ? "cmd.exe" : az)
				.WithArguments(
					viaCommandShell
						? ["/d", "/c", az, .. arguments]
						: arguments)
				.WithValidation(CommandResultValidation.None)
				.ExecuteBufferedAsync(timeout.Token)
				.ConfigureAwait(false);

			return result.ExitCode == 0 ? result.StandardOutput : null;
		}
		catch (OperationCanceledException)
			when (!cancellationToken.IsCancellationRequested)
		{
			return null;
		}
		catch (System.ComponentModel.Win32Exception)
		{
			return null;
		}
	}

	/// <summary>
	/// What <c>committy config</c> prints for Azure CLI auth, in place of the
	/// API key step: where <c>az</c> is, who is signed in (or how to sign in),
	/// and the role the account needs.
	/// </summary>
	/// <param name="output">Where to write.</param>
	/// <param name="azure">The saved Azure settings.</param>
	/// <param name="az">The located <c>az</c>, or null when there is none.</param>
	/// <param name="accountJson">
	/// <c>az account show</c> output, or null when it failed or did not run.
	/// </param>
	public static void WriteSetup(
		TextWriter output,
		AzureConfig azure,
		string? az,
		string? accountJson)
	{
		output.WriteLine();
		output.WriteLine(
			"Azure CLI sign-in (Microsoft Entra ID; no API key needed):");

		if (az is null)
		{
			output.WriteLine($"  {NotFoundMessage(azure)}");
			output.WriteLine($"  Then sign in with: {SignInCommand(azure)}");
		}
		else
		{
			output.WriteLine($"  az: {az}");
			output.WriteLine(
				ParseAccount(accountJson) is var (user, tenant)
					? $"  Signed in as {user} (tenant {tenant})."
					: $"  Not signed in. Run: {SignInCommand(azure)}");
		}

		output.WriteLine(
			$"  The account needs the \"{DATA_PLANE_ROLE}\" role on the Azure OpenAI resource.");
	}

	/// <summary>
	/// The signed-in user and tenant from <c>az account show</c> JSON, or null
	/// when the JSON lacks them.
	/// </summary>
	public static (string User, string Tenant)? ParseAccount(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			string? user =
				root.TryGetProperty("user", out JsonElement userElement)
				&& userElement.TryGetProperty("name", out JsonElement name)
					? name.GetString()
					: null;
			string? tenant =
				root.TryGetProperty("tenantId", out JsonElement tenantElement)
					? tenantElement.GetString()
					: null;

			return user is null || tenant is null ? null : (user, tenant);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
