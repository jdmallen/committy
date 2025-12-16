using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Committy;

internal class Program
{
	private static async Task<int> Main(string[] args)
	{
		var rootCommand = new RootCommand("Generate AI-powered commit messages from git patches")
		{
			new Option<string?>(
				aliases: ["--api-key", "-k"],
				description: "Azure OpenAI API key (can also be set via AZURE_OPENAI_API_KEY environment variable)"),
			new Option<string?>(
				aliases: ["--endpoint", "-e"],
				description: "Azure OpenAI endpoint URL (can also be set via AZURE_OPENAI_ENDPOINT environment variable)"),
			new Option<string?>(
				aliases: ["--deployment", "-d"],
				description: "Azure OpenAI deployment name (can also be set via AZURE_OPENAI_DEPLOYMENT environment variable)"),
			new Option<bool>(
				aliases: ["--stdin"],
				description: "Read patch from stdin instead of git diff --cached (default when no args)"),
			new Option<bool>(
				aliases: ["--clipboard", "-c"],
				description: "Copy first suggestion to clipboard")
		};

		rootCommand.SetHandler(async (string? apiKey, string? endpoint, string? deployment, bool useStdin, bool copyToClipboard) =>
		{
			var builder = Host.CreateApplicationBuilder();
			
			builder.Services.AddHttpClient<AzureOpenAIService>();
			builder.Services.AddSingleton<IAzureOpenAIService, AzureOpenAIService>();
			builder.Services.AddSingleton<CommittyService>();
			builder.Services.AddSingleton<GitService>();

			var host = builder.Build();

			var committyService = host.Services.GetRequiredService<CommittyService>();
			var gitService = host.Services.GetRequiredService<GitService>();

			try
			{
				var effectiveApiKey = apiKey ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
				var effectiveEndpoint = endpoint ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
				var effectiveDeployment = deployment ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");
				
				if (string.IsNullOrEmpty(effectiveApiKey))
				{
					Console.Error.WriteLine("Error: Azure OpenAI API key is required. Set AZURE_OPENAI_API_KEY environment variable or use --api-key option.");
					Environment.Exit(1);
				}

				if (string.IsNullOrEmpty(effectiveEndpoint))
				{
					Console.Error.WriteLine("Error: Azure OpenAI endpoint is required. Set AZURE_OPENAI_ENDPOINT environment variable or use --endpoint option.");
					Environment.Exit(1);
				}

				if (string.IsNullOrEmpty(effectiveDeployment))
				{
					Console.Error.WriteLine("Error: Azure OpenAI deployment name is required. Set AZURE_OPENAI_DEPLOYMENT environment variable or use --deployment option.");
					Environment.Exit(1);
				}

				string patch;

				// Determine input source: stdin vs git diff
				if (useStdin || Console.IsInputRedirected)
				{
					patch = await committyService.ReadPatchFromStdinAsync();
				}
				else
				{
					// Direct git usage (fallback for manual execution)
					patch = await gitService.GetStagedDiffAsync();
				}
				
				if (string.IsNullOrWhiteSpace(patch))
				{
					Console.Error.WriteLine("Error: No patch data available.");
					Environment.Exit(1);
				}

				var suggestions = await committyService.GenerateCommitMessageSuggestionsAsync(patch, effectiveApiKey, effectiveEndpoint, effectiveDeployment);
				
				// Output suggestions (for hook to capture)
				foreach (var suggestion in suggestions)
				{
					Console.WriteLine(suggestion);
				}

				// Optional clipboard copy for convenience
				if (copyToClipboard && suggestions.Count > 0)
				{
					await committyService.CopyToClipboardAsync(suggestions[0]);
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Error: {ex.Message}");
				Environment.Exit(1);
			}
		}, new Option<string?>("--api-key"), new Option<string?>("--endpoint"), new Option<string?>("--deployment"), new Option<bool>("--stdin"), new Option<bool>("--clipboard"));

		return await rootCommand.InvokeAsync(args);
	}
}