using System.Reflection;

namespace JDMallen.Committy;

/// <summary>
/// Builds the <see cref="HttpClient" /> for a run's single completion request.
/// A plain client is enough: <c>IHttpClientFactory</c> exists to recycle
/// handlers in long-lived processes, and committy makes one call and exits.
/// Providers build absolute request URIs and set their own auth headers, so no
/// base address is needed.
/// </summary>
public static class HttpClientProvider
{
	/// <param name="timeoutSeconds">
	/// Request timeout. Hosted providers answer well within the default; a
	/// self-hosted model may need minutes, most of it spent loading weights
	/// from disk before the first token.
	/// </param>
	public static HttpClient Create(
		int timeoutSeconds = CommittyConfig.DEFAULT_TIMEOUT_SECONDS)
	{
		var client = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(timeoutSeconds),
		};
		AssemblyName assembly = typeof(HttpClientProvider).Assembly.GetName();

		client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		client.DefaultRequestHeaders.UserAgent.ParseAdd(
			$"{assembly.Name}/{assembly.Version}");

		return client;
	}
}
