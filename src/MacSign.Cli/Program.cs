using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MacSign.Cli;
using MacSign.Signing;
using MacSign.Signing.Verification;

return await App.Run(args);

namespace MacSign.Cli
{
    /// <summary>
    /// A small command-line harness for the signing engine. Two verbs:
    ///   macsign sign          — Authenticode-sign a PE in place
    ///   macsign gen-test-cert — make a throwaway self-signed code-signing cert
    /// </summary>
    internal static class App
    {
        public static async Task<int> Run(string[] args)
        {
            // Enable the optional backends in the core engine (the core references neither).
            MacSign.Signing.Pkcs11.Pkcs11Backend.Register();
            MacSign.Signing.Msi.MsiBackend.Register();

            if (args.Length == 0)
            {
                Usage();
                return 2;
            }

            try
            {
                return args[0] switch
                {
                    "sign" => await Sign(new Flags(args[1..])),
                    "verify" => Verify(new Flags(args[1..])),
                    "gen-test-cert" => GenTestCert(new Flags(args[1..])),
                    "-h" or "--help" or "help" => Usage(),
                    var other => Fail($"Unknown command '{other}'."),
                };
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        private static async Task<int> Sign(Flags f)
        {
            var file = f.Positional ?? throw new ArgumentException("Missing the file (or folder, with --all) to sign.");
            var password = ResolvePassword(f);
            bool all = f.Has("all");
            var modulePath = f.Get("pkcs11-module");

            var options = new SigningOptions
            {
                CertMode = modulePath is null ? CertMode.Pfx : CertMode.Pkcs11,
                PfxPath = f.Get("pfx"),
                Pkcs11ModulePath = modulePath,
                Pkcs11CertThumbprint = f.Get("pkcs11-thumbprint"),
                Secret = password,
                Description = f.Get("description"),
                Url = f.Get("url"),
                TimestampUrl = f.Get("timestamp-url"),
                SignAllSignableFiles = all,
            };

            var signer = AuthenticodeSigner.TryCreate(options, out var error);
            if (signer is null)
                return Fail(error!);

            var sourceFolder = all ? file : (Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".");
            var progress = new ConsoleProgress();
            var result = await signer.SignAsync(sourceFolder, file, options, progress);

            if (!result.Success)
                return Fail(result.Error!);

            Console.WriteLine("Done.");
            return 0;
        }

        private static int Verify(Flags f)
        {
            var file = f.Positional ?? throw new ArgumentException("Missing the file to verify.");
            var r = SignatureVerifier.Verify(file);

            if (r.Error is not null) return Fail(r.Error);
            if (!r.IsSigned) { Console.WriteLine("Not signed."); return 1; }

            Console.WriteLine("Signed:      yes");
            Console.WriteLine($"Integrity:   {(r.SignatureValid ? "VALID — unmodified, signature verifies" : "INVALID")}");
            Console.WriteLine($"Signer:      {r.SignerSubject}");
            Console.WriteLine($"Issuer:      {r.SignerIssuer}");
            if (r.Timestamp is { } ts) Console.WriteLine($"Timestamp:   {ts:u}");
            Console.WriteLine($"Chain trust: {(r.ChainTrusted ? "trusted on this OS" : "not validated on this OS")}");
            if (!r.ChainTrusted && r.ChainNote is not null) Console.WriteLine($"             ({r.ChainNote})");

            return r.SignatureValid ? 0 : 2;
        }

        private static int GenTestCert(Flags f)
        {
            var pfxPath = f.Require("pfx");
            var cerPath = f.Require("cer");
            var password = ResolvePassword(f) ?? "testpw";
            var subject = f.Get("subject") ?? "MacSign Test";

            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest($"CN={subject}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, true)); // id-kp-codeSigning
            req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

            var now = DateTimeOffset.UtcNow;
            using var cert = req.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(3));

            File.WriteAllBytes(pfxPath, cert.Export(X509ContentType.Pfx, password));
            File.WriteAllBytes(cerPath, cert.Export(X509ContentType.Cert)); // DER public cert
            Console.WriteLine($"Wrote test PFX → {pfxPath}");
            Console.WriteLine($"Wrote public cert → {cerPath}");
            return 0;
        }

        private static string? ResolvePassword(Flags f)
        {
            if (f.Get("password") is { } direct) return direct;
            if (f.Get("password-env") is { } envVar)
                return Environment.GetEnvironmentVariable(envVar)
                    ?? throw new ArgumentException($"Environment variable '{envVar}' is not set.");
            return null;
        }

        private static int Usage()
        {
            Console.WriteLine("""
                macsign — native Authenticode signing (Phase 1: PE + PFX)

                  macsign sign --pfx <file> [--password <pw> | --password-env <VAR>]
                               [--description <text>] [--url <url>] [--timestamp-url <url>]
                               [--all] <file-or-folder>

                  macsign sign --pkcs11-module <lib> [--password-env <VAR>]
                               [--pkcs11-thumbprint <hex>] [--timestamp-url <url>] <file>

                  macsign verify <file>

                  macsign gen-test-cert --pfx <out.pfx> --cer <out.cer>
                               [--password <pw> | --password-env <VAR>] [--subject <CN>]
                """);
            return 0;
        }

        private static int Fail(string message)
        {
            Console.Error.WriteLine("error: " + message);
            return 1;
        }
    }

    /// <summary>Minimal <c>--flag value</c> / <c>--bool</c> parser with one trailing positional.</summary>
    internal sealed class Flags
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _bools = new(StringComparer.OrdinalIgnoreCase);

        public string? Positional { get; }

        public Flags(string[] args)
        {
            string? positional = null;
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a.StartsWith("--", StringComparison.Ordinal))
                {
                    var name = a[2..];
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        _values[name] = args[++i];
                    else
                        _bools.Add(name);
                }
                else
                {
                    positional = a; // last positional wins
                }
            }
            Positional = positional;
        }

        public string? Get(string name) => _values.TryGetValue(name, out var v) ? v : null;
        public bool Has(string name) => _bools.Contains(name) || _values.ContainsKey(name);
        public string Require(string name) => Get(name) ?? throw new ArgumentException($"Missing required --{name}.");
    }

    /// <summary>Writes engine progress straight to stdout (synchronous).</summary>
    internal sealed class ConsoleProgress : IProgress<string>
    {
        public void Report(string value) => Console.WriteLine(value);
    }
}
