using System.CommandLine;

namespace JDMallen.Committy;

/// <summary>
/// Wires the commands together. Each command lives in its own class under
/// <c>CLI/</c> and returns its exit code.
/// </summary>
internal class Program
{
	private static async Task<int> Main(string[] args)
	{
		RootCommand rootCommand = GenerateCommand.Build();
		rootCommand.Subcommands.Add(PrepareCommitMsgCommand.Build());
		rootCommand.Subcommands.Add(InstallHookCommand.Build());
		rootCommand.Subcommands.Add(RepairHooksCommand.Build());
		rootCommand.Subcommands.Add(ConfigCommand.Build());

		return await rootCommand.Parse(args).InvokeAsync();
	}
}
