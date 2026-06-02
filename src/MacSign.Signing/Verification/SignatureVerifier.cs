using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using MacSign.Signing.Cms;
using MacSign.Signing.Formats;

namespace MacSign.Signing.Verification;

/// <summary>
/// Verifies an Authenticode signature and reports what it finds. Reports signature
/// integrity (which is authoritative on macOS) separately from chain trust (which
/// usually can't be established on macOS — the Microsoft roots aren't in the system store).
/// </summary>
public static class SignatureVerifier
{
    public static VerifyReport Verify(string filePath)
    {
        byte[] fileBytes;
        try { fileBytes = File.ReadAllBytes(filePath); }
        catch (Exception ex) { return VerifyReport.Failed("Couldn't read file: " + ex.Message); }
        return Verify(fileBytes, filePath);
    }

    internal static VerifyReport Verify(byte[] fileBytes, string fileName)
    {
        try
        {
            return VerifyCore(fileBytes, fileName);
        }
        catch (Exception ex)
        {
            // A signing tool is fed hostile/malformed files on purpose; verify must report a
            // failure, never throw out of the public entry point.
            return VerifyReport.Failed("Couldn't verify the signature: " + ex.Message);
        }
    }

    private static VerifyReport VerifyCore(byte[] fileBytes, string fileName)
    {
        var format = FormatRegistry.For(fileName);
        if (format is null)
            return VerifyReport.Failed($"{Path.GetExtension(fileName)} verification isn't supported yet.");

        if (!format.TryExtractSignature(fileBytes, out var pkcs7))
            return VerifyReport.Unsigned();

        var cms = new SignedCms();
        try { cms.Decode(pkcs7); }
        catch (Exception ex) { return VerifyReport.Failed("Malformed signature: " + ex.Message); }

        if (cms.SignerInfos.Count == 0)
            return VerifyReport.Failed("Signature contains no signer.");

        var signer = cms.SignerInfos[0];
        var cert = signer.Certificate;

        bool signerSignatureValid;
        try { cms.CheckSignature(verifySignatureOnly: true); signerSignatureValid = true; }
        catch { signerSignatureValid = false; }

        // File integrity: the CMS must encapsulate SpcIndirectDataContent (a real
        // Authenticode verifier rejects any other content type), and the digest it embeds
        // must match a fresh digest of the file.
        bool digestMatches =
            cms.ContentInfo.ContentType?.Value == AuthenticodeOids.SpcIndirectDataContent
            && SpcDigest.TryReadSha256(cms.ContentInfo.Content, out var embedded)
            && embedded.AsSpan().SequenceEqual(format.ComputeDigest(fileBytes));

        var (chainTrusted, chainNote) = BuildChain(cert);

        return new VerifyReport
        {
            IsSigned = true,
            SignatureValid = signerSignatureValid && digestMatches,
            SignerSubject = cert?.Subject,
            SignerIssuer = cert?.Issuer,
            SignerSerialNumber = cert?.SerialNumber,
            Timestamp = TryGetTimestamp(signer),
            ChainTrusted = chainTrusted,
            ChainNote = chainNote,
        };
    }

    private static DateTimeOffset? TryGetTimestamp(SignerInfo signer)
    {
        foreach (var attr in signer.UnsignedAttributes)
        {
            if (attr.Oid?.Value != AuthenticodeOids.Rfc3161Timestamp)
                continue;
            // Decode AND cryptographically validate the token: its own signature must verify
            // and its imprint must bind THIS signer's signature value. The unsigned-attribute
            // bag isn't covered by the signature, so an unvalidated time is forgeable.
            if (Rfc3161TimestampToken.TryDecode(attr.Values[0].RawData, out var token, out _)
                && token.VerifySignatureForSignerInfo(signer, out _))
                return token.TokenInfo.Timestamp;
        }
        return null;
    }

    private static (bool Trusted, string? Note) BuildChain(X509Certificate2? cert)
    {
        if (cert is null)
            return (false, "No signer certificate was embedded.");

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        if (chain.Build(cert))
            return (true, null);

        return (false,
            "Chain not validated on this OS — Microsoft code-signing roots are typically " +
            "absent from the macOS trust store. This does NOT mean the signature is invalid; " +
            "see SignatureValid for signature integrity.");
    }
}
