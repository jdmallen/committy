using System.CommandLine;
using JetBrains.Annotations;

namespace Committy;

[UsedImplicitly]
internal class Program
{
	private static async Task AppendLinesAsync(string file, IEnumerable<string> lines)
	{
		await File.AppendAllTextAsync(file, string.Join('\n', lines) + "\n");
	}

	/// <summary>
	/// Persists provider + credentials to git config (global by default, or
	/// <c>--local</c>
	/// for the current repository). Values come from flags; missing required values
	/// are
	/// prompted for interactively. committy reads these at commit time.
	/// </summary>
	private static Command BuildConfigCommand()
	{
		var providerOption = new Option<string?>("--provider", ["-p"])
		{
			Description = "Provider to configure: azure or anthropic",
			HelpName = "provider",
		};
		var apiKeyOption = new Option<string?>("--api-key", ["-k"])
		{
			Description = "API key for the provider",
			HelpName = "API key",
		};
		var endpointOption = new Option<string?>("--endpoint", ["-e"])
		{
			Description = "Azure OpenAI endpoint host URL",
			HelpName = "endpoint URL",
		};
		var deploymentOption = new Option<string?>("--deployment", ["-d"])
		{
			Description = "Azure OpenAI deployment name",
			HelpName = "deployment name",
		};
		var modelOption = new Option<string?>("--model", ["-m"])
		{
			Description = "Anthropic model name",
			HelpName = "model",
		};
		var titlesOnlyOption = new Option<bool>("--titles-only", ["-t"])
		{
			Description = "Make titles-only mode the default",
		};
		var localOption = new Option<bool>("--local")
		{
			Description = "Write to the current repository's git config instead of the global config",
		};

		var command
			= new Command("config", "Configure the LLM provider and credentials (stored in git config)")
			{
				providerOption,
				apiKeyOption,
				endpointOption,
				deploymentOption,
				modelOption,
				titlesOnlyOption,
				localOption,
			};

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			bool global = !parseResult.GetValue(localOption);
			var store = new GitConfigStore();

			try
			{
				Provider provider = await ResolveProviderForConfigAsync(
					parseResult.GetValue(providerOption),
					cancellationToken);

				var written = new List<(string Key, string Display)>();

				await SetAsync(
					store,
					written,
					CommittyConfigResolver.ProviderKey,
					provider == Provider.Anthropic ? "anthropic" : "azure",
					global,
					false,
					cancellationToken);

				if (provider == Provider.Anthropic)
				{
					string? apiKey = Prompt(
						"Anthropic API key",
						parseResult.GetValue(apiKeyOption),
						await GitConfigStore.GetAsync(
							CommittyConfigResolver.AnthropicApiKeyKey,
							cancellationToken),
						true);
					string? model = Prompt(
						"Anthropic model",
						parseResult.GetValue(modelOption),
						await GitConfigStore.GetAsync(
							CommittyConfigResolver.AnthropicModelKey,
							cancellationToken)
						?? CommittyConfigResolver.DefaultAnthropicModel,
						false);

					await SetAsync(
						store,
						written,
						CommittyConfigResolver.AnthropicApiKeyKey,
						apiKey,
						global,
						true,
						cancellationToken);
					await SetAsync(
						store,
						written,
						CommittyConfigResolver.AnthropicModelKey,
						model,
						global,
						false,
						cancellationToken);
				}
				else
				{
					string? apiKey = Prompt(
						"Azure OpenAI API key",
						parseResult.GetValue(apiKeyOption),
						await GitConfigStore.GetAsync(CommittyConfigResolver.AzureApiKeyKey, cancellationToken),
						true);
					string? endpoint = Prompt(
						"Azure OpenAI endpoint",
						parseResult.GetValue(endpointOption),
						await GitConfigStore.GetAsync(
							CommittyConfigResolver.AzureEndpointKey,
							cancellationToken),
						false);
					string? deployment = Prompt(
						"Azure OpenAI deployment",
						parseResult.GetValue(deploymentOption),
						await GitConfigStore.GetAsync(
							CommittyConfigResolver.AzureDeploymentKey,
							cancellationToken)
						?? CommittyConfigResolver.DefaultDeployment,
						false);

					await SetAsync(
						store,
						written,
						CommittyConfigResolver.AzureApiKeyKey,
						apiKey,
						global,
						true,
						cancellationToken);
					await SetAsync(
						store,
						written,
						CommittyConfigResolver.AzureEndpointKey,
						endpoint,
						global,
						false,
						cancellationToken);
					await SetAsync(
						store,
						written,
						CommittyConfigResolver.AzureDeploymentKey,
						deployment,
						global,
						false,
						cancellationToken);
				}

				if (parseResult.GetValue(titlesOnlyOption))
				{
					await SetAsync(
						store,
						written,
						CommittyConfigResolver.TitlesOnlyKey,
						"true",
						global,
						false,
						cancellationToken);
				}

				PrintConfigSummary(written, global);
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
			["-g"])
		{
			Description = "Install as a global hook template for all future repositories",
		};
		var repoArgument = new Argument<string?>("repo")
		{
			Description
				= "Repository to install into (defaults to the current directory); ignored with --global",
			Arity = ArgumentArity.ZeroOrOne,
		};

		var command = new Command("install-hook", "Install the committy git hook (local or global)")
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
	private static Command BuildPrepareCommitMsgCommand(IHttpService http)
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
				string? patch = await GitService.TryGetStagedDiffAsync(cancellationToken);

				if (string.IsNullOrWhiteSpace(patch))
				{
					return;
				}

				CommittyConfig config = await ResolveConfigAsync(null, cancellationToken);
				string? error = config.Validate();

				if (error is not null)
				{
					await AppendLinesAsync(
						commitMsgFile,
						[
							$"# Committy: {error}",
							"# Run `committy config` to set up a provider.",
						]);

					return;
				}

				List<string> suggestions =
					await GenerateAsync(
						http,
						config,
						patch,
						config.TitlesOnly,
						cancellationToken);

				string existing = File.Exists(commitMsgFile)
					? await File.ReadAllTextAsync(commitMsgFile, cancellationToken)
					: string.Empty;

				string composed = CommitMessageComposer.Compose(suggestions, config.TitlesOnly, existing);

				await File.WriteAllTextAsync(commitMsgFile, composed, cancellationToken);
			}
			catch (Exception ex)
			{
				// Never let a hook failure abort the commit.
				try
				{
					await AppendLinesAsync(
						commitMsgFile,
						[$"# Committy: failed to generate AI suggestions ({ex.Message})"]);
				}
				catch
				{
					// Best effort only.
				}
			}
		});

		return command;
	}

	private static async Task<List<string>> GenerateAsync(
		IHttpService http,
		CommittyConfig config,
		string patch,
		bool titlesOnly,
		CancellationToken cancellationToken)
	{
		IChatCompletionClient client = new ChatCompletionClientFactory(http).Create(config);
		var generator = new CommitMessageGenerator(client);

		return await generator.GenerateAsync(patch, titlesOnly, cancellationToken);
	}

	private static async Task<int> Main(string[] args)
	{
		// Transport is shared across commands and backed by IHttpClientFactory.
		IHttpService http = HttpService.Create();

		var providerOption = new Option<string?>(
			"--provider",
			["-p"])
		{
			Description = "LLM provider to use: azure or anthropic (overrides config)",
			HelpName = "provider",
		};
		var apiKeyOption = new Option<string?>(
			"--api-key",
			["-k"])
		{
			Description = "API key for the selected provider (overrides config and environment)",
			HelpName = "API key",
		};
		var endpointOption = new Option<string?>(
			"--endpoint",
			["-e"])
		{
			Description = "Azure OpenAI endpoint host URL; omit everything after the domain",
			HelpName = "endpoint URL",
		};
		var deploymentOption = new Option<string?>(
			"--deployment",
			["-d"])
		{
			Description = "Azure OpenAI deployment name",
			HelpName = "deployment name",
		};
		var modelOption = new Option<string?>(
			"--model",
			["-m"])
		{
			Description = "Anthropic model name",
			HelpName = "model",
		};
		var noGitOption = new Option<bool>("--no-git")
		{
			Description =
				"When committy is called with nothing in stdin, it will call `git diff --cached` directly; this option disables that behavior and relies solely on stdin",
		};
		var clipboardOption = new Option<bool>(
			"--clipboard",
			["-c"]) { Description = "Copy first suggestion to clipboard" };
		var titlesOnlyOption = new Option<bool>(
			"--titles-only",
			["-t"])
		{
			Description = "Generate 5 title-only suggestions instead of a single title+body message",
		};

		var rootCommand = new RootCommand("Generate AI-powered commit messages from git patches")
		{
			providerOption,
			apiKeyOption,
			endpointOption,
			deploymentOption,
			modelOption,
			noGitOption,
			clipboardOption,
			titlesOnlyOption,
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
				parseResult.GetValue(modelOption));

			try
			{
				CommittyConfig config = await ResolveConfigAsync(overrides, cancellationToken);
				string? error = config.Validate();

				if (error is not null)
				{
					await Console.Error.WriteLineAsync($"Error: {error}");
					Environment.Exit(1);
				}

				bool titlesOnly = parseResult.GetValue(titlesOnlyOption) || config.TitlesOnly;

				string patch;

				if (isGitAccessDisabled || Console.IsInputRedirected)
				{
					patch = await CommittyService.ReadPatchFromStdinAsync(cancellationToken);
				}
				else
				{
					patch = await GitService.GetStagedDiffAsync(cancellationToken);
				}

				if (string.IsNullOrWhiteSpace(patch))
				{
					await Console.Error.WriteLineAsync("Error: No patch data available.");
					Environment.Exit(1);
				}

				List<string> suggestions =
					await GenerateAsync(
						http,
						config,
						patch,
						titlesOnly,
						cancellationToken);

				foreach (string suggestion in suggestions)
				{
					Console.WriteLine(suggestion);
				}

				if (copyToClipboard && suggestions.Count > 0)
				{
					await CommittyService.CopyToClipboardAsync(suggestions[0], cancellationToken);
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

		rootCommand.Subcommands.Add(BuildPrepareCommitMsgCommand(http));
		rootCommand.Subcommands.Add(BuildInstallHookCommand());
		rootCommand.Subcommands.Add(BuildConfigCommand());

		return await rootCommand.Parse(args).InvokeAsync();
	}

	private static string Mask(string value) =>
		value.Length <= 4 ? "****" : $"{value[..4]}…";

	private static void PrintConfigSummary(List<(string Key, string Display)> written, bool global)
	{
		string scope = global ? "global" : "local";

		if (written.Count == 0)
		{
			Console.WriteLine("No changes written.");

			return;
		}

		Console.WriteLine($"\nUpdated git config ({scope}):");

		foreach ((string key, string display) in written)
		{
			Console.WriteLine($"  {key} = {display}");
		}

		Console.WriteLine("\nEquivalent manual commands:");

		foreach ((string key, string display) in written)
		{
			Console.WriteLine($"  git config --{scope} {key} {display}");
		}

		Console.WriteLine(
			"\nCommitty reads these at commit time. Environment variables (e.g. ANTHROPIC_API_KEY,");
		Console.WriteLine("AZURE_OPENAI_API_KEY) override them for a single invocation.");
	}

	private static string? Prompt(string label, string? flagValue, string? current, bool secret)
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

		string shown = current is null ? string.Empty : secret ? "****" : current;
		Console.Write(current is null ? $"{label}: " : $"{label} [{shown}]: ");

		string? input = secret ? ReadSecret() : Console.ReadLine();

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

			if (!char.IsControl(key.KeyChar))
			{
				chars.Add(key.KeyChar);
				Console.Write('*');
			}
		}

		return new string(chars.ToArray());
	}

	private static async Task<CommittyConfig> ResolveConfigAsync(
		ConfigOverrides? overrides,
		CancellationToken cancellationToken)
	{
		var resolver = new CommittyConfigResolver(new GitConfigStore());

		return await resolver.ResolveAsync(overrides, cancellationToken);
	}

	private static async Task<Provider> ResolveProviderForConfigAsync(
		string? flag,
		CancellationToken cancellationToken)
	{
		if (flag is not null)
		{
			return CommittyConfigResolver.ParseProvider(flag);
		}

		string? current = await GitConfigStore.GetAsync(
			CommittyConfigResolver.ProviderKey,
			cancellationToken);

		if (Console.IsInputRedirected)
		{
			return CommittyConfigResolver.ParseProvider(current);
		}

		Console.WriteLine("Select a provider:");
		Console.WriteLine("  1) azure     (Azure OpenAI)");
		Console.WriteLine("  2) anthropic (Claude)");
		string defaultChoice = CommittyConfigResolver.ParseProvider(current) == Provider.Anthropic
			? "2"
			: "1";
		Console.Write($"Choice [{defaultChoice}]: ");

		string? choice = Console.ReadLine()?.Trim();
		choice = string.IsNullOrEmpty(choice) ? defaultChoice : choice;

		return choice is "2" or "anthropic" or "claude" ? Provider.Anthropic : Provider.Azure;
	}

	private static async Task SetAsync(
		GitConfigStore store,
		List<(string Key, string Display)> written,
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

		await store.SetAsync(
			key,
			value,
			global,
			cancellationToken);
		written.Add((key, secret ? Mask(value) : value));
	}
}
