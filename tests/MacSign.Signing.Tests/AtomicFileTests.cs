using MacSign.Signing.Engine;

namespace MacSign.Signing.Tests;

public class AtomicFileTests
{
    // A pre-planted symlink at the old predictable temp path ("file.signtmp") must not be
    // followed: the write goes to a randomized name opened with CreateNew, so an attacker
    // can't redirect the signed-file write through a symlink to clobber another file.
    [Fact]
    public void Write_does_not_follow_a_symlink_planted_at_the_legacy_temp_path()
    {
        using var tmp = new TempDir();
        var target = Path.Combine(tmp.Path, "app.dll");
        File.WriteAllBytes(target, new byte[] { 1, 2, 3 });

        var victim = Path.Combine(tmp.Path, "victim.txt");
        File.WriteAllText(victim, "IMPORTANT");
        File.CreateSymbolicLink(target + ".signtmp", victim);   // the attacker's pre-plant

        AtomicFile.Write(target, new byte[] { 9, 9, 9, 9 });

        Assert.Equal("IMPORTANT", File.ReadAllText(victim));            // victim untouched
        Assert.Equal(new byte[] { 9, 9, 9, 9 }, File.ReadAllBytes(target)); // real write happened
    }

    [Fact]
    public async Task WriteAsync_roundtrips_content()
    {
        using var tmp = new TempDir();
        var target = Path.Combine(tmp.Path, "x.bin");
        await AtomicFile.WriteAsync(target, new byte[] { 7, 7, 7 });
        Assert.Equal(new byte[] { 7, 7, 7 }, await File.ReadAllBytesAsync(target));
    }
}
