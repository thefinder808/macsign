using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MacSign.Signing.Tests;

/// <summary>
/// Provisions a throwaway SoftHSM2 token with a code-signing key + certificate for
/// the PKCS#11 integration test. Returns null (so the test self-skips) when
/// softhsm2-util / pkcs11-tool / the module aren't installed.
/// </summary>
internal sealed class SoftHsm : IDisposable
{
    public const string Pin = "1234";

    public string Dir { get; }
    public string ModulePath { get; }

    private SoftHsm(string dir, string modulePath) { Dir = dir; ModulePath = modulePath; }

    public static async Task<SoftHsm?> TryProvisionAsync()
    {
        string? module = FindModule();
        string? util = Which("softhsm2-util");
        string? p11tool = Which("pkcs11-tool");
        if (module is null || util is null || p11tool is null)
            return null;

        var dir = Path.Combine(Path.GetTempPath(), "macsign-hsm-" + Guid.NewGuid().ToString("N"));
        var tokens = Path.Combine(dir, "tokens");
        Directory.CreateDirectory(tokens);

        var conf = Path.Combine(dir, "softhsm2.conf");
        File.WriteAllText(conf, $"directories.tokendir = {tokens}\nobjectstore.backend = file\nlog.level = ERROR\n");
        // Set it both ways: managed (so child tools inherit it) and native setenv
        // (so the in-process libsofthsm2's getenv sees it — managed-only is not enough).
        Environment.SetEnvironmentVariable("SOFTHSM2_CONF", conf);
        setenv("SOFTHSM2_CONF", conf, 1);

        if (await Run(util, ["--init-token", "--free", "--label", "macsign", "--pin", Pin, "--so-pin", "5678"]) != 0)
            return null;

        // Key + cert, generated in-process; no openssl needed.
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=MacSign Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, true));
        var now = DateTimeOffset.UtcNow;
        using var cert = req.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(2));

        var keyPem = Path.Combine(dir, "key.pem");
        File.WriteAllText(keyPem, rsa.ExportPkcs8PrivateKeyPem());
        var certDer = Path.Combine(dir, "cert.cer");
        File.WriteAllBytes(certDer, cert.Export(X509ContentType.Cert));

        if (await Run(util, ["--import", keyPem, "--token", "macsign", "--label", "codesign", "--id", "a1b2", "--pin", Pin]) != 0)
            return null;
        if (await Run(p11tool, ["--module", module, "--login", "--pin", Pin, "--write-object", certDer, "--type", "cert", "--id", "a1b2", "--label", "codesign"]) != 0)
            return null;

        return new SoftHsm(dir, module);
    }

    private static string? FindModule()
    {
        var env = Environment.GetEnvironmentVariable("MACSIGN_SOFTHSM_MODULE");
        if (env is not null && File.Exists(env)) return env;

        var candidates = new List<string>
        {
            "/opt/homebrew/lib/softhsm/libsofthsm2.so",
            "/usr/local/lib/softhsm/libsofthsm2.so",
            "/usr/lib/softhsm/libsofthsm2.so",
            "/usr/lib/x86_64-linux-gnu/softhsm/libsofthsm2.so",
        };
        foreach (var cellar in new[] { "/opt/homebrew/Cellar/softhsm", "/usr/local/Cellar/softhsm" })
            if (Directory.Exists(cellar))
                foreach (var ver in Directory.GetDirectories(cellar))
                    candidates.Add(Path.Combine(ver, "lib/softhsm/libsofthsm2.so"));

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? Which(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var p = Path.Combine(dir, name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static async Task<int> Run(string file, string[] args)
    {
        var psi = new ProcessStartInfo { FileName = file, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi)!;
        await proc.WaitForExitAsync();
        return proc.ExitCode;
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); } catch { /* best effort */ }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);
}
