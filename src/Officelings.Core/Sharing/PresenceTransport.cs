using System.Net.Http.Headers;

namespace Officelings.Core.Sharing;

public readonly record struct PresenceResponse(int StatusCode, TimeSpan? RetryAfter = null);

public interface IPresenceTransport
{
    Task<PresenceResponse> SendAsync(SharingEndpoint endpoint, string token, PresencePayload payload, DateTimeOffset now, CancellationToken cancellation);
}

public sealed class PresenceTransport(HttpClient client) : IPresenceTransport
{
    public static HttpClient CreateClient() => new(new HttpClientHandler
    { AllowAutoRedirect = false, UseProxy = false, UseCookies = false, AutomaticDecompression = System.Net.DecompressionMethods.None })
    { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<PresenceResponse> SendAsync(SharingEndpoint endpoint, string token, PresencePayload payload, DateTimeOffset now, CancellationToken cancellation)
    {
        try
        {
            WindowsSharingTokenStore.ValidateToken(token);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Ingest);
            // Wire identifier kept for server compatibility.
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new ByteArrayContent(payload.CopyBytes());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            var retry = response.Headers.RetryAfter;
            var delay = retry?.Delta ?? (retry?.Date is { } date ? date - now : (TimeSpan?)null);
            return new((int)response.StatusCode, delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return new(0); }
        // Never propagate handler/HTTP exception messages or server bodies; either could echo Authorization.
        catch (Exception) { return new(0); }
    }
}
