namespace Committy;

public interface IAzureOpenAIService
{
	Task<List<string>> GenerateCommitMessageSuggestionsAsync(
		string patch,
		string apiKey,
		string endpoint,
		string deploymentName,
		bool titlesOnly = false,
		CancellationToken cancellationToken = default);
}
