namespace JDMallen.Committy.Tests;

internal static class ConfigAssertions
{
	/// <summary>
	/// The resolved Azure settings, failing the test when another provider
	/// resolved.
	/// </summary>
	public static AzureConfig Azure(this CommittyConfig config) =>
		Assert.IsType<AzureConfig>(config.Provider);
}
