using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Committy;

internal class Program
{
	private static async Task<int> Main(string[] args)
	{
		var rootCommand = new RootCommand("Generate AI-powered commit messages from git staged changes")
		{
			new Option<string?>(
				aliases: ["--api-key", "-k"],
				description: "Claude API key (can also be set via CLAUDE_API_KEY environment variable)"),
			new Option<bool>(
				aliases: ["--no-commit", "-n"],
				description: "Generate commit message but don't commit (just copy to clipboard)")
		};

		rootCommand.SetHandler(async (string? apiKey, bool noCommit) =>
		{
			var builder = Host.CreateApplicationBuilder();
			
			builder.Services.AddHttpClient<ClaudeService>(client =>
			{
				client.BaseAddress = new Uri("https://api.anthropic.com/");
				client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
			});

			builder.Services.AddSingleton<CommitMessageGenerator>();
			builder.Services.AddSingleton<GitService>();

			var host = builder.Build();

			var generator = host.Services.GetRequiredService<CommitMessageGenerator>();
			var gitService = host.Services.GetRequiredService<GitService>();

			try
			{
				if (!await gitService.IsGitRepositoryAsync())
				{
					Console.Error.WriteLine("Error: Not a git repository.");
					Environment.Exit(1);
				}

				if (!await gitService.HasStagedChangesAsync())
				{
					Console.Error.WriteLine("Error: No staged changes found. Use 'git add' to stage files for commit.");
					Environment.Exit(1);
				}

				var effectiveApiKey = apiKey ?? Environment.GetEnvironmentVariable("CLAUDE_API_KEY");
				
				if (string.IsNullOrEmpty(effectiveApiKey))
				{
					Console.Error.WriteLine("Error: Claude API key is required. Set CLAUDE_API_KEY environment variable or use --api-key option.");
					Environment.Exit(1);
				}

				Console.WriteLine("Reading staged changes...");
				var patch = await gitService.GetStagedDiffAsync();
				
				UserInterface.ShowStagedChanges(patch);

				Console.WriteLine("Generating commit message suggestions...");
				var suggestions = await generator.GenerateCommitMessageSuggestionsAsync(patch, effectiveApiKey);
				
				var selectedMessage = UserInterface.SelectCommitMessage(suggestions);
				
				if (noCommit)
				{
					Console.WriteLine($"Commit message: {selectedMessage}");
					await TextCopy.ClipboardService.SetTextAsync(selectedMessage);
					Console.WriteLine("✓ Commit message copied to clipboard");
					return;
				}

				if (UserInterface.ConfirmCommit(selectedMessage))
				{
					await gitService.CommitAsync(selectedMessage);
					Console.WriteLine("✓ Changes committed successfully!");
				}
				else
				{
					Console.WriteLine("Commit cancelled.");
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Error: {ex.Message}");
				Environment.Exit(1);
			}
		}, new Option<string?>("--api-key"), new Option<bool>("--no-commit"));

		return await rootCommand.InvokeAsync(args);
	}
}