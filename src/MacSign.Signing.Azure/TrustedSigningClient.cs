using System.Net;
using System.Text;
using System.Text.Json;

namespace MacSign.Signing.Azure;

/// <summary>The terminal result of a Trusted Signing sign operation.</summary>
internal sealed record TrustedSigningResult(byte[] Signature, string? SigningCertificate);

/// <summary>
/// Minimal client for the Azure Trusted Signing data-plane <c>sign</c> endpoint.
/// It POSTs a <b>pre-computed digest</b> (it never re-hashes anything) and polls the
/// long-running operation until it yields the signature and the signing certificate
/// chain. Transport is plain <see cref="HttpClient"/>; the handler is injectable so the
/// delegated path can be proven offline with a fake endpoint.
/// </summary>
internal sealed class TrustedSigningClient : IDisposable
{
    private const string ApiVersion = "2022-06-15-preview";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly string _host;          // normalized: no scheme, no trailing slash
    private readonly string _account;
    private readonly string _profile;
    private readonly IAzureTokenProvider _tokens;
    private readonly TimeSpan _pollInterval;
    private readonly int _maxPolls;

    public TrustedSigningClient(
        string endpoint, string account, string profile, IAzureTokenProvider tokens,
        HttpMessageHandler? handler = null, TimeSpan? pollInterval = null, int maxPolls = 60)
    {
        _host = NormalizeHost(endpoint);
        _account = account;
        _profile = profile;
        _tokens = tokens;
        // Don't follow redirects (a sign endpoint shouldn't bounce a token-bearing request
        // elsewhere) and cap both the response size and the wall-clock, so a hostile or
        // compromised endpoint can't exhaust memory or hang the sign — mirrors the RFC3161
        // TimestampClient. The correctness of the signature is still gated by SignHash's
        // self-verify against the leaf public key.
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false },
            disposeHandler: handler is null)
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = 1024 * 1024,
        };
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
        _maxPolls = maxPolls;
    }

    /// <summary>
    /// The account the most recent token was issued to, or null if none has been fetched or it
    /// couldn't be read. Display only — see <see cref="JwtIdentity"/>. Kept so a *successful*
    /// run can report who signed; previously this was computed and then discarded unless the
    /// request failed.
    /// </summary>
    internal string? LastIdentity { get; private set; }

    /// <summary>Strips the scheme and any trailing slash so a host or full URI both work.</summary>
    public static string NormalizeHost(string? endpoint)
    {
        var e = (endpoint ?? string.Empty).Trim();
        if (e.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) e = e["https://".Length..];
        else if (e.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) e = e["http://".Length..];
        return e.TrimEnd('/');
    }

    /// <summary>
    /// Sign <paramref name="digest"/> (already hashed) with the given JWA algorithm id
    /// (e.g. <c>RS256</c>). Returns the raw signature plus, when present, the signing
    /// certificate chain the service returns with each response.
    /// </summary>
    public async Task<TrustedSigningResult> SignDigestAsync(byte[] digest, string algorithm, CancellationToken ct)
    {
        var token = await _tokens.GetTokenAsync(ct).ConfigureAwait(false);

        // Rendered once, here, so the raw token never enters error-formatting scope below.
        // Display only — nothing downstream may treat this as verified. See JwtIdentity.
        var identity = JwtIdentity.Describe(token);
        LastIdentity = identity;

        var signUrl =
            $"https://{_host}/codesigningaccounts/{_account}/certificateprofiles/{_profile}/sign?api-version={ApiVersion}";
        var payload = JsonSerializer.Serialize(new
        {
            signatureAlgorithm = algorithm,
            digest = Convert.ToBase64String(digest),
        });

        using var post = new HttpRequestMessage(HttpMethod.Post, signUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        post.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);

        using var postResp = await _http.SendAsync(post, ct).ConfigureAwait(false);
        await ThrowIfError(postResp, identity).ConfigureAwait(false);
        var status = await ReadStatus(postResp).ConfigureAwait(false);

        // Poll the long-running operation until it terminates. Prefer the service's
        // Operation-Location header — but only follow it when it stays on the same https host
        // we posted to. Otherwise a malicious or redirecting response could steer the next
        // request (which re-attaches the Azure bearer token below) to an attacker's host and
        // exfiltrate the token. When the header is absent or off-host, fall back to the
        // status URL built from operationId, which is always same-host https.
        var expectedHost = new Uri(signUrl).Host;
        string? pollUrl = postResp.Headers.TryGetValues("Operation-Location", out var loc)
            ? loc.FirstOrDefault()
            : null;
        if (!IsSameHostHttps(pollUrl, expectedHost))
            pollUrl = null;
        pollUrl ??= status.OperationId is null
            ? null
            : $"https://{_host}/codesigningaccounts/{_account}/certificateprofiles/{_profile}/sign/{status.OperationId}?api-version={ApiVersion}";

        for (int i = 0; !IsTerminal(status.Status) && pollUrl is not null && i < _maxPolls; i++)
        {
            await Task.Delay(_pollInterval, ct).ConfigureAwait(false);

            using var get = new HttpRequestMessage(HttpMethod.Get, pollUrl);
            get.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            using var getResp = await _http.SendAsync(get, ct).ConfigureAwait(false);
            await ThrowIfError(getResp, identity).ConfigureAwait(false);
            status = await ReadStatus(getResp).ConfigureAwait(false);
        }

        if (!string.Equals(status.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Trusted Signing operation did not succeed (status: {status.Status ?? "unknown"}).");
        if (status.Signature is null)
            throw new InvalidOperationException("Trusted Signing succeeded but returned no signature.");

        return new TrustedSigningResult(DecodeBase64(status.Signature), status.SigningCertificate);
    }

    private static bool IsTerminal(string? s) =>
        string.Equals(s, "Succeeded", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(s, "Failed", StringComparison.OrdinalIgnoreCase);

    /// <summary>True only for an absolute https URL on the same host we posted the sign
    /// request to — the poll re-sends the bearer token, so it must not leave that origin.</summary>
    private static bool IsSameHostHttps(string? url, string expectedHost) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps
        && string.Equals(u.Host, expectedHost, StringComparison.OrdinalIgnoreCase);

    private static async Task<SignStatus> ReadStatus(HttpResponseMessage resp)
    {
        var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return new SignStatus();
        return JsonSerializer.Deserialize<SignStatus>(json, JsonOptions) ?? new SignStatus();
    }

    /// <summary>
    /// Turns a failed response into an actionable exception. <paramref name="identity"/> is the
    /// already-rendered description of the account the token was issued to (never the token
    /// itself — it must not reach error-formatting scope), or null when it couldn't be read.
    /// </summary>
    private static async Task ThrowIfError(HttpResponseMessage resp, string? identity)
    {
        if (resp.IsSuccessStatusCode) return;

        var body = Trim(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        // A 401 often carries no body at all; " Detail: " with nothing after it reads like the
        // message was cut off mid-sentence.
        var detail = string.IsNullOrWhiteSpace(body) ? "" : " Detail: " + body;

        // Naming the account is the difference between "some identity lacks a role" and a
        // one-line answer. Without it a user cannot tell whether the token even came from the
        // account they think they are signing with.
        var who = identity is null
            ? "The signing identity"
            : $"The token was issued to {identity}, which";
        var issuedTo = identity is null ? "" : $" The token was issued to {identity}.";

        if (resp.StatusCode == HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException(
                $"Trusted Signing returned 403 Forbidden. {who} likely needs the " +
                "\"Artifact Signing Certificate Profile Signer\" role " +
                "(2837e146-70d7-4cfd-ad55-7efa6464f958) on the certificate profile — role " +
                "assignments can take a few minutes to propagate. If the role is already " +
                "assigned, check the tenant: a token minted in a tenant other than the one " +
                "owning the signing account is rejected no matter which roles it holds." + detail);

        throw new InvalidOperationException(
            $"Trusted Signing request failed ({(int)resp.StatusCode} {resp.StatusCode})." +
            issuedTo + detail);
    }

    private static string Trim(string s) => s.Length > 500 ? s[..500] : s;

    /// <summary>Decode standard or URL-safe base64 (the service has used both).</summary>
    private static byte[] DecodeBase64(string s)
    {
        s = s.Trim().Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String((s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s });
    }

    public void Dispose() => _http.Dispose();

    /// <summary>The poll-able operation envelope returned by both the POST and the GET.</summary>
    private sealed class SignStatus
    {
        public string? OperationId { get; set; }
        public string? Status { get; set; }
        public string? Signature { get; set; }
        public string? SigningCertificate { get; set; }
    }
}
