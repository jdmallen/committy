using System.CommandLine;
using System.Reflection;
using System.Text;

using TextCopy;

namespace JDMallen.Committy;

/// <summary>
/// The root command: generates a commit message from the staged diff (or a
/// patch piped to stdin) and prints it. Also holds the generation helpers the
/// <c>prepare-commit-msg</c> hook shares.
/// </summary>
public static class GenerateCommand
{
	/// <summary>The usage log's directory name under local app data.</summary>
	private const string USAGE_APPLICATION_NAME = "committy";

	public static RootCommand Build()
	{
		Option<string?> providerOption = SharedOptions.Provider(
			"LLM provider to use: azure, anthropic, or openai (overrides config)");
		Option<string?> apiKeyOption = SharedOptions.ApiKey(
			"API key for the selected provider (overrides config and environment)");
		Option<string?> endpointOption = SharedOptions.Endpoint(
			"Azure OpenAI endpoint host URL (omit everything after the domain), or the openai base URL (overrides config)");
		Option<string?> deploymentOption = SharedOptions.Deployment();
		Option<string?> modelOption =
			SharedOptions.Model("Anthropic or openai model name (overrides config)");
		Option<string?> azureAuthOption = SharedOptions.AzureAuth(
			"Azure OpenAI auth for this run: apikey, azurecli or azureidentity (overrides config)");
		Option<string?> tenantOption = SharedOptions.Tenant(
			"Microsoft Entra tenant ID or domain for azurecli or azureidentity auth (overrides config)");
		Option<string?> credentialOption = SharedOptions.Credential(
			"Sign-in for azureidentity auth this run (overrides config)");
		Option<bool> titlesOnlyOption = SharedOptions.TitlesOnly(
			"Generate 5 title-only suggestions instead of a single title+body message");
		var noGitOption = new Option<bool>("--no-git")
		{
			Description =
				"When committy is called with nothing in stdin, it will call `git diff --cached` directly; this option disables that behavior and relies solely on stdin",
		};
		var clipboardOption = new Option<bool>("--clipboard", "-c")
		{
			Description = "Copy first suggestion to clipboard",
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

		rootCommand.SetAction((parseResult, cancellationToken) =>
			CommandErrors.HandleAsync(async () =>
			{
				var overrides = new ConfigOverrides(
					parseResult.GetValue(providerOption),
					parseResult.GetValue(apiKeyOption),
					parseResult.GetValue(endpointOption),
					parseResult.GetValue(deploymentOption),
					parseResult.GetValue(modelOption),
					parseResult.GetValue(azureAuthOption),
					parseResult.GetValue(tenantOption),
					parseResult.GetValue(credentialOption));

				if (!parseResult.GetValue(infoOption))
				{
					return await GenerateAndPrintAsync(
						overrides,
						parseResult.GetValue(titlesOnlyOption),
						parseResult.GetValue(noGitOption),
						parseResult.GetValue(clipboardOption),
						cancellationToken);
				}

				Console.WriteLine(
					await BuildInfoReportAsync(
						overrides,
						cancellationToken));

				return 0;
			},
			cancellationToken));

		return rootCommand;
	}

	private static async Task<int> GenerateAndPrintAsync(
		ConfigOverrides overrides,
		bool titlesOnlyFlag,
		bool isGitAccessDisabled,
		bool copyToClipboard,
		CancellationToken cancellationToken)
	{
		CommittyConfig config = await CommittyConfigResolver.ResolveAsync(
			overrides,
			cancellationToken);

		if (CheckReady(config) is { } error)
		{
			await Console.Error.WriteLineAsync(
				$"Error: {error}",
				cancellationToken);

			return 1;
		}

		string? patch = null;

		if (isGitAccessDisabled || Console.IsInputRedirected)
		{
			patch = await ReadPatchFromStdinAsync(cancellationToken);
		}

		// A GUI client, CI step or `< /dev/null` redirects stdin without
		// sending a patch; read the staged diff unless --no-git says not to.
		if (string.IsNullOrWhiteSpace(patch) && !isGitAccessDisabled)
		{
			patch = await GitService.GetStagedDiffAsync(cancellationToken);
		}

		if (string.IsNullOrWhiteSpace(patch))
		{
			await Console.Error.WriteLineAsync(
				"Error: No patch data available.",
				cancellationToken);

			return 1;
		}

		CommitSuggestions suggestions = await GenerateAsync(
			config,
			patch,
			titlesOnlyFlag || config.TitlesOnly,
			true,
			cancellationToken);
		RecordUsage(suggestions.Usage);

		if (suggestions.Messages.Count == 0)
		{
			await Console.Error.WriteLineAsync(
				$"Error: {CommitMessageParser.NO_MESSAGE}",
				cancellationToken);

			return 1;
		}

		foreach (string suggestion in suggestions.Messages)
		{
			Console.WriteLine(suggestion);
		}

		if (copyToClipboard)
		{
			await CopyToClipboardAsync(
				suggestions.Messages[0],
				cancellationToken);
		}

		return 0;
	}

	/// <summary>
	/// Builds the transport, client, and generator for the resolved config and
	/// runs one generation. The transport is built here rather than once at
	/// startup because its timeout is provider-dependent: a self-hosted model may
	/// need minutes where a hosted one needs seconds, and a single invocation
	/// makes exactly one completion call.
	/// </summary>
	public static async Task<CommitSuggestions> GenerateAsync(
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
			config.MaxTokensOverride,
			config.TimeoutSeconds,
			config.TimeoutSettingKey);

		return await generator.GenerateAsync(
			patch,
			titlesOnly,
			cancellationToken);
	}

	/// <summary>
	/// The resolved config's problem, if any, including an Azure CLI that
	/// cannot be found from the PATH committy runs with.
	/// </summary>
	public static string? CheckReady(CommittyConfig config) =>
		config.Validate()
		?? (config.Provider is AzureConfig azure
			? AzureCli.Problem(azure, Environment.GetEnvironmentVariable)
			: null);

	/// <summary>
	/// Adds one run's tokens to the lifetime totals. A failure is reported on
	/// stderr and otherwise ignored: the suggestions matter more than the
	/// count.
	/// </summary>
	public static void RecordUsage(TokenUsage? usage)
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

	/// <summary>
	/// Copies <paramref name="text" /> to the clipboard. A missing clipboard
	/// (e.g. no <c>xsel</c> on Linux) is a warning, never a failure.
	/// </summary>
	public static async Task CopyToClipboardAsync(
		string text,
		CancellationToken cancellationToken = default)
	{
		try
		{
			await ClipboardService.SetTextAsync(text, cancellationToken);
		}
		catch (Exception)
		{
			await Console.Error.WriteLineAsync(
				"Warning: Clipboard functionality not available on this system. If running Linux, try installing `xsel` package.",
				cancellationToken);
		}
	}

	public static async Task<string> ReadPatchFromStdinAsync(
		CancellationToken cancellationToken = default)
	{
		if (!Console.IsInputRedirected)
		{
			throw new InvalidOperationException(
				"No input data available. Please pipe git patch data to stdin.");
		}

		// A piped patch comes from git, which writes UTF-8; the console's code
		// page (often not UTF-8 on Windows) would garble non-ASCII text. A
		// UTF-16 byte order mark, as Windows PowerShell can send, still wins.
		using var reader = new StreamReader(
			Console.OpenStandardInput(),
			Encoding.UTF8);

		return await reader.ReadToEndAsync(cancellationToken);
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
		$"committy {typeof(GenerateCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}";
}
