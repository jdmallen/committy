namespace Committy;

public static class UserInterface
{
	public static string SelectCommitMessage(List<string> suggestions)
	{
		Console.WriteLine("\nGenerated commit message suggestions:");
		Console.WriteLine(new string('=', 45));

		for (int i = 0; i < suggestions.Count; i++)
		{
			Console.WriteLine($"{i + 1}. {suggestions[i]}");
		}

		Console.WriteLine($"{suggestions.Count + 1}. Enter custom message");
		Console.WriteLine();

		while (true)
		{
			Console.Write($"Select option (1-{suggestions.Count + 1}): ");
			var input = Console.ReadLine()?.Trim();

			if (int.TryParse(input, out int choice))
			{
				if (choice >= 1 && choice <= suggestions.Count)
				{
					return suggestions[choice - 1];
				}
				else if (choice == suggestions.Count + 1)
				{
					return GetCustomCommitMessage();
				}
			}

			Console.WriteLine($"Invalid selection. Please enter a number between 1 and {suggestions.Count + 1}.");
		}
	}

	public static bool ConfirmCommit(string commitMessage)
	{
		Console.WriteLine($"\nCommit message: {commitMessage}");
		Console.WriteLine();

		while (true)
		{
			Console.Write("Proceed with commit? (y/n): ");
			var input = Console.ReadLine()?.Trim().ToLowerInvariant();

			switch (input)
			{
				case "y":
				case "yes":
					return true;
				case "n":
				case "no":
					return false;
				default:
					Console.WriteLine("Please enter 'y' for yes or 'n' for no.");
					break;
			}
		}
	}

	public static void ShowStagedChanges(string diff)
	{
		Console.WriteLine("Staged changes:");
		Console.WriteLine(new string('-', 40));
		
		var lines = diff.Split('\n');
		var displayLines = lines.Take(20).ToList();
		
		foreach (var line in displayLines)
		{
			if (line.StartsWith("+") && !line.StartsWith("+++"))
			{
				Console.ForegroundColor = ConsoleColor.Green;
			}
			else if (line.StartsWith("-") && !line.StartsWith("---"))
			{
				Console.ForegroundColor = ConsoleColor.Red;
			}
			else if (line.StartsWith("@@"))
			{
				Console.ForegroundColor = ConsoleColor.Cyan;
			}

			Console.WriteLine(line);
			Console.ResetColor();
		}

		if (lines.Length > 20)
		{
			Console.WriteLine($"... and {lines.Length - 20} more lines");
		}

		Console.WriteLine();
	}

	private static string GetCustomCommitMessage()
	{
		Console.WriteLine();
		Console.Write("Enter your commit message: ");
		var message = Console.ReadLine()?.Trim();

		if (string.IsNullOrWhiteSpace(message))
		{
			Console.WriteLine("Commit message cannot be empty.");
			return GetCustomCommitMessage();
		}

		return message;
	}
}