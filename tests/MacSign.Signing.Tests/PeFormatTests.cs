using MacSign.Signing.Formats.Pe;

namespace MacSign.Signing.Tests;

public class PeFormatTests
{
    [Fact]
    public void Digest_is_32_bytes_and_deterministic()
    {
        var pe = new PeFormat();
        var bytes = FixturePe.UnsignedBytes();

        var d1 = pe.ComputeDigest(bytes);
        var d2 = pe.ComputeDigest((byte[])bytes.Clone());

        Assert.Equal(32, d1.Length);
        Assert.Equal(d1, d2);
    }

    [Fact]
    public void Digest_excludes_the_checksum_field()
    {
        var pe = new PeFormat();
        var bytes = FixturePe.UnsignedBytes();
        var layout = PeLayout.Parse(bytes);

        var baseline = pe.ComputeDigest(bytes);

        var mutated = (byte[])bytes.Clone();
        mutated[layout.ChecksumOffset] ^= 0xFF;
        mutated[layout.ChecksumOffset + 3] ^= 0xFF;

        Assert.Equal(baseline, pe.ComputeDigest(mutated)); // checksum bytes are not hashed
    }

    [Fact]
    public void Digest_excludes_the_certificate_table_directory_entry()
    {
        var pe = new PeFormat();
        var bytes = FixturePe.UnsignedBytes();
        var layout = PeLayout.Parse(bytes);

        var baseline = pe.ComputeDigest(bytes);

        var mutated = (byte[])bytes.Clone();
        for (int i = 0; i < 8; i++)
            mutated[layout.CertDirEntryOffset + i] ^= 0xFF;

        Assert.Equal(baseline, pe.ComputeDigest(mutated)); // the 8-byte security dir entry is not hashed
    }

    [Fact]
    public void Digest_changes_when_the_body_changes()
    {
        var pe = new PeFormat();
        var bytes = FixturePe.UnsignedBytes();

        var baseline = pe.ComputeDigest(bytes);

        var mutated = (byte[])bytes.Clone();
        mutated[bytes.Length / 2] ^= 0xFF; // a normal, hashed byte

        Assert.NotEqual(baseline, pe.ComputeDigest(mutated));
    }

    [Fact]
    public void Unsigned_file_reports_no_signature()
    {
        var pe = new PeFormat();
        Assert.False(pe.TryExtractSignature(FixturePe.UnsignedBytes(), out _));
    }
}
