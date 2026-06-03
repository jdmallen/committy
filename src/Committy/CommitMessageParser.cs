namespace Committy;

/// <summary>
/// Turns the model's raw completion text into the list of suggestions committy
/// emits.
/// Provider-agnostic: it depends only on the output mode, not on which LLM
/// produced
/// the text. Owns the fallbacks for empty or under-filled responses.
/// </summary>
public static class CommitMessageParser
{
	private const string FALLBACK = "feat: implement changes";

	public static List<string> Parse(string? raw, bool titlesOnly)
	{
		string trimmed = (raw ?? string.Empty).Trim();

		return titlesOnly
			? ParseTitleSuggestions(string.IsNullOrEmpty(trimmed) ? FALLBACK : trimmed)
			: ParseTitleAndBody(trimmed);
	}

	private static List<string> ParseTitleAndBody(string response)
	{
		string normalized = response.Replace("\r\n", "\n").Trim();

		return string.IsNullOrEmpty(normalized)
			? [FALLBACK]
			: [normalized];
	}

	private static List<string> ParseTitleSuggestions(string response)
	{
		var suggestions = new List<string>(5);
		string[] lines = response.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

		suggestions.AddRange(
			lines
				.Select(line => line.Trim())
				.Where(trimmed => !string.IsNullOrEmpty(trimmed)));

		while (suggestions.Count < 5)
		{
			suggestions.Add($"{FALLBACK} ({suggestions.Count + 1})");
		}

		return suggestions;
	}
}
