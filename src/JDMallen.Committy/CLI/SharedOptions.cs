using System.CommandLine;

namespace JDMallen.Committy;

/// <summary>
/// The options both the root command and <c>config</c> take. Each command
/// supplies its own description, since "use this for one run" and "save this"
/// read differently; the names, aliases and help names live here once.
/// </summary>
public static class SharedOptions
{
	public static Option<string?> Provider(string description) =>
		new("--provider", "-p")
		{
			Description = description,
			HelpName = "provider",
		};

	public static Option<string?> ApiKey(string description) =>
		new("--api-key", "-k")
		{
			Description = description,
			HelpName = "API key",
		};

	public static Option<string?> Endpoint(string description) =>
		new("--endpoint", "-e")
		{
			Description = description,
			HelpName = "endpoint URL",
		};

	public static Option<string?> Deployment() =>
		new("--deployment", "-d")
		{
			Description = "Azure OpenAI deployment name",
			HelpName = "deployment name",
		};

	public static Option<string?> Model(string description) =>
		new("--model", "-m")
		{
			Description = description,
			HelpName = "model",
		};

	public static Option<string?> AzureAuth(string description) =>
		new("--azure-auth")
		{
			Description = description,
			HelpName = "method",
		};

	public static Option<string?> Tenant(string description) =>
		new("--tenant")
		{
			Description = description,
			HelpName = "tenant",
		};

	public static Option<string?> Credential(string description) =>
		new("--credential")
		{
			Description = description,
			HelpName = "credential",
		};

	public static Option<bool> TitlesOnly(string description) =>
		new("--titles-only", "-t")
		{
			Description = description,
		};
}
