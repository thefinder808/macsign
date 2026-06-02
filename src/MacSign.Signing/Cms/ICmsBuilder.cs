using MacSign.Signing.Credentials;

namespace MacSign.Signing.Cms;

/// <summary>
/// Assembles the Authenticode PKCS#7/CMS blob from a pre-built
/// <c>SpcIndirectDataContent</c> and a credential. Behind an interface so the
/// hand-rolled ASN.1 fallback can swap in if the BCL's framing is ever rejected
/// by <c>signtool</c> — without touching formats or credentials.
/// </summary>
internal interface ICmsBuilder
{
    byte[] Build(byte[] spcIndirectDataDer, ICredentialSigner credential, SigningOptions options);
}
