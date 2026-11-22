using CliWrap;
using CliWrap.Buffered;

namespace JDMallen.Committy.Tests;

public class HookInstallerTests
{
	private static string CreateTempDir()
	{
		string dir = Path.Combine(
			Path.GetTempPath(),
			"committy-test-" + Path.GetRandomFileName());
		Directory.CreateDirectory(dir);

		return dir;
	}

	private static async Task GitInitAsync(string repo) =>

		// "--template=" (empty) opts out of any global init.templateDir, so a developer
		// machine with its own committy template installed doesn't pollute these repos
		// with a pre-existing hook before HookInstaller ever runs.
		await Cli.Wrap("git")
			.WithArguments(["-C", repo, "init", "--template="])
			.ExecuteBufferedAsync();

	[Fact]
	public async Task InstallAsync_LocalRepo_WritesTrampolineHook()
	{
		string repo = CreateTempDir();
		try
		{
			await GitInitAsync(repo);
			var output = new StringWriter();

			int code = await HookInstaller.InstallAsync(false, repo, output);

			Assert.Equal(0, code);

			string hookPath = Path.Combine(
				repo,
				".git",
				"hooks",
				HookInstaller.HOOK_NAME);
			Assert.True(File.Exists(hookPath));

			string contents = await File.ReadAllTextAsync(hookPath);
			Assert.Contains("committy prepare-commit-msg", contents);
			Assert.DoesNotContain("\r\n", contents);

			if (!OperatingSystem.IsWindows())
			{
				UnixFileMode mode = File.GetUnixFileMode(hookPath);
				Assert.True(mode.HasFlag(UnixFileMode.UserExecute));
			}
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	[Fact]
	public async Task InstallAsync_CustomHooksPath_WritesToConfiguredDirectory()
	{
		string repo = CreateTempDir();
		try
		{
			await GitInitAsync(repo);

			string customHooksDir = Path.Combine(repo, ".githooks");
			Directory.CreateDirectory(customHooksDir);

			await Cli.Wrap("git")
				.WithArguments(
					["-C", repo, "config", "core.hooksPath", ".githooks"])
				.ExecuteBufferedAsync();

			var output = new StringWriter();

			int code = await HookInstaller.InstallAsync(false, repo, output);

			Assert.Equal(0, code);

			string hookPath = Path.Combine(
				customHooksDir,
				HookInstaller.HOOK_NAME);
			Assert.True(File.Exists(hookPath));

			string defaultHookPath = Path.Combine(
				repo,
				".git",
				"hooks",
				HookInstaller.HOOK_NAME);
			Assert.False(File.Exists(defaultHookPath));

			Assert.Contains("custom core.hooksPath", output.ToString());
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	[Fact]
	public async Task InstallAsync_NonexistentPath_ReturnsError()
	{
		var output = new StringWriter();
		string missing = Path.Combine(
			Path.GetTempPath(),
			Path.GetRandomFileName());

		int code = await HookInstaller.InstallAsync(false, missing, output);

		Assert.Equal(1, code);
		Assert.Contains("does not exist", output.ToString());
	}

	[Fact]
	public async Task InstallAsync_NotAGitRepo_ReturnsError()
	{
		string dir = CreateTempDir();
		try
		{
			var output = new StringWriter();

			int code = await HookInstaller.InstallAsync(false, dir, output);

			Assert.Equal(1, code);
			Assert.Contains("Not a valid git repository", output.ToString());
		}
		finally
		{
			Directory.Delete(dir, true);
		}
	}

	private static string LocalHookPath(string repo) =>
		Path.Combine(repo, ".git", "hooks", HookInstaller.HOOK_NAME);

	private static async Task<string> WriteLocalHookAsync(
		string repo,
		string content)
	{
		string hookPath = LocalHookPath(repo);
		Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);
		await File.WriteAllTextAsync(hookPath, content);

		return hookPath;
	}

	private const string FOREIGN_HOOK = "#!/bin/sh\necho my own hook\n";

	[Fact]
	public async Task InstallAsync_ForeignHook_RefusesAndLeavesItAlone()
	{
		string repo = CreateTempDir();
		try
		{
			await GitInitAsync(repo);
			string hookPath = await WriteLocalHookAsync(repo, FOREIGN_HOOK);
			var output = new StringWriter();

			int code = await HookInstaller.InstallAsync(false, repo, output);

			Assert.Equal(1, code);
			Assert.Equal(FOREIGN_HOOK, await File.ReadAllTextAsync(hookPath));
			Assert.False(File.Exists(hookPath + ".bak"));
			Assert.Contains("--force", output.ToString());
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	[Fact]
	public async Task InstallAsync_ForeignHookWithForce_ReplacesAndBacksUp()
	{
		string repo = CreateTempDir();
		try
		{
			await GitInitAsync(repo);
			string hookPath = await WriteLocalHookAsync(repo, FOREIGN_HOOK);
			var output = new StringWriter();

			int code = await HookInstaller.InstallAsync(
				false,
				repo,
				output,
				true);

			Assert.Equal(0, code);
			Assert.True(
				HookInstaller.IsCurrentTrampoline(
					await File.ReadAllTextAsync(hookPath)));
			Assert.Equal(
				FOREIGN_HOOK,
				await File.ReadAllTextAsync(hookPath + ".bak"));
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	[Fact]
	public async Task InstallAsync_StaleCommittyHook_ReplacesWithoutBackup()
	{
		string repo = CreateTempDir();
		try
		{
			await GitInitAsync(repo);
			string hookPath = await WriteLocalHookAsync(
				repo,
				"#!/bin/sh\n# old committy hook\ncommitty prepare-commit-msg \"$1\"\n");
			var output = new StringWriter();

			int code = await HookInstaller.InstallAsync(false, repo, output);

			Assert.Equal(0, code);
			Assert.True(
				HookInstaller.IsCurrentTrampoline(
					await File.ReadAllTextAsync(hookPath)));
			Assert.False(File.Exists(hookPath + ".bak"));
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	[Fact]
	public async Task InstallAsync_CurrentTrampoline_IsANoOp()
	{
		string repo = CreateTempDir();
		try
		{
			await GitInitAsync(repo);
			string hookPath = await WriteLocalHookAsync(
				repo,
				HookInstaller.Trampoline);
			var output = new StringWriter();

			int code = await HookInstaller.InstallAsync(false, repo, output);

			Assert.Equal(0, code);
			Assert.Contains("already up to date", output.ToString());
			Assert.False(File.Exists(hookPath + ".bak"));
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	private static async Task<string?> GetGlobalTemplateDirAsync(
		string configFile)
	{
		BufferedCommandResult result = await Cli.Wrap("git")
			.WithArguments(
				["config", "--file", configFile, "--get", "init.templateDir"])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync();

		return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
	}

	[Fact]
	public async Task InstallGlobalAsync_TemplateDirUnset_CreatesDefaultAndSetsIt()
	{
		string root = CreateTempDir();
		try
		{
			string configFile = Path.Combine(root, "gitconfig");
			string defaultTemplateDir = Path.Combine(root, ".git-templates");
			var output = new StringWriter();

			int code = await HookInstaller.InstallGlobalAsync(
				output,
				false,
				defaultTemplateDir,
				configFile);

			Assert.Equal(0, code);
			Assert.True(
				File.Exists(
					Path.Combine(
						defaultTemplateDir,
						"hooks",
						HookInstaller.HOOK_NAME)));
			Assert.Equal(
				defaultTemplateDir,
				await GetGlobalTemplateDirAsync(configFile));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public async Task InstallGlobalAsync_TemplateDirSet_InstallsThereAndKeepsIt()
	{
		string root = CreateTempDir();
		try
		{
			string configFile = Path.Combine(root, "gitconfig");
			string existingTemplateDir = Path.Combine(root, "my-template");
			string defaultTemplateDir = Path.Combine(root, ".git-templates");
			await File.WriteAllTextAsync(
				configFile,
				$"[init]\n\ttemplateDir = {existingTemplateDir}\n");
			var output = new StringWriter();

			int code = await HookInstaller.InstallGlobalAsync(
				output,
				false,
				defaultTemplateDir,
				configFile);

			Assert.Equal(0, code);
			Assert.True(
				File.Exists(
					Path.Combine(
						existingTemplateDir,
						"hooks",
						HookInstaller.HOOK_NAME)));
			Assert.False(Directory.Exists(defaultTemplateDir));
			Assert.Equal(
				existingTemplateDir,
				await GetGlobalTemplateDirAsync(configFile));
			Assert.Contains("existing init.templateDir", output.ToString());
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}
}
