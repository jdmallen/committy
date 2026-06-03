using System.CommandLine;
using JetBrains.Annotations;

namespace Committy;

[UsedImplicitly]
internal class Program
{
	private const string AZURE_OPEN_AI_API_KEY_KEY = "AZURE_OPENAI_API_KEY";
	private const string AZURE_OPEN_AI_ENDPOINT_KEY = "AZURE_OPENAI_ENDPOINT_HOST";
	private const string AZURE_OPEN_AI_DEPLOYMENT_KEY = "AZURE_OPENAI_DEPLOYMENT";
	private const string TITLES_ONLY_ENV_KEY = "COMMITTY_TITLES_ONLY";
	private const string DEFAULT_DEPLOYMENT = "gpt-4.1-mini";

	private static async Task<int> Main(string[] args)
	{
		var apiKeyOption = new Option<string?>(
			name: "--api-key",
			aliases: ["-k"])
		{
			Description =
				$"Azure OpenAI API key (can also be set via {AZURE_OPEN_AI_API_KEY_KEY} environment variable)",
			HelpName = "API key",
		};
		var endpointOption = new Option<string?>(
			name: "--endpoint",
			aliases: ["-e"])
		{
			Description =
				$"Azure OpenAI endpoint host URL (can also be set via {AZURE_OPEN_AI_ENDPOINT_KEY} environment variable); omit everything after the domain",
			HelpName = "endpoint URL",
		};
		var deploymentOption = new Option<string?>(
			name: "--deployment",
			aliases: ["-d"])
		{
			Description =
				$"Azure OpenAI deployment name (can also be set via {AZURE_OPEN_AI_DEPLOYMENT_KEY} environment variable; defaults to {DEFAULT_DEPLOYMENT})",
			HelpName = "deployment name",
		};
		var noGitOption = new Option<bool>(name: "--no-git")
		{
			Description
				= "When committy is called with nothing in stdin, it will call `git diff --cached` directly; this option disables that behavior and relies solely on stdin",
		};
		var clipboardOption = new Option<bool>(
			name: "--clipboard",
			aliases: ["-c"])
		{
			Description = "Copy first suggestion to clipboard",
		};
		var titlesOnlyOption = new Option<bool>(
			name: "--titles-only",
			aliases: ["-t"])
		{
			Description =
				$"Generate 5 title-only suggestions instead of a single title+body message (can also be set via {TITLES_ONLY_ENV_KEY} environment variable)",
		};

		var rootCommand = new RootCommand("Generate AI-powered commit messages from git patches")
		{
			apiKeyOption,
			endpointOption,
			deploymentOption,
			noGitOption,
			clipboardOption,
			titlesOnlyOption,
		};

		rootCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			string? apiKey = parseResult.GetValue(apiKeyOption);
			string? endpoint = parseResult.GetValue(endpointOption);
			string? deployment = parseResult.GetValue(deploymentOption);
			bool isGitAccessDisabled = parseResult.GetValue(noGitOption);
			bool copyToClipboard = parseResult.GetValue(clipboardOption);
			bool titlesOnly = parseResult.GetValue(titlesOnlyOption)
				|| IsEnvFlagSet(Environment.GetEnvironmentVariable(TITLES_ONLY_ENV_KEY));

			try
			{
				string? effectiveApiKey =
					apiKey ?? Environment.GetEnvironmentVariable(AZURE_OPEN_AI_API_KEY_KEY);
				string? effectiveEndpoint =
					endpoint ?? Environment.GetEnvironmentVariable(AZURE_OPEN_AI_ENDPOINT_KEY);

				// Precedence: --deployment flag → env var → built-in default.
				string effectiveDeployment =
					deployment
					?? Environment.GetEnvironmentVariable(AZURE_OPEN_AI_DEPLOYMENT_KEY)
					?? DEFAULT_DEPLOYMENT;

				if (string.IsNullOrEmpty(effectiveApiKey))
				{
					await Console.Error.WriteLineAsync(
						$"Error: Azure OpenAI API key is required. Set {AZURE_OPEN_AI_API_KEY_KEY} environment variable or use --api-key option.");
					Environment.Exit(1);
				}

				if (string.IsNullOrEmpty(effectiveEndpoint))
				{
					await Console.Error.WriteLineAsync(
						$"Error: Azure OpenAI endpoint is required. Set {AZURE_OPEN_AI_ENDPOINT_KEY} environment variable or use --endpoint option.");
					Environment.Exit(1);
				}

				if (string.IsNullOrEmpty(effectiveDeployment))
				{
					await Console.Error.WriteLineAsync(
						$"Error: Azure OpenAI deployment name is required. Set {AZURE_OPEN_AI_DEPLOYMENT_KEY} environment variable or use --deployment option.");
					Environment.Exit(1);
				}

				string patch;

				// Determine input source: stdin vs git diff
				if (isGitAccessDisabled || Console.IsInputRedirected)
				{
					patch = await CommittyService.ReadPatchFromStdinAsync(cancellationToken);
				}
				else
				{
					// Direct git usage (fallback for manual execution)
					patch = await GitService.GetStagedDiffAsync(cancellationToken);
				}

				if (string.IsNullOrWhiteSpace(patch))
				{
					await Console.Error.WriteLineAsync("Error: No patch data available.");
					Environment.Exit(1);
				}

				List<string> suggestions = await RunGenerationAsync(
					effectiveApiKey,
					effectiveEndpoint,
					effectiveDeployment,
					patch,
					titlesOnly,
					cancellationToken);

				// Output suggestions (for hook to capture)
				foreach (string suggestion in suggestions)
				{
					Console.WriteLine(suggestion);
				}

				// Optional clipboard copy for convenience
				if (copyToClipboard && suggestions.Count > 0)
				{
					await CommittyService.CopyToClipboardAsync(
						suggestions[0],
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

		return await rootCommand.Parse(args).InvokeAsync();
	}

	/// <summary>
	/// The git hook entry point. The installed trampoline invokes this with the
	/// path to the commit message file. All guards, mode detection, generation,
	/// and formatting live here so they stay in sync with the binary. This action
	/// never returns a non-zero exit code: a hook failure must not block a commit.
	/// </summary>
	private static Command BuildPrepareCommitMsgCommand()
	{
		var commitMsgFileArgument = new Argument<string>(name: "commit-msg-file")
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

				string? apiKey = Environment.GetEnvironmentVariable(AZURE_OPEN_AI_API_KEY_KEY);
				string? endpoint = Environment.GetEnvironmentVariable(AZURE_OPEN_AI_ENDPOINT_KEY);
				string deployment =
					Environment.GetEnvironmentVariable(AZURE_OPEN_AI_DEPLOYMENT_KEY) ?? DEFAULT_DEPLOYMENT;

				if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(endpoint))
				{
					await AppendLinesAsync(
						commitMsgFile,
						[
							"# Committy: Azure OpenAI configuration incomplete - skipping AI commit message generation",
							"# Required: AZURE_OPENAI_API_KEY, AZURE_OPENAI_ENDPOINT_HOST",
						]);

					return;
				}

				bool titlesOnly = IsEnvFlagSet(Environment.GetEnvironmentVariable(TITLES_ONLY_ENV_KEY));

				List<string> suggestions = await RunGenerationAsync(
					apiKey,
					endpoint,
					deployment,
					patch,
					titlesOnly,
					cancellationToken);

				string existing = File.Exists(commitMsgFile)
					? await File.ReadAllTextAsync(commitMsgFile, cancellationToken)
					: string.Empty;

				string composed = CommitMessageComposer.Compose(suggestions, titlesOnly, existing);

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

	/// <summary>
	/// Installs the trampoline hook, locally or globally. The binary owns the hook
	/// text, so installation works from any directory as long as committy is on PATH.
	/// </summary>
	private static Command BuildInstallHookCommand()
	{
		var globalOption = new Option<bool>(
			name: "--global",
			aliases: ["-g"])
		{
			Description = "Install as a global hook template for all future repositories",
		};
		var repoArgument = new Argument<string?>(name: "repo")
		{
			Description
				= "Repository to install into (defaults to the current directory); ignored with --global",
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

	private static async Task<List<string>> RunGenerationAsync(
		string apiKey,
		string endpoint,
		string deployment,
		string patch,
		bool titlesOnly,
		CancellationToken cancellationToken)
	{
		Http.Initialize(endpoint);

		IHttpService httpService = new HttpService();
		var azureOpenAIService = new AzureOpenAIService(httpService);
		var committyService = new CommittyService(azureOpenAIService);

		return await committyService.GenerateCommitMessageSuggestionsAsync(
			patch,
			apiKey,
			endpoint,
			deployment,
			titlesOnly,
			cancellationToken);
	}

	private static async Task AppendLinesAsync(string file, IEnumerable<string> lines)
	{
		await File.AppendAllTextAsync(file, string.Join('\n', lines) + "\n");
	}

	private static bool IsEnvFlagSet(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		return value.Trim().ToLowerInvariant() switch
		{
			"1" or "true" or "yes" or "on" => true,
			_                              => false,
		};
	}
}
