using System.CommandLine;
using System.Globalization;

using Azure.Identity;

using JDMallen.Toolbox.AI.Azure;

namespace JDMallen.Committy;

/// <summary>
/// Persists provider + credentials to git config (global by default, or
/// <c>--local</c> for the current repository). Values come from flags; missing
/// required values are prompted for interactively. committy reads these at
/// commit time.
/// </summary>
public static class ConfigCommand
{
	public static Command Build()
	{
		Option<string?> providerOption = SharedOptions.Provider(
			"Provider to configure: azure, anthropic, or openai");
		Option<string?> apiKeyOption = SharedOptions.ApiKey(
			"API key for the provider, saved as a user environment variable (Windows only)");
		Option<string?> endpointOption = SharedOptions.Endpoint(
			"Azure OpenAI endpoint host URL, or the openai base URL");
		Option<string?> deploymentOption = SharedOptions.Deployment();
		Option<string?> modelOption =
			SharedOptions.Model("Anthropic or openai model name");
		Option<string?> azureAuthOption = SharedOptions.AzureAuth(
			"How to authenticate to Azure OpenAI: apikey (default), azurecli (Microsoft Entra ID via a signed-in Azure CLI), or azureidentity (Microsoft Entra ID via a browser or device code sign-in)");
		Option<string?> tenantOption = SharedOptions.Tenant(
			"Microsoft Entra tenant ID or domain for azurecli or azureidentity auth; '-' clears it");
		Option<string?> credentialOption = SharedOptions.Credential(
			$"Sign-in for azureidentity auth: {CommittyConfigResolver.AZURE_CREDENTIAL_NAMES} (default interactivebrowser)");
		Option<bool> titlesOnlyOption =
			SharedOptions.TitlesOnly("Make titles-only mode the default");
		var localOption = new Option<bool>("--local")
		{
			Description =
				"Write to the current repository's git config instead of the global config",
		};

		var command = new Command(
			"config",
			"Configure the LLM provider (stored in git config) and check its API key variable")
		{
			providerOption,
			apiKeyOption,
			endpointOption,
			deploymentOption,
			modelOption,
			azureAuthOption,
			tenantOption,
			credentialOption,
			titlesOnlyOption,
			localOption,
		};

		command.SetAction((parseResult, cancellationToken) =>
			CommandErrors.HandleAsync(() =>
				RunAsync(
					new ConfigFlags(
						parseResult.GetValue(providerOption),
						parseResult.GetValue(apiKeyOption),
						parseResult.GetValue(endpointOption),
						parseResult.GetValue(deploymentOption),
						parseResult.GetValue(modelOption),
						parseResult.GetValue(azureAuthOption),
						parseResult.GetValue(tenantOption),
						parseResult.GetValue(credentialOption),
						parseResult.GetValue(titlesOnlyOption),
						!parseResult.GetValue(localOption)),
					cancellationToken)));

		return command;
	}

	private static async Task<int> RunAsync(
		ConfigFlags flags,
		CancellationToken cancellationToken)
	{
		// Checked before anything is written, so a typo leaves the config as
		// it was.
		if (flags.Tenant?.Trim() is { } tenantFlag and not "-"
			&& !AzureCli.IsValidTenant(tenantFlag))
		{
			throw new InvalidOperationException(
				$"'{tenantFlag}' is not a tenant GUID or domain name.");
		}

		if (flags.Credential is { } credentialFlag
			&& NormalizeAzureCredential(credentialFlag) is null)
		{
			throw new InvalidOperationException(
				$"'{credentialFlag}' is not a credential ({CommittyConfigResolver.AZURE_CREDENTIAL_NAMES}).");
		}

		var written = new List<(string Key, string? Display)>();
		Provider provider = await WriteProviderFieldsAsync(
			flags,
			written,
			cancellationToken);

		AzureAuth? azureAuth = provider == Provider.Azure
			? CommittyConfigResolver.ParseAzureAuth(
				await GitConfigStore.GetAsync(
					CommittyConfigResolver.AZURE_AUTH_KEY,
					cancellationToken))
			: null;
		bool usesAzureCli = azureAuth == AzureAuth.AzureCli;
		bool usesAzureIdentity = azureAuth == AzureAuth.AzureIdentity;

		if (usesAzureCli || usesAzureIdentity)
		{
			await WriteAzureSignInFieldsAsync(
				flags,
				written,
				usesAzureIdentity,
				cancellationToken);
		}

		PrintConfigSummary(
			written,
			flags.Global,
			!usesAzureCli && !usesAzureIdentity);

		if (usesAzureCli)
		{
			await WriteAzureCliSetupAsync(cancellationToken);
		}
		else if (usesAzureIdentity)
		{
			await SignInWithAzureIdentityAsync(cancellationToken);
		}
		else
		{
			await RunApiKeyStepAsync(provider, flags.ApiKey, cancellationToken);
		}

		return 0;
	}

