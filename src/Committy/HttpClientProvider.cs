using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Committy;

/// <summary>
/// Builds the shared <see cref="HttpClient" /> backed by
/// <see cref="IHttpClientFactory" />. The factory owns connection pooling and
/// handler lifetime; providers build absolute request URIs and set their own auth
/// headers, so no per-provider base address is needed.
/// </summary>
public static class HttpClientProvider
{
	private const string CLIENT_NAME = "committy";

	/// <param name="timeoutSeconds">
	/// Request timeout. Hosted providers answer well within the default; a
	/// self-hosted model may need minutes, most of it spent loading weights from
	/// disk before the first token.
	/// </param>
	public static HttpClient Create(int timeoutSeconds = CommittyConfig.DefaultTimeoutSeconds)
	{
		var services = new ServiceCollection();

		services.AddHttpClient(
			CLIENT_NAME,
			client =>
			{
				client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
				client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

				AssemblyName? assembly = Assembly.GetAssembly(typeof(HttpClientProvider))?.GetName();
				client.DefaultRequestHeaders.UserAgent.ParseAdd(
					$"{assembly?.Name}/{assembly?.Version?.ToString() ?? string.Empty}");
			});

		ServiceProvider provider = services.BuildServiceProvider();

		return provider.GetRequiredService<IHttpClientFactory>().CreateClient(CLIENT_NAME);
	}
}
