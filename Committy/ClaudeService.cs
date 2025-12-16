using System.Text;
using System.Text.Json;

namespace Committy;

public class ClaudeService
{
	private readonly HttpClient _httpClient;

	public ClaudeService(HttpClient httpClient)
	{
		_httpClient = httpClient;
	}

	public async Task<List<string>> GenerateCommitMessageSuggestionsAsync(string patch, string apiKey)
	{
		_httpClient.DefaultRequestHeaders.Authorization = new("Bearer", apiKey);

		var prompt = BuildPrompt(patch);
		
		var request = new
		{
			model = "claude-3-5-sonnet-20241022",
			max_tokens = 400,
			messages = new[]
			{
				new { role = "user", content = prompt }
			}
		};

		var json = JsonSerializer.Serialize(request, new JsonSerializerOptions 
		{ 
			PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower 
		});

		var content = new StringContent(json, Encoding.UTF8, "application/json");
		
		var response = await _httpClient.PostAsync("v1/messages", content);
		
		if (!response.IsSuccessStatusCode)
		{
			var errorContent = await response.Content.ReadAsStringAsync();
			throw new HttpRequestException($"Claude API request failed: {response.StatusCode} - {errorContent}");
		}

		var responseContent = await response.Content.ReadAsStringAsync();
		var responseObj = JsonSerializer.Deserialize<JsonElement>(responseContent);
		
		var messageContent = responseObj
			.GetProperty("content")[0]
			.GetProperty("text")
			.GetString();

		var suggestions = ParseSuggestions(messageContent?.Trim() ?? "feat: implement changes");
		return suggestions;
	}

	private static string BuildPrompt(string patch)
	{
		var sb = new StringBuilder();
		sb.AppendLine("Generate exactly 5 different commit messages following Conventional Commits v1.0.0 specification.");
		sb.AppendLine();
		sb.AppendLine("FORMAT: <type>[optional scope]: <description>");
		sb.AppendLine();
		sb.AppendLine("TYPES:");
		sb.AppendLine("- feat: new feature");
		sb.AppendLine("- fix: bug fix");
		sb.AppendLine("- docs: documentation");
		sb.AppendLine("- style: code style/formatting");
		sb.AppendLine("- refactor: code refactoring");
		sb.AppendLine("- perf: performance improvement");
		sb.AppendLine("- test: adding/updating tests");
		sb.AppendLine("- build: build system changes");
		sb.AppendLine("- ci: CI configuration");
		sb.AppendLine("- chore: maintenance tasks");
		sb.AppendLine();
		sb.AppendLine("RULES:");
		sb.AppendLine("1. Use imperative mood: 'add' not 'adds' or 'added'");
		sb.AppendLine("2. No period at end");
		sb.AppendLine("3. Keep under 50 characters when possible");
		sb.AppendLine("4. Add scope when it clarifies context");
		sb.AppendLine("5. Use ! for breaking changes: feat!: or feat(api)!:");
		sb.AppendLine();
		sb.AppendLine("EXAMPLES:");
		sb.AppendLine("feat(auth): add OAuth2 integration");
		sb.AppendLine("fix(api): prevent memory leak in parser");
		sb.AppendLine("docs: update installation guide");
		sb.AppendLine("perf(db): optimize query performance");
		sb.AppendLine("feat!: remove deprecated login API");
		sb.AppendLine();
		sb.AppendLine("Git patch:");
		sb.AppendLine("```");
		sb.AppendLine(patch);
		sb.AppendLine("```");
		sb.AppendLine();
		sb.AppendLine("Return exactly 5 commit messages, one per line, no numbering or bullets:");

		return sb.ToString();
	}

	private static List<string> ParseSuggestions(string response)
	{
		var lines = response.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
			.Select(line => line.Trim())
			.Where(line => !string.IsNullOrEmpty(line))
			.ToList();

		if (lines.Count >= 5)
		{
			return lines.Take(5).ToList();
		}

		var suggestions = lines.ToList();
		
		while (suggestions.Count < 5)
		{
			suggestions.Add($"feat: implement changes ({suggestions.Count + 1})");
		}

		return suggestions;
	}
}