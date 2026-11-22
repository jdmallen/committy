using System.Security.Cryptography;
using System.Text;

namespace JDMallen.Committy.Tests;

public class TrampolineTests
{
	/// <summary>
	/// SHA-256 of the exact hook text committy installs. Every installed hook
	/// is compared against it, so any change, even to a comment or a trailing
	/// newline, makes every installed hook "stale" for repair-hooks. Change it
	/// only on purpose, and then add the old text's hash to
	/// HookInstaller.LegacyHookHashes.
	/// </summary>
	private const string INSTALLED_TRAMPOLINE_SHA256 =
		"a5dd1df6c19e849561c3beece5907e169c42bcbc5bf022040f43015d414f4345";

	[Fact]
	public void Trampoline_IsTheTextInstalledHooksCarry()
		=> Assert.Equal(
			INSTALLED_TRAMPOLINE_SHA256,
			Convert.ToHexStringLower(
				SHA256.HashData(
					Encoding.UTF8.GetBytes(HookInstaller.Trampoline))));
}
