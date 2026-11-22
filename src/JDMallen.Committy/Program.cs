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

		// Windows consoles default to an OEM code page, which turns ✓, —,
		// arrows and typographic quotes into '?'. Only when stdout is the
		// console itself: whatever reads a pipe or redirect (clip, PowerShell)
		// decodes it with that same code page, so UTF-8 there turns é into ├⌐.
		if (!OperatingSystem.IsWindows() || Console.IsOutputRedirected)
		{
			return await rootCommand.Parse(args).InvokeAsync();
		}

		// No byte order mark, so stderr redirected on its own stays plain
		// UTF-8. Setting it changes the console's code page, which outlives
		// this process, so the original is put back.
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
