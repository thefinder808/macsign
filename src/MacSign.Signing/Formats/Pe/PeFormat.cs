using System.Buffers.Binary;
using System.Formats.Asn1;
using System.Security.Cryptography;
using MacSign.Signing.Cms;

namespace MacSign.Signing.Formats.Pe;

/// <summary>
/// Authenticode signing for PE images (<c>.exe</c>/<c>.dll</c>/<c>.sys</c>),
/// including managed assemblies. Computes the Authenticode digest with the
/// mandated byte exclusions, embeds the signature in the attribute certificate
/// table, and recomputes the optional-header checksum.
/// </summary>
internal sealed class PeFormat : ISignatureFormat
{
    private static readonly string[] Extensions = [".exe", ".dll", ".sys"];

    public bool CanHandle(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public byte[] ComputeDigest(byte[] fileBytes)
    {
        var layout = PeLayout.Parse(fileBytes);
        var file = fileBytes.AsSpan();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        // [0, CheckSum) — then skip the 4-byte CheckSum field.
        hash.AppendData(file[..layout.ChecksumOffset]);
        // [after CheckSum, cert-dir entry) — then skip the 8-byte entry.
        hash.AppendData(file[(layout.ChecksumOffset + 4)..layout.CertDirEntryOffset]);

        int afterEntry = layout.CertDirEntryOffset + 8;
        if (layout.HasCertTable)
        {
            // Exclude the existing attribute cert table; hash before and after it.
            hash.AppendData(file[afterEntry..layout.CertTableOffset]);
            int tail = layout.CertTableOffset + layout.CertTableSize;
            if (tail < file.Length)
                hash.AppendData(file[tail..]);
        }
        else
        {
            // Unsigned: hash to EOF, then the zero padding that will precede the
            // (8-byte-aligned) cert table once we embed it.
            hash.AppendData(file[afterEntry..]);
            int pad = (8 - (file.Length % 8)) % 8;
            if (pad > 0)
                hash.AppendData(new byte[pad]);
        }

        return hash.GetHashAndReset();
    }

    public byte[] BuildSpcIndirectData(byte[] fileDigest) =>
        SpcEncoder.BuildPeIndirectData(fileDigest);

    public byte[] Embed(byte[] fileBytes, byte[] pkcs7Der)
    {
        var layout = PeLayout.Parse(fileBytes);
        // If a (trailing, 8-aligned) attribute certificate table is already present, replace
        // it rather than appending after it — a leftover table would fall inside the hashed
        // region at verify time and invalidate the new signature.
        int fileSize = layout.HasCertTable ? layout.CertTableOffset : fileBytes.Length;
        int tableOffset = Align8(fileSize);

        int sigLen = pkcs7Der.Length;
        int certPad = (8 - (sigLen % 8)) % 8;
        int dwLength = 8 + sigLen + certPad; // WIN_CERTIFICATE header (8) + padded PKCS#7

        var result = new byte[tableOffset + dwLength];
        Array.Copy(fileBytes, result, fileSize);
        // [fileSize, tableOffset) is already zero (alignment padding, in the digest).

        // WIN_CERTIFICATE { DWORD dwLength; WORD wRevision; WORD wCertificateType; BYTE[] }
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(tableOffset, 4), (uint)dwLength);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(tableOffset + 4, 2), 0x0200); // WIN_CERT_REVISION_2_0
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(tableOffset + 6, 2), 0x0002); // WIN_CERT_TYPE_PKCS_SIGNED_DATA
        Array.Copy(pkcs7Der, 0, result, tableOffset + 8, sigLen);
        // [tableOffset+8+sigLen, end) is already zero (cert padding).

        // Point the Certificate Table data directory at the new table.
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(layout.CertDirEntryOffset, 4), (uint)tableOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(layout.CertDirEntryOffset + 4, 4), (uint)dwLength);

        // Recompute the optional-header checksum over the final image.
        uint checksum = PeChecksum.Compute(result, layout.ChecksumOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(layout.ChecksumOffset, 4), checksum);

        return result;
    }

    public bool TryExtractSignature(byte[] fileBytes, out byte[] pkcs7Der)
    {
        pkcs7Der = [];
        PeLayout layout;
        try { layout = PeLayout.Parse(fileBytes); }
        catch { return false; }

        if (!layout.HasCertTable || layout.CertTableSize < 8)
            return false;
        long end = (long)layout.CertTableOffset + layout.CertTableSize;
        if (layout.CertTableOffset + 8 > fileBytes.Length || end > fileBytes.Length)
            return false;

        int bCertStart = layout.CertTableOffset + 8;
        int bCertLen = layout.CertTableSize - 8;
        if (bCertLen <= 0)
            return false;

        try
        {
            // The bCertificate is the (zero-padded) DER PKCS#7; read exactly one
            // encoded value so trailing alignment padding is ignored.
            var reader = new AsnReader(new ReadOnlyMemory<byte>(fileBytes, bCertStart, bCertLen), AsnEncodingRules.BER);
            pkcs7Der = reader.PeekEncodedValue().ToArray();
            return pkcs7Der.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public bool TryRemoveSignature(byte[] fileBytes, out byte[] unsignedBytes)
    {
        var layout = PeLayout.Parse(fileBytes);
        if (!layout.HasCertTable)
        {
            unsignedBytes = fileBytes;
            return false;
        }

        // Drop the trailing attribute cert table, zero its data-directory entry, and
        // recompute the optional-header checksum over the now-unsigned image.
        var result = fileBytes[..layout.CertTableOffset];
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(layout.CertDirEntryOffset, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(layout.CertDirEntryOffset + 4, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(
            result.AsSpan(layout.ChecksumOffset, 4), PeChecksum.Compute(result, layout.ChecksumOffset));

        unsignedBytes = result;
        return true;
    }

    private static int Align8(int value) => (value + 7) & ~7;
}
