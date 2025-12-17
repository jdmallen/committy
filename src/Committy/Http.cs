using System.Net;
using System.Reflection;

namespace Committy;

internal static class Http
{
	// Shared handler manages connection pool, DNS refresh, decompression, etc.
	private static readonly SocketsHttpHandler Handler = new()
	{
		AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
		// Rotate pooled connections periodically so DNS updates are honored.
		PooledConnectionLifetime = TimeSpan.FromMinutes(5),
		// Cap idle connections and lifetime for load-balanced backends
		PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
		// Reasonable defaults
		MaxConnectionsPerServer = 64,
		AllowAutoRedirect = false,
	};

	private static HttpClient? _openAIClient;

	public static HttpClient OpenAI => _openAIClient ?? throw new InvalidOperationException($"{nameof(OpenAI)} is not initialized. Http.Initialize() must be called first.");

	public static void Initialize(string openAIBaseAddress)
	{
		_openAIClient = CreateClient(new Uri(openAIBaseAddress));
	}

	private static HttpClient CreateClient(Uri baseAddress)
	{
		var client = new HttpClient(Handler, disposeHandler: false)
		{
			BaseAddress = baseAddress,
			Timeout = TimeSpan.FromSeconds(30),
		};
		AssemblyName? assy = Assembly.GetAssembly(typeof(Program))?.GetName();
		client.DefaultRequestHeaders.UserAgent.ParseAdd(
			$"{assy?.Name}/{assy?.Version?.ToString() ?? string.Empty}"
		);
		client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		return client;
	}
}
