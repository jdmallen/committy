namespace Committy;

public class HttpService : IHttpService
{
	public async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken = default)
	{
		return await Http.OpenAI.SendAsync(request, cancellationToken).ConfigureAwait(false);
	}
}
