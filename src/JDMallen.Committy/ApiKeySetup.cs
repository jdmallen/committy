namespace JDMallen.Committy;

/// <summary>
/// Reads environment variables and, where the OS keeps them outside shell
/// startup files, persists them for the user.
/// </summary>
public interface IUserEnvironment
{
	/// <summary>
	/// Whether <see cref="SetPersistent" /> works here. True only on Windows,
	/// where user variables live in the registry; on Linux and macOS they live
	/// in shell startup files, which committy doesn't edit.
	/// </summary>
	bool CanPersist { get; }

	string? GetVariable(string name);

	/// <summary>
	/// Saves the variable for the user and sets it for this process, so it
	/// takes effect immediately as well as in programs started later.
	/// </summary>
	void SetPersistent(string name, string value);
}

public sealed class SystemUserEnvironment : IUserEnvironment
{
	public bool CanPersist => OperatingSystem.IsWindows();

	public string? GetVariable(string name)
		=> Environment.GetEnvironmentVariable(name);

	public void SetPersistent(string name, string value)
	{
		if (!CanPersist)
		{
			throw new PlatformNotSupportedException();
		}

		Environment.SetEnvironmentVariable(
			name,
			value,
			EnvironmentVariableTarget.User);
		Environment.SetEnvironmentVariable(name, value);
	}
}

