namespace MacSign.Signing.Formats.Pe;

/// <summary>
/// The IMAGHELP <c>CheckSumMappedFile</c> algorithm: a 16-bit ones-complement
/// running sum over the image (treating the CheckSum field itself as zero), folded
/// and added to the file length. <c>signtool verify /pa</c> does not validate this
/// for user-mode PEs, but we compute it correctly anyway.
/// </summary>
internal static class PeChecksum
{
    public static uint Compute(ReadOnlySpan<byte> image, int checksumOffset)
    {
        uint sum = 0;
        int len = image.Length;

        for (int i = 0; i + 1 < len; i += 2)
        {
            // The 4-byte CheckSum field is treated as zero, byte by byte, so the result is
            // correct even when the field isn't 2-byte aligned (an odd e_lfanew).
            byte lo = InChecksumField(i, checksumOffset) ? (byte)0 : image[i];
            byte hi = InChecksumField(i + 1, checksumOffset) ? (byte)0 : image[i + 1];
            uint word = (uint)(lo | (hi << 8));
            sum += word;
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        if ((len & 1) != 0)
        {
            byte last = InChecksumField(len - 1, checksumOffset) ? (byte)0 : image[len - 1];
            sum += last;
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        sum = (sum & 0xFFFF) + (sum >> 16);
        return sum + (uint)len;
    }

    private static bool InChecksumField(int index, int checksumOffset) =>
        index >= checksumOffset && index < checksumOffset + 4;
}
