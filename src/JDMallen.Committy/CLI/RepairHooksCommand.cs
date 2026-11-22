using System.CommandLine;

namespace JDMallen.Committy;

/// <summary>
/// Replaces stale committy hooks with the current trampoline. By default it
/// repairs only the current repository; <c>--scan &lt;dir&gt;</c> recursively
/// sweeps a tree, and <c>--global</c> also re-stamps the global template. Only
/// committy-managed hooks are touched; foreign hooks are reported and left
/// alone.
/// </summary>
public static class RepairHooksCommand
{
	public static Command Build()
	{
		var globalOption = new Option<bool>("--global", "-g")
		{
			Description = "Also re-stamp the global hook template",
		};
		var scanOption = new Option<string[]>("--scan")
		{
			Description =
				"Directory to recursively scan for repositories (repeatable); default is the current repo only",
			Arity = ArgumentArity.ZeroOrMore,
			AllowMultipleArgumentsPerToken = true,
			HelpName = "dir",
		};
		var gitDirOption = new Option<string[]>("--git-dir")
		{
			Description =
				"Git directory to repair directly (repeatable); use for headless repos such as a ~/.cfg dotfiles setup that recursive scanning skips",
			Arity = ArgumentArity.ZeroOrMore,
			AllowMultipleArgumentsPerToken = true,
			HelpName = "dir",
		};
		var dryRunOption = new Option<bool>("--dry-run")
		{
			Description = "Report what would change without modifying anything",
		};

		// Replaced hooks are always backed up now; the flag stays accepted
		// so existing scripts that pass it keep working.
		var backupOption = new Option<bool>("--backup")
		{
			Description =
				"No longer needed: replaced hooks are always backed up",
			Hidden = true,
		};

		var command = new Command(
			"repair-hooks",
			"Replace stale committy hooks with the current trampoline (current repo by default; --scan to sweep)")
		{
			globalOption,
			scanOption,
			gitDirOption,
			dryRunOption,
			backupOption,
		};

		command.SetAction((parseResult, cancellationToken) =>
			CommandErrors.HandleAsync(() =>
				RepairAsync(
					parseResult.GetValue(globalOption),
					parseResult.GetValue(scanOption) ?? [],
					parseResult.GetValue(gitDirOption) ?? [],
					parseResult.GetValue(dryRunOption),
					cancellationToken)));

		return command;
	}

	private static async Task<int> RepairAsync(
		bool global,
		string[] scan,
		string[] gitDirs,
		bool dryRun,
		CancellationToken cancellationToken)
	{
		var templateExitCode = 0;

		if (global)
		{
			if (dryRun)
			{
				await Console.Out.WriteLineAsync(
					"[dry-run] would re-stamp the global hook template",
					cancellationToken);
			}
			else
			{
				templateExitCode = await HookInstaller.InstallAsync(
					true,
					null,
					Console.Out,
					cancellationToken: cancellationToken);
			}
		}

		int repairExitCode = await HookRepairer.RunAsync(
			scan,
			gitDirs,
			dryRun,
			!global,
			Console.Out,
			cancellationToken);

		return Math.Max(templateExitCode, repairExitCode);
	}
}
