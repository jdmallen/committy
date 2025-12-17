namespace Committy;

public interface IHttpService
{
	Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken = default);
}
