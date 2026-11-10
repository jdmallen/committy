using System.Globalization;
using System.Text;

namespace JDMallen.Committy;

/// <summary>
/// Builds the <c>committy --info</c> screen: the effective configuration for
/// the directory committy runs in, where each setting came from, and lifetime
/// token usage. API keys are reported as set or not, never shown.
/// </summary>
public static class InfoReport
{
	/// <param name="versionLine">e.g. "committy 1.5.0".</param>
	/// <param name="repository">
	/// The repository committy runs in, or null outside one.
	/// </param>
	/// <param name="resolved">The configuration and its sources.</param>
	/// <param name="usage">The usage block's inputs.</param>
	/// <param name="locateAzureCli">
	/// Finds the <c>az</c> an Azure config would run, or null when there is
	/// none; defaults to <see cref="AzureCli.Locate" /> on this process's PATH.
	/// </param>
	/// <param name="fileExists">
	/// Whether a file exists, for the saved Azure sign-in; defaults to
	/// <see cref="File.Exists" />.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException"></exception>
	public static string Build(
		string versionLine,
		string? repository,
		ResolvedConfig resolved,
		UsageReport usage,
		Func<AzureConfig, string?>? locateAzureCli = null,
		Func<string, bool>? fileExists = null)
	{
		fileExists ??= File.Exists;
		locateAzureCli ??= azure =>
			AzureCli.Locate(azure, Environment.GetEnvironmentVariable);
		CommittyConfig config = resolved.Config;
		string? problem = config.Validate();
		var rows = new List<(string Label, string Value)>
		{
			("repository",
				repository
				?? "(none: only global and system git config apply)"),
			Row(
				"provider",
				CommittyConfigResolver.ProviderName(config.Provider),
				CommittyConfigResolver.PROVIDER_KEY),
		};

		switch (config.Provider)
		{
			case Provider.Anthropic when config.Anthropic is { } anthropic:
				rows.Add(
					Row(
						"model",
						anthropic.Model,
						CommittyConfigResolver.ANTHROPIC_MODEL_KEY));
				rows.Add(
					KeyRow(
						anthropic.ApiKey,
						CommittyConfigResolver.ANTHROPIC_API_KEY_KEY,
						false));

				break;
			case Provider.OpenAI when config.OpenAI is { } openAI:
				rows.Add(
					Row(
						"base url",
						openAI.BaseUrl,
						CommittyConfigResolver.OPEN_AI_BASE_URL_KEY));
				rows.Add(
					Row(
						"model",
						openAI.Model,
						CommittyConfigResolver.OPEN_AI_MODEL_KEY));
				rows.Add(
					Row(
						"timeout",
						$"{openAI.TimeoutSeconds}s",
						CommittyConfigResolver.OPEN_AI_TIMEOUT_KEY));
				rows.Add(
					Row(
						"max tokens",
						Count(openAI.MaxTokens),
						CommittyConfigResolver.OPEN_AI_MAX_TOKENS_KEY));
				rows.Add(
					KeyRow(
						openAI.ApiKey,
						CommittyConfigResolver.OPEN_AI_API_KEY_KEY,
						true));

				break;
			case Provider.Azure when config.Azure is { } azure:
				rows.Add(
					Row(
						"endpoint",
						azure.Endpoint,
						CommittyConfigResolver.AZURE_ENDPOINT_KEY));
				rows.Add(
					Row(
						"deployment",
						azure.Deployment,
						CommittyConfigResolver.AZURE_DEPLOYMENT_KEY));
				rows.Add(
					Row(
						"reasoning",
						azure.ReasoningModel ? "yes" : "no",
						CommittyConfigResolver.AZURE_REASONING_MODEL_KEY));
				rows.Add(
					Row(
						"max tokens",
						azure.EffectiveMaxTokens is { } maxTokens
							? Count(maxTokens)
							: "built-in (100 for titles, 500 for a message)",
						CommittyConfigResolver.AZURE_MAX_TOKENS_KEY));
				AddAzureAuthRows(azure);

				break;
			default:
#pragma warning disable CA2208
				throw new ArgumentOutOfRangeException(
#pragma warning restore CA2208
					nameof(resolved.Config.Provider),
					$"unknown provider: {resolved.Config.Provider}");
		}

		rows.Add(
			Row(
				"titles only",
				config.TitlesOnly ? "yes" : "no",
				CommittyConfigResolver.TITLES_ONLY_KEY));

		if (problem is not null)
		{
			rows.Add(("problem", problem));
		}

		StringBuilder report =
			new StringBuilder(versionLine).AppendLine().AppendLine();
		List<(string Label, string Value)> usageRows = UsageRows(usage);

		// One label width for both blocks, so every value starts in the same
		// column.
		int labelWidth =
			rows.Concat(usageRows).Max(row => row.Label.Length) + 1;
		AppendRows(report, rows, labelWidth);
		report.AppendLine();
		AppendRows(report, usageRows, labelWidth);

		return report.ToString().TrimEnd();

		(string, string) Row(string label, string? value, string gitKey)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return (label, $"(not set: {gitKey})");
			}

