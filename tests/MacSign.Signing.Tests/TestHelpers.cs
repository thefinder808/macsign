using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MacSign.Signing.Tests;

/// <summary>Generates throwaway self-signed code-signing certs in-process (no openssl shell).</summary>
internal static class TestCerts
{
    public const string Password = "testpw";

    public static string CreatePfx(string dir, string password = Password, string subject = "MacSign Test")
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={subject}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, true));

        var now = DateTimeOffset.UtcNow;
        using var cert = req.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(2));

        var pfx = Path.Combine(dir, "test.pfx");
        File.WriteAllBytes(pfx, cert.Export(X509ContentType.Pfx, password));
        return pfx;
    }
}

/// <summary>The compiled MacSign.Fixture.dll — a real, unsigned managed PE.</summary>
internal static class FixturePe
{
    private static string SourcePath => typeof(MacSign.Fixture.Hello).Assembly.Location;

    public static byte[] UnsignedBytes() => File.ReadAllBytes(SourcePath);

    public static string CopyToTemp(string dir)
    {
        var dst = Path.Combine(dir, "MacSign.Fixture.dll");
        File.Copy(SourcePath, dst, overwrite: true);
        return dst;
    }
}

/// <summary>A temp directory that deletes itself.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "macsign-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>Captures engine progress synchronously (unlike Progress&lt;T&gt;, no threadpool race).</summary>
internal sealed class ListProgress : IProgress<string>
{
    public List<string> Messages { get; } = [];
    public void Report(string value) => Messages.Add(value);
}

internal static class Bytes
{
    public static bool Contains(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0) return true;
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return true;
        }
        return false;
    }
}
