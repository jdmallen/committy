namespace JDMallen.Committy;

/// <summary>Interactive console input for <c>committy config</c>.</summary>
public static class ConsolePrompts
{
	/// <summary>
	/// A flag value always wins. Otherwise asks interactively, showing the
	/// current value, which a blank answer keeps; with redirected input, the
	/// current value is used without asking. With nothing current,
	/// <paramref name="defaultValue" /> is shown instead, but a blank answer
	/// still returns null, so the built-in default is not written to config
	/// and a later change to it still reaches the user.
	/// </summary>
	public static string? Prompt(
		string label,
		string? flagValue,
		string? current,
		string? defaultValue = null)
	{
		if (flagValue is not null)
		{
			return flagValue;
		}

		if (Console.IsInputRedirected)
		{
			return current;
		}

		string? shown = current ?? defaultValue;
		Console.Write(shown is null ? $"{label}: " : $"{label} [{shown}]: ");

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
