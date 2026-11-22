using System.Text.RegularExpressions;

namespace JDMallen.Committy;

/// <summary>
/// Turns the model's raw completion text into the list of suggestions committy
/// emits. Provider-agnostic: it depends only on the output mode, not on which
/// LLM produced the text. An empty or unusable response gives an empty list,
/// never a made-up message, so a commit can't pick one up by accident.
/// </summary>
public static partial class CommitMessageParser
{
	/// <summary>The most title suggestions shown in titles-only mode.</summary>
	public const int MAX_TITLES = 5;

	/// <summary>
	/// What the callers report when <see cref="Parse" /> finds no message.
	/// </summary>
	public const string NO_MESSAGE =
		"The model returned no message. A reasoning model may have spent its whole token budget thinking; raising its max tokens setting can help.";

	public static List<string> Parse(string? raw, bool titlesOnly)
	{
		string normalized = (raw ?? string.Empty).Replace("\r\n", "\n").Trim();

		if (normalized.Length == 0)
		{
			return [];
		}

		return titlesOnly ? ParseTitleSuggestions(normalized) : [normalized];
	}

	/// <summary>
	/// One title per line, without the list markers (<c>1.</c>, <c>-</c>,
	/// <c>*</c>) and code fences models add despite the prompt, capped at
	/// <see cref="MAX_TITLES" />.
	/// </summary>
	private static List<string> ParseTitleSuggestions(string response) =>
	[
		.. response
			.Split('\n')
			.Select(line => line.Trim())
			.Where(line => !line.StartsWith("```", StringComparison.Ordinal))
			.Select(line => ListMarker().Replace(line, string.Empty).Trim())
			.Where(line => line.Length > 0)
			.Take(MAX_TITLES),
	];

	[GeneratedRegex(@"^(?:[-*+•]|\d{1,2}[.)])\s+")]
	private static partial Regex ListMarker();
}