	/// <summary>
	/// Writes the provider, its settings, and the titles-only default.
	/// </summary>
	/// <returns>The configured provider.</returns>
	private static async Task<Provider> WriteProviderFieldsAsync(
		ConfigFlags flags,
		List<(string Key, string? Display)> written,
		CancellationToken cancellationToken)
	{
		Provider provider = await ResolveProviderAsync(
			flags.Provider,
			cancellationToken);

		await SetAsync(
			written,
			CommittyConfigResolver.PROVIDER_KEY,
			CommittyConfigResolver.ProviderName(provider),
			flags.Global,
			cancellationToken);

		ConfigField[] fields = provider switch
		{
			Provider.Anthropic =>
			[
				new ConfigField(
					"Anthropic model",
					CommittyConfigResolver.ANTHROPIC_MODEL_KEY,
					flags.Model,
					CommittyConfigResolver.DEFAULT_ANTHROPIC_MODEL),
			],
			Provider.OpenAI =>
			[
				new ConfigField(
					"OpenAI base URL (e.g. http://10.10.0.20:8080/v1)",
					CommittyConfigResolver.OPEN_AI_BASE_URL_KEY,
					flags.Endpoint),
				new ConfigField(
					"OpenAI model",
					CommittyConfigResolver.OPEN_AI_MODEL_KEY,
					flags.Model),
				new ConfigField(
					"OpenAI timeout in seconds",
					CommittyConfigResolver.OPEN_AI_TIMEOUT_KEY,
					null,
					CommittyConfigResolver
						.DEFAULT_OPEN_AI_TIMEOUT_SECONDS.ToString(
							CultureInfo.CurrentCulture)),
				new ConfigField(
					"OpenAI max tokens",
					CommittyConfigResolver.OPEN_AI_MAX_TOKENS_KEY,
					null,
					CommittyConfigResolver.DEFAULT_OPEN_AI_MAX_TOKENS
						.ToString(CultureInfo.CurrentCulture)),
			],
			_ =>
			[
				new ConfigField(
					"Azure OpenAI endpoint",
					CommittyConfigResolver.AZURE_ENDPOINT_KEY,
					flags.Endpoint),
				new ConfigField(
					"Azure OpenAI deployment",
					CommittyConfigResolver.AZURE_DEPLOYMENT_KEY,
					flags.Deployment,
					CommittyConfigResolver.DEFAULT_DEPLOYMENT),
				new ConfigField(
					"Azure reasoning model, e.g. o-series or gpt-5 (y/n)",
					CommittyConfigResolver.AZURE_REASONING_MODEL_KEY,
					null,
					"false",
					CommittyConfigResolver.NormalizeBool,
					"Please answer y or n."),

				// Blank keeps the prompt's own budget, or 2048 for a
				// reasoning deployment.
				new ConfigField(
					$"Azure max tokens (blank for the default; {CommittyConfigResolver.DEFAULT_AZURE_REASONING_MAX_TOKENS} for reasoning models)",
					CommittyConfigResolver.AZURE_MAX_TOKENS_KEY,
					null),
				new ConfigField(
					"Azure auth: apikey, azurecli (Entra ID via az), or azureidentity (Entra ID via browser sign-in)",
					CommittyConfigResolver.AZURE_AUTH_KEY,
					flags.AzureAuth,
					CommittyConfigResolver.AzureAuthName(AzureAuth.ApiKey),
					NormalizeAzureAuth,
					"Please answer apikey, azurecli or azureidentity."),
			],
		};

		foreach (ConfigField field in fields)
		{
			await ApplyFieldAsync(
				written,
				field,
				flags.Global,
				cancellationToken);
		}

		if (flags.TitlesOnly)
		{
			await SetAsync(
				written,
				CommittyConfigResolver.TITLES_ONLY_KEY,
				"true",
				flags.Global,
				cancellationToken);
		}

		return provider;
	}

