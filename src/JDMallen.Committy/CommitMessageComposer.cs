using System.Globalization;
using System.Text;

namespace JDMallen.Committy;

/// <summary>
/// Builds the contents of the prepare-commit-msg file from the suggestions the
/// binary generated. This is the presentation logic that previously lived in the
/// bash hook; keeping it in the binary means it can evolve without re-installing
/// per-repo hooks.
/// </summary>
public static class CommitMessageComposer
{
	private const string TITLES_MARKER =
		"# 🡅 Suggested commit messages from Committy (uncomment and edit as needed)🡅:";

	private const string TITLES_FOOTER =
		"# Or write your own commit message above";

	/// <summary>
	/// The line <c>git commit --verbose</c> puts above the diff. Git ignores
	/// everything after it, so the token count goes above it.
	/// </summary>
	private const string SCISSORS_LINE =
		"# ------------------------ >8 ------------------------";

	/// <summary>
	/// Produces the new commit-message file contents.
	/// </summary>
	/// <param name="suggestions">
	/// In titles-only mode, the title suggestions (one per line). Otherwise the
	/// single title+body message as the first element.
	/// </param>
	/// <param name="titlesOnly">Whether titles-only mode is active.</param>
	/// <param name="existingContent">
	/// The current contents of the commit message file
	/// (git's template).
	/// </param>
	/// <param name="usage">
	/// The tokens the suggestions took, written as a comment at the bottom; null
	/// (the provider didn't report usage) writes nothing.
	/// </param>
	public static string Compose(
		IReadOnlyList<string> suggestions,
		bool titlesOnly,
		string existingContent,
		TokenUsage? usage = null)
	{
		string existing = NormalizeNewlines(existingContent).TrimEnd('\n');
		var scissors = string.Empty;
		int scissorsAt =
			existing.StartsWith(SCISSORS_LINE, StringComparison.Ordinal)
				? 0
				: existing.IndexOf(
					$"\n{SCISSORS_LINE}",
					StringComparison.Ordinal);
		if (usage is not null && scissorsAt >= 0)
		{
			scissors = existing[scissorsAt..].TrimStart('\n');
			existing = existing[..scissorsAt].TrimEnd('\n');
		}

		var sb = new StringBuilder();

		if (titlesOnly)
		{
			// Commented-suggestion UX: insert commented titles above git's template
			// so the user can uncomment the one they want.
			sb.Append('\n');

			foreach (string line in suggestions.SelectMany(suggestion
				         => NormalizeNewlines(suggestion).Split('\n')))
			{
				sb.Append("# ").Append(line).Append('\n');
			}

			sb.Append(TITLES_MARKER).Append('\n');
			sb.Append("#\n");
			sb.Append(existing).Append('\n');
			sb.Append("#\n");
			sb.Append(TITLES_FOOTER).Append('\n');
		}
		else
		{
			// Pre-fill the single title+body message above git's template so the
			// user can edit or accept it.
			string message =
				suggestions.Count > 0 ? suggestions[0] : string.Empty;

			sb.Append(NormalizeNewlines(message).TrimEnd('\n')).Append('\n');
			sb.Append('\n');
			sb.Append(existing).Append('\n');
		}

		if (usage is not null)
		{
			sb.Append(UsageComment(usage)).Append('\n');
		}

		if (scissors.Length > 0)
		{
			sb.Append(scissors).Append('\n');
		}

		return sb.ToString();
	}

	/// <summary>The token-count comment, e.g. "# Committy: 1,234 tokens (…)".</summary>
	private static string UsageComment(TokenUsage usage) =>
		string.Create(
			CultureInfo.CurrentCulture,
			$"# Committy: {usage.TotalTokens:N0} tokens ({usage.InputTokens:N0} in, {usage.OutputTokens:N0} out)");

	private static string NormalizeNewlines(string value) =>
		value.Replace("\r\n", "\n").Replace("\r", "\n");
}
