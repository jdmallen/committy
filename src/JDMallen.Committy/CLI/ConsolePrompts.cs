namespace JDMallen.Committy;

/// <summary>Interactive console input for <c>committy config</c>.</summary>
public static class ConsolePrompts
{
	/// <summary>
	/// A flag value always wins. Otherwise asks interactively, showing
	/// <paramref name="shown" /> (or the current value) in brackets; a blank
	/// answer keeps the current value, which may be null. With redirected
	/// input, the current value is used without asking. Showing a built-in
	/// default or another scope's value without returning it keeps either
	/// from being copied into config on Enter.
	/// </summary>
	public static string? Prompt(
		string label,
		string? flagValue,
		string? current,
		string? shown = null)
	{
		if (flagValue is not null)
		{
			return flagValue;
		}

		if (Console.IsInputRedirected)
		{
			return current;
		}

		string? bracketed = shown ?? current;
		Console.Write(
			bracketed is null ? $"{label}: " : $"{label} [{bracketed}]: ");

		// A typed value may be pasted with quotes around it (e.g. a tenant
		// ID).
		string? input = Console.ReadLine() is { } line
			? CommittyConfigResolver.Unquote(line)
			: null;

		return string.IsNullOrEmpty(input) ? current : input;
	}

	/// <summary>
	/// Reads a line without echoing it, showing <c>*</c> per character, for
	/// API keys.
	/// </summary>
	public static string ReadSecret()
	{
		var chars = new List<char>();

		while (true)
		{
			ConsoleKeyInfo key = Console.ReadKey(true);

			if (key.Key == ConsoleKey.Enter)
			{
				Console.WriteLine();

				break;
			}

			if (key.Key == ConsoleKey.Backspace)
			{
				if (chars.Count > 0)
				{
					chars.RemoveAt(chars.Count - 1);
					Console.Write("\b \b");
				}

				continue;
			}

			if (char.IsControl(key.KeyChar))
			{
				continue;
			}

			chars.Add(key.KeyChar);
			Console.Write('*');
		}

		return new string(chars.ToArray());
	}
}