	/// <summary>
	/// Writes the Entra ID settings: the credential (azureidentity only) and
	/// the tenant.
	/// </summary>
	private static async Task WriteAzureSignInFieldsAsync(
		ConfigFlags flags,
		List<(string Key, string? Display)> written,
		bool azureIdentity,
		CancellationToken cancellationToken)
	{
		if (azureIdentity)
		{
			await ApplyFieldAsync(
				written,
				new ConfigField(
					$"Azure sign-in: {CommittyConfigResolver.AZURE_CREDENTIAL_NAMES}",
					CommittyConfigResolver.AZURE_CREDENTIAL_KEY,
					flags.Credential,
					CommittyConfigResolver.AzureCredentialName(
						AzureCredentialKind.InteractiveBrowser),
					NormalizeAzureCredential,
					$"Please answer {CommittyConfigResolver.AZURE_CREDENTIAL_NAMES}."),
				flags.Global,
				cancellationToken);
		}

		await ConfigureTenantAsync(
			written,
			flags.Tenant,
			flags.Global,
			azureIdentity,
			cancellationToken);
	}

	/// <summary>
	/// Keys live in environment variables, not git config. A key typed on
	/// Windows is saved last, once the git config is written.
	/// </summary>
	private static async Task RunApiKeyStepAsync(
		Provider provider,
		string? apiKeyFlag,
		CancellationToken cancellationToken)
	{
		(string apiKeyVariable, string apiKeyGitKey) = provider switch
		{
			Provider.Anthropic => (
				CommittyConfigResolver.ANTHROPIC_API_KEY_ENV,
				CommittyConfigResolver.ANTHROPIC_API_KEY_KEY),
			Provider.OpenAI => (
				CommittyConfigResolver.OPENAI_API_KEY_ENV,
				CommittyConfigResolver.OPEN_AI_API_KEY_KEY),
			_ => (CommittyConfigResolver.AZURE_API_KEY_ENV,
				CommittyConfigResolver.AZURE_API_KEY_KEY),
		};
		string? storedGitKey = await GitConfigStore.GetAsync(
				apiKeyGitKey,
				cancellationToken) is null
			? null
			: apiKeyGitKey;
		var apiKeySetup = new ApiKeySetup(
			new SystemUserEnvironment(),
			Console.Out,
			Console.ReadLine,
			ConsolePrompts.ReadSecret,
			Environment.GetEnvironmentVariable("SHELL"),
			OperatingSystem.IsLinux());
		string? pendingApiKey = apiKeySetup.Run(
			apiKeyVariable,
			provider == Provider.OpenAI,
			apiKeyFlag,
			storedGitKey,
			!Console.IsInputRedirected);
		apiKeySetup.Apply(apiKeyVariable, pendingApiKey);
	}

