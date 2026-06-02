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
            // Skip the two 16-bit words of the CheckSum field (treated as zero).
            if (i == checksumOffset || i == checksumOffset + 2)
                continue;

            uint word = (uint)(image[i] | (image[i + 1] << 8));
            sum += word;
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        if ((len & 1) != 0)
        {
            sum += image[len - 1];
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        sum = (sum & 0xFFFF) + (sum >> 16);
        return sum + (uint)len;
    }
}
