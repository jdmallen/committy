namespace JDMallen.Committy.Tests;

public class ApiKeySetupTests
{
	private const string VARIABLE = "AZURE_OPENAI_API_KEY";

	private readonly FakeUserEnvironment _environment = new();
	private readonly StringWriter _output = new();
	private readonly Queue<string?> _lines = new();
	private readonly Queue<string?> _secrets = new();

	private ApiKeySetup NewSetup(
		string? shellPath = "/usr/bin/zsh",
		bool isLinux = false) =>
		new(
			_environment,
			_output,
			() => _lines.Count > 0 ? _lines.Dequeue() : null,
			() => _secrets.Count > 0 ? _secrets.Dequeue() : null,
			shellPath,
			isLinux);

	[Fact]
	public void Run_ReportsWhetherTheVariableIsSet_NeverItsValue()
	{
		_environment.Variables[VARIABLE] = "super-secret";

		NewSetup()
		.Run(
			VARIABLE,
			false,
			null,
			null,
			true);

		var output = _output.ToString();
		Assert.Contains($"{VARIABLE} is set.", output);
		Assert.DoesNotContain("super-secret", output);
	}

	[Fact]
	public void Run_Unix_Unset_PrintsTheStartupLineAndPersistsNothing()
	{
		ApiKeySetup setup = NewSetup();

		string? pending = setup.Run(
			VARIABLE,
			false,
			null,
			null,
			true);
		setup.Apply(VARIABLE, pending);

		Assert.Null(pending);
		Assert.Empty(_environment.Persisted);
		Assert.Contains("~/.zshrc", _output.ToString());
		Assert.Contains($"export {VARIABLE}='<your-key>'", _output.ToString());
	}

	[Fact]
	public void Run_Unix_AlreadySet_PrintsNoInstructions()
	{
		_environment.Variables[VARIABLE] = "k";

		NewSetup()
		.Run(
			VARIABLE,
			false,
			null,
			null,
			true);

		Assert.DoesNotContain("export", _output.ToString());
	}

	[Fact]
	public void Run_Unix_FlagKey_IsNotSavedAndSaysSo()
	{
		_environment.Variables[VARIABLE] = "k";

		string? pending = NewSetup()
		.Run(
			VARIABLE,
			false,
			"flag-key",
			null,
			true);

		Assert.Null(pending);
		Assert.Contains("only saved on Windows", _output.ToString());
		Assert.DoesNotContain("flag-key", _output.ToString());
	}

	[Fact]
	public void Run_Windows_SavesTheTypedKeyOnlyOnApply()
	{
		_environment.CanPersist = true;
		_lines.Enqueue("");
		_secrets.Enqueue("  typed-key  ");
		ApiKeySetup setup = NewSetup();

		string? pending = setup.Run(
			VARIABLE,
			false,
			null,
			null,
			true);

		Assert.Equal("typed-key", pending);
		Assert.Empty(_environment.Persisted);

		setup.Apply(VARIABLE, pending);

		Assert.Equal("typed-key", _environment.Persisted[VARIABLE]);
	}

	[Theory]
	[InlineData("n")]
	[InlineData("NO")]
	public void Run_Windows_Declined_SavesNothing(string answer)
	{
		_environment.CanPersist = true;
		_lines.Enqueue(answer);

		Assert.Null(
			NewSetup()
			.Run(
				VARIABLE,
				false,
				null,
				null,
				true));
		Assert.Empty(_secrets);
	}

	[Fact]
	public void Run_Windows_InvalidAnswer_AsksAgain()
	{
		_environment.CanPersist = true;
		_lines.Enqueue("maybe");
		_lines.Enqueue("Y");
		_secrets.Enqueue("k");

		Assert.Equal(
			"k",
			NewSetup()
			.Run(
				VARIABLE,
				false,
				null,
				null,
				true));
		Assert.Contains("Please answer y or n.", _output.ToString());
	}

	[Fact]
	public void Run_Windows_AlreadySet_DefaultsToNo()
	{
		_environment.CanPersist = true;
		_environment.Variables[VARIABLE] = "k";
		_lines.Enqueue("");

		Assert.Null(
			NewSetup()
			.Run(
				VARIABLE,
				false,
				null,
				null,
				true));
		Assert.Contains("[y/N]", _output.ToString());
	}

	[Fact]
	public void Run_Windows_FlagKey_IsSavedWithoutPrompting()
	{
		_environment.CanPersist = true;

		Assert.Equal(
			"flag-key",
			NewSetup()
			.Run(
				VARIABLE,
				false,
				"flag-key",
				null,
				false));
		Assert.DoesNotContain("Save ", _output.ToString());
	}

	[Fact]
	public void Run_Windows_NotInteractive_DoesNotPrompt()
	{
		_environment.CanPersist = true;

		Assert.Null(
			NewSetup()
			.Run(
				VARIABLE,
				false,
				null,
				null,
				false));
		Assert.DoesNotContain("Save ", _output.ToString());
	}

	[Fact]
	public void Run_StoredGitKey_OffersRemovalOnlyForTerminalOnlyUse()
	{
		NewSetup()
		.Run(
			VARIABLE,
			false,
			null,
			"committy.azure.apikey",
			true);

		var output = _output.ToString();
		Assert.Contains("If you only", output);
		Assert.Contains("desktop", output);
		Assert.Contains(
			"git config --global --unset committy.azure.apikey",
			output);
	}

	[Fact]
	public void Run_Linux_Unset_AlsoPointsAtEnvironmentD()
	{
		NewSetup(isLinux: true)
		.Run(
			VARIABLE,
			false,
			null,
			null,
			true);

		var output = _output.ToString();
		Assert.Contains(ApiKeySetup.ENVIRONMENT_D_FILE, output);
		Assert.Contains($"    {VARIABLE}=<your-key>", output);
	}

	[Fact]
	public void Run_NotLinux_Unset_DoesNotMentionEnvironmentD()
	{
		NewSetup(isLinux: false)
		.Run(
			VARIABLE,
			false,
			null,
			null,
			true);

		Assert.DoesNotContain("environment.d", _output.ToString());
	}

	[Fact]
	public void Run_Optional_SaysSo()
	{
		NewSetup()
		.Run(
			"OPENAI_API_KEY_COMMITTY",
			true,
			null,
			null,
			true);

		Assert.Contains("Optional", _output.ToString());
	}

	[Theory]
	[InlineData("/usr/bin/fish", "set -Ux V '<your-key>'", null)]
	[InlineData("/bin/bash", "export V='<your-key>'", "~/.bashrc")]
	[InlineData(
		@"C:\Program Files\PowerShell\7\pwsh.exe",
		"$env:V = '<your-key>'",
		"$PROFILE")]
	[InlineData("/usr/bin/nu", "$env.V = '<your-key>'", "$nu.env-path")]
	[InlineData(null, "export V='<your-key>'", "your shell's startup file")]
	public void StartupLine_MatchesTheShell(
		string? shellPath,
		string line,
		string? startupFile) => Assert.Equal(
		(line, startupFile),
		ApiKeySetup.StartupLine(ApiKeySetup.ShellName(shellPath), "V"));

	private sealed class FakeUserEnvironment : IUserEnvironment
	{
		public Dictionary<string, string> Variables { get; } = [];

		public Dictionary<string, string> Persisted { get; } = [];

		public bool CanPersist { get; set; }

		public string? GetVariable(string name) => Variables.GetValueOrDefault(name);

		public void SetPersistent(string name, string value)
		{
			Persisted[name] = value;
			Variables[name] = value;
		}
	}
}
