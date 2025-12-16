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

	public async Task<string> GenerateCommitMessageAsync(string patch, string apiKey)
	{
		_httpClient.DefaultRequestHeaders.Authorization = new("Bearer", apiKey);

		var prompt = BuildPrompt(patch);
		
		var request = new
		{
			model = "claude-3-5-sonnet-20241022",
			max_tokens = 200,
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

		return messageContent?.Trim() ?? "feat: implement changes";
	}

	private static string BuildPrompt(string patch)
	{
		return $@"Generate a concise, conventional commit message for the following git patch. Follow these guidelines:

1. Use conventional commit format: type(scope): description
2. Common types: feat, fix, docs, style, refactor, test, chore
3. Keep description under 50 characters when possible
4. Focus on the 'what' and 'why', not the 'how'
5. Use imperative mood (""add"" not ""adds"" or ""added"")
6. Don't end with a period

Examples:
- feat(auth): add OAuth2 login support
- fix(api): handle null response in user service
- docs: update API documentation
- refactor: extract validation logic

Git patch:
```
{patch}
```

Return only the commit message, nothing else.";
	}
}