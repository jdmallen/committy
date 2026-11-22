using JDMallen.Toolbox.AI.Azure;

namespace JDMallen.Committy.Tests;

public class NameTableTests
{
	// Every value must have a canonical name that parses back to it, so an
	// enum value added without a table entry fails here, not at run time.
	[Fact]
	public void Provider_EveryValueRoundTrips() => Assert.All(
		Enum.GetValues<Provider>(),
		value => Assert.Equal(
			value,
			CommittyConfigResolver.ParseProvider(
				CommittyConfigResolver.ProviderName(value))));

	[Fact]
	public void AzureAuth_EveryValueRoundTrips() => Assert.All(
		Enum.GetValues<AzureAuth>(),
		value => Assert.Equal(
			value,
			CommittyConfigResolver.ParseAzureAuth(
				CommittyConfigResolver.AzureAuthName(value))));

	[Fact]
	public void AzureCredential_EveryValueRoundTrips() => Assert.All(
		Enum.GetValues<AzureCredentialKind>(),
		value => Assert.Equal(
			value,
			CommittyConfigResolver.ParseAzureCredential(
				CommittyConfigResolver.AzureCredentialName(value))));

	[Theory]
	[InlineData(null, AzureAuth.ApiKey)]
	[InlineData("  ", AzureAuth.ApiKey)]
	[InlineData(" Entra ", AzureAuth.AzureIdentity)]
	[InlineData("AZ", AzureAuth.AzureCli)]
	public void Parse_BlankMeansDefault_AliasesIgnoreCaseAndSpace(
		string? name,
		AzureAuth expected)
		=> Assert.Equal(expected, CommittyConfigResolver.ParseAzureAuth(name));

	[Fact]
	public void Parse_UnknownName_IsNull()
		=> Assert.Null(CommittyConfigResolver.ParseAzureAuth("kerberos"));
}
