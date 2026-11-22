using System.CommandLine;
using System.Text;

namespace JDMallen.Committy;

/// <summary>
/// Wires the commands together. Each command lives in its own class under
/// <c>CLI/</c> and returns its exit code.
/// </summary>
internal abstract class Program
{
	private static async Task<int> Main(string[] args)
	{
		RootCommand rootCommand = GenerateCommand.Build();
		rootCommand.Subcommands.Add(PrepareCommitMsgCommand.Build());
		rootCommand.Subcommands.Add(InstallHookCommand.Build());
		rootCommand.Subcommands.Add(RepairHooksCommand.Build());
		rootCommand.Subcommands.Add(ConfigCommand.Build());

		if (!OperatingSystem.IsWindows())
		{
			return await rootCommand.Parse(args).InvokeAsync();
		}

		// Windows consoles default to an OEM code page, which turns ✓, —,
		// arrows and typographic quotes in output (and in generated messages
		// piped to clip or a file) into '?'. No byte order mark, so redirected
		// output stays plain UTF-8. Setting it changes the console's code page,
		// which outlives this process, so the original is put back.
		Encoding originalEncoding = Console.OutputEncoding;
		Console.OutputEncoding = new UTF8Encoding(false);
		try
		{
			return await rootCommand.Parse(args).InvokeAsync();
		}
		finally
		{
			Console.OutputEncoding = originalEncoding;
		}
	}
}