/// <summary>
/// The API-key step of <c>committy config</c>. Keys live in environment
/// variables, not git config: this reports whether the variable is set (never
/// its value), and either offers to save it as a Windows user variable or
/// prints the line to add to the shell's startup file.
/// </summary>
/// <param name="environment">Reads and, on Windows, persists variables.</param>
/// <param name="output">Where messages go.</param>
/// <param name="readLine">Reads a line of input; null at end of input.</param>
/// <param name="readSecret">Reads a line without echoing it.</param>
/// <param name="shellPath">The user's shell, e.g. <c>$SHELL</c>.</param>
/// <param name="isLinux">
/// Whether to mention <c>environment.d</c>, which systemd desktop sessions read
/// instead of shell startup files.
/// </param>
public sealed class ApiKeySetup(
	IUserEnvironment environment,
	TextWriter output,
	Func<string?> readLine,
	Func<string?> readSecret,
	string? shellPath,
	bool isLinux = false)
{
	public const string ENVIRONMENT_D_FILE =
		"~/.config/environment.d/committy.conf";

	private const string KEY_PLACEHOLDER = "<your-key>";

	/// <summary>
	/// Reports on <paramref name="variable" /> and collects a key to persist.
	/// </summary>
	/// <param name="variable">The provider's key variable.</param>
	/// <param name="optional">Whether the provider works without a key.</param>
	/// <param name="flagKey">A key passed with <c>--api-key</c>, if any.</param>
	/// <param name="storedGitKey">
	/// The git config key holding a key from an older committy, if one is set.
	/// </param>
	/// <param name="interactive">False when input is redirected.</param>
	/// <returns>
	/// A key to persist with <see cref="Apply" /> once the rest of the config
	/// is written, or null.
	/// </returns>
	public string? Run(
		string variable,
		bool optional,
		string? flagKey,
		string? storedGitKey,
		bool interactive)
	{
		bool isSet = !string.IsNullOrEmpty(environment.GetVariable(variable));
		output.WriteLine();
		output.WriteLine("API key:");
		output.WriteLine($"  {variable} is {(isSet ? "set" : "not set")}.");

		if (optional)
		{
			output.WriteLine(
				"  Optional: most self-hosted servers don't check a key.");
		}

		if (storedGitKey is not null)
		{
			output.WriteLine(
				$"  {storedGitKey} in git config also holds a key, in plaintext. committy");
			output.WriteLine(
				$"  still reads it when {variable} is unset, which keeps commits from desktop");
			output.WriteLine(
				"  apps working when they don't load your shell's startup file. If you only");
			output.WriteLine(
				"  commit from terminals, you can remove it once the variable is set:");
			output.WriteLine($"    git config --global --unset {storedGitKey}");
		}

		if (environment.CanPersist)
		{
			if (!string.IsNullOrWhiteSpace(flagKey))
			{
				return flagKey.Trim();
			}

			return interactive
				? AskForKeyToPersist(variable, !isSet && !optional)
				: null;
		}

		if (!string.IsNullOrWhiteSpace(flagKey))
		{
			output.WriteLine(
				"  --api-key is only saved on Windows; elsewhere committy doesn't edit startup files.");
		}

		if (!isSet || !string.IsNullOrWhiteSpace(flagKey))
		{
			WriteStartupInstructions(variable);
		}

		return null;
	}

	/// <summary>Persists a key that <see cref="Run" /> returned.</summary>
	public void Apply(string variable, string? pendingKey)
	{
		if (pendingKey is null)
		{
			return;
		}

		environment.SetPersistent(variable, pendingKey);
		output.WriteLine(
			$"\nSaved {variable}. Terminals opened from now on will see it; ones already open won't.");
	}

	private string? AskForKeyToPersist(string variable, bool defaultYes)
	{
		string hint = defaultYes ? "[Y/n]" : "[y/N]";
		while (true)
		{
			output.Write(
				$"  Save {variable} as a Windows user environment variable now? {hint} ");
			string? answer = readLine()?.Trim().ToLowerInvariant();
			bool? save = answer switch
			{
				null or "" => defaultYes,
				"y" or "yes" => true,
				"n" or "no" => false,
				_ => null,
			};

			if (save is false)
			{
				return null;
			}

			if (save is true)
			{
				break;
			}

			output.WriteLine("  Please answer y or n.");
		}

		output.Write("  API key (Enter to skip): ");
		string? key = readSecret();
		if (!string.IsNullOrWhiteSpace(key))
		{
			return key.Trim();
		}

		output.WriteLine("  Skipped.");

		return null;
	}

	private void WriteStartupInstructions(string variable)
	{
		(string line, string? startupFile) = StartupLine(
			ShellName(shellPath),
			variable);
		output.WriteLine(
			startupFile is null
				? $"  Run this once, replacing {KEY_PLACEHOLDER} with your key. Fish remembers it across sessions, so no startup file is needed:"
				: $"  Add this line to {startupFile}, replacing {KEY_PLACEHOLDER} with your key, then open a new terminal:");
		output.WriteLine();
		output.WriteLine($"    {line}");

		if (!isLinux)
		{
			return;
		}

		// committy runs from a git hook, so it also runs under desktop git apps,
		// which a systemd session (e.g. GNOME) starts without the shell's files.
		output.WriteLine();
		output.WriteLine(
			"  Commits from desktop apps (git GUIs, some IDEs) may not see that file. In a");
		output.WriteLine(
			"  systemd desktop session such as GNOME, also add this line to");
		output.WriteLine(
			$"  {ENVIRONMENT_D_FILE}, chmod 600 it, and log out and back");
		output.WriteLine("  in. committy must be on that session's PATH too:");
		output.WriteLine();
		output.WriteLine($"    {variable}={KEY_PLACEHOLDER}");
	}

	/// <summary>The shell's name from its path, e.g. "zsh" from "/usr/bin/zsh".</summary>
	/// <remarks>
	/// Splits on both separators itself, because <see cref="Path" /> on Linux
	/// treats a Windows path's backslashes as part of the name.
	/// </remarks>
	public static string ShellName(string? shellPath)
	{
		if (string.IsNullOrWhiteSpace(shellPath))
		{
			return string.Empty;
		}

		string trimmedPath = shellPath.Trim();
		string fileName =
			trimmedPath[(trimmedPath.LastIndexOfAny(['/', '\\']) + 1)..];

		return Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
	}

	/// <summary>
	/// The line that sets <paramref name="variable" /> in the shell, and the
	/// startup file it belongs in; a null file means the line is run once
	/// instead (fish's universal variables persist on their own).
	/// </summary>
	public static (string Line, string? StartupFile) StartupLine(
		string shellName,
		string variable)
	{
		const string value = $"'{KEY_PLACEHOLDER}'";

		return shellName switch
		{
			"fish" => ($"set -Ux {variable} {value}", null),
			"zsh" => ($"export {variable}={value}", "~/.zshrc"),
			"bash" => ($"export {variable}={value}", "~/.bashrc"),
			"tcsh" => ($"setenv {variable} {value}", "~/.tcshrc"),
			"csh" => ($"setenv {variable} {value}", "~/.cshrc"),
			"pwsh" or "powershell" => ($"$env:{variable} = {value}",
				"$PROFILE"),
			"nu" => ($"$env.{variable} = {value}", "$nu.env-path"),
			"elvish" => ($"set E:{variable} = {value}",
				"~/.config/elvish/rc.elv"),
			"ksh" or "ksh93" or "mksh" or "lksh" or "oksh" or "pdksh"
				=> ($"export {variable}={value}", "~/.kshrc"),
			"sh" or "dash" or "ash" or "posh" or "yash"
				=> ($"export {variable}={value}", "~/.profile"),
			_ => ($"export {variable}={value}", "your shell's startup file"),
		};
	}
}