	private static void PrintConfigSummary(
		List<(string Key, string? Display)> written,
		bool global,
		bool apiKeyStepFollows)
	{
		string scope = global ? "global" : "local";

		if (written.Count == 0)
		{
			Console.WriteLine("No changes written.");

			return;
		}

		Console.WriteLine($"\nUpdated git config ({scope}):");

		foreach ((string key, string? display) in written)
		{
			Console.WriteLine(
				display is null ? $"  {key} (unset)" : $"  {key} = {display}");
		}

		Console.WriteLine("\nEquivalent manual commands:");

		foreach ((string key, string? display) in written)
		{
			Console.WriteLine(
				display is null
					? $"  git config --{scope} --unset {key}"
					: $"  git config --{scope} {key} {display}");
		}

		Console.WriteLine(
			"\nCommitty reads these at commit time, ahead of environment variables such as");
		Console.WriteLine(
			apiKeyStepFollows
				? "OPENAI_BASE_URL. API keys come from environment variables instead (below)."
				: "AZURE_OPENAI_ENDPOINT_HOST.");
	}

	private static async Task<Provider> ResolveProviderAsync(
		string? flag,
		CancellationToken cancellationToken)
	{
		if (flag is not null)
		{
			return CommittyConfigResolver.ParseProvider(flag)
				?? throw UnknownProvider(flag);
		}

		string? current = await GitConfigStore.GetAsync(
			CommittyConfigResolver.PROVIDER_KEY,
			cancellationToken);
		Provider? currentProvider =
			CommittyConfigResolver.ParseProvider(current);

		if (Console.IsInputRedirected)
		{
			return currentProvider ?? throw UnknownProvider(current);
		}

		Console.WriteLine("Select a provider:");
		Console.WriteLine("  1) azure     (Azure OpenAI)");
		Console.WriteLine("  2) anthropic (Claude)");
		Console.WriteLine(
			"  3) openai    (openai.com or a self-hosted OpenAI-compatible server)");
		string defaultChoice = currentProvider switch
		{
			Provider.Anthropic => "2",
			Provider.OpenAI    => "3",
			_                  => "1",
		};

		while (true)
		{
			Console.Write($"Choice [{defaultChoice}]: ");

			string? choice = Console.ReadLine()?.Trim();

			Provider? chosen = choice switch
			{
				null or "" => ChoiceProvider(defaultChoice),
				_ => ChoiceProvider(choice)
					?? CommittyConfigResolver.ParseProvider(choice),
			};

			if (chosen is { } provider)
			{
				return provider;
			}

			Console.WriteLine(
				$"'{choice}' is not a provider. Enter 1, 2 or 3, or {CommittyConfigResolver.PROVIDER_NAMES}.");
		}

		static Provider? ChoiceProvider(string choice) =>
			choice switch
			{
				"1" => Provider.Azure,
				"2" => Provider.Anthropic,
				"3" => Provider.OpenAI,
				_   => null,
			};

		static InvalidOperationException UnknownProvider(string? name) =>
			new(
				$"'{name}' is not a provider ({CommittyConfigResolver.PROVIDER_NAMES}).");
	}

	private static async Task SetAsync(
		List<(string Key, string? Display)> written,
		string key,
		string? value,
		bool global,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return;
		}