			return resolved.Sources.TryGetValue(
				gitKey,
				out ConfigSource? source)
				? (label, $"{value} ({source})")
				: (label, value);
		}

		void AddAzureAuthRows(AzureConfig azure)
		{
			string authValue = azure.UnknownAuth is { } unknown
				? $"{unknown}, unknown (expected apikey, azurecli or azureidentity)"
				: azure.Auth switch
				{
					AzureAuth.AzureCli =>
						"azurecli, Microsoft Entra ID via the Azure CLI",
					AzureAuth.AzureIdentity =>
						"azureidentity, Microsoft Entra ID via Azure.Identity",
					_ => "apikey",
				};
			rows.Add(
				Row("auth", authValue, CommittyConfigResolver.AZURE_AUTH_KEY));

			if (azure.UnknownAuth is null
				&& azure.Auth == AzureAuth.AzureIdentity)
			{
				AddAzureIdentityRows(azure);

				return;
			}

			if (azure.Auth != AzureAuth.AzureCli
				|| azure.UnknownAuth is not null)
			{
				rows.Add(
					KeyRow(
						azure.ApiKey,
						CommittyConfigResolver.AZURE_API_KEY_KEY,
						false));

				return;
			}

			AzureCliTokenProviderOptions options = azure.TokenProviderOptions();
			rows.Add(
				options.TenantId is null
					? ("tenant",
						$"az's current tenant (not set: {CommittyConfigResolver.AZURE_TENANT_KEY})")
					: Row(
						"tenant",
						options.TenantId,
						CommittyConfigResolver.AZURE_TENANT_KEY));
			rows.Add(
				Row(
					"scope",
					options.Scope,
					CommittyConfigResolver.AZURE_SCOPE_KEY));

			string? az = locateAzureCli(azure);
			rows.Add(
				az is null
					? ("az", "not found")
					: options.AzExecutablePath is null
						? ("az", $"{az} (PATH)")
						: Row(
							"az",
							az,
							CommittyConfigResolver.AZURE_AZ_PATH_KEY));
			rows.Add(("sign in", AzureCli.SignInCommand(azure)));

			if (az is null)
			{
				problem ??= AzureCli.NotFoundMessage(azure);
			}
		}

		void AddAzureIdentityRows(AzureConfig azure)
		{
			rows.Add(
				azure.UnknownCredential is { } unknown
					? Row(
						"credential",
						$"{unknown}, unknown (expected {CommittyConfigResolver.AZURE_CREDENTIAL_NAMES})",
						CommittyConfigResolver.AZURE_CREDENTIAL_KEY)
					: Row(
						"credential",
						CommittyConfigResolver.AzureCredentialName(
							azure.Credential),
						CommittyConfigResolver.AZURE_CREDENTIAL_KEY));
			rows.Add(
				string.IsNullOrWhiteSpace(azure.TenantId)
					? ("tenant",
						$"the credential's default (not set: {CommittyConfigResolver.AZURE_TENANT_KEY})")
					: Row(
						"tenant",
						azure.TenantId.Trim(),
						CommittyConfigResolver.AZURE_TENANT_KEY));
			rows.Add(
				Row(
					"scope",
					azure.EffectiveScope,
					CommittyConfigResolver.AZURE_SCOPE_KEY));

			if (!azure.UsesInteractiveSignIn)
			{
				return;
			}

			bool signedIn = fileExists(azure.AuthenticationRecordPath);
			rows.Add(
				("signed in",
					signedIn
						? $"yes, saved in {azure.AuthenticationRecordPath}"
						: "no (run `committy config`)"));

			if (!signedIn)
			{
				problem ??=
					"Not signed in to Azure yet. Run `committy config` to sign in.";
			}
		}

		(string, string) KeyRow(string? apiKey, string gitKey, bool optional)
		{
			if (string.IsNullOrWhiteSpace(apiKey))
			{
				return ("api key", optional ? "not set (optional)" : "not set");
			}

			return resolved.Sources.TryGetValue(
				gitKey,
				out ConfigSource? source)
				? ("api key", $"set ({source})")
				: ("api key", "set");
		}
	}

	private static void AppendRows(
		StringBuilder report,
		List<(string Label, string Value)> rows,
		int labelWidth)
	{
		foreach ((string label, string value) in rows)
		{
			report.AppendLine(
				CultureInfo.CurrentCulture,
				$"  {$"{label}:".PadRight(labelWidth)} {value}");
		}
	}

	private static List<(string Label, string Value)> UsageRows(
		UsageReport usage)
	{
		var rows = new List<(string Label, string Value)>
		{
			("usage log", usage.Path),
		};

		if (usage.Totals is not { } totals)
		{
			rows.Add(("total", $"(unreadable: {usage.Error})"));

			return rows;
		}

		if (totals.AveragePerRun() is not { } average)
		{
			rows.Add(("total", "none recorded yet"));

			return rows;
		}

		string since = totals.Since is { } sinceDate
			? $" since {sinceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
			: string.Empty;
		string runs = totals.Runs == 1 ? "1 run" : $"{Count(totals.Runs)} runs";
		rows.Add(
			("total",
				$"{Count(totals.TotalTokens)} tokens ({Count(totals.InputTokens)} in, {Count(totals.OutputTokens)} out) over {runs}{since}"));
		rows.Add(
			("average",
				$"{Count(average.TotalTokens)} tokens per run ({Count(average.InputTokens)} in, {Count(average.OutputTokens)} out)"));

		return rows;
	}

	private static string Count(long value) =>
		value.ToString("N0", CultureInfo.CurrentCulture);

	/// <summary>The usage block's inputs.</summary>
	/// <param name="Path">Where the totals live.</param>
	/// <param name="Totals">The totals, or null when the file is unreadable.</param>
	/// <param name="Error">Why the file is unreadable, when it is.</param>
	public readonly record struct UsageReport(
		string Path,
		TokenUsageTotals? Totals,
		string? Error);
}
