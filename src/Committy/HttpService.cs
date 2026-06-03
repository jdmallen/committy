using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Committy;

public class HttpService(IHttpClientFactory httpClientFactory) : IHttpService
{
	private const string CLIENT_NAME = "committy";

	public async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken = default)
	{
		HttpClient client = httpClientFactory.CreateClient(CLIENT_NAME);

		return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Builds an <see cref="IHttpService" /> backed by
	/// <see cref="IHttpClientFactory" />.
	/// The factory owns connection pooling and handler lifetime; providers build
	/// absolute
	/// request URIs and set their own auth headers, so no per-provider base address is
	/// needed.
	/// </summary>
	public static IHttpService Create()
	{
		var services = new ServiceCollection();

		services.AddHttpClient(
			CLIENT_NAME,
			client =>
			{
				client.Timeout = TimeSpan.FromSeconds(30);
				client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

				AssemblyName? assembly = Assembly.GetAssembly(typeof(HttpService))?.GetName();
				client.DefaultRequestHeaders.UserAgent.ParseAdd(
					$"{assembly?.Name}/{assembly?.Version?.ToString() ?? string.Empty}");
			});

		ServiceProvider provider = services.BuildServiceProvider();

		return new HttpService(provider.GetRequiredService<IHttpClientFactory>());
	}
}
