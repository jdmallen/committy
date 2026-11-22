using System.Diagnostics;

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
		await TestGit.RunAsync(
			"-C",
			repo,
			"init",
			"--template=");

	[Fact]
	public async Task InstallAsync_LocalRepo_WritesTrampolineHook()
	{
		string repo = CreateTempDir();
		try
		{
			await GitInitAsync(repo);
			var output = new StringWriter();

			int code = await HookInstaller.InstallAsync(
				false,
				repo,
				output,
				cancellationToken: TestContext.Current.CancellationToken);

			Assert.Equal(0, code);

			string hookPath = Path.Combine(
				repo,
				".git",
				"hooks",
				HookInstaller.HOOK_NAME);
			Assert.True(File.Exists(hookPath));

			string contents = await File.ReadAllTextAsync(
				hookPath,
				TestContext.Current.CancellationToken);
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

			await TestGit.RunAsync(
				"-C",
				repo,
				"config",
				"core.hooksPath",
				".githooks");

			var output = new StringWriter();

			int code = await HookInstaller.InstallAsync(
				false,
				repo,
				output,
				cancellationToken: TestContext.Current.CancellationToken);

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

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => HookInstaller.InstallAsync(
				false,
				missing,
				output,
				cancellationToken: TestContext.Current.CancellationToken));

		Assert.Contains("does not exist", exception.Message);
		Assert.DoesNotContain("Error", output.ToString());
	}

	[Fact]
	public async Task InstallAsync_NotAGitRepo_ReturnsError()
	{
		string dir = CreateTempDir();
		try
		{
			var output = new StringWriter();

			var exception =
				await Assert.ThrowsAsync<InvalidOperationException>(() =>
					HookInstaller.InstallAsync(
						false,
						dir,
						output,
						cancellationToken:
							TestContext.Current.CancellationToken));

			Assert.Contains("Not a valid git repository", exception.Message);
		}
		finally
		{
			Directory.Delete(dir, true);
		}
	}

	private static string LocalHookPath(string repo) =>
		Path.Combine(
			repo,
			".git",
			"hooks",
			HookInstaller.HOOK_NAME);

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

			var exception =
				await Assert.ThrowsAsync<InvalidOperationException>(() =>
					HookInstaller.InstallAsync(
						false,
						repo,
						output,
						cancellationToken:
							TestContext.Current.CancellationToken));

			Assert.Equal(FOREIGN_HOOK, await File.ReadAllTextAsync(
				hookPath,
				TestContext.Current.CancellationToken));
			Assert.False(File.Exists(hookPath + ".bak"));
			Assert.Contains("--force", exception.Message);
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
				true,
				cancellationToken: TestContext.Current.CancellationToken);

			Assert.Equal(0, code);
			Assert.True(
				HookInstaller.IsCurrentTrampoline(
					await File.ReadAllTextAsync(
						hookPath,
						TestContext.Current.CancellationToken)));
			Assert.Equal(
				FOREIGN_HOOK,
				await File.ReadAllTextAsync(
					hookPath + ".bak",
					TestContext.Current.CancellationToken));
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	[Fact]
	public async Task InstallAsync_StaleCommittyHook_ReplacesAndBacksUp()
	{
		string repo = CreateTempDir();
		try
		{
			await GitInitAsync(repo);
			string hookPath = await WriteLocalHookAsync(
				repo,
				LegacyHooks.UNSTAMPED_TRAMPOLINE);
			var output = new StringWriter();

			int code = await HookInstaller.InstallAsync(
				false,
				repo,
				output,
				cancellationToken: TestContext.Current.CancellationToken);

			Assert.Equal(0, code);
			Assert.True(
				HookInstaller.IsCurrentTrampoline(
					await File.ReadAllTextAsync(
						hookPath,
						TestContext.Current.CancellationToken)));
			Assert.Equal(
				LegacyHooks.UNSTAMPED_TRAMPOLINE,
				await File.ReadAllTextAsync(
					hookPath + ".bak",
					TestContext.Current.CancellationToken));
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	[Fact]
	public async Task InstallAsync_HandWrittenHookCallingCommitty_Refuses()
	{
		const string handWritten =
			"#!/bin/sh\n./scripts/lint-message.sh \"$1\"\n"
			+ "committy prepare-commit-msg \"$1\"\n";
		string repo = CreateTempDir();
		try
		{
			await GitInitAsync(repo);
			string hookPath = await WriteLocalHookAsync(repo, handWritten);
			var output = new StringWriter();

			await Assert.ThrowsAsync<InvalidOperationException>(() =>
				HookInstaller.InstallAsync(
					false,
					repo,
					output,
					cancellationToken: TestContext.Current.CancellationToken));

			Assert.Equal(handWritten, await File.ReadAllTextAsync(
				hookPath,
				TestContext.Current.CancellationToken));
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

			int code = await HookInstaller.InstallAsync(
				false,
				repo,
				output,
				cancellationToken: TestContext.Current.CancellationToken);

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
		ProcessTextOutput result = await Git.RunAsync(
			["config", "--file", configFile, "--get", "init.templateDir"]);

		return result.ExitStatus.ExitCode == 0
			? result.StandardOutput.Trim()
			: null;
	}

	[Fact]
	public async Task
		InstallGlobalAsync_TemplateDirUnset_CreatesDefaultAndSetsIt()
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
				configFile,
				TestContext.Current.CancellationToken);

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
	public async Task
		InstallGlobalAsync_TemplateDirSet_InstallsThereAndKeepsIt()
	{
		string root = CreateTempDir();
		try
		{
			string configFile = Path.Combine(root, "gitconfig");
			string existingTemplateDir = Path.Combine(root, "my-template");
			string defaultTemplateDir = Path.Combine(root, ".git-templates");
			await File.WriteAllTextAsync(
				configFile,
				$"[init]\n\ttemplateDir = {existingTemplateDir}\n",
				TestContext.Current.CancellationToken);
			var output = new StringWriter();

			int code = await HookInstaller.InstallGlobalAsync(
				output,
				false,
				defaultTemplateDir,
				configFile,
				TestContext.Current.CancellationToken);

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
