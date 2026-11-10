using System.CommandLine;
using System.Globalization;
using System.Reflection;

using Azure.Identity;

using JDMallen.Toolbox.AI.Azure;

using JetBrains.Annotations;

namespace JDMallen.Committy;

[UsedImplicitly]
internal class Program
{
	/// <summary>The usage log's directory name under local app data.</summary>
	private const string USAGE_APPLICATION_NAME = "committy";

	private static async Task AppendLinesAsync(
		string file,
		IEnumerable<string> lines) => await File.AppendAllTextAsync(
		file,
		string.Join('\n', lines) + "\n");

	/// <summary>
	/// Persists provider + credentials to git config (global by default, or
	/// <c>--local</c>
	/// for the current repository). Values come from flags; missing required values
	/// are
	/// prompted for interactively. committy reads these at commit time.
	/// </summary>
	private static Command BuildConfigCommand()
	{
		var providerOption = new Option<string?>("--provider", "-p")
		{
			Description = "Provider to configure: azure, anthropic, or openai",
			HelpName = "provider",
		};
		var apiKeyOption = new Option<string?>("--api-key", "-k")
		{
			Description =
				"API key for the provider, saved as a user environment variable (Windows only)",
			HelpName = "API key",
		};
		var endpointOption = new Option<string?>("--endpoint", "-e")
		{
			Description =
				"Azure OpenAI endpoint host URL, or the openai base URL",
			HelpName = "endpoint URL",
		};
		var deploymentOption = new Option<string?>("--deployment", "-d")
		{
			Description = "Azure OpenAI deployment name",
			HelpName = "deployment name",
		};
		var modelOption = new Option<string?>("--model", "-m")
		{
			Description = "Anthropic or openai model name",
			HelpName = "model",
		};
		var azureAuthOption = new Option<string?>("--azure-auth")
		{
			Description =
				"How to authenticate to Azure OpenAI: apikey (default), azurecli (Microsoft Entra ID via a signed-in Azure CLI), or azureidentity (Microsoft Entra ID via a browser or device code sign-in)",
			HelpName = "method",
		};
		var tenantOption = new Option<string?>("--tenant")
		{
			Description =
				"Microsoft Entra tenant ID or domain for azurecli or azureidentity auth; '-' clears it",
			HelpName = "tenant",
		};
		var credentialOption = new Option<string?>("--credential")
		{
			Description =
				$"Sign-in for azureidentity auth: {CommittyConfigResolver.AZURE_CREDENTIAL_NAMES} (default interactivebrowser)",
			HelpName = "credential",
		};
		var titlesOnlyOption = new Option<bool>("--titles-only", "-t")
		{
			Description = "Make titles-only mode the default",
		};
		var localOption = new Option<bool>("--local")
		{
			Description =
				"Write to the current repository's git config instead of the global config",
		};

		var command
			= new Command(
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

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			bool global = !parseResult.GetValue(localOption);

			try
			{
				// Checked before anything is written, so a typo leaves the
				// config as it was.
				if (parseResult.GetValue(tenantOption)?.Trim() is
						{ } tenantFlag and not "-"
					&& !AzureCli.IsValidTenant(tenantFlag))
				{
					throw new InvalidOperationException(
						$"'{tenantFlag}' is not a tenant GUID or domain name.");
				}

				if (parseResult.GetValue(credentialOption) is { } credentialFlag
					&& NormalizeAzureCredential(credentialFlag) is null)
				{
					throw new InvalidOperationException(
						$"'{credentialFlag}' is not a credential ({CommittyConfigResolver.AZURE_CREDENTIAL_NAMES}).");
				}

				Provider provider = await ResolveProviderForConfigAsync(
					parseResult.GetValue(providerOption),
					cancellationToken);

				var written = new List<(string Key, string? Display)>();

				await SetAsync(
					written,
					CommittyConfigResolver.PROVIDER_KEY,
					CommittyConfigResolver.ProviderName(provider),
					global,
					false,
					cancellationToken);

				ConfigField[] fields = provider switch
				{
					Provider.Anthropic =>
					[
						new ConfigField(
							"Anthropic model",
							CommittyConfigResolver.ANTHROPIC_MODEL_KEY,
							parseResult.GetValue(modelOption),
							CommittyConfigResolver.DEFAULT_ANTHROPIC_MODEL),
					],
					Provider.OpenAI =>
					[
						new ConfigField(
							"OpenAI base URL (e.g. http://10.10.0.20:8080/v1)",
							CommittyConfigResolver.OPEN_AI_BASE_URL_KEY,
							parseResult.GetValue(endpointOption)),
						new ConfigField(
							"OpenAI model",
							CommittyConfigResolver.OPEN_AI_MODEL_KEY,
							parseResult.GetValue(modelOption)),
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
							parseResult.GetValue(endpointOption)),
						new ConfigField(
							"Azure OpenAI deployment",
							CommittyConfigResolver.AZURE_DEPLOYMENT_KEY,
							parseResult.GetValue(deploymentOption),
							CommittyConfigResolver.DEFAULT_DEPLOYMENT),
						new ConfigField(
							"Azure reasoning model, e.g. o-series or gpt-5 (y/n)",
							CommittyConfigResolver.AZURE_REASONING_MODEL_KEY,
							null,
							"false",
							Normalize: CommittyConfigResolver.NormalizeBool,
							RetryHint: "Please answer y or n."),

						// Blank keeps the prompt's own budget, or 2048 for a
						// reasoning deployment.
						new ConfigField(
							$"Azure max tokens (blank for the default; {CommittyConfigResolver.DEFAULT_AZURE_REASONING_MAX_TOKENS} for reasoning models)",
							CommittyConfigResolver.AZURE_MAX_TOKENS_KEY,
							null),
						new ConfigField(
							"Azure auth: apikey, azurecli (Entra ID via az), or azureidentity (Entra ID via browser sign-in)",
							CommittyConfigResolver.AZURE_AUTH_KEY,
							parseResult.GetValue(azureAuthOption),
							CommittyConfigResolver.AzureAuthName(
								AzureAuth.ApiKey),
							Normalize: NormalizeAzureAuth,
							RetryHint:
							"Please answer apikey, azurecli or azureidentity."),
					],
				};

				foreach (ConfigField field in fields)
				{
					await ApplyFieldAsync(
						written,
						field,
						global,
						cancellationToken);
				}

				if (parseResult.GetValue(titlesOnlyOption))
				{
					await SetAsync(
						written,
						CommittyConfigResolver.TITLES_ONLY_KEY,
						"true",
						global,
						false,
						cancellationToken);
				}

				AzureAuth? azureAuth = provider == Provider.Azure
					? CommittyConfigResolver.ParseAzureAuth(
						await GitConfigStore.GetAsync(
							CommittyConfigResolver.AZURE_AUTH_KEY,
							cancellationToken))
					: null;
				bool usesAzureCli = azureAuth == AzureAuth.AzureCli;
				bool usesAzureIdentity = azureAuth == AzureAuth.AzureIdentity;

				if (usesAzureIdentity)
				{
					await ApplyFieldAsync(
						written,
						new ConfigField(
							$"Azure sign-in: {CommittyConfigResolver.AZURE_CREDENTIAL_NAMES}",
							CommittyConfigResolver.AZURE_CREDENTIAL_KEY,
							parseResult.GetValue(credentialOption),
							CommittyConfigResolver.AzureCredentialName(
								AzureCredentialKind.InteractiveBrowser),
							Normalize: NormalizeAzureCredential,
							RetryHint:
							$"Please answer {CommittyConfigResolver.AZURE_CREDENTIAL_NAMES}."),
						global,
						cancellationToken);
				}

				if (usesAzureCli || usesAzureIdentity)
				{
					await ConfigureTenantAsync(
						written,
						parseResult.GetValue(tenantOption),
						global,
						usesAzureIdentity,
						cancellationToken);
				}

				PrintConfigSummary(
					written,
					global,
					!usesAzureCli && !usesAzureIdentity);

				if (usesAzureCli)
				{
					await WriteAzureCliSetupAsync(cancellationToken);

					return;
				}

				if (usesAzureIdentity)
				{
					await SignInWithAzureIdentityAsync(cancellationToken);

					return;
				}

				// Keys live in environment variables, not git config. A key typed
				// on Windows is saved last, once the git config is written.
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
				string? storedGitKey =
					await GitConfigStore.GetAsync(
						apiKeyGitKey,
						cancellationToken) is null
						? null
						: apiKeyGitKey;
				var apiKeySetup = new ApiKeySetup(
					new SystemUserEnvironment(),
					Console.Out,
					Console.ReadLine,
					ReadSecret,
					Environment.GetEnvironmentVariable("SHELL"),
					OperatingSystem.IsLinux());
				string? pendingApiKey = apiKeySetup.Run(
					apiKeyVariable,
					provider == Provider.OpenAI,
					parseResult.GetValue(apiKeyOption),
					storedGitKey,
					!Console.IsInputRedirected);
				apiKeySetup.Apply(apiKeyVariable, pendingApiKey);
			}
			catch (Exception ex)
			{
				await Console.Error.WriteLineAsync($"Error: {ex.Message}");
				Environment.Exit(1);
			}
		});

		return command;
	}

	/// <summary>
	/// Installs the trampoline hook, locally or globally. The binary owns the hook
	/// text,
	/// so installation works from any directory as long as committy is on PATH.
	/// </summary>
	private static Command BuildInstallHookCommand()
	{
		var globalOption = new Option<bool>(
			"--global",
			"-g")
		{
			Description =
				"Install as a global hook template for all future repositories",
		};
		var repoArgument = new Argument<string?>("repo")
		{
			Description
				= "Repository to install into (defaults to the current directory); may be a headless git directory such as ~/.cfg; ignored with --global",
			Arity = ArgumentArity.ZeroOrOne,
		};

		var command = new Command(
			"install-hook",
			"Install the committy git hook (local or global)")
		{
			globalOption,
			repoArgument,
		};

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			bool global = parseResult.GetValue(globalOption);
			string? repo = parseResult.GetValue(repoArgument);

			int exitCode = await HookInstaller.InstallAsync(
				global,
				repo,
				Console.Out,
				cancellationToken);

			if (exitCode != 0)
			{
				Environment.Exit(exitCode);
			}
		});

		return command;
	}

	/// <summary>
	/// The git hook entry point. The installed trampoline invokes this with the path
	/// to
	/// the commit message file. Reads config (provider + credentials) the same way the
	/// rest of committy does, generates, and writes the file. Never returns a non-zero
	/// exit code: a hook failure must not block a commit.
	/// </summary>
	private static Command BuildPrepareCommitMsgCommand()
	{
		var commitMsgFileArgument = new Argument<string>("commit-msg-file")
		{
			Description = "Path to the commit message file (provided by git)",
		};

		var command = new Command(
			"prepare-commit-msg",
			"Git hook entry point: writes suggestions into the given commit message file")
		{
			commitMsgFileArgument,
		};

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			string commitMsgFile = parseResult.GetValue(commitMsgFileArgument)!;

			try
			{
				// Nothing staged → no-op, so an empty commit message stays clean.
				string? patch =
					await GitService.TryGetStagedDiffAsync(cancellationToken);

				if (string.IsNullOrWhiteSpace(patch))
				{
					return;
				}

				CommittyConfig config =
					await ResolveConfigAsync(null, cancellationToken);
				string? error = CheckReady(config);

				if (error is not null)
				{
					await AppendLinesAsync(
						commitMsgFile,
						[
							.. CommitMessageComposer.CommentLines(
								$"Committy: {error}"),
							"# Run `committy config` to set up a provider.",
						]);

					return;
				}

				// A hook must never open a browser from inside `git commit`.
				CommitSuggestions suggestions =
					await GenerateAsync(
						config,
						patch,
						config.TitlesOnly,
						false,
						cancellationToken);
				RecordUsage(suggestions.Usage);

				string existing = File.Exists(commitMsgFile)
					? await File.ReadAllTextAsync(
						commitMsgFile,
						cancellationToken)
					: string.Empty;

				string composed = CommitMessageComposer.Compose(
					suggestions.Messages,
					config.TitlesOnly,
					existing,
					suggestions.Usage);

				await File.WriteAllTextAsync(
					commitMsgFile,
					composed,
					cancellationToken);
			}
			catch (Exception ex)
				when (ChatCompletionClientFactory.IsSignInRequired(ex))
			{
				await TryAppendLinesAsync(
					commitMsgFile,
					[
						"# Committy: not signed in to Azure (or the sign-in expired).",
						"# Run `committy config` to sign in; hooks never open a browser.",
					]);
			}
			catch (Exception ex)
			{
				// Messages can span lines (the Azure CLI's own error is quoted
				// on the next one); every line must stay a comment.
				await TryAppendLinesAsync(
					commitMsgFile,
					CommitMessageComposer.CommentLines(
						$"Committy: failed to generate AI suggestions ({ex.Message})"));
			}
		});

		return command;
	}

	/// <summary>
	/// Replaces stale committy hooks with the current trampoline. By default it
	/// repairs
	/// only the current repository; <c>--scan &lt;dir&gt;</c> recursively sweeps a
	/// tree, and
	/// <c>--global</c> also re-stamps the global template. Only committy-managed hooks
	/// are
	/// touched; foreign hooks are reported and left alone.
	/// </summary>
	private static Command BuildRepairHooksCommand()
	{
		var globalOption = new Option<bool>("--global", "-g")
		{
			Description = "Also re-stamp the global hook template",
		};
		var scanOption = new Option<string[]>("--scan")
		{
			Description
				= "Directory to recursively scan for repositories (repeatable); default is the current repo only",
			Arity = ArgumentArity.ZeroOrMore,
			AllowMultipleArgumentsPerToken = true,
			HelpName = "dir",
		};
		var gitDirOption = new Option<string[]>("--git-dir")
		{
			Description
				= "Git directory to repair directly (repeatable); use for headless repos such as a ~/.cfg dotfiles setup that recursive scanning skips",
			Arity = ArgumentArity.ZeroOrMore,
			AllowMultipleArgumentsPerToken = true,
			HelpName = "dir",
		};
		var dryRunOption = new Option<bool>("--dry-run")
		{
			Description = "Report what would change without modifying anything",
		};
		var backupOption = new Option<bool>("--backup")
		{
			Description =
				"Save the previous hook as prepare-commit-msg.bak before replacing it",
		};

		var command = new Command(
			"repair-hooks",
			"Replace stale committy hooks with the current trampoline (current repo by default; --scan to sweep)")
		{
			globalOption,
			scanOption,
			gitDirOption,
			dryRunOption,
			backupOption,
		};

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			bool global = parseResult.GetValue(globalOption);
			string[] scan = parseResult.GetValue(scanOption) ?? [];
			string[] gitDirs = parseResult.GetValue(gitDirOption) ?? [];
			bool dryRun = parseResult.GetValue(dryRunOption);
			bool backup = parseResult.GetValue(backupOption);

			if (global)
			{
				if (dryRun)
				{
					await Console.Out.WriteLineAsync(
						"[dry-run] would re-stamp the global hook template");
				}
				else
				{
					await HookInstaller.InstallAsync(
						true,
						null,
						Console.Out,
						cancellationToken);
				}
			}

			int exitCode = await HookRepairer.RunAsync(
				scan,
				gitDirs,
				dryRun,
				backup,
				!global,
				Console.Out,
				cancellationToken);

			if (exitCode != 0)
			{
				Environment.Exit(exitCode);
			}
		});

		return command;
	}

	/// <summary>
	/// Builds the transport, client, and generator for the resolved config and
	/// runs one generation. The transport is built here rather than once at
	/// startup because its timeout is provider-dependent: a self-hosted model may
	/// need minutes where a hosted one needs seconds, and a single invocation
	/// makes exactly one completion call.
	/// </summary>
	/// <summary>
	/// Appends comment lines to the commit message file. Never throws: a hook
	/// failure must not abort the commit.
	/// </summary>
	private static async Task TryAppendLinesAsync(
		string file,
		IEnumerable<string> lines)
	{
		try
		{
			await AppendLinesAsync(file, lines);
		}
		catch
		{
			// Best effort only.
		}
	}

	private static async Task<CommitSuggestions> GenerateAsync(
		CommittyConfig config,
		string patch,
		bool titlesOnly,
		bool allowInteractiveSignIn,
		CancellationToken cancellationToken)
	{
		HttpClient http = HttpClientProvider.Create(config.TimeoutSeconds);
		IChatCompletionClient client =
			new ChatCompletionClientFactory(http).Create(
				config,
				allowInteractiveSignIn);
		var generator = new CommitMessageGenerator(
			client,
			config.MaxTokensOverride);

		return await generator.GenerateAsync(
			patch,
			titlesOnly,
			cancellationToken);
	}

	private static async Task<int> Main(string[] args)
	{
		var providerOption = new Option<string?>(
			"--provider",
			"-p")
		{
			Description =
				"LLM provider to use: azure, anthropic, or openai (overrides config)",
			HelpName = "provider",
		};
		var apiKeyOption = new Option<string?>(
			"--api-key",
			"-k")
		{
			Description =
				"API key for the selected provider (overrides config and environment)",
			HelpName = "API key",
		};
		var endpointOption = new Option<string?>(
			"--endpoint",
			"-e")
		{
			Description =
				"Azure OpenAI endpoint host URL; omit everything after the domain",
			HelpName = "endpoint URL",
		};
		var deploymentOption = new Option<string?>(
			"--deployment",
			"-d")
		{
			Description = "Azure OpenAI deployment name",
			HelpName = "deployment name",
		};
		var modelOption = new Option<string?>(
			"--model",
			"-m")
		{
			Description = "Anthropic model name",
			HelpName = "model",
		};
		var azureAuthOption = new Option<string?>("--azure-auth")
		{
			Description =
				"Azure OpenAI auth for this run: apikey, azurecli or azureidentity (overrides config)",
			HelpName = "method",
		};
		var tenantOption = new Option<string?>("--tenant")
		{
			Description =
				"Microsoft Entra tenant ID or domain for azurecli or azureidentity auth (overrides config)",
			HelpName = "tenant",
		};
		var credentialOption = new Option<string?>("--credential")
		{
			Description =
				"Sign-in for azureidentity auth this run (overrides config)",
			HelpName = "credential",
		};
		var noGitOption = new Option<bool>("--no-git")
		{
			Description =
				"When committy is called with nothing in stdin, it will call `git diff --cached` directly; this option disables that behavior and relies solely on stdin",
		};
		var clipboardOption = new Option<bool>(
			"--clipboard",
			"-c") { Description = "Copy first suggestion to clipboard" };
		var titlesOnlyOption = new Option<bool>(
			"--titles-only",
			"-t")
		{
			Description =
				"Generate 5 title-only suggestions instead of a single title+body message",
		};
		var infoOption = new Option<bool>("--info")
		{
			Description =
				"Show the configuration in effect here, where each setting comes from, and lifetime token usage",
		};

		var rootCommand =
			new RootCommand(
				"Generate AI-powered commit messages from git patches")
			{
				providerOption,
				apiKeyOption,
				endpointOption,
				deploymentOption,
				modelOption,
				azureAuthOption,
				tenantOption,
				credentialOption,
				noGitOption,
				clipboardOption,
				titlesOnlyOption,
				infoOption,
			};

		rootCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			bool isGitAccessDisabled = parseResult.GetValue(noGitOption);
			bool copyToClipboard = parseResult.GetValue(clipboardOption);

			var overrides = new ConfigOverrides(
				parseResult.GetValue(providerOption),
				parseResult.GetValue(apiKeyOption),
				parseResult.GetValue(endpointOption),
				parseResult.GetValue(deploymentOption),
				parseResult.GetValue(modelOption),
				parseResult.GetValue(azureAuthOption),
				parseResult.GetValue(tenantOption),
				parseResult.GetValue(credentialOption));

			if (parseResult.GetValue(infoOption))
			{
				Console.WriteLine(
					await BuildInfoReportAsync(overrides, cancellationToken));

				return;
			}

			try
			{
				CommittyConfig config = await ResolveConfigAsync(
					overrides,
					cancellationToken);
				string? error = CheckReady(config);

				if (error is not null)
				{
					await Console.Error.WriteLineAsync($"Error: {error}");
					Environment.Exit(1);
				}

				bool titlesOnly = parseResult.GetValue(titlesOnlyOption)
					|| config.TitlesOnly;

				string patch;

				if (isGitAccessDisabled || Console.IsInputRedirected)
				{
					patch = await CommittyService.ReadPatchFromStdinAsync(
						cancellationToken);
				}
				else
				{
					patch = await GitService.GetStagedDiffAsync(
						cancellationToken);
				}

				if (string.IsNullOrWhiteSpace(patch))
				{
					await Console.Error.WriteLineAsync(
						"Error: No patch data available.");
					Environment.Exit(1);
				}

				CommitSuggestions suggestions =
					await GenerateAsync(
						config,
						patch,
						titlesOnly,
						true,
						cancellationToken);
				RecordUsage(suggestions.Usage);

				foreach (string suggestion in suggestions.Messages)
				{
					Console.WriteLine(suggestion);
				}

				if (copyToClipboard && suggestions.Messages.Count > 0)
				{
					await CommittyService.CopyToClipboardAsync(
						suggestions.Messages[0],
						cancellationToken);
				}
			}
			catch (OperationCanceledException)
			{
				Environment.Exit(130); // exit code for SIGINT
			}
			catch (Exception ex)
			{
				await Console.Error.WriteLineAsync($"Error: {ex.Message}");
				Environment.Exit(1);
			}
		});

		rootCommand.Subcommands.Add(BuildPrepareCommitMsgCommand());
		rootCommand.Subcommands.Add(BuildInstallHookCommand());
		rootCommand.Subcommands.Add(BuildRepairHooksCommand());
		rootCommand.Subcommands.Add(BuildConfigCommand());

		return await rootCommand.Parse(args).InvokeAsync();
	}

	private static async Task<string> BuildInfoReportAsync(
		ConfigOverrides overrides,
		CancellationToken cancellationToken)
	{
		ResolvedConfig resolved =
			await CommittyConfigResolver.ResolveWithSourcesAsync(
				overrides,
				cancellationToken);
		string? repository =
			await GitService.TryGetRepositoryAsync(cancellationToken);
		string usagePath = TokenUsageLog.DefaultPath(USAGE_APPLICATION_NAME);
		TokenUsageTotals? totals =
			TokenUsageLog.Read(usagePath, out string? usageError);

		return InfoReport.Build(
			BuildVersionLine(),
			repository,
			resolved,
			new InfoReport.UsageReport(usagePath, totals, usageError),
			azure => AzureCli.Locate(
				azure,
				Environment.GetEnvironmentVariable));
	}

	private static string BuildVersionLine() =>
		$"committy {typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}";

	/// <summary>
	/// Adds one run's tokens to the lifetime totals. A failure is reported on
	/// stderr and otherwise ignored: the suggestions matter more than the
	/// count.
	/// </summary>
	private static void RecordUsage(TokenUsage? usage)
	{
		var run = new TokenUsageTally();
		run.Add(usage);
		string path = TokenUsageLog.DefaultPath(USAGE_APPLICATION_NAME);
		if (!TokenUsageLog.TryRecord(
				path,
				run,
				DateTimeOffset.Now,
				out string? error))
		{
			Console.Error.WriteLine(
				$"Warning: token usage not recorded in {path}: {error}");
		}
	}

	private static string Mask(string value) =>
		value.Length <= 4 ? "****" : $"{value[..4]}…";

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

	private static string? Prompt(
		string label,
		string? flagValue,
		string? current,
		bool secret)
	{
		// A flag always wins; otherwise prompt interactively, falling back to the current value.
		if (flagValue is not null)
		{
			return flagValue;
		}

		if (Console.IsInputRedirected)
		{
			return current;
		}

		string shown =
			current is null ? string.Empty : secret ? "****" : current;
		Console.Write(current is null ? $"{label}: " : $"{label} [{shown}]: ");

		// A typed value may be pasted with quotes around it (e.g. a tenant
		// ID); a secret is taken exactly as typed.
		string? input = secret
			? ReadSecret()
			: Console.ReadLine() is { } line
				? CommittyConfigResolver.Unquote(line)
				: null;

		return string.IsNullOrEmpty(input) ? current : input;
	}

	private static string ReadSecret()
	{
		var chars = new List<char>();

		while (true)
		{
			ConsoleKeyInfo key = Console.ReadKey(true);

			if (key.Key == ConsoleKey.Enter)
			{
				Console.WriteLine();

				break;
			}

			if (key.Key == ConsoleKey.Backspace)
			{
				if (chars.Count > 0)
				{
					chars.RemoveAt(chars.Count - 1);
					Console.Write("\b \b");
				}

				continue;
			}

			if (char.IsControl(key.KeyChar))
			{
				continue;
			}

			chars.Add(key.KeyChar);
			Console.Write('*');
		}

		return new string(chars.ToArray());
	}

	private static async Task<CommittyConfig> ResolveConfigAsync(
		ConfigOverrides? overrides,
		CancellationToken cancellationToken)
		=> await CommittyConfigResolver.ResolveAsync(
			overrides,
			cancellationToken);

	private static async Task<Provider> ResolveProviderForConfigAsync(
		string? flag,
		CancellationToken cancellationToken)
	{
		if (flag is not null)
		{
			return CommittyConfigResolver.ParseProvider(flag);
		}

		string? current = await GitConfigStore.GetAsync(
			CommittyConfigResolver.PROVIDER_KEY,
			cancellationToken);

		if (Console.IsInputRedirected)
		{
			return CommittyConfigResolver.ParseProvider(current);
		}

		Console.WriteLine("Select a provider:");
		Console.WriteLine("  1) azure     (Azure OpenAI)");
		Console.WriteLine("  2) anthropic (Claude)");
		Console.WriteLine(
			"  3) openai    (openai.com or a self-hosted OpenAI-compatible server)");
		string defaultChoice =
			CommittyConfigResolver.ParseProvider(current) switch
			{
				Provider.Anthropic => "2",
				Provider.OpenAI    => "3",
				_                  => "1",
			};
		Console.Write($"Choice [{defaultChoice}]: ");

		string? choice = Console.ReadLine()?.Trim();
		choice = string.IsNullOrEmpty(choice) ? defaultChoice : choice;

		return choice switch
		{
			"2" or "anthropic" or "claude" => Provider.Anthropic,
			"3" or "openai" or "local"     => Provider.OpenAI,
			_                              => Provider.Azure,
		};
	}

	private static async Task SetAsync(
		List<(string Key, string? Display)> written,
		string key,
		string? value,
		bool global,
		bool secret,
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
		written.Add((key, secret ? Mask(value) : value));
	}

	/// <summary>
	/// One persisted <c>committy config</c> field: a label for prompting, the git
	/// config key it maps to, the flag value (if any), an optional default shown when
	/// nothing is stored, whether the value should be masked, and, for a field with
	/// fixed answers, how to read an answer as its canonical value (null when it is
	/// not one) and what to say before asking again.
	/// </summary>
	private sealed record ConfigField(
		string Label,
		string Key,
		string? FlagValue,
		string? Default = null,
		bool Secret = false,
		Func<string?, string?>? Normalize = null,
		string? RetryHint = null);

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
			await GitConfigStore.GetAsync(
				field.Key,
				cancellationToken)
			?? field.Default;
		string? value = Prompt(
			field.Label,
			field.FlagValue,
			current,
			field.Secret);

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
					Prompt(
						field.Label,
						null,
						current,
						false));
			}

			value = normalized ?? normalize(current);
		}

		await SetAsync(
			written,
			field.Key,
			value,
			global,
			field.Secret,
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
	/// The resolved config's problem, if any, including an Azure CLI that
	/// cannot be found from the PATH committy runs with.
	/// </summary>
	private static string? CheckReady(CommittyConfig config) =>
		config.Validate()
		?? (config is { Provider: Provider.Azure, Azure: { } azure }
			? AzureCli.Problem(azure, Environment.GetEnvironmentVariable)
			: null);

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
				&& (await ResolveConfigAsync(null, cancellationToken)).Azure is
					{ UsesInteractiveSignIn: true }
					? await AzureCliTenantAsync(cancellationToken)
					: null);
		string? value = Prompt(
				label,
				flagValue,
				offered,
				false)
			?.Trim();

		while (value is not null and not "-" && !AzureCli.IsValidTenant(value))
		{
			if (flagValue is not null || Console.IsInputRedirected)
			{
				throw new InvalidOperationException(
					$"'{value}' is not a tenant GUID or domain name.");
			}

			Console.WriteLine("  Please enter a tenant GUID or domain name.");
			value = Prompt(
					label,
					null,
					offered,
					false)
				?.Trim();
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
			false,
			cancellationToken);
	}

	/// <summary>
	/// In place of the API key step: where az is, who is signed in (a
	/// read-only <c>az account show</c>), and the role the account needs.
	/// </summary>
	private static async Task WriteAzureCliSetupAsync(
		CancellationToken cancellationToken)
	{
		AzureConfig azure =
			(await ResolveConfigAsync(null, cancellationToken)).Azure!;
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
				await AzureCli.ShowAccountAsync(az, cancellationToken))?.Tenant;
	}

	/// <summary>
	/// In place of the API key step for azureidentity: signs in now (a browser
	/// or device code prompt, saved for later runs), so a git hook never has
	/// to; for the other credentials, gets one token to prove they work.
	/// </summary>
	private static async Task SignInWithAzureIdentityAsync(
		CancellationToken cancellationToken)
	{
		AzureConfig azure =
			(await ResolveConfigAsync(null, cancellationToken)).Azure!;
		string credential =
			CommittyConfigResolver.AzureCredentialName(azure.Credential);
		string roleLine =
			$"  The account needs the \"{AzureCli.DATA_PLANE_ROLE}\" role on the Azure OpenAI resource.";

		Console.WriteLine();
		Console.WriteLine(
			$"Azure sign-in (Microsoft Entra ID, {credential}; no API key needed):");

		if (azure.Validate() is { } problem)
		{
			await Console.Error.WriteLineAsync($"  {problem}");
			Environment.Exit(1);
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
			await Console.Error.WriteLineAsync($"  Sign-in failed: {ex.Message}");
			await Console.Error.WriteLineAsync(
				"  The settings above are saved; run `committy config` again to retry.");
			Environment.Exit(1);
		}
	}
}
