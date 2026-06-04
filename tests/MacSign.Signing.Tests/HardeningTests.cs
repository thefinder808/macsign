using System.Buffers.Binary;
using MacSign.Signing.Formats.Pe;
using MacSign.Signing.Verification;

namespace MacSign.Signing.Tests;

/// <summary>
/// Robustness of the verify entry point and the PE parser against malformed / hostile
/// input. A signing tool is deliberately fed adversarial binaries, so verify must never
/// throw and the parser must not read garbage as an existing signature.
/// </summary>
public class HardeningTests
{
    [Fact]
    public void Verify_returns_a_failure_instead_of_throwing_on_a_missing_file()
    {
        var missing = Path.Combine(Path.GetTempPath(), "macsign-missing-" + Guid.NewGuid().ToString("N") + ".dll");

        var r = SignatureVerifier.Verify(missing); // must not throw

        Assert.NotNull(r.Error);
        Assert.False(r.SignatureValid);
    }

    [Fact]
    public void ComputeDigest_does_not_throw_when_the_cert_table_overlaps_the_headers()
    {
        var bytes = FixturePe.UnsignedBytes();
        int certEntry = SecurityDirEntryOffset(bytes);
        // A Security directory whose offset lies *before* the directory entry: the value
        // that used to make ComputeDigest slice a negative range and throw.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(certEntry, 4), 64);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(certEntry + 4, 4), 32);

        var digest = new PeFormat().ComputeDigest(bytes); // must not throw

        Assert.Equal(32, digest.Length); // treated as unsigned, hashed cleanly
    }

    [Fact]
    public void Parse_ignores_the_security_directory_when_fewer_than_five_data_directories_exist()
    {
        var bytes = FixturePe.UnsignedBytes();
        int certEntry = SecurityDirEntryOffset(bytes);
        int numRvaOffset = certEntry - 8 * 4 - 4; // NumberOfRvaAndSizes sits just before the array
        // Declare only 2 data directories, but leave nonzero garbage in the (now absent) slot 4.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(numRvaOffset, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(certEntry, 4), 0x4000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(certEntry + 4, 4), 0x0200);

        var layout = PeLayout.Parse(bytes);

        Assert.False(layout.HasCertTable);
    }

    [Fact]
    public void Parse_rejects_a_pe_whose_optional_header_size_excludes_the_cert_directory()
    {
        var bytes = FixturePe.UnsignedBytes();
        int pe = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x3C, 4));
        // Shrink the COFF SizeOfOptionalHeader so the declared optional header ends long
        // before the Security data-directory entry MacSign would write — a malformed image
        // that must be rejected rather than mutated outside its declared optional header.
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(pe + 20, 2), 2);

        Assert.Throws<InvalidDataException>(() => PeLayout.Parse(bytes));
    }

    [Fact]
    public async Task Signing_a_pe_with_a_too_small_optional_header_fails_and_leaves_the_file_unchanged()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);

        var bytes = FixturePe.UnsignedBytes();
        int pe = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x3C, 4));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(pe + 20, 2), 2); // SizeOfOptionalHeader

        var file = Path.Combine(tmp.Path, "tooSmallOptHeader.dll");
        await File.WriteAllBytesAsync(file, bytes);
        var before = await File.ReadAllBytesAsync(file);

        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        var result = await signer.SignAsync(tmp.Path, file, options);

        Assert.False(result.Success);                                  // rejected cleanly
        Assert.Equal(before, await File.ReadAllBytesAsync(file));      // hostile input untouched
    }

    [Fact]
    public async Task Signing_a_pe_with_a_malformed_trailing_cert_table_strips_it_and_verifies()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);

        // Build a PE whose Security directory points at a well-formed-looking 8-aligned
        // trailing table whose bCertificate is NOT valid ASN.1. The existing-signature gate
        // rejects it (so the file is signed), but the layout sees a cert table — re-signing
        // must STRIP it, not leave it embedded (which would self-invalidate the signature).
        var baseBytes = FixturePe.UnsignedBytes();
        int origLen = baseBytes.Length;
        int tableOffset = (origLen + 7) & ~7; // 8-aligned, like a real attribute cert table
        var bytes = new byte[tableOffset + 16];
        Array.Copy(baseBytes, bytes, origLen); // [origLen, tableOffset) stays zero (alignment pad)
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tableOffset, 4), 16);      // dwLength
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(tableOffset + 4, 2), 0x0200);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(tableOffset + 6, 2), 0x0002);
        for (int i = tableOffset + 8; i < bytes.Length; i++) bytes[i] = 0xFF; // not ASN.1
        int certEntry = SecurityDirEntryOffset(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(certEntry, 4), (uint)tableOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(certEntry + 4, 4), 16);

        var file = Path.Combine(tmp.Path, "malformed.dll");
        await File.WriteAllBytesAsync(file, bytes);

        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        var result = await signer.SignAsync(tmp.Path, file, options);
        Assert.True(result.Success, result.Error);

        var r = SignatureVerifier.Verify(file);
        Assert.True(r.IsSigned);
        Assert.True(r.SignatureValid, r.Error);
    }

    /// <summary>File offset of the 8-byte Certificate Table (Security) data-directory entry.</summary>
    private static int SecurityDirEntryOffset(byte[] b)
    {
        int pe = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0x3C, 4));
        int optStart = pe + 24;
        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(optStart, 2));
        int dataDirStart = magic == 0x20B ? optStart + 112 : optStart + 96;
        return dataDirStart + 8 * 4; // IMAGE_DIRECTORY_ENTRY_SECURITY = 4
    }
}
