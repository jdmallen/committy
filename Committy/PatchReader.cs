namespace Committy;

public class PatchReader
{
	public async Task<string> ReadPatchFromStdinAsync()
	{
		if (Console.IsInputRedirected)
		{
			using var reader = new StreamReader(Console.OpenStandardInput(), Console.InputEncoding);
			return await reader.ReadToEndAsync();
		}
		else
		{
			throw new InvalidOperationException("No input data available. Please pipe git patch data to stdin.");
		}
	}
}