		await GitConfigStore.SetAsync(
			key,
			value,
			global,
			cancellationToken);
		written.Add((key, value));
	}

	/// <summary>
	/// One persisted <c>committy config</c> field: a label for prompting, the
	/// git config key it maps to, the flag value (if any), an optional default
	/// shown when nothing is stored, and, for a field with fixed answers, how
	/// to read an answer as its canonical value (null when it is not one) and
	/// what to say before asking again.
	/// </summary>
	private sealed record ConfigField(
		string Label,
		string Key,
		string? FlagValue,
		string? Default = null,
		Func<string?, string?>? Normalize = null,
		string? RetryHint = null);

	/// <summary>The <c>committy config</c> flags, as parsed.</summary>
	/// <param name="Global">
	/// True to write the global config; false for <c>--local</c>.
	/// </param>
	private sealed record ConfigFlags(
		string? Provider,
		string? ApiKey,
		string? Endpoint,
		string? Deployment,
		string? Model,
		string? AzureAuth,
		string? Tenant,
		string? Credential,
		bool TitlesOnly,
		bool Global);

	/// <summary>
	/// Prompts for one field (or takes its flag), re-asking until a field with
	/// fixed answers gets a valid one, and writes it.
	/// </summary>
	private static async Task ApplyFieldAsync(
		List<(string Key, string? Display)> written,
		ConfigField field,
		bool global,
		CancellationToken cancellationToken)
	{
		string? current =
			await GitConfigStore.GetAsync(field.Key, cancellationToken)
			?? field.Default;
		string? value = ConsolePrompts.Prompt(
			field.Label,
			field.FlagValue,
			current);

		if (field.Normalize is { } normalize)
		{
			string? normalized = normalize(value);
			if (normalized is null && field.FlagValue is not null)
			{
				throw new InvalidOperationException(
					$"'{field.FlagValue}' is not valid for {field.Key}. {field.RetryHint}");
			}

			while (normalized is null && !Console.IsInputRedirected)
			{
				Console.WriteLine($"  {field.RetryHint}");
				normalized = normalize(
					ConsolePrompts.Prompt(field.Label, null, current));
			}

			value = normalized ?? normalize(current);
		}

		await SetAsync(
			written,
			field.Key,
			value,
			global,
			cancellationToken);
	}

	private static string? NormalizeAzureCredential(string? value) =>
		CommittyConfigResolver.ParseAzureCredential(value) is { } kind
			? CommittyConfigResolver.AzureCredentialName(kind)
			: null;

	private static string? NormalizeAzureAuth(string? value) =>
		CommittyConfigResolver.ParseAzureAuth(value) is { } auth
			? CommittyConfigResolver.AzureAuthName(auth)
			: null;

	/// <summary>
	/// Asks for the Entra tenant (azurecli and azureidentity). Blank keeps the
	/// current value; "-" unsets it, so tokens come from az's current tenant
	/// (azurecli) or the credential's default (azureidentity). For a browser or
	/// device code sign-in with no tenant saved, az's current tenant is offered,
	/// because a personal Microsoft account cannot sign in without one.
	/// </summary>
	private static async Task ConfigureTenantAsync(
		List<(string Key, string? Display)> written,
		string? flagValue,
		bool global,
		bool azureIdentity,
		CancellationToken cancellationToken)
	{
		string label = azureIdentity
			? "Azure tenant ID or domain (personal Microsoft accounts need one; '-' for none)"
			: "Azure tenant ID or domain (blank keeps; '-' for az's current tenant)";
		string? current = await GitConfigStore.GetAsync(
			CommittyConfigResolver.AZURE_TENANT_KEY,
			cancellationToken);
		string? offered = current
			?? (azureIdentity
				&& flagValue is null
				&& !Console.IsInputRedirected
				&& (await CommittyConfigResolver.ResolveAsync(
						null,
						cancellationToken))
					.Azure is { UsesInteractiveSignIn: true }
					? await AzureCliTenantAsync(cancellationToken)
					: null);
		string? value =
			ConsolePrompts.Prompt(label, flagValue, offered)?.Trim();

		while (value is not null and not "-" && !AzureCli.IsValidTenant(value))
		{
			if (flagValue is not null || Console.IsInputRedirected)
			{
				throw new InvalidOperationException(
					$"'{value}' is not a tenant GUID or domain name.");
			}

			Console.WriteLine("  Please enter a tenant GUID or domain name.");
			value = ConsolePrompts.Prompt(label, null, offered)?.Trim();
		}

		if (value == "-")
		{
			if (current is null)
			{
				return;
			}

			await GitConfigStore.UnsetAsync(
				CommittyConfigResolver.AZURE_TENANT_KEY,
				global,
				cancellationToken);
			written.Add((CommittyConfigResolver.AZURE_TENANT_KEY, null));

			return;
		}

		await SetAsync(
			written,
			CommittyConfigResolver.AZURE_TENANT_KEY,
			value,
			global,
			cancellationToken);
	}

	/// <summary>
	/// In place of the API key step: where az is, who is signed in (a
	/// read-only <c>az account show</c>), and the role the account needs.
	/// </summary>
	private static async Task WriteAzureCliSetupAsync(
		CancellationToken cancellationToken)
	{
		AzureConfig azure = (await CommittyConfigResolver.ResolveAsync(
				null,
				cancellationToken))
			.Azure!;
		string? az = AzureCli.Locate(azure, Environment.GetEnvironmentVariable);
		string? accountJson = az is null
			? null
			: await AzureCli.ShowAccountAsync(az, cancellationToken);

		AzureCli.WriteSetup(
			Console.Out,
			azure,
			az,
			accountJson);
	}

	/// <summary>
	/// The tenant of the signed-in Azure CLI (a read-only
	/// <c>az account show</c>), or null when az is missing or signed out.
	/// </summary>
	private static async Task<string?> AzureCliTenantAsync(
		CancellationToken cancellationToken)
	{
		string? az = AzureCli.Find(Environment.GetEnvironmentVariable);

		return az is null
			? null
			: AzureCli.ParseAccount(
					await AzureCli.ShowAccountAsync(az, cancellationToken))
				?.Tenant;
	}

	/// <summary>
	/// In place of the API key step for azureidentity: signs in now (a browser
	/// or device code prompt, saved for later runs), so a git hook never has
	/// to; for the other credentials, gets one token to prove they work.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// The settings are incomplete, or the sign-in failed.
	/// </exception>
	private static async Task SignInWithAzureIdentityAsync(
		CancellationToken cancellationToken)
	{
		AzureConfig azure = (await CommittyConfigResolver.ResolveAsync(
				null,
				cancellationToken))
			.Azure!;
		string credential =
			CommittyConfigResolver.AzureCredentialName(azure.Credential);
		const string roleLine =
			$"  The account needs the \"{AzureCli.DATA_PLANE_ROLE}\" role on the Azure OpenAI resource.";

		Console.WriteLine();
		Console.WriteLine(
			$"Azure sign-in (Microsoft Entra ID, {credential}; no API key needed):");

		if (azure.Validate() is { } problem)
		{
			throw new InvalidOperationException(problem);
		}

		if (azure.UsesInteractiveSignIn && Console.IsInputRedirected)
		{
			Console.WriteLine(
				"  Run `committy config` in a terminal to sign in.");
			Console.WriteLine(roleLine);

			return;
		}

		Console.WriteLine(
			azure.Credential switch
			{
				AzureCredentialKind.InteractiveBrowser =>
					"  Opening a browser to sign in...",
				AzureCredentialKind.DeviceCode =>
					"  Sign in with the code below:",
				_ => $"  Getting a token from the {credential} credential...",
			});

		try
		{
			AzureSignInResult signedIn =
				await AzureCredentialFactory.SignInAsync(
					azure.CredentialOptions(true),
					azure.EffectiveScope,
					cancellationToken);

			Console.WriteLine(
				signedIn.Username is null
					? "  Got a token."
					: $"  Signed in as {signedIn.Username} (tenant {signedIn.TenantId}).");

			if (azure.UsesInteractiveSignIn)
			{
				Console.WriteLine(
					$"  Saved to {azure.AuthenticationRecordPath}; git hooks reuse it without prompting.");
			}

			Console.WriteLine(roleLine);
		}
		catch (Exception ex)
			when (ex is AuthenticationFailedException
					  or CredentialUnavailableException)
		{
			throw new InvalidOperationException(
				$"Sign-in failed: {ex.Message}{Environment.NewLine}"
				+ "The settings above are saved; run `committy config` again to retry.",
				ex);
		}
	}
}
