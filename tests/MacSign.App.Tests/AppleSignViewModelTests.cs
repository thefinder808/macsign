using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MacSign.App.Services;
using MacSign.App.ViewModels;
using Xunit;

namespace MacSign.App.Tests;

public class AppleSignViewModelTests
{
    private const string Sha = "ABCDEF0123456789ABCDEF0123456789ABCDEF01";
    private const string IdName = "Developer ID Application: Nathaniel Graham (Q6LRJQSA42)";
    private static string FindIdentityOutput => $"  1) {Sha} \"{IdName}\"\n     1 valid identities found\n";

    private static string MakeDmg()
    {
        var dir = Path.Combine(Path.GetTempPath(), "macsign-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dmg = Path.Combine(dir, "Disk" + Guid.NewGuid().ToString("N")[..6] + ".dmg");
        File.WriteAllText(dmg, "not-a-real-dmg");
        return dmg;
    }

    private static SettingsStore TempStore() =>
        new(Path.Combine(Path.GetTempPath(), "macsign-vmtest-" + Guid.NewGuid().ToString("N")));

    // Fake that models: inner .app unsigned (verify fails), the .dmg itself verifies OK.
    private static FakeRunner UnsignedContentsDmg()
    {
        var f = new FakeRunner();
        f.Respond = (file, args) =>
        {
            var a = args.ToList();
            if (file.EndsWith("security", StringComparison.Ordinal)) return new ProcessResult(0, FindIdentityOutput, "", false);
            if (a.Contains("attach")) { var mp = a[a.IndexOf("-mountpoint") + 1]; Directory.CreateDirectory(Path.Combine(mp, "Demo.app")); return new ProcessResult(0, "", "", false); }
            if (a.Contains("convert") && a.Contains("UDZO")) { File.WriteAllText(a[a.IndexOf("-o") + 1], "x"); return new ProcessResult(0, "", "", false); }
            if (a.Contains("--verify")) return a[^1].EndsWith(".app", StringComparison.Ordinal)
                ? new ProcessResult(1, "", "not signed", false)
                : new ProcessResult(0, "", "valid on disk", false);
            return new ProcessResult(0, "", "", false);
        };
        return f;
    }

    [Fact]
    public async Task Preflight_block_on_dmg_offers_sign_contents()
    {
        var dmg = MakeDmg();
        var vm = new AppleSignViewModel(new AppData(), TempStore(), new AppleSigningService(UnsignedContentsDmg()))
        { TargetPath = dmg, SelectedIdentity = new SigningIdentity(Sha, IdName), Notarize = true, NotaryProfile = "p" };

        await vm.RunCommand.ExecuteAsync(null);

        Assert.True(vm.IsDone);
        Assert.True(vm.ShowSignContents);
        Assert.True(vm.ShowNotarizeAnyway);
    }

    [Fact]
    public async Task SignContentsAndContinue_signs_contents_before_sealing_the_dmg()
    {
        var dmg = MakeDmg();
        var f = UnsignedContentsDmg();
        var vm = new AppleSignViewModel(new AppData(), TempStore(), new AppleSigningService(f))
        { TargetPath = dmg, SelectedIdentity = new SigningIdentity(Sha, IdName), Notarize = true, NotaryProfile = "p" };

        await vm.SignContentsAndContinueCommand.ExecuteAsync(null);

        int firstConvert = f.Calls.FindIndex(c => c.File.EndsWith("hdiutil") && c.Args.Contains("convert") && c.Args.Contains("UDRW"));
        int dmgSign = f.Calls.FindIndex(c => c.File.EndsWith("codesign") && c.Args.Contains("--sign") && c.Args[^1] == dmg);
        Assert.True(firstConvert >= 0, "contents conversion ran");
        Assert.True(dmgSign >= 0, "the .dmg itself was signed");
        Assert.True(firstConvert < dmgSign, "contents were signed before the .dmg was sealed");
    }
}
