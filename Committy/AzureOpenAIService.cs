using System.Text;
using System.Text.Json;

namespace Committy;

public class AzureOpenAIService
{
	private readonly HttpClient _httpClient;

	public AzureOpenAIService(HttpClient httpClient)
	{
		_httpClient = httpClient;
	}

	public async Task<string> GenerateCommitMessageAsync(string patch, string apiKey, string endpoint, string deploymentName)
	{
		_httpClient.DefaultRequestHeaders.Clear();
		_httpClient.DefaultRequestHeaders.Add("api-key", apiKey);

		var prompt = BuildPrompt(patch);

		var request = new
		{
			messages = new[]
			{
				new { role = "system", content = "You are a helpful assistant that generates conventional commit messages." },
				new { role = "user", content = prompt }
			},
			max_tokens = 200,
			temperature = 0.1,
			top_p = 1.0,
			frequency_penalty = 0,
			presence_penalty = 0
		};

		var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
		});

		var content = new StringContent(json, Encoding.UTF8, "application/json");

		var requestUrl = $"{endpoint.TrimEnd('/')}/openai/deployments/{deploymentName}/chat/completions?api-version=2024-02-15-preview";
		
		var response = await _httpClient.PostAsync(requestUrl, content);

		if (!response.IsSuccessStatusCode)
		{
			var errorContent = await response.Content.ReadAsStringAsync();
			throw new HttpRequestException($"Azure OpenAI API request failed: {response.StatusCode} - {errorContent}");
		}

		var responseContent = await response.Content.ReadAsStringAsync();
		var responseObj = JsonSerializer.Deserialize<JsonElement>(responseContent);

		var messageContent = responseObj
			.GetProperty("choices")[0]
			.GetProperty("message")
			.GetProperty("content")
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