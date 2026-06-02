using System.Buffers.Binary;

namespace MacSign.Signing.Formats.Pe;

/// <summary>
/// The handful of byte offsets Authenticode signing needs out of a PE file,
/// parsed by hand (no writes — <c>PEReader</c> is read-only and we hand-roll the
/// mutations). Works for both PE32 and PE32+.
/// </summary>
internal readonly struct PeLayout
{
    /// <summary>File offset of the 4-byte optional-header <c>CheckSum</c> field.</summary>
    public required int ChecksumOffset { get; init; }

    /// <summary>File offset of the 8-byte Certificate Table data-directory entry (RVA + Size).</summary>
    public required int CertDirEntryOffset { get; init; }

    /// <summary>File offset of an existing attribute certificate table, or 0 if unsigned.</summary>
    public required int CertTableOffset { get; init; }

    /// <summary>Size of an existing attribute certificate table, or 0 if unsigned.</summary>
    public required int CertTableSize { get; init; }

    public bool HasCertTable => CertTableOffset > 0 && CertTableSize > 0;

    /// <summary>Parse the layout, validating just enough structure to be safe.</summary>
    public static PeLayout Parse(ReadOnlySpan<byte> file)
    {
        if (file.Length < 0x40)
            throw new InvalidDataException("File is too small to be a PE image.");
        if (file[0] != (byte)'M' || file[1] != (byte)'Z')
            throw new InvalidDataException("Not a PE image (missing 'MZ').");

        int peHeaderOffset = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(0x3C, 4));
        if (peHeaderOffset <= 0 || peHeaderOffset + 24 > file.Length)
            throw new InvalidDataException("Invalid PE header offset.");
        if (file[peHeaderOffset] != (byte)'P' || file[peHeaderOffset + 1] != (byte)'E'
            || file[peHeaderOffset + 2] != 0 || file[peHeaderOffset + 3] != 0)
            throw new InvalidDataException("Not a PE image (missing 'PE\\0\\0' signature).");

        int optHeaderStart = peHeaderOffset + 24;
        if (optHeaderStart + 2 > file.Length)
            throw new InvalidDataException("Truncated optional header.");

        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(optHeaderStart, 2));
        // PE32 = 0x10B, PE32+ = 0x20B. CheckSum is at +64 for both; the data
        // directory array begins at +96 (PE32) or +112 (PE32+).
        int dataDirStart = magic == 0x20B ? optHeaderStart + 112 : optHeaderStart + 96;

        int checksumOffset = optHeaderStart + 64;
        const int IMAGE_DIRECTORY_ENTRY_SECURITY = 4;
        int certDirEntryOffset = dataDirStart + 8 * IMAGE_DIRECTORY_ENTRY_SECURITY;
        if (certDirEntryOffset + 8 > file.Length)
            throw new InvalidDataException("Optional header has no Certificate Table data directory.");

        int certTableOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(certDirEntryOffset, 4));
        int certTableSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(certDirEntryOffset + 4, 4));

        return new PeLayout
        {
            ChecksumOffset = checksumOffset,
            CertDirEntryOffset = certDirEntryOffset,
            CertTableOffset = certTableOffset,
            CertTableSize = certTableSize,
        };
    }
}
