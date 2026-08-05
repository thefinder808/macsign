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
    public async Task<byte[]> BuildAsync(
        byte[] spcIndirectDataDer, ICredentialSigner credential, SigningOptions options, CancellationToken ct)
    {
        // The encapsulated content is the raw SpcIndirectDataContent SEQUENCE under
        // the SPC content-type OID. (Authenticode requires this NOT be re-wrapped in
        // an OCTET STRING — verified locally by CmsBuilderTests, proven by signtool.)
        var content = new ContentInfo(new Oid(AuthenticodeOids.SpcIndirectDataContent), spcIndirectDataDer);
        var cms = new SignedCms(content, detached: false);

        // Pass the signing key explicitly so a delegating key (PKCS#11 / cloud) works
        // the same as an in-proc PFX key — the private key never has to be in this process.
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, credential.Certificate, credential.SigningKey)
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

        // ComputeSignature is synchronous and calls straight into the signing key, so a
        // credential that signs over the network can only see the caller's token if we hand it
        // over first. Without this, cancelling waits out the whole round-trip.
        using (credential.UseCancellation(ct))
            cms.ComputeSignature(signer);

        // Phase 2: attach an RFC3161 timestamp (over the signer's signature value)
        // as an unsigned attribute, so the signature outlives the cert's validity.
        if (!string.IsNullOrWhiteSpace(options.TimestampUrl))
            await AddTimestampAsync(cms, options.TimestampUrl, ct);

        return cms.Encode();
    }

    private static async Task AddTimestampAsync(SignedCms cms, string timestampUrls, CancellationToken ct)
    {
        // Accept a comma-separated list and try each in order, so a single TSA outage
        // doesn't fail the sign. The first server that issues a valid token wins.
        var urls = timestampUrls.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (urls.Length == 0)
            throw new ArgumentException("No timestamp URL was provided.");

        var signerInfo = cms.SignerInfos[0];
        // A fresh nonce lets ProcessResponse detect a replayed/substituted token on the
        // (usually plaintext-HTTP) TSA channel.
        var request = Rfc3161TimestampRequest.CreateFromSignerInfo(
            signerInfo, HashAlgorithmName.SHA256,
            nonce: RandomNumberGenerator.GetBytes(16),
            requestSignerCertificates: true);

        var errors = new List<string>();
        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                errors.Add($"{url}: not a valid absolute URL");
                continue;
            }
            try
            {
                var token = await new TimestampClient().RequestAsync(request, uri, ct);
                // szOID_RFC3161_counterSign, value = the timestamp token's full SignedData.
                signerInfo.AddUnsignedAttribute(new AsnEncodedData(
                    new Oid(AuthenticodeOids.Rfc3161Timestamp), token.AsSignedCms().Encode()));
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"{uri}: {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            "Every timestamp server failed:\n  " + string.Join("\n  ", errors));
    }
}
