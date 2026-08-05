using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MacSign.Cli;
using MacSign.Signing;
using MacSign.Signing.Verification;

return await App.Run(args);

namespace MacSign.Cli
{
    /// <summary>
    /// A small command-line harness for the signing engine. Verbs:
    ///   macsign sign          — Authenticode-sign a PE/PowerShell/MSI in place
    ///   macsign verify        — report a signature's integrity, signers, and chain trust
    ///   macsign remove        — strip an existing signature in place
    ///   macsign gen-test-cert — make a throwaway self-signed code-signing cert
    /// Plus <c>--help</c> and <c>--version</c>.
    /// </summary>
    internal static class App
    {
        public static async Task<int> Run(string[] args)
        {
            // Enable the optional backends in the core engine (the core references none of them).
            MacSign.Signing.Pkcs11.Pkcs11Backend.Register();
            MacSign.Signing.Msi.MsiBackend.Register();
            MacSign.Signing.Azure.AzureBackend.Register();

            if (args.Length == 0)
            {
                Usage();
                return 2;
            }

            try
            {
                return args[0] switch
                {
                    "sign" => await Sign(new Flags(args[1..], "all")),
                    "verify" => Verify(new Flags(args[1..])),
                    "remove" => Remove(new Flags(args[1..])),
                    "gen-test-cert" => GenTestCert(new Flags(args[1..])),
                    "-h" or "--help" or "help" => Usage(),
                    "-v" or "--version" or "version" => Version(),
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
            bool all = f.Has("all");
            // Never silently sign just one of several files: earlier `positional = a; last wins`
            // dropped all but the last, exiting 0 as if every file had been signed.
            if (f.Positionals.Count > 1)
                return Fail(
                    $"sign takes a single {(all ? "folder" : "file")}, but got {f.Positionals.Count}: " +
                    $"{string.Join(", ", f.Positionals)}. To sign multiple files, use: macsign sign --all <folder>.");
            var file = f.Positional ?? throw new ArgumentException("Missing the file (or folder, with --all) to sign.");
            var password = ResolvePassword(f);
            var modulePath = f.Get("pkcs11-module");
            var tsEndpoint = f.Get("trusted-signing-endpoint");

            var mode = tsEndpoint is not null ? CertMode.TrustedSigning
                : modulePath is not null ? CertMode.Pkcs11
                : CertMode.Pfx;

            var options = new SigningOptions
            {
                CertMode = mode,
                PfxPath = f.Get("pfx"),
                Pkcs11ModulePath = modulePath,
                Pkcs11CertThumbprint = f.Get("pkcs11-thumbprint"),
                TrustedSigningEndpoint = tsEndpoint,
                TrustedSigningAccount = f.Get("trusted-signing-account"),
                TrustedSigningProfile = f.Get("trusted-signing-profile"),
                TrustedSigningTenantId = f.Get("trusted-signing-tenant"),
                TrustedSigningAccessToken = ResolveTrustedSigningToken(f),
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
            var file = f.RequireSinglePositional("file to verify");
            var r = SignatureVerifier.Verify(file);

            if (r.Error is not null) return Fail(r.Error);
            if (!r.IsSigned) { Console.WriteLine("Not signed."); return 1; }

            Console.WriteLine("Signed:      yes");
            Console.WriteLine($"Integrity:   {(r.SignatureValid ? "VALID — unmodified, signature verifies" : "INVALID")}");
            Console.WriteLine($"Signer:      {r.SignerSubject}");
            Console.WriteLine($"Issuer:      {r.SignerIssuer}");
            if (r.Signers.Count > 1)
            {
                Console.WriteLine($"Signers:     {r.Signers.Count} (co-signed)");
                foreach (var s in r.Signers)
                    Console.WriteLine($"             - {s.Subject} [{(s.SignatureValid ? "valid" : "INVALID")}]");
            }
            if (r.HasNestedSignature)
                Console.WriteLine("Nested sig:  present (the signer above is the primary)");
            if (r.Timestamp is { } ts) Console.WriteLine($"Timestamp:   {ts:u}");
            Console.WriteLine($"Chain trust: {(r.ChainTrusted ? "trusted on this OS" : "not validated on this OS")}");
            if (!r.ChainTrusted && r.ChainNote is not null) Console.WriteLine($"             ({r.ChainNote})");

            return r.SignatureValid ? 0 : 2;
        }

        private static int Remove(Flags f)
        {
            var file = f.RequireSinglePositional("file to remove the signature from");
            if (SignatureRemover.Remove(file))
                Console.WriteLine($"Removed the signature from {Path.GetFileName(file)}.");
            else
                Console.WriteLine($"{Path.GetFileName(file)} was not signed — nothing to remove.");
            return 0;
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
            RestrictToOwner(pfxPath); // the PFX holds the private key — keep it owner-readable only
            File.WriteAllBytes(cerPath, cert.Export(X509ContentType.Cert)); // DER public cert
            Console.WriteLine($"Wrote test PFX → {pfxPath}  (throwaway self-signed cert — not for production)");
            Console.WriteLine($"Wrote public cert → {cerPath}");
            return 0;
        }

        private static string? ResolvePassword(Flags f)
        {
            if (f.Get("password") is { } direct) { WarnPlaintextSecret("--password"); return direct; }
            if (f.Get("password-env") is { } envVar)
                return Environment.GetEnvironmentVariable(envVar)
                    ?? throw new ArgumentException($"Environment variable '{envVar}' is not set.");
            return null;
        }

        private static string? ResolveTrustedSigningToken(Flags f)
        {
            if (f.Get("trusted-signing-token") is { } direct) { WarnPlaintextSecret("--trusted-signing-token"); return direct; }
            if (f.Get("trusted-signing-token-env") is { } envVar)
                return Environment.GetEnvironmentVariable(envVar)
                    ?? throw new ArgumentException($"Environment variable '{envVar}' is not set.");
            return null; // fall back to Azure.Identity (az login / env service principal / managed identity)
        }

        /// <summary>A plaintext secret on argv lands in shell history and the process list — nudge to the env form.</summary>
        private static void WarnPlaintextSecret(string flag) =>
            Console.Error.WriteLine(
                $"warning: {flag} puts the secret in your shell history and the process list (ps); prefer {flag}-env.");

        private static void RestrictToOwner(string path)
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch { /* best effort — file permissions are advisory for a throwaway cert */ }
        }

        private static int Usage()
        {
            Console.WriteLine("""
                macsign — native Authenticode signing (PE / PowerShell / MSI)

                  macsign sign --pfx <file> [--password <pw> | --password-env <VAR>]
                               [--description <text>] [--url <url>] [--timestamp-url <url[,url2,…]>]
                               [--all] <file-or-folder>

                  macsign sign --pkcs11-module <lib> [--password-env <VAR>]
                               [--pkcs11-thumbprint <hex>] [--timestamp-url <url[,url2,…]>] <file>

                  macsign sign --trusted-signing-endpoint <host> --trusted-signing-account <acct>
                               --trusted-signing-profile <profile> [--trusted-signing-tenant <id>]
                               [--trusted-signing-token <jwt> | --trusted-signing-token-env <VAR>]
                               [--timestamp-url <url[,url2,…]>] <file>
                               (no token flag → Azure.Identity: az login / env service principal / managed identity)
                               --trusted-signing-tenant pins the directory (GUID or domain). Without it the
                               token comes from whichever account `az login` last selected — to sign as a
                               different account, run `az login --tenant <id>` or use the app's Sign screen,
                               which can sign in through a browser and let you choose.

                  macsign verify <file>

                  macsign remove <file>

                  macsign gen-test-cert --pfx <out.pfx> --cer <out.cer>
                               [--password <pw> | --password-env <VAR>] [--subject <CN>]
                """);
            return 0;
        }

        private static int Version()
        {
            var asm = Assembly.GetExecutingAssembly();
            var v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? asm.GetName().Version?.ToString()
                    ?? "unknown";
            var plus = v.IndexOf('+'); // strip the SourceLink "+<gitsha>" build-metadata suffix, if any
            if (plus >= 0) v = v[..plus];
            Console.WriteLine($"macsign {v}");
            return 0;
        }

        private static int Fail(string message)
        {
            Console.Error.WriteLine("error: " + message);
            return 1;
        }
    }

    /// <summary>Minimal <c>--flag value</c> / <c>--bool</c> parser. Positionals are collected
    /// (not "last wins"), and declared boolean flags never consume the token after them.</summary>
    internal sealed class Flags
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _bools = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _positionals = new();

        /// <summary>Every non-flag argument, in order.</summary>
        public IReadOnlyList<string> Positionals => _positionals;

        /// <summary>The single positional, or null when there were zero or more than one.</summary>
        public string? Positional => _positionals.Count == 1 ? _positionals[0] : null;

        /// <param name="booleanFlags">Flags that take no value (e.g. <c>all</c>). Declaring them
        /// stops the parser from greedily swallowing the following positional as their value — so
        /// <c>--all &lt;folder&gt;</c> works, and the folder isn't mistaken for the flag's value.</param>
        public Flags(string[] args, params string[] booleanFlags)
        {
            var bools = new HashSet<string>(booleanFlags, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a.StartsWith("--", StringComparison.Ordinal))
                {
                    var name = a[2..];
                    if (!bools.Contains(name)
                        && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        _values[name] = args[++i];
                    else
                        _bools.Add(name);
                }
                else
                {
                    _positionals.Add(a);
                }
            }
        }

        public string? Get(string name) => _values.TryGetValue(name, out var v) ? v : null;
        public bool Has(string name) => _bools.Contains(name) || _values.ContainsKey(name);
        public string Require(string name) => Get(name) ?? throw new ArgumentException($"Missing required --{name}.");

        /// <summary>Exactly one positional, or a clear error — never silently drops extras.</summary>
        public string RequireSinglePositional(string noun) => _positionals.Count switch
        {
            1 => _positionals[0],
            0 => throw new ArgumentException($"Missing the {noun}."),
            _ => throw new ArgumentException(
                $"Expected a single {noun}, but got {_positionals.Count}: {string.Join(", ", _positionals)}."),
        };
    }

    /// <summary>Writes engine progress straight to stdout (synchronous).</summary>
    internal sealed class ConsoleProgress : IProgress<string>
    {
        public void Report(string value) => Console.WriteLine(value);
    }
}
