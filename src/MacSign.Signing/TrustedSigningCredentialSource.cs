namespace MacSign.Signing;

/// <summary>
/// Which Microsoft Entra identity signs, when <see cref="CertMode.TrustedSigning"/> is used.
/// Without this, Azure.Identity's default chain silently picks the first credential that
/// answers — on a Mac that is usually whatever <c>az login</c> last selected, which is not
/// necessarily the account holding the signing role.
/// </summary>
public enum TrustedSigningCredentialSource
{
    /// <summary>
    /// Azure.Identity's <c>DefaultAzureCredential</c> chain: an environment service
    /// principal, a managed identity, <c>az login</c>, and so on. Change which account it
    /// resolves to with <c>az login --tenant &lt;id&gt;</c>, or pin the tenant with
    /// <see cref="SigningOptions.TrustedSigningTenantId"/>.
    /// </summary>
    Default,

    /// <summary>
    /// An account signed in explicitly through the system browser, letting you pick a
    /// specific account rather than inheriting the machine's default. Signing itself never
    /// opens a browser — it re-uses the sign-in recorded in
    /// <see cref="SigningOptions.TrustedSigningAuthRecord"/> and fails with an actionable
    /// message if there is none.
    /// </summary>
    InteractiveBrowser,
}
