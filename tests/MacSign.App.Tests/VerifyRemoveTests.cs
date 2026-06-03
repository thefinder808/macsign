using System;
using System.IO;
using System.Threading.Tasks;
using MacSign.App.Services;
using MacSign.App.ViewModels;
using MacSign.Signing.Verification;
using Xunit;

namespace MacSign.App.Tests;

public class EngineRemoveTests
{
    [Fact]
    public void Remove_is_no_throw_on_unsupported_file()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "macsign-rm-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(tmp, "not a signable file");

        var r = new EngineService().Remove(tmp);

        Assert.False(r.Removed);
        Assert.NotNull(r.Error); // NotSupportedException captured, not thrown
    }
}

// Fakeable engine: overrides Verify/Remove (made virtual in Task 1).
internal sealed class FakeEngine : EngineService
{
    public VerifyReport NextReport = new() { IsSigned = true, SignatureValid = true };
    public RemoveOutcome NextRemove = new(true, null);
    public int RemoveCalls;
    public override VerifyReport Verify(string filePath) => NextReport;
    public override RemoveOutcome Remove(string filePath)
    {
        RemoveCalls++;
        if (NextRemove.Removed) NextReport = VerifyReport.Unsigned(); // re-verify will show unsigned
        return NextRemove;
    }
}

public class VerifyRemoveViewModelTests
{
    private static VerifyViewModel Vm(FakeEngine engine, FakeRunner? runner = null)
        => new(new AppleSigningService(runner ?? new FakeRunner()), engine);

    [Fact]
    public async Task CanRemove_true_for_signed_authenticode()
    {
        var vm = Vm(new FakeEngine { NextReport = new() { IsSigned = true, SignatureValid = true } });
        await vm.VerifyPathAsync("/tmp/foo.dll");
        Assert.True(vm.CanRemove);
    }

    [Fact]
    public async Task CanRemove_false_for_unsigned()
    {
        var vm = Vm(new FakeEngine { NextReport = VerifyReport.Unsigned() });
        await vm.VerifyPathAsync("/tmp/foo.dll");
        Assert.False(vm.CanRemove);
    }

    [Fact]
    public async Task CanRemove_false_on_verify_error()
    {
        var vm = Vm(new FakeEngine { NextReport = VerifyReport.Failed("boom") });
        await vm.VerifyPathAsync("/tmp/foo.dll");
        Assert.False(vm.CanRemove);
    }

    [Fact]
    public async Task CanRemove_false_for_mac_artifact()
    {
        var app = Path.Combine(Path.GetTempPath(), "macsign-rmtest-" + Guid.NewGuid().ToString("N"), "Demo.app");
        Directory.CreateDirectory(app);
        var f = new FakeRunner { Respond = (_, _) => new ProcessResult(0, "", "code object is not signed at all", false) };
        var vm = Vm(new FakeEngine(), f);
        await vm.VerifyPathAsync(app);
        Assert.True(vm.IsMacReport);
        Assert.False(vm.CanRemove);
    }

    [Fact]
    public async Task First_click_confirms_without_removing_second_click_removes_and_reverifies()
    {
        var engine = new FakeEngine { NextReport = new() { IsSigned = true, SignatureValid = true } };
        var vm = Vm(engine);
        await vm.VerifyPathAsync("/tmp/foo.dll");
        Assert.True(vm.CanRemove);

        await vm.RemoveSignatureCommand.ExecuteAsync(null);   // first click → arm
        Assert.True(vm.ConfirmRemove);
        Assert.Equal(0, engine.RemoveCalls);

        await vm.RemoveSignatureCommand.ExecuteAsync(null);   // second click → remove + re-verify
        Assert.Equal(1, engine.RemoveCalls);
        Assert.False(vm.ConfirmRemove);
        Assert.False(vm.CanRemove);  // re-verified as unsigned
    }

    [Fact]
    public async Task CanRemove_true_for_signed_but_invalid()
    {
        // A tampered (modified-after-signing) file still has a signature to strip.
        var vm = Vm(new FakeEngine { NextReport = new() { IsSigned = true, SignatureValid = false } });
        await vm.VerifyPathAsync("/tmp/foo.dll");
        Assert.True(vm.CanRemove);
    }

    [Fact]
    public async Task VerifyAnother_cancels_pending_confirm()
    {
        var vm = Vm(new FakeEngine { NextReport = new() { IsSigned = true, SignatureValid = true } });
        await vm.VerifyPathAsync("/tmp/foo.dll");
        await vm.RemoveSignatureCommand.ExecuteAsync(null); // arm
        Assert.True(vm.ConfirmRemove);
        vm.VerifyAnotherCommand.Execute(null);
        Assert.False(vm.ConfirmRemove);
    }

    [Fact]
    public async Task Remove_error_is_surfaced_and_report_unchanged()
    {
        var engine = new FakeEngine
        {
            NextReport = new() { IsSigned = true, SignatureValid = true },
            NextRemove = new(false, "Access denied"),
        };
        var vm = Vm(engine);
        await vm.VerifyPathAsync("/tmp/foo.dll");
        await vm.RemoveSignatureCommand.ExecuteAsync(null); // arm
        await vm.RemoveSignatureCommand.ExecuteAsync(null); // attempt

        Assert.Equal("Access denied", vm.RemoveError);
        Assert.True(vm.CanRemove); // still signed
    }
}
