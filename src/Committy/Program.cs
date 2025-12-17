using System.CommandLine;
using JetBrains.Annotations;

namespace Committy;

[UsedImplicitly]
internal class Program
{
	private static async Task<int> Main(string[] args)
	{
		var apiKeyOption = new Option<string?>(
			name: "--api-key",
			aliases: ["-k"])
		{
			Description =
				"Azure OpenAI API key (can also be set via AZURE_OPENAI_API_KEY environment variable)",
			HelpName = "API key",
		};
		var endpointOption = new Option<string?>(
			name: "--endpoint",
			aliases: ["-e"])
		{
			Description =
				"Azure OpenAI endpoint URL (can also be set via AZURE_OPENAI_ENDPOINT environment variable)",
			HelpName = "endpoint URL",
		};
		var deploymentOption = new Option<string?>(
			name: "--deployment",
			aliases: ["-d"])
		{
			DefaultValueFactory = _ => "gpt-4.1-mini",
			Description =
				"Azure OpenAI deployment name (can also be set via AZURE_OPENAI_DEPLOYMENT environment variable)",
			HelpName = "deployment name",
		};
		var stdinOption = new Option<bool>(name: "--stdin")
		{
			Description = "Read patch from stdin instead of `git diff --cached` (default when no args)",
		};
		var clipboardOption = new Option<bool>(
			name: "--clipboard",
			aliases: ["-c"]) { Description = "Copy first suggestion to clipboard", };

		var rootCommand = new RootCommand("Generate AI-powered commit messages from git patches")
		{
			apiKeyOption,
			endpointOption,
			deploymentOption,
			stdinOption,
			clipboardOption,
		};

		rootCommand.SetAction(async (parseResult, cancellationToken) =>
		{
			string? apiKey = parseResult.GetValue(apiKeyOption);
			string? endpoint = parseResult.GetValue(endpointOption);
			string? deployment = parseResult.GetValue(deploymentOption);
			bool useStdin = parseResult.GetValue(stdinOption);
			bool copyToClipboard = parseResult.GetValue(clipboardOption);

			var azureOpenAIService = new AzureOpenAIService();
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
					patch = await CommittyService.ReadPatchFromStdinAsync(cancellationToken);
				}
				else
				{
					// Direct git usage (fallback for manual execution)
					patch = await GitService.GetStagedDiffAsync(cancellationToken);

#if DEBUG
					if (string.IsNullOrWhiteSpace(patch))
					{
						// TEMP file read for debugging
						patch = await File.ReadAllTextAsync(
							@"~/git/hackathon25/test.diff",
							cancellationToken);
					}
#endif
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
					// call a copy to clipboard method
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
