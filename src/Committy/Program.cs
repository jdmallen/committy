using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Committy;

internal class Program
{
	private static async Task<int> Main(string[] args)
	{
		var apiKeyOption = new Option<string?>(
			aliases: ["--api-key", "-k"],
			description:
			"Azure OpenAI API key (can also be set via AZURE_OPENAI_API_KEY environment variable)");
		var endpointOption = new Option<string?>(
			aliases: ["--endpoint", "-e"],
			description:
			"Azure OpenAI endpoint URL (can also be set via AZURE_OPENAI_ENDPOINT environment variable)");
		var deploymentOption = new Option<string?>(
			aliases: ["--deployment", "-d"],
			description:
			"Azure OpenAI deployment name (can also be set via AZURE_OPENAI_DEPLOYMENT environment variable)");
		var stdinOption = new Option<bool>(
			aliases: ["--stdin"],
			description: "Read patch from stdin instead of git diff --cached (default when no args)");
		var clipboardOption = new Option<bool>(
			aliases: ["--clipboard", "-c"],
			description: "Copy first suggestion to clipboard");

		var rootCommand = new RootCommand("Generate AI-powered commit messages from git patches")
		{
			apiKeyOption,
			endpointOption,
			deploymentOption,
			stdinOption,
			clipboardOption
		};

		rootCommand.SetHandler(
			async (InvocationContext context) =>
			{
				var apiKey = context.ParseResult.GetValueForOption(apiKeyOption);
				var endpoint = context.ParseResult.GetValueForOption(endpointOption);
				var deployment = context.ParseResult.GetValueForOption(deploymentOption);
				var useStdin = context.ParseResult.GetValueForOption(stdinOption);
				var copyToClipboard = context.ParseResult.GetValueForOption(clipboardOption);
				var cancellationToken = context.GetCancellationToken();

				// Direct instantiation for better CLI performance
				using var httpClient = new HttpClient();
				var azureOpenAIService = new AzureOpenAIService(httpClient);
				var committyService = new CommittyService(azureOpenAIService);

				try
				{
					string? effectiveApiKey =
						apiKey ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
					string? effectiveEndpoint =
						endpoint ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
					string? effectiveDeployment =
						deployment ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");

					if (string.IsNullOrEmpty(effectiveApiKey))
					{
						await Console.Error.WriteLineAsync(
							"Error: Azure OpenAI API key is required. Set AZURE_OPENAI_API_KEY environment variable or use --api-key option.");
						Environment.Exit(1);
					}

					if (string.IsNullOrEmpty(effectiveEndpoint))
					{
						await Console.Error.WriteLineAsync(
							"Error: Azure OpenAI endpoint is required. Set AZURE_OPENAI_ENDPOINT environment variable or use --endpoint option.");
						Environment.Exit(1);
					}

					if (string.IsNullOrEmpty(effectiveDeployment))
					{
						await Console.Error.WriteLineAsync(
							"Error: Azure OpenAI deployment name is required. Set AZURE_OPENAI_DEPLOYMENT environment variable or use --deployment option.");
						Environment.Exit(1);
					}

					string patch;

					// Determine input source: stdin vs git diff
					if (useStdin || Console.IsInputRedirected)
					{
						patch = await committyService.ReadPatchFromStdinAsync(cancellationToken);
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
						await committyService.CopyToClipboardAsync(suggestions[0], cancellationToken);
					}
				}
				catch (OperationCanceledException)
				{
					// Graceful shutdown on cancellation
					Environment.Exit(130); // Standard exit code for SIGINT
				}
				catch (Exception ex)
				{
					await Console.Error.WriteLineAsync($"Error: {ex.Message}");
					Environment.Exit(1);
				}
			});


		return await rootCommand.InvokeAsync(args);
	}
}
