using System.Net.Http.Headers;
using System.Security.Cryptography.Pkcs;

namespace MacSign.Signing.Cms;

/// <summary>
/// Talks to an RFC3161 Time-Stamping Authority over HTTP: POST a
/// <c>application/timestamp-query</c>, receive a <c>application/timestamp-reply</c>,
/// and turn it into a validated <see cref="Rfc3161TimestampToken"/>. All BCL.
/// </summary>
internal sealed class TimestampClient
{
    private static readonly HttpClient Http = CreateClient();
    private const int MaxAttempts = 3;

    private static HttpClient CreateClient()
    {
        // Don't follow redirects (a TSA shouldn't bounce the timestamp POST elsewhere) and
        // cap the response so a hostile/compromised server can't exhaust memory — RFC3161
        // tokens are a few KB; the token's correctness is still gated by ProcessResponse.
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = 1024 * 1024,
        };
    }

    /// <summary>Request a timestamp token for <paramref name="request"/> from <paramref name="tsaUrl"/>.</summary>
    public async Task<Rfc3161TimestampToken> RequestAsync(
        Rfc3161TimestampRequest request, Uri tsaUrl, CancellationToken ct)
    {
        byte[] encodedRequest = request.Encode();
        Exception? last = null;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var content = new ByteArrayContent(encodedRequest);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");

                using var response = await Http.PostAsync(tsaUrl, content, ct);
                response.EnsureSuccessStatusCode();
                byte[] body = await response.Content.ReadAsByteArrayAsync(ct);

                // Validates the response matches our request (nonce, imprint, etc.).
                return request.ProcessResponse(body, out _);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                last = ex; // transient — retry
            }
        }

        throw new InvalidOperationException(
            $"Timestamp server '{tsaUrl}' did not respond after {MaxAttempts} attempts: {last?.Message}");
    }
}
