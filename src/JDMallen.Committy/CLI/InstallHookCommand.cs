using System.CommandLine;

namespace JDMallen.Committy;

/// <summary>
/// Installs the trampoline hook, locally or globally. The binary owns the hook
/// text, so installation works from any directory as long as committy is on
/// PATH.
/// </summary>
public static class InstallHookCommand
{
	public static Command Build()
	{
		var globalOption = new Option<bool>("--global", "-g")
		{
			Description =
				"Install as a global hook template for all future repositories",
		};
		var forceOption = new Option<bool>("--force", "-f")
		{
			Description =
				"Replace an existing prepare-commit-msg hook that committy did not write; the old hook is backed up first",
		};
		var repoArgument = new Argument<string?>("repo")
		{
			Description =
				"Repository to install into (defaults to the current directory); may be a headless git directory such as ~/.cfg; ignored with --global",
			Arity = ArgumentArity.ZeroOrOne,
		};

		var command = new Command(
			"install-hook",
			"Install the committy git hook (local or global)")
		{
			globalOption,
			forceOption,
			repoArgument,
		};

		command.SetAction((parseResult, cancellationToken) =>
			CommandErrors.HandleAsync(() =>
				HookInstaller.InstallAsync(
					parseResult.GetValue(globalOption),
					parseResult.GetValue(repoArgument),
					Console.Out,
					parseResult.GetValue(forceOption),
					cancellationToken),
				cancellationToken));

		return command;
	}
}
