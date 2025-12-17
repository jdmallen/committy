using System.CommandLine;
using JetBrains.Annotations;

namespace Committy;

[UsedImplicitly]
internal class Program
{
	private const string AzureOpenAIApiKeyKey = "AZURE_OPENAI_API_KEY";
	private const string AzureOpenAIEndpointKey = "AZURE_OPENAI_ENDPOINT_HOST";
	private const string AzureOpenAIDeploymentKey = "AZURE_OPENAI_DEPLOYMENT";

	private static async Task<int> Main(string[] args)
	{
		var apiKeyOption = new Option<string?>(
			name: "--api-key",
			aliases: ["-k"])
		{
			Description =
				$"Azure OpenAI API key (can also be set via {AzureOpenAIApiKeyKey} environment variable)",
			HelpName = "API key",
		};
		var endpointOption = new Option<string?>(
			name: "--endpoint",
			aliases: ["-e"])
		{
			Description =
				$"Azure OpenAI endpoint host URL (can also be set via {AzureOpenAIEndpointKey} environment variable); omit everything after the domain",
			HelpName = "endpoint URL",
		};
		var deploymentOption = new Option<string?>(
			name: "--deployment",
			aliases: ["-d"])
		{
			DefaultValueFactory = _ => "gpt-4.1-mini",
			Description =
				$"Azure OpenAI deployment name (can also be set via {AzureOpenAIDeploymentKey} environment variable)",
			HelpName = "deployment name",
		};
		var noGitOption = new Option<bool>(name: "--no-git")
		{
			Description = "When committy is called with nothing in stdin, it will call `git diff --cached` directly; this option disables that behavior and relies solely on stdin",
		};
		var clipboardOption = new Option<bool>(
			name: "--clipboard",
			aliases: ["-c"]) { Description = "Copy first suggestion to clipboard", };

		var rootCommand = new RootCommand("Generate AI-powered commit messages from git patches")
		{
			apiKeyOption,
			endpointOption,
			deploymentOption,
			noGitOption,
			clipboardOption,
		};

		rootCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			string? apiKey = parseResult.GetValue(apiKeyOption);
			string? endpoint = parseResult.GetValue(endpointOption);
			string? deployment = parseResult.GetValue(deploymentOption);
			bool isGitAccessDisabled = parseResult.GetValue(noGitOption);
			bool copyToClipboard = parseResult.GetValue(clipboardOption);

			try
			{
				string? effectiveApiKey =
					apiKey ?? Environment.GetEnvironmentVariable(AzureOpenAIApiKeyKey);
				string? effectiveEndpoint =
					endpoint ?? Environment.GetEnvironmentVariable(AzureOpenAIEndpointKey);
				string? effectiveDeployment =
					deployment ?? Environment.GetEnvironmentVariable(AzureOpenAIDeploymentKey);

				if (string.IsNullOrEmpty(effectiveApiKey))
				{
					await Console.Error.WriteLineAsync(
						$"Error: Azure OpenAI API key is required. Set {AzureOpenAIApiKeyKey} environment variable or use --api-key option.");
					Environment.Exit(1);
				}

				if (string.IsNullOrEmpty(effectiveEndpoint))
				{
					await Console.Error.WriteLineAsync(
						$"Error: Azure OpenAI endpoint is required. Set {AzureOpenAIEndpointKey} environment variable or use --endpoint option.");
					Environment.Exit(1);
				}

				// Initialize client
				Http.Initialize(effectiveEndpoint);

				if (string.IsNullOrEmpty(effectiveDeployment))
				{
					await Console.Error.WriteLineAsync(
						$"Error: Azure OpenAI deployment name is required. Set {AzureOpenAIDeploymentKey} environment variable or use --deployment option.");
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

				IHttpService httpService = new HttpService();
				var azureOpenAIService = new AzureOpenAIService(httpService);
				var committyService = new CommittyService(azureOpenAIService);

				List<string> suggestions = await committyService.GenerateCommitMessageSuggestionsAsync(
					patch,
					effectiveApiKey,
					effectiveEndpoint,
					effectiveDeployment,
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

		return await rootCommand.Parse(args).InvokeAsync();
	}
}
