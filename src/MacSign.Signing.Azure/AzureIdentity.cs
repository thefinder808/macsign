namespace MacSign.Signing.Azure;

/// <summary>
/// Answers "which account would sign right now?" without signing anything.
/// <para>
/// The default credential chain resolves to whichever account the machine happens to be signed
/// in as, and that answer is only knowable by actually acquiring a token — which is why it
/// can't simply be displayed. This makes it a deliberate, one-call operation instead.
/// </para>
/// <para>
/// It fetches a token and reads it; it does <b>not</b> build a credential signer, because that
/// runs the certificate-discovery probe — a real Trusted Signing operation against the
/// account's quota. Checking who you are shouldn't cost a signature.
/// </para>
/// </summary>
public static class AzureIdentity
{
    /// <summary>
    /// Returns something like <c>user@contoso.com (tenant 1234…)</c>, or null when no identity
    /// could be read. Throws whatever token acquisition throws — a caller asking this question
    /// wants the sign-in failure surfaced, not swallowed.
    /// <para>
    /// Display only, from an unvalidated token: never gate a decision on it.
    /// </para>
    /// </summary>
    public static Task<string?> DescribeAsync(SigningOptions options, CancellationToken ct = default) =>
        DescribeAsync(new DefaultAzureTokenProvider(options), ct);

    internal static async Task<string?> DescribeAsync(IAzureTokenProvider tokens, CancellationToken ct)
    {
        var token = await tokens.GetTokenAsync(ct).ConfigureAwait(false);
        return JwtIdentity.Describe(token);
    }
}
