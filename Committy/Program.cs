using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TextCopy;

namespace Committy;

internal class Program
{
	private static async Task<int> Main(string[] args)
	{
		var rootCommand = new RootCommand("Generate AI-powered commit messages from git patches")
		{
			new Option<string?>(
				aliases: ["--api-key", "-k"],
				description: "Claude API key (can also be set via CLAUDE_API_KEY environment variable)")
		};

		rootCommand.SetHandler(async (string? apiKey) =>
		{
			var builder = Host.CreateApplicationBuilder();
			
			builder.Services.AddHttpClient<ClaudeService>(client =>
			{
				client.BaseAddress = new Uri("https://api.anthropic.com/");
				client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
			});

			builder.Services.AddSingleton<CommitMessageGenerator>();
			builder.Services.AddSingleton<PatchReader>();

			var host = builder.Build();

			var generator = host.Services.GetRequiredService<CommitMessageGenerator>();
			var patchReader = host.Services.GetRequiredService<PatchReader>();

			try
			{
				var effectiveApiKey = apiKey ?? Environment.GetEnvironmentVariable("CLAUDE_API_KEY");
				
				if (string.IsNullOrEmpty(effectiveApiKey))
				{
					Console.Error.WriteLine("Error: Claude API key is required. Set CLAUDE_API_KEY environment variable or use --api-key option.");
					Environment.Exit(1);
				}

				var patch = await patchReader.ReadPatchFromStdinAsync();
				
				if (string.IsNullOrWhiteSpace(patch))
				{
					Console.Error.WriteLine("Error: No patch data received from stdin.");
					Environment.Exit(1);
				}

				var commitMessage = await generator.GenerateCommitMessageAsync(patch, effectiveApiKey);
				
				Console.WriteLine(commitMessage);
				
				await ClipboardService.SetTextAsync(commitMessage);
				Console.Error.WriteLine("✓ Commit message copied to clipboard");
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Error: {ex.Message}");
				Environment.Exit(1);
			}
		}, new Option<string?>("--api-key"));

		return await rootCommand.InvokeAsync(args);
	}
}