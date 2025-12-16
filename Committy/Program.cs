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
				description: "Claude API key (can also be set via CLAUDE_API_KEY environment variable)"),
			new Option<bool>(
				aliases: ["--stdin"],
				description: "Read patch from stdin instead of git diff --cached (default when no args)"),
			new Option<bool>(
				aliases: ["--clipboard", "-c"],
				description: "Copy first suggestion to clipboard")
		};

		rootCommand.SetHandler(async (string? apiKey, bool useStdin, bool copyToClipboard) =>
		{
			var builder = Host.CreateApplicationBuilder();
			
			builder.Services.AddHttpClient<ClaudeService>(client =>
			{
				client.BaseAddress = new Uri("https://api.anthropic.com/");
				client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
			});

			builder.Services.AddSingleton<CommittyService>();
			builder.Services.AddSingleton<GitService>();

			var host = builder.Build();

			var committyService = host.Services.GetRequiredService<CommittyService>();
			var gitService = host.Services.GetRequiredService<GitService>();

			try
			{
				var effectiveApiKey = apiKey ?? Environment.GetEnvironmentVariable("CLAUDE_API_KEY");
				
				if (string.IsNullOrEmpty(effectiveApiKey))
				{
					Console.Error.WriteLine("Error: Claude API key is required. Set CLAUDE_API_KEY environment variable or use --api-key option.");
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

				var suggestions = await committyService.GenerateCommitMessageSuggestionsAsync(patch, effectiveApiKey);
				
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
		}, new Option<string?>("--api-key"), new Option<bool>("--stdin"), new Option<bool>("--clipboard"));

		return await rootCommand.InvokeAsync(args);
	}
}
