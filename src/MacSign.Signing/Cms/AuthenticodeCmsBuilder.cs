using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using MacSign.Signing.Credentials;

namespace MacSign.Signing.Cms;

/// <summary>
/// Builds the Authenticode CMS via the BCL <see cref="SignedCms"/> high-level path.
/// Works in Phase 1 because the PFX private key is in-proc. (HSM/cloud keys, which
/// are not in-proc, need a detached signed-attributes path — Phase 3.)
/// </summary>
internal sealed class AuthenticodeCmsBuilder : ICmsBuilder
{
    public byte[] Build(byte[] spcIndirectDataDer, ICredentialSigner credential, SigningOptions options)
    {
        // The encapsulated content is the raw SpcIndirectDataContent SEQUENCE under
        // the SPC content-type OID. (Authenticode requires this NOT be re-wrapped in
        // an OCTET STRING — verified locally by CmsBuilderTests, proven by signtool.)
        var content = new ContentInfo(new Oid(AuthenticodeOids.SpcIndirectDataContent), spcIndirectDataDer);
        var cms = new SignedCms(content, detached: false);

        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, credential.Certificate)
        {
            DigestAlgorithm = new Oid(AuthenticodeOids.Sha256),
            // Phase 1 uses self-signed test certs, so embed the leaf only. (Real CA
            // chains will switch to ExcludeRoot + explicit intermediates.)
            IncludeOption = X509IncludeOption.EndCertOnly,
        };

        // Authenticode signed attributes. Adding any signed attribute makes the BCL
        // also emit the standard content-type + message-digest attributes.
        signer.SignedAttributes.Add(new AsnEncodedData(
            new Oid(AuthenticodeOids.SpcStatementType), SpcEncoder.StatementTypeValue()));
        signer.SignedAttributes.Add(new AsnEncodedData(
            new Oid(AuthenticodeOids.SpcSpOpusInfo), SpcEncoder.OpusInfoValue(options.Description, options.Url)));

        // Embed any intermediate certs from the credential (none for self-signed).
        foreach (var intermediate in credential.Chain)
            signer.Certificates.Add(intermediate);

        cms.ComputeSignature(signer);
        return cms.Encode();
    }
}
