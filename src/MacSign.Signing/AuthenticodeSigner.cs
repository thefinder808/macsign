using MacSign.Signing.Credentials;
using MacSign.Signing.Engine;
using MacSign.Signing.Formats;

namespace MacSign.Signing;

/// <summary>
/// Public entry point. Signs one or more Windows artifacts in place. Phase 1
/// implements PE (<c>.exe</c>/<c>.dll</c>/<c>.sys</c>) with a local PFX certificate.
/// </summary>
public sealed class AuthenticodeSigner
{
    private AuthenticodeSigner() { }

    /// <summary>
    /// Validate the options for the chosen mode. Returns null + an actionable
    /// <paramref name="error"/> if they're not usable.
    /// </summary>
    public static AuthenticodeSigner? TryCreate(SigningOptions options, out string? error)
    {
        switch (options.CertMode)
        {
            case CertMode.Pfx:
                if (string.IsNullOrWhiteSpace(options.PfxPath) || !File.Exists(options.PfxPath))
                {
                    error = $"PFX file not found: {options.PfxPath ?? "(none)"}";
                    return null;
                }
                break;

            case CertMode.Pkcs11:
                if (CredentialBackends.Pkcs11Factory is null)
                {
                    error = "PKCS#11 support isn't loaded — reference MacSign.Signing.Pkcs11 and call Pkcs11Backend.Register().";
                    return null;
                }
                if (string.IsNullOrWhiteSpace(options.Pkcs11ModulePath) || !File.Exists(options.Pkcs11ModulePath))
                {
                    error = $"PKCS#11 module not found: {options.Pkcs11ModulePath ?? "(none)"}";
                    return null;
                }
                break;

            case CertMode.TrustedSigning:
                if (CredentialBackends.TrustedSigningFactory is null)
                {
                    error = "Azure Trusted Signing support isn't loaded — reference MacSign.Signing.Azure and call AzureBackend.Register().";
                    return null;
                }
                if (string.IsNullOrWhiteSpace(options.TrustedSigningEndpoint) ||
                    string.IsNullOrWhiteSpace(options.TrustedSigningAccount) ||
                    string.IsNullOrWhiteSpace(options.TrustedSigningProfile))
                {
                    error = "Azure Trusted Signing needs --trusted-signing-endpoint, --trusted-signing-account, and --trusted-signing-profile.";
                    return null;
                }
                break;

            default:
                error = $"MacSign doesn't implement {options.CertMode} signing yet.";
                return null;
        }

        error = null;
        return new AuthenticodeSigner();
    }

    /// <summary>
    /// Sign <paramref name="setupFile"/> (and, if <see cref="SigningOptions.SignAllSignableFiles"/>,
    /// every signable file under <paramref name="sourceFolder"/>). Already-signed files are skipped.
    /// </summary>
    public async Task<SignResult> SignAsync(
        string sourceFolder, string setupFile, SigningOptions options,
        IProgress<string>? log = null, CancellationToken ct = default)
    {
        bool setupSignable = SignableExtensions.IsSignable(setupFile);
        if (!options.SignAllSignableFiles && !setupSignable)
            return SignResult.Fail(
                $"'{Path.GetFileName(setupFile)}' is not an Authenticode-signable type.");

        var targets = CollectTargets(sourceFolder, setupFile, options);
        if (targets.Count == 0)
            return SignResult.Fail("No Authenticode-signable files were found to sign.");

        ICredentialSigner credential;
        try
        {
            credential = options.CertMode switch
            {
                CertMode.Pkcs11 => CredentialBackends.Pkcs11Factory!(options),
                CertMode.TrustedSigning => CredentialBackends.TrustedSigningFactory!(options),
                _ => new PfxCredentialSigner(options.PfxPath!, options.Secret),
            };
        }
        catch (Exception ex)
        {
            return SignResult.Fail($"Could not load the signing credential: {ex.Message}");
        }

        try
        {
            var failures = new List<string>();
            int signedCount = 0;
            foreach (var file in targets)
            {
                ct.ThrowIfCancellationRequested();

                var format = FormatRegistry.For(file);
                if (format is null)
                {
                    // A signable-in-principle file we don't implement yet (e.g. .msi).
                    var ext = Path.GetExtension(file);
                    if (options.SignAllSignableFiles)
                    {
                        log?.Report($"Skipping {Path.GetFileName(file)} — {ext} signing isn't implemented yet (PE and PowerShell only).");
                        continue;
                    }
                    return SignResult.Fail($"{ext} signing isn't implemented yet (PE and PowerShell only).");
                }

                try
                {
                    byte[] bytes = await File.ReadAllBytesAsync(file, ct);

                    if (ExistingSignatureGate.IsSigned(format, bytes, out var subject))
                    {
                        log?.Report($"Skipping {Path.GetFileName(file)} — already signed ({subject ?? "unknown signer"}).");
                        continue;
                    }

                    log?.Report($"Signing {Path.GetFileName(file)}…");
                    byte[] signed = await SignEngine.SignFileBytesAsync(format, credential, options, bytes, ct);
                    await AtomicFile.WriteAsync(file, signed, ct);
                    signedCount++;
                    log?.Report($"Signed {Path.GetFileName(file)}.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Single-file mode fails fast; batch (--all) keeps going so one bad file
                    // doesn't strand the rest, and every failure is reported at the end.
                    if (!options.SignAllSignableFiles)
                        return SignResult.Fail($"Failed to sign {Path.GetFileName(file)}: {ex.Message}");
                    log?.Report($"Failed {Path.GetFileName(file)}: {ex.Message}");
                    failures.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }

            if (failures.Count > 0)
                return SignResult.Fail(
                    $"Signed {signedCount} file(s); {failures.Count} failed:\n  " + string.Join("\n  ", failures));

            return SignResult.Ok();
        }
        finally
        {
            credential.Dispose();
        }
    }

    private static List<string> CollectTargets(string sourceFolder, string setupFile, SigningOptions options)
    {
        if (!options.SignAllSignableFiles)
            return [setupFile];

        return Directory
            .EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories)
            .Where(SignableExtensions.IsSignable)
            .ToList();
    }

}
