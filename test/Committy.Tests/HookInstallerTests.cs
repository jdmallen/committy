using CliWrap;
using CliWrap.Buffered;

namespace Committy.Tests;

public class HookInstallerTests
{
	private static string CreateTempDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "committy-test-" + Path.GetRandomFileName());
		Directory.CreateDirectory(dir);

		return dir;
	}

	private static async Task GitInitAsync(string repo) =>

		// "--template=" (empty) opts out of any global init.templateDir, so a developer
		// machine with its own committy template installed doesn't pollute these repos
		// with a pre-existing hook before HookInstaller ever runs.
		await Cli.Wrap("git").WithArguments(["-C", repo, "init", "--template="]).ExecuteBufferedAsync();

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
				HookInstaller.HookName);
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
				.WithArguments(["-C", repo, "config", "core.hooksPath", ".githooks"])
				.ExecuteBufferedAsync();

			var output = new StringWriter();

			int code = await HookInstaller.InstallAsync(false, repo, output);

			Assert.Equal(0, code);

			string hookPath = Path.Combine(customHooksDir, HookInstaller.HookName);
			Assert.True(File.Exists(hookPath));

			string defaultHookPath = Path.Combine(
				repo,
				".git",
				"hooks",
				HookInstaller.HookName);
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
		string missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

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
}
