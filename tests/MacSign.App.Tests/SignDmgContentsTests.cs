using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MacSign.App.Services;
using Xunit;

namespace MacSign.App.Tests;

public class SignDmgContentsTests
{
    private const string Sha = "ABCDEF0123456789ABCDEF0123456789ABCDEF01";
    private const string IdName = "Developer ID Application: Nathaniel Graham (Q6LRJQSA42)";
    private static string FindIdentityOutput =>
        $"  1) {Sha} \"{IdName}\"\n     1 valid identities found\n";

    private static string MakeDmg()
    {
        var dir = Path.Combine(Path.GetTempPath(), "macsign-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dmg = Path.Combine(dir, "Disk" + Guid.NewGuid().ToString("N")[..6] + ".dmg");
        File.WriteAllText(dmg, "not-a-real-dmg");
        return dmg;
    }

    [Fact]
    public async Task SignDmgContents_converts_signs_flagged_app_and_reseals()
    {
        var dmg = MakeDmg();
        var f = new FakeRunner();
        f.Respond = (file, args) =>
        {
            var a = args.ToList();
            if (file.EndsWith("security", StringComparison.Ordinal))
                return new ProcessResult(0, FindIdentityOutput, "", false);
            if (a.Contains("attach"))
            {
                var mp = a[a.IndexOf("-mountpoint") + 1];
                Directory.CreateDirectory(Path.Combine(mp, "Demo.app"));
                return new ProcessResult(0, "", "", false);
            }
            if (a.Contains("convert") && a.Contains("UDZO"))
            {
                File.WriteAllText(a[a.IndexOf("-o") + 1], "recompressed");
                return new ProcessResult(0, "", "", false);
            }
            if (a.Contains("--verify")) return new ProcessResult(1, "", "not signed at all", false);
            return new ProcessResult(0, "", "", false); // convert UDRW, resize, -d, --sign, detach
        };

        var r = await new AppleSigningService(f)
            .SignDmgContentsAsync(dmg, new SigningIdentity(Sha, IdName), null, null, default);

        Assert.True(r.Success);
        Assert.Contains(f.Calls, c => c.File.EndsWith("hdiutil") && c.Args.Contains("convert") && c.Args.Contains("UDRW"));
        Assert.Contains(f.Calls, c => c.File.EndsWith("hdiutil") && c.Args.Contains("convert") && c.Args.Contains("UDZO"));
        Assert.Contains(f.Calls, c => c.File.EndsWith("hdiutil") && c.Args.Contains("attach") && c.Args.Contains("-readwrite"));
        Assert.Contains(f.Calls, c => c.File.EndsWith("hdiutil") && c.Args.Contains("detach"));
        Assert.Contains(f.Calls, c => c.File.EndsWith("codesign")
            && c.Args.Contains("--sign") && c.Args.Contains(Sha)
            && c.Args.Contains("--options") && c.Args.Contains("runtime") && c.Args.Contains("--deep")
            && c.Args[^1].EndsWith("Demo.app", StringComparison.Ordinal));
        Assert.Equal("recompressed", File.ReadAllText(dmg)); // original .dmg replaced in place
    }

    [Fact]
    public async Task SignDmgContents_skips_already_signed_hardened_app()
    {
        var dmg = MakeDmg();
        var f = new FakeRunner();
        f.Respond = (file, args) =>
        {
            var a = args.ToList();
            if (file.EndsWith("security", StringComparison.Ordinal)) return new ProcessResult(0, FindIdentityOutput, "", false);
            if (a.Contains("attach"))
            {
                var mp = a[a.IndexOf("-mountpoint") + 1];
                Directory.CreateDirectory(Path.Combine(mp, "Good.app"));
                return new ProcessResult(0, "", "", false);
            }
            if (a.Contains("convert") && a.Contains("UDZO")) { File.WriteAllText(a[a.IndexOf("-o") + 1], "x"); return new ProcessResult(0, "", "", false); }
            if (a.Contains("--verify")) return new ProcessResult(0, "", "valid on disk", false); // already signed
            if (a.Contains("-d")) return new ProcessResult(0, "flags=0x10000(runtime)", "", false); // hardened
            return new ProcessResult(0, "", "", false);
        };

        var r = await new AppleSigningService(f)
            .SignDmgContentsAsync(dmg, new SigningIdentity(Sha, IdName), null, null, default);

        Assert.True(r.Success);
        Assert.DoesNotContain(f.Calls, c => c.File.EndsWith("codesign") && c.Args.Contains("--sign"));
    }

    [Fact]
    public async Task SignDmgContents_detaches_when_a_sign_fails()
    {
        var dmg = MakeDmg();
        var f = new FakeRunner();
        f.Respond = (file, args) =>
        {
            var a = args.ToList();
            if (file.EndsWith("security", StringComparison.Ordinal)) return new ProcessResult(0, FindIdentityOutput, "", false);
            if (a.Contains("attach"))
            {
                var mp = a[a.IndexOf("-mountpoint") + 1];
                Directory.CreateDirectory(Path.Combine(mp, "Demo.app"));
                return new ProcessResult(0, "", "", false);
            }
            if (a.Contains("--verify")) return new ProcessResult(1, "", "not signed", false); // needs signing
            if (a.Contains("--sign")) return new ProcessResult(1, "", "errSecInternalComponent", false); // signing fails
            return new ProcessResult(0, "", "", false);
        };

        var r = await new AppleSigningService(f)
            .SignDmgContentsAsync(dmg, new SigningIdentity(Sha, IdName), null, null, default);

        Assert.False(r.Success);
        Assert.Contains(f.Calls, c => c.File.EndsWith("hdiutil") && c.Args.Contains("detach")); // released despite failure
    }
}
