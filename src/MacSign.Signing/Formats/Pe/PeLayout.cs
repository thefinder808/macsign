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
        if (magic != 0x10B && magic != 0x20B)
            throw new InvalidDataException("Unsupported PE optional-header magic (not PE32 or PE32+).");

        // PE32 = 0x10B, PE32+ = 0x20B. CheckSum is at +64 for both; the data-directory
        // array begins at +96 (PE32) or +112 (PE32+), with NumberOfRvaAndSizes just before it.
        int dataDirStart = magic == 0x20B ? optHeaderStart + 112 : optHeaderStart + 96;

        int checksumOffset = optHeaderStart + 64;
        const int IMAGE_DIRECTORY_ENTRY_SECURITY = 4;
        int certDirEntryOffset = dataDirStart + 8 * IMAGE_DIRECTORY_ENTRY_SECURITY;
        if (certDirEntryOffset + 8 > file.Length)
            throw new InvalidDataException("Optional header has no Certificate Table data directory.");

        // The COFF SizeOfOptionalHeader (the 2 bytes just before the optional header) declares
        // how far the optional header — which holds CheckSum, NumberOfRvaAndSizes and the data
        // directories — actually extends. The Security data-directory entry is the furthest
        // byte we read/write inside it; if a malformed image declares a header too small to
        // contain that entry, reject it rather than mutate bytes outside the declared header.
        // (The read at peHeaderOffset+20 is safe: peHeaderOffset+24 <= file.Length above.)
        ushort sizeOfOptionalHeader = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(peHeaderOffset + 20, 2));
        if ((long)certDirEntryOffset + 8 > (long)optHeaderStart + sizeOfOptionalHeader)
            throw new InvalidDataException("Declared optional-header size does not cover the Certificate Table data directory.");

        // NumberOfRvaAndSizes declares how many data directories actually exist; the
        // Security directory (index 4) is present only when the file declares more than 4.
        // Without this check, a PE with fewer directories has section-table bytes read as a
        // bogus cert table.
        uint numberOfRvaAndSizes = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(dataDirStart - 4, 4));

        int certTableOffset = 0, certTableSize = 0;
        if (numberOfRvaAndSizes > IMAGE_DIRECTORY_ENTRY_SECURITY)
        {
            int rawOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(certDirEntryOffset, 4));
            int rawSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(certDirEntryOffset + 4, 4));

            // Recognise an attribute certificate table only when it is the well-formed
            // trailing region Authenticode mandates: 8-aligned, at/after the directory entry,
            // at least a WIN_CERTIFICATE header, and ending exactly at EOF. Anything else (a
            // bogus directory, or an overlapping/non-tail offset) is treated as unsigned, so
            // it can neither crash the digest slice nor be re-signed into a corrupt artifact.
            if (rawOffset >= certDirEntryOffset + 8
                && rawOffset % 8 == 0
                && rawSize >= 8
                && (long)rawOffset + rawSize == file.Length)
            {
                certTableOffset = rawOffset;
                certTableSize = rawSize;
            }
        }

        return new PeLayout
        {
            ChecksumOffset = checksumOffset,
            CertDirEntryOffset = certDirEntryOffset,
            CertTableOffset = certTableOffset,
            CertTableSize = certTableSize,
        };
    }
}
